using System.Text;
using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed partial class OpenAiCompatibleTranslationProvider
{
    private readonly record struct RequestBuildMetadata(
        LmStudioModelFamily ModelFamily,
        LmStudioThinkingControlMode ThinkingControlMode,
        int? MaxTokens,
        bool FallbackUsed,
        string? MaxTokensFieldName = null);

    private enum ResponseMode
    {
        JsonObject,
        JsonTextRetry,
        JsonSchema,
        JsonSchemaRetry,
        TokenizedFallback,
        TranslateGemmaDedicated,
    }

    private static (object Payload, RequestBuildMetadata Metadata) BuildRequestPayload(
        string model,
        ProviderSettings settings,
        IReadOnlyList<ProtectedSegment> requests,
        ResponseMode responseMode,
        bool includeAdvancedSamplingParameters,
        bool allowApiThinkingControl,
        IReadOnlyList<GlossaryHint>? glossaryHints,
        bool allowPresencePenalty = true,
        bool allowSeed = true)
    {
        if (responseMode == ResponseMode.TranslateGemmaDedicated)
        {
            return BuildTranslateGemmaRequestPayload(model, settings, requests);
        }

        var useTokenizedProtocol = UsesTokenizedProtocol(responseMode);
        var useRetryPrompt = UsesRetryPrompt(responseMode);
        var modelFamily = includeAdvancedSamplingParameters
            ? LmStudioSamplingDefaults.DetectModelFamily(model)
            : LmStudioModelFamily.Unknown;
        var promptProfile = ResolvePromptProfile(settings, model);
        var thinkingControlMode = includeAdvancedSamplingParameters
            ? LmStudioSamplingDefaults.GetThinkingControlMode(model, settings.DisableThinking)
            : settings.DisableThinking ? LmStudioThinkingControlMode.PromptFallback : LmStudioThinkingControlMode.None;
        if (!allowApiThinkingControl && thinkingControlMode == LmStudioThinkingControlMode.ApiCustomField)
        {
            thinkingControlMode = settings.DisableThinking
                ? LmStudioThinkingControlMode.PromptFallback
                : LmStudioThinkingControlMode.None;
        }
        var effectiveMaxTokens = ResolveEffectiveMaxTokens(settings, responseMode, model, requests.Count);
        var metadata = new RequestBuildMetadata(
            ModelFamily: modelFamily,
            ThinkingControlMode: thinkingControlMode,
            MaxTokens: effectiveMaxTokens,
            FallbackUsed: responseMode is ResponseMode.JsonTextRetry or ResponseMode.JsonSchemaRetry or ResponseMode.TokenizedFallback);
        var systemPrompt = BuildSystemPrompt(settings, useRetryPrompt, useTokenizedProtocol, requests, thinkingControlMode, glossaryHints, promptProfile, modelFamily);
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["temperature"] = GetEffectiveTemperature(settings, responseMode),
            ["messages"] = BuildMessages(systemPrompt, requests, settings, useTokenizedProtocol),
        };

        if (includeAdvancedSamplingParameters)
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

            if (allowPresencePenalty && settings.PresencePenalty.HasValue)
            {
                payload["presence_penalty"] = settings.PresencePenalty.Value;
            }

            if (allowSeed && settings.Seed.HasValue)
            {
                payload["seed"] = settings.Seed.Value;
            }

            if (effectiveMaxTokens.HasValue && effectiveMaxTokens.Value > 0)
            {
                payload["max_tokens"] = effectiveMaxTokens.Value;
            }

            if (allowApiThinkingControl && thinkingControlMode == LmStudioThinkingControlMode.ApiCustomField)
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

    private static (object Payload, RequestBuildMetadata Metadata) BuildXiaomiMiMoRequestPayload(
        string model,
        ProviderSettings settings,
        IReadOnlyList<ProtectedSegment> requests,
        ResponseMode responseMode,
        IReadOnlyList<GlossaryHint>? glossaryHints)
    {
        var useRetryPrompt = UsesRetryPrompt(responseMode);
        var promptProfile = ResolvePromptProfile(settings, model);
        var thinkingControlMode = settings.DisableThinking
            ? LmStudioThinkingControlMode.PromptFallback
            : LmStudioThinkingControlMode.None;
        var effectiveMaxTokens = settings.MaxTokens;
        var metadata = new RequestBuildMetadata(
            ModelFamily: LmStudioModelFamily.Unknown,
            ThinkingControlMode: thinkingControlMode,
            MaxTokens: effectiveMaxTokens,
            FallbackUsed: responseMode == ResponseMode.JsonTextRetry,
            MaxTokensFieldName: effectiveMaxTokens.HasValue ? "max_completion_tokens" : null);
        var systemPrompt = BuildSystemPrompt(settings, useRetryPrompt, useTokenizedProtocol: false, requests, thinkingControlMode, glossaryHints, promptProfile, LmStudioModelFamily.Unknown);
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["temperature"] = GetEffectiveTemperature(settings, responseMode),
            ["messages"] = BuildMessages(systemPrompt, requests, settings, useTokenizedProtocol: false),
            ["thinking"] = new Dictionary<string, object?>
            {
                ["type"] = settings.DisableThinking ? "disabled" : "enabled",
            },
        };

        if (settings.TopP.HasValue)
        {
            payload["top_p"] = settings.TopP.Value;
        }

        if (settings.PresencePenalty.HasValue)
        {
            payload["presence_penalty"] = settings.PresencePenalty.Value;
        }

        if (effectiveMaxTokens.HasValue)
        {
            payload["max_completion_tokens"] = effectiveMaxTokens.Value;
        }

        payload["response_format"] = new Dictionary<string, object?>
        {
            ["type"] = "json_object",
        };

        return (payload, metadata);
    }

    private static (object Payload, RequestBuildMetadata Metadata) BuildTranslateGemmaRequestPayload(
        string model,
        ProviderSettings settings,
        IReadOnlyList<ProtectedSegment> requests)
    {
        if (requests.Count != 1)
        {
            throw new TranslationProviderException(TranslationErrorKind.Validation, "TranslateGemma 전용 요청은 한 번에 하나의 세그먼트만 지원합니다.");
        }

        var request = requests[0];
        var metadata = new RequestBuildMetadata(
            ModelFamily: LmStudioModelFamily.TranslateGemma,
            ThinkingControlMode: LmStudioThinkingControlMode.None,
            MaxTokens: settings.MaxTokens,
            FallbackUsed: false);

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["temperature"] = settings.Temperature,
            ["messages"] = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "text",
                            source_lang_code = settings.SourceLanguage,
                            target_lang_code = settings.TargetLanguage,
                            text = request.Text,
                            image = (string?)null,
                        },
                    },
                },
            },
        };

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

        if (settings.MaxTokens.HasValue)
        {
            payload["max_tokens"] = settings.MaxTokens.Value;
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
        IReadOnlyList<GlossaryHint>? glossaryHints,
        PromptProfile promptProfile,
        LmStudioModelFamily modelFamily)
    {
        var targetLanguageLabel = GetPromptLanguageLabel(settings.TargetLanguage, promptProfile);
        var rendered = TranslationPromptTemplates.Render(
            useRetryPrompt ? settings.RetryPromptTemplate : settings.SystemPromptTemplate,
            settings.SourceLanguage,
            settings.TargetLanguage,
            settings.DisableThinking,
            useRetryPrompt,
            thinkingControlMode,
            promptProfile);
        var commonInstruction = BuildCommonTranslationInstruction(requests, targetLanguageLabel, modelFamily);
        var glossaryInstruction = BuildGlossaryInstruction(glossaryHints, promptProfile);
        var formatInstruction = useTokenizedProtocol
            ? BuildTokenizedFormatInstruction(requests, targetLanguageLabel)
            : BuildJsonFormatInstruction(requests);

        return rendered
            + commonInstruction
            + glossaryInstruction
            + formatInstruction;
    }

    private static string BuildCommonTranslationInstruction(
        IReadOnlyList<ProtectedSegment> requests,
        string targetLanguageLabel,
        LmStudioModelFamily modelFamily)
    {
        var hasProtectedPlaceholders = requests.Any(request => request.Placeholders.Count > 0);
        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine("Shared translation constraints:");
        if (hasProtectedPlaceholders)
        {
            builder.AppendLine("These inputs contain placeholder tokens such as __PH0__.");
            builder.AppendLine("Treat every placeholder token as immutable script syntax, not as natural language.");
            builder.AppendLine("Do not translate, rename, split, remove, duplicate, reorder, or add spaces inside placeholder tokens.");
            builder.AppendLine("If a placeholder token would be broken, keep the source structure unchanged instead.");
        }

        builder.AppendLine("Preserve line breaks, escape sequences, and meaningful surrounding whitespace exactly when they exist.");
        builder.AppendLine("Treat script syntax and code-like expressions as immutable.");
        builder.AppendLine("Do not rewrite ERB-style expressions, function names, ASCII identifiers, delimiters, or punctuation inside code-like expressions.");
        builder.AppendLine("Translate only the natural-language portion when script syntax and text appear together.");
        builder.AppendLine("Choose exactly one final translation for each item.");
        builder.AppendLine("Do not provide alternatives, substitutes, multiple candidates, slash-separated variants, pipe-separated variants, or fallback options.");
        builder.AppendLine("Do not append explanatory parentheses, glosses, or notes unless the source text already contains them.");
        builder.AppendLine("Do not include the source text, romanization, translator comments, metadata, or extra annotation inside the final translated content.");
        builder.AppendLine($"For kanji-heavy labels, glossary entries, item names, and stat names translated into {targetLanguageLabel}, prefer a Hangul reading of the Japanese term over an explanatory replacement when uncertain.");
        if (modelFamily is LmStudioModelFamily.Gemma or LmStudioModelFamily.Gemma4E4B)
        {
            builder.AppendLine("Short-label accuracy rules for Gemma:");
            builder.AppendLine("Translate single words, short noun phrases, stat labels, item names, trait names, and glossary-like fragments as concise dictionary-style terms, not as sentences.");
            builder.AppendLine("Prefer the most specific in-game equivalent for the source term. Do not broaden, soften, summarize, or explain it.");
            builder.AppendLine("If no reliable semantic translation is available, prefer a stable Hangul transliteration over an explanatory paraphrase.");
            builder.AppendLine("When short labels are separated by ／, |, /, or similar delimiters, translate each label independently and preserve the separator structure.");
            builder.AppendLine("Do not output context disclaimers such as \"depending on context\" or attach multiple candidate terms.");
            if (modelFamily == LmStudioModelFamily.Gemma4E4B)
            {
                builder.AppendLine("For Gemma 4 E4B, prioritize term precision over stylistic variation when translating short glossary-like entries.");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildJsonFormatInstruction(IReadOnlyList<ProtectedSegment> requests)
    {
        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine("Output format rules:");
        builder.AppendLine("1. Return exactly one JSON object.");
        builder.AppendLine("2. Do not output markdown, code fences, prose, comments, XML, or any text before or after the JSON.");
        builder.AppendLine("3. The root schema must be: {\"translations\":[{\"id\":\"...\",\"translated\":\"...\"}]}.");
        builder.AppendLine("4. Keep every id unchanged.");
        builder.AppendLine("5. Return exactly one translated item for every input item.");
        builder.AppendLine("6. The translated field must contain only the final translated text itself.");
        builder.AppendLine("7. Escape JSON strings correctly with double quotes.");
        if (requests.Count == 1)
        {
            builder.AppendLine($"8. There is exactly one input item. In the output JSON, use id \"{requests[0].Id}\" for the single translation item.");
        }
        else
        {
            builder.AppendLine("8. The user message uses repeated |id| blocks. Read the text inside each block and return one JSON item per id.");
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildTokenizedFormatInstruction(
        IReadOnlyList<ProtectedSegment> requests,
        string targetLanguageLabel)
    {
        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine("Output format rules:");
        builder.AppendLine("1. Do not return JSON.");
        builder.AppendLine("2. Do not return markdown, code fences, prose, comments, or extra explanations.");
        builder.AppendLine("3. Keep each segment id unchanged.");
        if (requests.Count == 1)
        {
            builder.AppendLine("4. There is exactly one input segment. Return only the translated text itself with no label, no separator, and no extra line.");
        }
        else
        {
            builder.AppendLine("4. Return exactly one output block for every input block.");
            builder.AppendLine("5. For multiple input segments, use this exact format only:");
            builder.AppendLine("|<id>|");
            builder.AppendLine("<translated text>");
            builder.AppendLine("|");
            builder.AppendLine("6. Do not write any text before the first output or after the last output.");
        }

        var finalRuleNumber = requests.Count == 1 ? 5 : 7;
        builder.AppendLine($"{finalRuleNumber}. The translated text itself must be written in {targetLanguageLabel}. Do not answer in English unless the target language is English.");
        return builder.ToString().TrimEnd();
    }

    private static string BuildGlossaryInstruction(IReadOnlyList<GlossaryHint>? glossaryHints, PromptProfile promptProfile)
    {
        if (glossaryHints is null || glossaryHints.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine();
        if (promptProfile == PromptProfile.HyMt2)
        {
            builder.AppendLine("Reference the following translations:");
            builder.AppendLine("If a source entry exactly matches the current item, use the referenced target wording exactly.");
            builder.AppendLine("If multiple source entries overlap, prefer the longest exact source match.");
            foreach (var hint in glossaryHints)
            {
                builder.AppendLine($"`{hint.Source}` translates to `{hint.Target}`");
            }
        }
        else
        {
            builder.AppendLine("Glossary hints:");
            builder.AppendLine("Prefer these pairs when they fit the current script context.");
            builder.AppendLine("If a glossary source exactly matches the full input item, use that glossary target exactly.");
            builder.AppendLine("If multiple glossary hints overlap, prefer the longest exact source match.");
            builder.AppendLine("Do not copy them mechanically if they would make the line unnatural.");
            foreach (var hint in glossaryHints)
            {
                builder.AppendLine($"{hint.Source} => {hint.Target}");
            }
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
        builder.AppendLine($"Target language: {GetPromptLanguageLabel(settings.TargetLanguage, ResolvePromptProfile(settings, settings.Model))}");
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
                $"Target language: {GetPromptLanguageLabel(settings.TargetLanguage, ResolvePromptProfile(settings, settings.Model))}" + Environment.NewLine +
                "Return only the translated text with no label or separator." + Environment.NewLine +
                requests[0].Text;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Target language: {GetPromptLanguageLabel(settings.TargetLanguage, ResolvePromptProfile(settings, settings.Model))}");
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

    private static PromptProfile ResolvePromptProfile(ProviderSettings settings, string? model)
    {
        if (settings.PromptProfile != PromptProfile.Auto)
        {
            return settings.PromptProfile;
        }

        if (model?.Contains("gemma-4-e4b", StringComparison.OrdinalIgnoreCase) == true)
        {
            return PromptProfile.Gemma4E4B;
        }

        return model?.Contains("hy-mt2", StringComparison.OrdinalIgnoreCase) == true
            ? PromptProfile.HyMt2
            : PromptProfile.Generic;
    }

    private static string GetPromptLanguageLabel(string language, PromptProfile promptProfile)
    {
        if (promptProfile != PromptProfile.HyMt2)
        {
            return LanguageDisplayService.ToInstructionLabel(language);
        }

        return (language ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "ja" or "jp" => "Japanese",
            "ko" => "Korean",
            "en" => "English",
            _ => LanguageDisplayService.ToInstructionLabel(language ?? string.Empty),
        };
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
