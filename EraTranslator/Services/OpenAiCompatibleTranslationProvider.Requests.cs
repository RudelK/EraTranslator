using System.Text;
using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed partial class OpenAiCompatibleTranslationProvider
{
    private static object BuildRequestPayload(
        string model,
        ProviderSettings settings,
        IReadOnlyList<ProtectedSegment> requests,
        bool includeJsonResponseFormat,
        bool useTokenizedProtocol,
        bool retryMode)
    {
        var systemPrompt = BuildSystemPrompt(settings, retryMode, useTokenizedProtocol, requests);

        return includeJsonResponseFormat
            ? new
            {
                model,
                temperature = retryMode ? 0 : settings.Temperature,
                response_format = new
                {
                    type = "json_object",
                },
                messages = BuildMessages(systemPrompt, requests, settings, useTokenizedProtocol: false),
            }
            : new
            {
                model,
                temperature = retryMode ? 0 : settings.Temperature,
                messages = BuildMessages(systemPrompt, requests, settings, useTokenizedProtocol),
            };
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
        bool retryMode,
        bool useTokenizedProtocol,
        IReadOnlyList<ProtectedSegment> requests)
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

        if (!useTokenizedProtocol)
        {
            var rendered = TranslationPromptTemplates.Render(
                retryMode ? settings.RetryPromptTemplate : settings.SystemPromptTemplate,
                settings.SourceLanguage,
                settings.TargetLanguage,
                settings.DisableThinking,
                retryMode);

            if (requests.Count == 1)
            {
                return rendered
                    + placeholderInstruction
                    + scriptSyntaxInstruction
                    + termStyleInstruction
                    + Environment.NewLine
                    + $"There is exactly one input item. In the output JSON, use id \"{requests[0].Id}\" for the single translation item.";
            }

            return rendered
                + placeholderInstruction
                + scriptSyntaxInstruction
                + termStyleInstruction
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
            Environment.NewLine + Environment.NewLine +
            TranslationPromptTemplates.BuildThinkingInstruction(settings.DisableThinking);
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
}
