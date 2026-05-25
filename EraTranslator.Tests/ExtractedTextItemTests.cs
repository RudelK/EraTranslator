using EraTranslator.Models;

namespace EraTranslator.Tests;

public sealed class ExtractedTextItemTests
{
    [Fact]
    public void ApplyManualTranslationEdit_MarksItemAsManualAndSaveable()
    {
        var item = new ExtractedTextItem
        {
            SegmentId = "doc:1",
            DocumentId = "doc",
            FileType = "ERB",
            RelativePath = "A.ERB",
            EncodingName = "utf-8",
            SegmentType = "PRINT",
            LineNumber = 1,
            OriginalText = "こんにちは",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
        };

        item.TranslatedText = "안녕하세요";
        item.ApplyManualTranslationEdit();

        Assert.Equal("수동 수정", item.Status);
        Assert.Equal("통과", item.ValidationStatus);
        Assert.True(item.CanSave);
        Assert.True(item.IsTranslatedSuccessfully);
        Assert.False(item.NeedsTranslation);
    }

    [Fact]
    public void ApplyManualTranslationEdit_MarksLargeLengthDifferenceAsReviewNeeded()
    {
        var item = new ExtractedTextItem
        {
            SegmentId = "doc:1",
            DocumentId = "doc",
            FileType = "CSV",
            RelativePath = "A.CSV",
            EncodingName = "utf-8",
            SegmentType = "csv",
            LineNumber = 1,
            OriginalText = "股間札",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
        };

        item.TranslatedText = "가랑이 표식 또는 사타구니 표식";
        item.ApplyManualTranslationEdit();

        Assert.Equal("검수 필요", item.Status);
        Assert.Equal("통과", item.ValidationStatus);
        Assert.True(item.CanSave);
        Assert.True(item.IsTranslatedSuccessfully);
    }

    [Fact]
    public void ApplyManualStatusOverride_UpdatesValidationAndSaveState()
    {
        var item = new ExtractedTextItem
        {
            SegmentId = "doc:1",
            DocumentId = "doc",
            FileType = "ERB",
            RelativePath = "A.ERB",
            EncodingName = "utf-8",
            SegmentType = "PRINT",
            LineNumber = 1,
            OriginalText = "원문",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
        };

        item.TranslatedText = "번역";
        item.ApplyManualStatusOverride("번역 완료");

        Assert.Equal("번역 완료", item.Status);
        Assert.Equal("통과", item.ValidationStatus);
        Assert.True(item.CanSave);

        item.ApplyManualStatusOverride("검증 실패");

        Assert.Equal("검증 실패", item.Status);
        Assert.Equal("검증 실패", item.ValidationStatus);
        Assert.False(item.CanSave);
    }
}
