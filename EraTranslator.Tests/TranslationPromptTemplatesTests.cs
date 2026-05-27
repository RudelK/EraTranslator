using EraTranslator.Services;

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
    public void Render_IncludesCurrentScriptSyntaxGuidance()
    {
        var rendered = TranslationPromptTemplates.Render(
            TranslationPromptTemplates.DefaultRetryPrompt,
            "ja",
            "ko",
            disableThinking: true,
            isRetryPrompt: true);

        Assert.Contains("script syntax such as %...%, {...}, <...>", rendered, StringComparison.Ordinal);
        Assert.Contains("Do not translate or rewrite script syntax", rendered, StringComparison.Ordinal);
    }
}
