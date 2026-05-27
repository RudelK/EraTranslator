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
    public void TranslatedSymbolKey_RemovesSpacesForCsvReferenceBearingKeyWhenWhitespaceIsNotPreserved()
    {
        var item = new ExtractedTextItem
        {
            SegmentId = "doc:1",
            DocumentId = "doc",
            FileType = "CSV",
            RelativePath = "Chara1.csv",
            EncodingName = "utf-8",
            SegmentType = "csv-CharacterSheet-field-1",
            LineNumber = 1,
            OriginalText = "彼氏姓",
            CsvFieldRole = CsvFieldRole.MetaKey,
            PreserveWhitespace = false,
            SymbolNamespace = "CSTR",
            OriginalSymbolKey = "彼氏姓",
            IsReferenceBearingKey = true,
        };

        item.TranslatedText = "남자친구 성씨";

        Assert.Equal("남자친구성씨", item.TranslatedSymbolKey);
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

        Assert.Equal("검수 필요", item.Status);
        Assert.Equal("검증 실패", item.ValidationStatus);
        Assert.False(item.CanSave);
    }

    [Fact]
    public void ApplyManualStatusOverride_ExcludedStateRemainsSaveableAndClearsTranslation()
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
            TranslatedText = "번역",
            Status = "번역 완료",
            ValidationStatus = "통과",
        };

        item.ApplyManualStatusOverride("제외됨");

        Assert.Equal("제외됨", item.Status);
        Assert.Equal("수동 제외", item.ValidationStatus);
        Assert.True(item.CanSave);
        Assert.Equal(string.Empty, item.TranslatedText);
    }

    [Fact]
    public void ApplyManualStatusOverride_RaisesNamedStatePropertyChanges()
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
        var changedProperties = new List<string?>();
        item.PropertyChanged += (_, eventArgs) => changedProperties.Add(eventArgs.PropertyName);

        item.ApplyManualStatusOverride("번역 완료");

        Assert.Contains(nameof(ExtractedTextItem.Status), changedProperties);
        Assert.Contains(nameof(ExtractedTextItem.ValidationStatus), changedProperties);
        Assert.Contains(nameof(ExtractedTextItem.CanSave), changedProperties);
        Assert.Contains(nameof(ExtractedTextItem.EditableStatus), changedProperties);
        Assert.Contains(nameof(ExtractedTextItem.StateText), changedProperties);
        Assert.Contains(nameof(ExtractedTextItem.HasPersistableState), changedProperties);
        Assert.DoesNotContain(string.Empty, changedProperties);
    }

    [Fact]
    public void NeedsTranslation_IsTrueForPendingFailedOrStoppedStatesOnly()
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

        Assert.True(item.NeedsTranslation);

        item.ApplyTranslationState("번역 실패", "HTTP 500", "server error", false);
        Assert.True(item.NeedsTranslation);

        item.ApplyTranslationState("중지됨", "검증 전", "stopped", false);
        Assert.True(item.NeedsTranslation);

        item.ApplyTranslationState("검수 필요", "토큰 손실", "review", false, "번역");
        Assert.False(item.NeedsTranslation);

        item.ApplyTranslationState("검수 필요", "통과", "review", true, "번역");
        Assert.False(item.NeedsTranslation);
    }
}
