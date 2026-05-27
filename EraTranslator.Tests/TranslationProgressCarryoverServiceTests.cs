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
        Assert.Equal("번역 대기", currentItems[0].Status);
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
        Assert.Equal("번역 대기", currentItems[0].Status);
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
        Assert.Equal("번역 대기", currentItems[0].Status);
        Assert.Equal(string.Empty, currentItems[0].TranslatedText);
    }

    [Fact]
    public void Apply_ExactRestorePreservesCurrentReferenceAnalysis()
    {
        var previousSession = BuildSession(BuildItem("ERB/Test.ERB:0", "ERB/Test.ERB", 1, "같은 원문"));
        var previousState = BuildState("ERB/Test.ERB:0", "번역 완료", "기존 번역");
        previousState = new TranslationProgressItemState
        {
            SegmentId = previousState.SegmentId,
            Status = previousState.Status,
            ValidationStatus = previousState.ValidationStatus,
            TranslationError = previousState.TranslationError,
            TranslatedText = previousState.TranslatedText,
            CanSave = previousState.CanSave,
            ReferenceImpactCount = 1,
            RequiresReferenceRewrite = false,
            ReferenceResolutionStatus = "이전 참조 상태",
        };
        var previousProgress = BuildProgressState(previousState);
        var currentItem = BuildItem("ERB/Test.ERB:0", "ERB/Test.ERB", 1, "같은 원문");
        currentItem.ReferenceImpactCount = 4;
        currentItem.RequiresReferenceRewrite = true;
        currentItem.ReferenceResolutionStatus = "새 참조 상태";
        var currentItems = new List<ExtractedTextItem> { currentItem };

        var result = _service.Apply(previousSession, previousProgress, currentItems);

        Assert.Equal(1, result.ExactRestoredCount);
        Assert.Equal("번역 완료", currentItems[0].Status);
        Assert.Equal("기존 번역", currentItems[0].TranslatedText);
        Assert.Equal(4, currentItems[0].ReferenceImpactCount);
        Assert.True(currentItems[0].RequiresReferenceRewrite);
        Assert.Equal("새 참조 상태", currentItems[0].ReferenceResolutionStatus);
    }

    [Fact]
    public void Apply_ReferenceBearingExactRestoreMatchesRescannedTranslatedKeyAndCarriesOriginalSymbolKey()
    {
        var previousItem = BuildItem(
            "CSV/Talent.csv:0",
            "CSV/Talent.csv",
            1,
            "永久発情",
            fileType: "CSV",
            sourceKey: "178",
            fieldIndex: 1,
            symbolNamespace: "TALENT",
            originalSymbolKey: "永久発情",
            isReferenceBearingKey: true);
        previousItem.ReferenceOriginalSymbolKey = "永久発情";
        var previousSession = BuildSession(previousItem);
        var previousProgress = BuildProgressState(
            new TranslationProgressItemState
            {
                SegmentId = "CSV/Talent.csv:0",
                Status = "번역 완료",
                ValidationStatus = "통과",
                TranslationError = string.Empty,
                TranslatedText = "영구발정",
                CanSave = true,
                ReferenceOriginalSymbolKey = "永久発情",
            });
        var currentItem = BuildItem(
            "CSV/Talent.csv:0",
            "CSV/Talent.csv",
            1,
            "영구발정",
            fileType: "CSV",
            sourceKey: "178",
            fieldIndex: 1,
            symbolNamespace: "TALENT",
            originalSymbolKey: "영구발정",
            isReferenceBearingKey: true);
        var currentItems = new List<ExtractedTextItem> { currentItem };

        var result = _service.Apply(previousSession, previousProgress, currentItems);

        Assert.Equal(1, result.ExactRestoredCount);
        Assert.Equal("영구발정", currentItems[0].TranslatedText);
        Assert.Equal("永久発情", currentItems[0].ReferenceOriginalSymbolKey);
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
        int? fieldIndex = null,
        string symbolNamespace = "",
        string originalSymbolKey = "",
        bool isReferenceBearingKey = false)
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
            SymbolNamespace = symbolNamespace,
            OriginalSymbolKey = originalSymbolKey,
            IsReferenceBearingKey = isReferenceBearingKey,
            WarningText = string.Empty,
        };
    }
}
