using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class TranslationProgressStateServiceTests : IDisposable
{
    private readonly string _gameRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_gameRoot))
        {
            Directory.Delete(_gameRoot, recursive: true);
        }
    }

    [Fact]
    public void SaveAndApply_RestoresTranslatedAndFailedStates()
    {
        Directory.CreateDirectory(_gameRoot);
        var service = new TranslationProgressStateService();
        var items = new[]
        {
            new ExtractedTextItem
            {
                SegmentId = "doc:1",
                DocumentId = "doc",
                FileType = "ERB",
                RelativePath = "A.ERB",
                EncodingName = "utf-8",
                SegmentType = "PRINT",
                LineNumber = 1,
                OriginalText = "원문1",
                CsvFieldRole = CsvFieldRole.TranslatableValue,
            },
            new ExtractedTextItem
            {
                SegmentId = "doc:2",
                DocumentId = "doc",
                FileType = "ERB",
                RelativePath = "A.ERB",
                EncodingName = "utf-8",
                SegmentType = "PRINT",
                LineNumber = 2,
                OriginalText = "원문2",
                CsvFieldRole = CsvFieldRole.TranslatableValue,
            },
        };

        items[0].ApplyTranslationState("번역 완료", "통과", string.Empty, true, "translated");
        items[0].ReferenceImpactCount = 3;
        items[0].RequiresReferenceRewrite = true;
        items[0].ReferenceResolutionStatus = "간접 참조 있음";
        items[1].ApplyTranslationState("번역 실패", "HTTP 500", "server error", false);

        service.Save(_gameRoot, items);

        var freshItems = new[]
        {
            new ExtractedTextItem
            {
                SegmentId = "doc:1",
                DocumentId = "doc",
                FileType = "ERB",
                RelativePath = "A.ERB",
                EncodingName = "utf-8",
                SegmentType = "PRINT",
                LineNumber = 1,
                OriginalText = "원문1",
                CsvFieldRole = CsvFieldRole.TranslatableValue,
            },
            new ExtractedTextItem
            {
                SegmentId = "doc:2",
                DocumentId = "doc",
                FileType = "ERB",
                RelativePath = "A.ERB",
                EncodingName = "utf-8",
                SegmentType = "PRINT",
                LineNumber = 2,
                OriginalText = "원문2",
                CsvFieldRole = CsvFieldRole.TranslatableValue,
            },
        };

        var restoredCount = service.Apply(_gameRoot, freshItems);

        Assert.Equal(2, restoredCount);
        Assert.Equal("번역 완료", freshItems[0].Status);
        Assert.Equal("translated", freshItems[0].TranslatedText);
        Assert.Equal(3, freshItems[0].ReferenceImpactCount);
        Assert.True(freshItems[0].RequiresReferenceRewrite);
        Assert.Equal("번역 실패", freshItems[1].Status);
        Assert.Equal("server error", freshItems[1].TranslationError);
        Assert.Equal(Path.Combine(_gameRoot, ".era-translator", "translation-progress.json"), service.GetProgressFilePath(_gameRoot));
    }
}
