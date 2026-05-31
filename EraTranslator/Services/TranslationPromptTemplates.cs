using EraTranslator.Models;

namespace EraTranslator.Services;

public static class TranslationPromptTemplates
{
    public const string Gemma4E4BSystemPrompt =
        """
        You are a precision translation engine for Emuera game scripts.
        Translate each input text from {sourceLanguage} into {targetLanguage}.

        Translation rules:
        1. Treat short labels, glossary entries, item names, skill names, stat names, trait names, and body-part terms as dictionary-style entries.
        2. For short entries, choose the single most specific in-game term. Do not turn labels into sentences or explanations.
        3. Prefer stable Korean in-game wording. If semantic translation is uncertain, use a concise Hangul transliteration instead of an explanatory paraphrase.
        4. Preserve tone, honorific level, and relationship cues whenever the source implies them.
        5. Keep the translated wording compact and faithful to the source.
        6. Choose exactly one final translation per item.
        7. Do not provide alternatives, notes, commentary, parenthetical glosses, or fallback options unless the source already contains them.
        8. If a line is unsafe or ambiguous, copy the source text instead of explaining.

        Examples:
        Source: 候補割合
        Target: 후보 비율

        Source: もちもち
        Target: 모찌모찌

        Source: 快楽
        Target: 쾌락

        Source: __PH0__を選ぶ
        Target: __PH0__를 선택한다

        {thinkingInstruction}
        """;

    public const string Gemma4E4BRetryPrompt =
        """
        Translate each input text from {sourceLanguage} into {targetLanguage}.
        Prioritize exact short-label, glossary, and term-level accuracy.
        Return only the final translation content required by the format rules.
        If the source looks like a short label, output a compact dictionary-style term rather than an explanation.
        Choose exactly one final translation per item.
        Do not provide alternatives, notes, or explanations.
        If a line is unsafe or ambiguous, copy the source text instead of explaining.

        {thinkingInstruction}
        """;

    public const string HyMt2SystemPrompt =
        """
        Translate the following text from {sourceLanguage} into {targetLanguage}.
        Note that you should only output the translated result without any additional explanation.

        Translation rules:
        1. Preserve placeholders, script syntax, and code-like expressions exactly.
        2. Keep the translated wording compact and faithful to the source.
        3. Prefer consistent translations for repeated terms, names, and labels.
        4. Do not provide alternatives, notes, commentary, or extra annotation.
        5. If the text is ambiguous, choose the single most likely final translation.

        {thinkingInstruction}
        """;

    public const string HyMt2RetryPrompt =
        """
        Translate the following text from {sourceLanguage} into {targetLanguage}.
        You must ONLY output the translated result without any additional explanation.
        Preserve placeholders, script syntax, and code-like expressions exactly.

        {thinkingInstruction}
        """;

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
        LmStudioThinkingControlMode thinkingControlMode = LmStudioThinkingControlMode.PromptFallback,
        PromptProfile promptProfile = PromptProfile.Generic)
    {
        var source = string.IsNullOrWhiteSpace(template)
            ? GetDefaultTemplate(promptProfile, isRetryPrompt)
            : template;

        var thinkingInstruction = BuildThinkingInstruction(disableThinking, thinkingControlMode);
        var sourceLanguageLabel = GetPromptLanguageLabel(sourceLanguage, promptProfile);
        var targetLanguageLabel = GetPromptLanguageLabel(targetLanguage, promptProfile);

        return source
            .Replace("{sourceLanguage}", sourceLanguageLabel, StringComparison.Ordinal)
            .Replace("{targetLanguage}", targetLanguageLabel, StringComparison.Ordinal)
            .Replace("{thinkingInstruction}", thinkingInstruction, StringComparison.Ordinal)
            .Trim();
    }

    internal static string GetDefaultTemplate(PromptProfile promptProfile, bool isRetryPrompt)
    {
        return promptProfile switch
        {
            PromptProfile.Gemma4E4B => isRetryPrompt ? Gemma4E4BRetryPrompt : Gemma4E4BSystemPrompt,
            PromptProfile.HyMt2 => isRetryPrompt ? HyMt2RetryPrompt : HyMt2SystemPrompt,
            _ => isRetryPrompt ? DefaultRetryPrompt : DefaultSystemPrompt,
        };
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
            "zh" or "zh-cn" or "zh-tw" => "Chinese",
            _ => LanguageDisplayService.ToInstructionLabel(language ?? string.Empty),
        };
    }
}
