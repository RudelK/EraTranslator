using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class LanguageDisplayServiceTests
{
    [Theory]
    [InlineData("ko", "Korean (ko, 한국어)")]
    [InlineData("ja", "Japanese (ja, 日本語)")]
    [InlineData("en", "English (en)")]
    [InlineData("custom-language", "custom-language")]
    public void ToInstructionLabel_ReturnsReadableLanguageLabel(string input, string expected)
    {
        Assert.Equal(expected, LanguageDisplayService.ToInstructionLabel(input));
    }
}
