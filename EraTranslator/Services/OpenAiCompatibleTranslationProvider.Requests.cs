using System.Text;
using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed partial class OpenAiCompatibleTranslationProvider
{
    private readonly record struct RequestBuildMetadata(
        LmStudioModelFamily ModelFamily,
        LmStudioThinkingControlMode ThinkingControlMode,
        int? MaxTokens,
        bool FallbackUsed);

    private enum ResponseMode
    {
        JsonObject,
        JsonTextRetry,
        JsonSchema,
        JsonSchemaRetry,
        TokenizedFallback,
    }

    private static (object Payload, RequestBuildMetadata Metadata) BuildRequestPayload(
        string model,
        ProviderSettings settings,
        IReadOnlyList<ProtectedSegment> requests,
        ResponseMode responseMode,
        bool includeLmStudioSamplingParameters,
        IReadOnlyList<GlossaryHint>? glossaryHints)
    {
        var useTokenizedProtocol = UsesTokenizedProtocol(responseMode);
        var useRetryPrompt = UsesRetryPrompt(responseMode);
        var modelFamily = includeLmStudioSamplingParameters
            ? LmStudioSamplingDefaults.DetectModelFamily(model)
            : LmStudioModelFamily.Unknown;
        var thinkingControlMode = includeLmStudioSamplingParameters
            ? LmStudioSamplingDefaults.GetThinkingControlMode(model, settings.DisableThinking)
            : settings.DisableThinking ? LmStudioThinkingControlMode.PromptFallback : LmStudioThinkingControlMode.None;
        var effectiveMaxTokens = ResolveEffectiveMaxTokens(settings, responseMode, model, requests.Count);
        var metadata = new RequestBuildMetadata(
            ModelFamily: modelFamily,
            ThinkingControlMode: thinkingControlMode,
            MaxTokens: effectiveMaxTokens,
            FallbackUsed: responseMode is ResponseMode.JsonTextRetry or ResponseMode.JsonSchemaRetry or ResponseMode.TokenizedFallback);
        var systemPrompt = BuildSystemPrompt(settings, useRetryPrompt, useTokenizedProtocol, requests, thinkingControlMode, glossaryHints);
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["temperature"] = GetEffectiveTemperature(settings, responseMode),
            ["messages"] = BuildMessages(systemPrompt, requests, settings, useTokenizedProtocol),
        };

        if (includeLmStudioSamplingParameters)
        {
            if (settings.TopP.HasValue)
            {
                payload["top_p"] = settings.TopP.Value;
            }

            if (settings.TopK.HasValue)
            {
                payload["top_k"] = settings.TopK.Value;
            }

            if (settings.RepeatPenalty.HasValue)
            {
                payload["repeat_penalty"] = settings.RepeatPenalty.Value;
            }

            if (settings.PresencePenalty.HasValue)
            {
                payload["presence_penalty"] = settings.PresencePenalty.Value;
            }

            if (settings.Seed.HasValue)
            {
                payload["seed"] = settings.Seed.Value;
            }

            if (effectiveMaxTokens.HasValue && effectiveMaxTokens.Value > 0)
            {
                payload["max_tokens"] = effectiveMaxTokens.Value;
            }

            if (thinkingControlMode == LmStudioThinkingControlMode.ApiCustomField)
            {
                payload["enable_thinking"] = !settings.DisableThinking;
            }
        }

        if (responseMode == ResponseMode.JsonObject)
        {
            payload["response_format"] = new Dictionary<string, object?>
            {
                ["type"] = "json_object",
            };
        }
        else if (UsesJsonSchema(responseMode))
        {
            payload["response_format"] = BuildJsonSchemaResponseFormat(requests);
        }

        return (payload, metadata);
    }

    private static object[] BuildMessages(
        string systemPrompt,
        IReadOnlyList<ProtectedSegment> requests,
        ProviderSettings settings,
        bool useTokenizedProtocol)
    {
        return
        [
            new
            {
                role = "system",
                content = systemPrompt,
            },
            new
            {
                role = "user",
                content = useTokenizedProtocol
                    ? BuildTokenizedUserContent(requests, settings)
                    : BuildJsonCompatibleUserContent(requests, settings),
            },
        ];
    }

    private static string BuildSystemPrompt(
        ProviderSettings settings,
        bool useRetryPrompt,
        bool useTokenizedProtocol,
        IReadOnlyList<ProtectedSegment> requests,
        LmStudioThinkingControlMode thinkingControlMode,
        IReadOnlyList<GlossaryHint>? glossaryHints)
    {
        var targetLanguageLabel = LanguageDisplayService.ToInstructionLabel(settings.TargetLanguage);
        var hasProtectedPlaceholders = requests.Any(request => request.Placeholders.Count > 0);
        var placeholderInstruction = hasProtectedPlaceholders
            ? Environment.NewLine
              + "These inputs contain placeholder tokens such as __PH0__."
              + Environment.NewLine
              + "Treat every placeholder token as immutable script syntax, not as natural language."
              + Environment.NewLine
              + "Do not translate, rename, split, remove, duplicate, reorder, or add spaces inside placeholder tokens."
              + Environment.NewLine
              + "If a placeholder token would be broken, keep the source structure unchanged instead."
            : string.Empty;
        var scriptSyntaxInstruction =
            Environment.NewLine
            + "Treat script syntax and code-like expressions as immutable."
            + Environment.NewLine
            + "Do not rewrite ERB-style expressions, function names, ASCII identifiers, delimiters, or punctuation inside code-like expressions."
            + Environment.NewLine
            + "Translate only the natural-language portion when script syntax and text appear together.";
        var termStyleInstruction =
            Environment.NewLine
            + "Choose exactly one final translation for each item."
            + Environment.NewLine
            + "Do not provide alternatives, substitutes, multiple candidates, slash-separated variants, pipe-separated variants, or fallback options."
            + Environment.NewLine
            + "Do not append explanatory parentheses, glosses, or notes unless the source text already contains them."
            + Environment.NewLine
            + $"For kanji-heavy labels, glossary entries, item names, and stat names translated into {targetLanguageLabel}, prefer a Hangul reading of the Japanese term over an explanatory replacement when uncertain.";
        var glossaryInstruction = BuildGlossaryInstruction(glossaryHints);

        if (!useTokenizedProtocol)
        {
            var rendered = TranslationPromptTemplates.Render(
                useRetryPrompt ? settings.RetryPromptTemplate : settings.SystemPromptTemplate,
                settings.SourceLanguage,
                settings.TargetLanguage,
                settings.DisableThinking,
                useRetryPrompt,
                thinkingControlMode);

            if (requests.Count == 1)
            {
                return rendered
                    + placeholderInstruction
                    + scriptSyntaxInstruction
                    + termStyleInstruction
                    + glossaryInstruction
                    + Environment.NewLine
                    + $"There is exactly one input item. In the output JSON, use id \"{requests[0].Id}\" for the single translation item.";
            }

            return rendered
                + placeholderInstruction
                + scriptSyntaxInstruction
                + termStyleInstruction
                + glossaryInstruction
                + Environment.NewLine
                + "The user message uses repeated |id| blocks. Read the text inside each block and return one JSON item per id.";
        }

        var sourceLanguageLabel = LanguageDisplayService.ToInstructionLabel(settings.SourceLanguage);
        return
            $"""
            You are a translation engine for Emuera game scripts.
            Translate each input segment from {sourceLanguageLabel} into {targetLanguageLabel}.

            Output rules:
            1. Do not return JSON.
            2. Do not return markdown, code fences, prose, comments, or extra explanations.
            3. Preserve placeholder tokens such as __PH0__ exactly.
            4. Preserve line breaks, escape sequences, and meaningful surrounding whitespace exactly when they exist.
            5. Treat script syntax and code-like expressions as immutable. Do not rewrite function names, identifiers, delimiters, or punctuation inside them.
            6. Keep each segment id unchanged.
            7. If there is only one input segment, return only the translated text itself with no label, no separator, and no extra line.
            8. If there are multiple input segments, return exactly one output block for every input block.
            9. If a line is unsafe or ambiguous, copy the source text into the translated block instead of explaining.
            10. For multiple input segments, use this exact format only:
            |<id>|
            <translated text>
            |
            11. Do not write any text before the first output or after the last output.
            12. The translated text itself must be written in {targetLanguageLabel}. Do not answer in English unless the target language is English.
            """ +
            placeholderInstruction +
            scriptSyntaxInstruction +
            termStyleInstruction +
            glossaryInstruction +
            Environment.NewLine + Environment.NewLine +
            TranslationPromptTemplates.BuildThinkingInstruction(settings.DisableThinking, thinkingControlMode);
    }

    private static string BuildGlossaryInstruction(IReadOnlyList<GlossaryHint>? glossaryHints)
    {
        if (glossaryHints is null || glossaryHints.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine("Glossary hints:");
        builder.AppendLine("Prefer these pairs when they fit the current script context.");
        builder.AppendLine("Do not copy them mechanically if they would make the line unnatural.");
        foreach (var hint in glossaryHints)
        {
            builder.AppendLine($"{hint.Source} => {hint.Target}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildJsonCompatibleUserContent(IReadOnlyList<ProtectedSegment> requests, ProviderSettings settings)
    {
        if (requests.Count == 1)
        {
            return requests[0].Text;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Target language: {LanguageDisplayService.ToInstructionLabel(settings.TargetLanguage)}");
        foreach (var request in requests)
        {
            builder.AppendLine($"|{request.Id}|");
            builder.AppendLine(request.Text);
            builder.AppendLine("|");
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildTokenizedUserContent(IReadOnlyList<ProtectedSegment> requests, ProviderSettings settings)
    {
        if (requests.Count == 1)
        {
            return
                $"Target language: {LanguageDisplayService.ToInstructionLabel(settings.TargetLanguage)}" + Environment.NewLine +
                "Return only the translated text with no label or separator." + Environment.NewLine +
                requests[0].Text;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Target language: {LanguageDisplayService.ToInstructionLabel(settings.TargetLanguage)}");
        builder.AppendLine("Input segments:");
        foreach (var request in requests)
        {
            builder.AppendLine($"|{request.Id}|");
            builder.AppendLine(request.Text);
            builder.AppendLine("|");
        }

        builder.Append("Return only |id| ... | blocks with the same ids and count. Write translated text in the target language.");
        return builder.ToString();
    }

    private static bool UsesTokenizedProtocol(ResponseMode responseMode)
    {
        return responseMode == ResponseMode.TokenizedFallback;
    }

    private static bool UsesJsonSchema(ResponseMode responseMode)
    {
        return responseMode is ResponseMode.JsonSchema or ResponseMode.JsonSchemaRetry;
    }

    private static bool UsesRetryPrompt(ResponseMode responseMode)
    {
        return responseMode is ResponseMode.JsonTextRetry or ResponseMode.JsonSchemaRetry or ResponseMode.TokenizedFallback;
    }

    private static double GetEffectiveTemperature(ProviderSettings settings, ResponseMode responseMode)
    {
        return responseMode is ResponseMode.JsonTextRetry or ResponseMode.JsonSchemaRetry
            ? 0
            : settings.Temperature;
    }

    private static int? ResolveEffectiveMaxTokens(
        ProviderSettings settings,
        ResponseMode responseMode,
        string model,
        int requestCount)
    {
        if (settings.MaxTokens.HasValue)
        {
            return settings.MaxTokens.Value;
        }

        if (!UsesJsonSchema(responseMode))
        {
            return null;
        }

        var recommended = LmStudioSamplingDefaults.GetRecommendedStructuredMaxTokens(model, requestCount);
        return recommended > 0 ? recommended : null;
    }

    private static object BuildJsonSchemaResponseFormat(IReadOnlyList<ProtectedSegment> requests)
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "json_schema",
            ["json_schema"] = new Dictionary<string, object?>
            {
                ["name"] = "translations_response",
                ["strict"] = true,
                ["schema"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new[] { "translations" },
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["translations"] = new Dictionary<string, object?>
                        {
                            ["type"] = "array",
                            ["minItems"] = requests.Count,
                            ["maxItems"] = requests.Count,
                            ["items"] = new Dictionary<string, object?>
                            {
                                ["type"] = "object",
                                ["additionalProperties"] = false,
                                ["required"] = new[] { "id", "translated" },
                                ["properties"] = new Dictionary<string, object?>
                                {
                                    ["id"] = new Dictionary<string, object?>
                                    {
                                        ["type"] = "string",
                                        ["enum"] = requests.Select(request => request.Id).ToArray(),
                                    },
                                    ["translated"] = new Dictionary<string, object?>
                                    {
                                        ["type"] = "string",
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };
    }
}
