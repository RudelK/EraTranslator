using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class SourceLanguageFilterServiceTests
{
    [Theory]
    [InlineData("こんにちは", "ja", true)]
    [InlineData("안녕하세요", "ja", false)]
    [InlineData("デフォ子", "ja", true)]
    [InlineData("Hello world", "en", true)]
    [InlineData("12345", "ja", false)]
    public void Heuristics_DetectsLikelySourceLanguage(string text, string language, bool expected)
    {
        Assert.Equal(expected, SourceLanguageHeuristics.IsLikelySourceText(text, language));
    }

    [Fact]
    public void Apply_MarksNonSourcePendingItemsAsExcluded_AndRestoresWhenDisabled()
    {
        var item = new ExtractedTextItem
        {
            SegmentId = "seg-1",
            DocumentId = "ERB/Test.ERB",
            FileType = "ERB",
            RelativePath = "ERB/Test.ERB",
            EncodingName = "UTF-8",
            SegmentType = "print",
            LineNumber = 1,
            OriginalText = "안녕하세요",
            WarningText = string.Empty,
        };

        var service = new SourceLanguageFilterService();
        var changedCount = service.Apply([item], "ja", enabled: true);

        Assert.Equal(1, changedCount);
        Assert.Equal("제외됨", item.Status);
        Assert.Equal("언어 제외", item.ValidationStatus);
        Assert.False(item.NeedsTranslation);

        changedCount = service.Apply([item], "ja", enabled: false);

        Assert.Equal(1, changedCount);
        Assert.Equal("대기", item.Status);
        Assert.Equal("검증 전", item.ValidationStatus);
        Assert.True(item.NeedsTranslation);
    }
}
