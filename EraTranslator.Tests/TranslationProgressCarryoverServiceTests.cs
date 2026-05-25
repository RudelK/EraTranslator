using System.Text;
using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class TranslationProgressCarryoverServiceTests
{
    private readonly TranslationProgressCarryoverService _service = new();

    [Fact]
    public void Apply_DoesNotUseSegmentIdOnlyWhenOriginalTextChanged()
    {
        var previousSession = BuildSession(BuildItem("ERB/Test.ERB:0", "ERB/Test.ERB", 1, "이전 원문"));
        var previousProgress = BuildProgressState(BuildState("ERB/Test.ERB:0", "번역 완료", "기존 번역"));
        var currentItems = new List<ExtractedTextItem>
        {
            BuildItem("ERB/Test.ERB:0", "ERB/Test.ERB", 1, "새 원문"),
        };

        var result = _service.Apply(previousSession, previousProgress, currentItems);

        Assert.Equal(0, result.ExactRestoredCount);
        Assert.Equal(0, result.HeuristicRestoredCount);
        Assert.Equal("대기", currentItems[0].Status);
        Assert.Equal(string.Empty, currentItems[0].TranslatedText);
    }

    [Fact]
    public void Apply_RestoresShiftedErbItemsByOriginalTextOccurrence()
    {
        var previousSession = BuildSession(
            BuildItem("ERB/Test.ERB:0", "ERB/Test.ERB", 1, "안녕"),
            BuildItem("ERB/Test.ERB:1", "ERB/Test.ERB", 2, "세계"));
        var previousProgress = BuildProgressState(
            BuildState("ERB/Test.ERB:0", "번역 완료", "hello"),
            BuildState("ERB/Test.ERB:1", "번역 완료", "world"));
        var currentItems = new List<ExtractedTextItem>
        {
            BuildItem("ERB/Test.ERB:0", "ERB/Test.ERB", 1, "새 줄"),
            BuildItem("ERB/Test.ERB:1", "ERB/Test.ERB", 2, "안녕"),
            BuildItem("ERB/Test.ERB:2", "ERB/Test.ERB", 3, "세계"),
        };

        var result = _service.Apply(previousSession, previousProgress, currentItems);

        Assert.Equal(0, result.ExactRestoredCount);
        Assert.Equal(2, result.HeuristicRestoredCount);
        Assert.Equal(1, result.UnmatchedCount);
        Assert.Equal("검수 필요", currentItems[1].Status);
        Assert.Equal("hello", currentItems[1].TranslatedText);
        Assert.Equal("검수 필요", currentItems[2].Status);
        Assert.Equal("world", currentItems[2].TranslatedText);
        Assert.Equal("대기", currentItems[0].Status);
    }

    [Fact]
    public void Apply_RestoresCsvItemsBySourceKeyWhenRowsMove()
    {
        var previousSession = BuildSession(
            BuildItem("CSV/GameBase.csv:0", "CSV/GameBase.csv", 1, "未設定", fileType: "CSV", sourceKey: "NAME_A", fieldIndex: 1),
            BuildItem("CSV/GameBase.csv:1", "CSV/GameBase.csv", 2, "未設定", fileType: "CSV", sourceKey: "NAME_B", fieldIndex: 1));
        var previousProgress = BuildProgressState(
            BuildState("CSV/GameBase.csv:0", "번역 완료", "설정 A"),
            BuildState("CSV/GameBase.csv:1", "번역 완료", "설정 B"));
        var currentItems = new List<ExtractedTextItem>
        {
            BuildItem("CSV/GameBase.csv:0", "CSV/GameBase.csv", 1, "未設定", fileType: "CSV", sourceKey: "NAME_B", fieldIndex: 1),
            BuildItem("CSV/GameBase.csv:1", "CSV/GameBase.csv", 2, "未設定", fileType: "CSV", sourceKey: "NAME_A", fieldIndex: 1),
        };

        var result = _service.Apply(previousSession, previousProgress, currentItems);

        Assert.Equal(0, result.ExactRestoredCount);
        Assert.Equal(2, result.HeuristicRestoredCount);
        Assert.Equal("설정 B", currentItems[0].TranslatedText);
        Assert.Equal("설정 A", currentItems[1].TranslatedText);
    }

    [Fact]
    public void Apply_LeavesAmbiguousStrongKeyItemsUnmatched()
    {
        var previousSession = BuildSession(
            BuildItem("CSV/GameBase.csv:0", "CSV/GameBase.csv", 1, "未設定", fileType: "CSV", sourceKey: "DUP_KEY", fieldIndex: 1),
            BuildItem("CSV/GameBase.csv:1", "CSV/GameBase.csv", 2, "未設定", fileType: "CSV", sourceKey: "DUP_KEY", fieldIndex: 1));
        var previousProgress = BuildProgressState(
            BuildState("CSV/GameBase.csv:0", "번역 완료", "설정 A"),
            BuildState("CSV/GameBase.csv:1", "번역 완료", "설정 B"));
        var currentItems = new List<ExtractedTextItem>
        {
            BuildItem("CSV/GameBase.csv:5", "CSV/GameBase.csv", 5, "未設定", fileType: "CSV", sourceKey: "DUP_KEY", fieldIndex: 1),
        };

        var result = _service.Apply(previousSession, previousProgress, currentItems);

        Assert.Equal(0, result.ExactRestoredCount);
        Assert.Equal(0, result.HeuristicRestoredCount);
        Assert.Equal(1, result.UnmatchedCount);
        Assert.Equal("대기", currentItems[0].Status);
        Assert.Equal(string.Empty, currentItems[0].TranslatedText);
    }

    private static ScanSession BuildSession(params ExtractedTextItem[] items)
    {
        var session = new ScanSession
        {
            GameRoot = @"D:\Game",
        };

        foreach (var item in items)
        {
            session.Items.Add(item);
        }

        return session;
    }

    private static TranslationProgressState BuildProgressState(params TranslationProgressItemState[] items)
    {
        return new TranslationProgressState
        {
            Items = items.ToList(),
        };
    }

    private static TranslationProgressItemState BuildState(string segmentId, string status, string translatedText)
    {
        return new TranslationProgressItemState
        {
            SegmentId = segmentId,
            Status = status,
            ValidationStatus = "통과",
            TranslationError = string.Empty,
            TranslatedText = translatedText,
            CanSave = true,
        };
    }

    private static ExtractedTextItem BuildItem(
        string segmentId,
        string relativePath,
        int lineNumber,
        string originalText,
        string fileType = "ERB",
        string? sourceKey = null,
        int? fieldIndex = null)
    {
        return new ExtractedTextItem
        {
            SegmentId = segmentId,
            DocumentId = relativePath.Replace('\\', '/'),
            FileType = fileType,
            RelativePath = relativePath,
            EncodingName = "UTF-8 BOM",
            SegmentType = fileType == "CSV" ? "csv-field" : "quoted-string",
            LineNumber = lineNumber,
            OriginalText = originalText,
            SourceKey = sourceKey,
            FieldIndex = fieldIndex,
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            WarningText = string.Empty,
        };
    }
}
