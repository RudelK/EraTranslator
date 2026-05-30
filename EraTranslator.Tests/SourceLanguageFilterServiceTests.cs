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
    [InlineData("%CALLNAME:MASTER%の町", "ja", true)]
    [InlineData("{TARGET}に話しかける", "ja", true)]
    [InlineData("AV嬢", "ja", true)]
    [InlineData("[巨乳]×0.7（ALL）", "ja", true)]
    [InlineData("上位陥落済;IS_UPPER_FALLEN(index)", "ja", true)]
    [InlineData("引き継ぎ;TALENT:index:引き継ぎ", "ja", true)]
    public void Heuristics_DetectsLikelySourceLanguage(string text, string language, bool expected)
    {
        Assert.Equal(expected, SourceLanguageHeuristics.IsLikelySourceText(text, language));
    }

    [Fact]
    public void Apply_MarksNonSourcePendingItemsAsExcluded_AndRestoresWhenDisabled()
    {
        var item = BuildItem("seg-1", "안녕하세요");

        var service = new SourceLanguageFilterService();
        var changedCount = service.Apply([item], "ja", "en", enabled: true);

        Assert.Equal(1, changedCount);
        Assert.Equal("제외됨", item.Status);
        Assert.Equal("언어 제외", item.ValidationStatus);
        Assert.False(item.NeedsTranslation);

        changedCount = service.Apply([item], "ja", "en", enabled: false);

        Assert.Equal(1, changedCount);
        Assert.Equal("번역 대기", item.Status);
        Assert.Equal("검증 전", item.ValidationStatus);
        Assert.True(item.NeedsTranslation);
    }

    [Fact]
    public void Apply_DoesNotExcludePlaceholderLedJapaneseText()
    {
        var item = BuildItem("seg-2", "%CALLNAME:MASTER%の町");

        var service = new SourceLanguageFilterService();
        var changedCount = service.Apply([item], "ja", "ko", enabled: true);

        Assert.Equal(0, changedCount);
        Assert.Equal("번역 대기", item.Status);
        Assert.Equal("검증 전", item.ValidationStatus);
        Assert.True(item.NeedsTranslation);
    }

    [Fact]
    public void Apply_DoesNotRestoreManuallyExcludedItems()
    {
        var item = BuildItem("seg-3", "こんにちは");
        item.ApplyManualStatusOverride("제외됨");

        var service = new SourceLanguageFilterService();
        var changedCount = service.Apply([item], "ja", "ko", enabled: false);

        Assert.Equal(0, changedCount);
        Assert.Equal("제외됨", item.Status);
        Assert.Equal("수동 제외", item.ValidationStatus);
        Assert.False(item.NeedsTranslation);
    }

    [Fact]
    public void Apply_ReusesOriginalWhenTextIsEntirelyTargetLanguage()
    {
        var item = BuildItem("seg-4", "안녕하세요");

        var service = new SourceLanguageFilterService();
        var changedCount = service.Apply([item], "ja", "ko", enabled: true);

        Assert.Equal(1, changedCount);
        Assert.Equal("번역 완료", item.Status);
        Assert.Equal("통과", item.ValidationStatus);
        Assert.Equal("안녕하세요", item.TranslatedText);
        Assert.True(item.CanSave);
        Assert.False(item.NeedsTranslation);
    }

    [Fact]
    public void Apply_ReusesOriginalWithSameNormalizationRules()
    {
        var item = BuildItem("seg-5", "테 스트", fileType: "CSV");

        var service = new SourceLanguageFilterService();
        var changedCount = service.Apply([item], "ja", "ko", enabled: true);

        Assert.Equal(1, changedCount);
        Assert.Equal("번역 완료", item.Status);
        Assert.Equal("테스트", item.TranslatedText);
    }

    [Fact]
    public void Apply_DoesNotReuseOriginalWhenFeatureDisabled()
    {
        var item = BuildItem("seg-6", "안녕하세요");

        var service = new SourceLanguageFilterService();
        var changedCount = service.Apply([item], "ja", "ko", enabled: false);

        Assert.Equal(0, changedCount);
        Assert.Equal("번역 대기", item.Status);
        Assert.Equal(string.Empty, item.TranslatedText);
        Assert.True(item.NeedsTranslation);
    }

    private static ExtractedTextItem BuildItem(string segmentId, string originalText, string fileType = "ERB")
    {
        return new ExtractedTextItem
        {
            SegmentId = segmentId,
            DocumentId = $"{fileType}/Test.{fileType.ToLowerInvariant()}",
            FileType = fileType,
            RelativePath = $"{fileType}/Test.{fileType.ToLowerInvariant()}",
            EncodingName = "UTF-8",
            SegmentType = "print",
            LineNumber = 1,
            OriginalText = originalText,
            WarningText = string.Empty,
        };
    }
}
