namespace EraTranslator.Services;

public static class TranslationPromptTemplates
{
    public const string DefaultSystemPrompt =
        """
        You are a translation engine for Emuera game scripts.
        Translate each input text from {sourceLanguage} into {targetLanguage}.

        Translation rules:
        1. Preserve tone, honorific level, and relationship cues whenever the source implies them.
        2. Prefer consistent translations for repeated proper nouns, skill names, item names, and stat labels.
        3. Keep the translated wording as compact as the source. Do not expand short labels into long explanations.
        4. Choose exactly one final translation per item.
        5. Do not provide alternatives, substitute candidates, numbered choices, or fallback options.
        6. Do not append parenthetical glosses, readings, or explanations unless the source itself already includes them.
        7. If a line is unsafe or ambiguous, copy the source text instead of explaining.

        Examples:
        Source: ご主人さま
        Target: 주인님

        Source: 快楽
        Target: 쾌락

        Source: __PH0__を選ぶ
        Target: __PH0__를 선택한다

        {thinkingInstruction}
        """;

    public const string DefaultRetryPrompt =
        """
        Translate each input text from {sourceLanguage} into {targetLanguage}.
        Follow the same translation rules as before.
        Return only the final translation content required by the format rules.
        Choose exactly one final translation per item.
        Do not provide alternatives, notes, or explanations.
        If a line is unsafe or ambiguous, copy the source text instead of explaining.

        {thinkingInstruction}
        """;

    internal static string Render(
        string? template,
        string sourceLanguage,
        string targetLanguage,
        bool disableThinking,
        bool isRetryPrompt,
        LmStudioThinkingControlMode thinkingControlMode = LmStudioThinkingControlMode.PromptFallback)
    {
        var source = string.IsNullOrWhiteSpace(template)
            ? (isRetryPrompt ? DefaultRetryPrompt : DefaultSystemPrompt)
            : template;

        var thinkingInstruction = BuildThinkingInstruction(disableThinking, thinkingControlMode);
        var sourceLanguageLabel = LanguageDisplayService.ToInstructionLabel(sourceLanguage);
        var targetLanguageLabel = LanguageDisplayService.ToInstructionLabel(targetLanguage);

        return source
            .Replace("{sourceLanguage}", sourceLanguageLabel, StringComparison.Ordinal)
            .Replace("{targetLanguage}", targetLanguageLabel, StringComparison.Ordinal)
            .Replace("{thinkingInstruction}", thinkingInstruction, StringComparison.Ordinal)
            .Trim();
    }

    internal static string BuildThinkingInstruction(
        bool disableThinking,
        LmStudioThinkingControlMode thinkingControlMode = LmStudioThinkingControlMode.PromptFallback)
    {
        if (!disableThinking)
        {
            return string.Empty;
        }

        return thinkingControlMode == LmStudioThinkingControlMode.ApiCustomField
            ? "Return the final answer directly. Do not output <think> tags, chain-of-thought, analysis, commentary, or any text outside the final answer."
            : "Do not output <think> tags, chain-of-thought, analysis, commentary, or any text outside the final answer.";
    }
}
