using EraTranslator.Services;
using EraTranslator.Models;

namespace EraTranslator.Tests;

public sealed class TranslationPromptTemplatesTests
{
    [Fact]
    public void Render_UsesConfiguredSourceAndTargetLanguages()
    {
        var rendered = TranslationPromptTemplates.Render(
            TranslationPromptTemplates.DefaultSystemPrompt,
            "ja",
            "ko",
            disableThinking: true,
            isRetryPrompt: false);

        Assert.Contains("from Japanese (ja, 日本語) into Korean (ko, 한국어)", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_RetryPromptIncludesCoreMeaningRules()
    {
        var rendered = TranslationPromptTemplates.Render(
            TranslationPromptTemplates.DefaultRetryPrompt,
            "ja",
            "ko",
            disableThinking: true,
            isRetryPrompt: true);

        Assert.Contains("Return only the final translation content required by the format rules.", rendered, StringComparison.Ordinal);
        Assert.Contains("Choose exactly one final translation per item.", rendered, StringComparison.Ordinal);
        Assert.Contains("Do not provide alternatives, notes, or explanations.", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_DefaultSystemPrompt_IncludesMeaningFocusedExamples()
    {
        var rendered = TranslationPromptTemplates.Render(
            TranslationPromptTemplates.DefaultSystemPrompt,
            "ja",
            "ko",
            disableThinking: true,
            isRetryPrompt: false);

        Assert.Contains("Preserve tone, honorific level, and relationship cues whenever the source implies them.", rendered, StringComparison.Ordinal);
        Assert.Contains("Source: ご主人さま", rendered, StringComparison.Ordinal);
        Assert.Contains("Target: __PH0__를 선택한다", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_HyMt2Prompt_UsesFullLanguageNames()
    {
        var rendered = TranslationPromptTemplates.Render(
            null,
            "ja",
            "ko",
            disableThinking: true,
            isRetryPrompt: false,
            promptProfile: PromptProfile.HyMt2);

        Assert.Contains("from Japanese into Korean", rendered, StringComparison.Ordinal);
        Assert.Contains("only output the translated result without any additional explanation", rendered, StringComparison.Ordinal);
    }
}
