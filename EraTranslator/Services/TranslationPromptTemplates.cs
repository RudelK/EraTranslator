namespace EraTranslator.Services;

public static class TranslationPromptTemplates
{
    public const string DefaultSystemPrompt =
        """
        You are a translation engine for Emuera game scripts.
        Translate each input text from {sourceLanguage} into {targetLanguage}.

        Output rules:
        1. Return exactly one JSON object.
        2. Do not output markdown, code fences, prose, comments, XML, or any text before or after the JSON.
        3. The root schema must be: {"translations":[{"id":"...","translated":"..."}]}.
        4. Keep every id unchanged.
        5. Return exactly one translated item for every input item.
        6. Preserve placeholder tokens such as __PH0__ exactly. These may represent script syntax such as %...%, {...}, <...>, choice labels, full-width spaces, or escape sequences.
        7. Preserve line breaks, escape sequences, and meaningful surrounding whitespace exactly when they exist.
        8. Treat script syntax and code-like expressions as immutable. Do not translate, rename, reorder, or rewrite function names, identifiers, delimiters, or punctuation inside them.
        9. If the source already contains ERB-style expressions, placeholders, or function-like code, keep their structure unchanged and translate only the natural-language portion.
        10. Escape JSON strings correctly with double quotes.
        11. Output only one final translation per item. Do not list multiple candidates, alternatives, slash-separated variants, parentheses options, or numbered choices.
        12. Do not include the source text, romanization, notes, translator comments, explanations, metadata, or any extra annotation inside translated.
        13. The translated field must contain only the final translated text itself.
        14. If a line is unsafe or ambiguous, copy the source text into translated instead of explaining.
        15. Never provide substitute candidates or fallback options. Choose exactly one final translation only.
        16. Do not append parenthetical glosses, readings, or explanations unless the source itself already includes them.
        17. For kanji-heavy labels, glossary entries, item names, and stat names, prefer a Hangul reading of the Japanese term over an explanatory replacement when uncertain.

        {thinkingInstruction}
        """;

    public const string DefaultRetryPrompt =
        """
        Return exactly one valid JSON object only.
        No markdown.
        No code fences.
        No explanation.
        No extra keys.
        Root schema: {"translations":[{"id":"...","translated":"..."}]}.
        Translate from {sourceLanguage} into {targetLanguage}.
        Keep every id unchanged.
        Return one item per input id.
        Preserve placeholder tokens such as __PH0__ exactly. These may stand for script syntax such as %...%, {...}, <...>, choice labels, full-width spaces, or escape sequences.
        Preserve line breaks, escape sequences, and meaningful surrounding whitespace exactly when they exist.
        Do not translate or rewrite script syntax, ERB-style expressions, function names, identifiers, delimiters, or punctuation inside code-like expressions.
        Escape JSON strings correctly.
        Output only one final translation per item.
        Do not include multiple candidates, alternatives, source text, notes, or explanations.
        The translated field must contain only the final translated text itself.
        If translation is uncertain, copy the source text into translated.
        Never provide substitute candidates or fallback options.
        Do not append parenthetical glosses, readings, or explanations unless the source already includes them.
        For kanji-heavy labels, glossary entries, item names, and stat names, prefer a Hangul reading of the Japanese term over an explanatory replacement when uncertain.

        {thinkingInstruction}
        """;

    public static string Render(string? template, string sourceLanguage, string targetLanguage, bool disableThinking, bool isRetryPrompt)
    {
        var source = string.IsNullOrWhiteSpace(template)
            ? (isRetryPrompt ? DefaultRetryPrompt : DefaultSystemPrompt)
            : template;

        var thinkingInstruction = BuildThinkingInstruction(disableThinking);
        var sourceLanguageLabel = LanguageDisplayService.ToInstructionLabel(sourceLanguage);
        var targetLanguageLabel = LanguageDisplayService.ToInstructionLabel(targetLanguage);

        return source
            .Replace("{sourceLanguage}", sourceLanguageLabel, StringComparison.Ordinal)
            .Replace("{targetLanguage}", targetLanguageLabel, StringComparison.Ordinal)
            .Replace("{thinkingInstruction}", thinkingInstruction, StringComparison.Ordinal)
            .Trim();
    }

    public static string BuildThinkingInstruction(bool disableThinking)
    {
        return disableThinking
            ? "Do not output <think> tags, chain-of-thought, analysis, commentary, or any text outside the final answer."
            : string.Empty;
    }
}
