using System.Text;
using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class SqliteProjectStateStoreTests : IDisposable
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
    public void SaveAndLoadScanSession_RestoresLastScanSession()
    {
        Directory.CreateDirectory(_gameRoot);
        var store = new SqliteProjectStateStore();
        var session = BuildScanSession();

        store.SaveScanSession(session, _gameRoot);
        var restored = store.LoadScanSession(_gameRoot);

        Assert.NotNull(restored);
        Assert.Equal(_gameRoot, restored!.GameRoot);
        Assert.Single(restored.Documents);
        Assert.Single(restored.Items);
        Assert.Equal("warning", restored.Items[0].WarningText);
        Assert.Equal("CFLAG", restored.Items[0].SymbolNamespace);
        Assert.True(restored.Items[0].RequiresReferenceRewrite);
        Assert.Single(restored.Documents.Values.Single().SymbolReferences);
        Assert.Equal(1, restored.Metrics["Items"]);
        Assert.Equal(Path.Combine(_gameRoot, ".era-translator", "state.db"), store.GetDatabasePath(_gameRoot));
    }

    [Fact]
    public void SaveAndApplyTranslationProgress_RestoresTranslatedAndFailedStates()
    {
        Directory.CreateDirectory(_gameRoot);
        var store = new SqliteProjectStateStore();
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

        store.SaveTranslationProgressSnapshot(_gameRoot, items);

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

        var restoredCount = store.ApplyTranslationProgress(_gameRoot, freshItems);

        Assert.Equal(2, restoredCount);
        Assert.Equal("번역 완료", freshItems[0].Status);
        Assert.Equal("translated", freshItems[0].TranslatedText);
        Assert.Equal(3, freshItems[0].ReferenceImpactCount);
        Assert.True(freshItems[0].RequiresReferenceRewrite);
        Assert.Equal("번역 실패", freshItems[1].Status);
        Assert.Equal("server error", freshItems[1].TranslationError);
    }

    [Fact]
    public void UpsertTranslationProgressItems_UpdatesExistingRowsWithoutRemovingOthers()
    {
        Directory.CreateDirectory(_gameRoot);
        var store = new SqliteProjectStateStore();
        var items = BuildProgressItems();
        items[0].ApplyTranslationState("번역 완료", "통과", string.Empty, true, "translated-1");
        items[1].ApplyTranslationState("번역 실패", "HTTP 500", "server error", false);
        store.SaveTranslationProgressSnapshot(_gameRoot, items);

        items[0].ApplyTranslationState("수동 수정", "통과", string.Empty, true, "translated-2");
        store.UpsertTranslationProgressItems(_gameRoot, [items[0]]);

        var snapshot = store.LoadTranslationProgress(_gameRoot);

        Assert.Equal(2, snapshot.Items.Count);
        Assert.Contains(snapshot.Items, item => item.SegmentId == "doc:1" && item.TranslatedText == "translated-2" && item.Status == "수동 수정");
        Assert.Contains(snapshot.Items, item => item.SegmentId == "doc:2" && item.Status == "번역 실패");
    }

    [Fact]
    public void DeleteTranslationProgressItems_RemovesOnlySpecifiedRows()
    {
        Directory.CreateDirectory(_gameRoot);
        var store = new SqliteProjectStateStore();
        var items = BuildProgressItems();
        items[0].ApplyTranslationState("번역 완료", "통과", string.Empty, true, "translated-1");
        items[1].ApplyTranslationState("번역 실패", "HTTP 500", "server error", false);
        store.SaveTranslationProgressSnapshot(_gameRoot, items);

        store.DeleteTranslationProgressItems(_gameRoot, ["doc:1"]);

        var snapshot = store.LoadTranslationProgress(_gameRoot);

        Assert.Single(snapshot.Items);
        Assert.Equal("doc:2", snapshot.Items[0].SegmentId);
    }

    [Fact]
    public void SaveTranslationProgressSnapshot_RemovesStaleRows()
    {
        Directory.CreateDirectory(_gameRoot);
        var store = new SqliteProjectStateStore();
        var items = BuildProgressItems();
        items[0].ApplyTranslationState("번역 완료", "통과", string.Empty, true, "translated-1");
        items[1].ApplyTranslationState("번역 실패", "HTTP 500", "server error", false);
        store.SaveTranslationProgressSnapshot(_gameRoot, items);

        store.SaveTranslationProgressSnapshot(_gameRoot, [items[0]]);

        var snapshot = store.LoadTranslationProgress(_gameRoot);

        Assert.Single(snapshot.Items);
        Assert.Equal("doc:1", snapshot.Items[0].SegmentId);
    }

    private static ExtractedTextItem[] BuildProgressItems()
    {
        return
        [
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
        ];
    }

    private ScanSession BuildScanSession()
    {
        var session = new ScanSession
        {
            GameRoot = _gameRoot,
        };
        var document = new SourceFileDocument
        {
            DocumentId = "ERB/Test.ERB",
            FullPath = Path.Combine(_gameRoot, "ERB", "Test.ERB"),
            RelativePath = Path.Combine("ERB", "Test.ERB"),
            FileType = "ERB",
            OriginalText = "PRINTFORM \"test\"",
            EncodingInfo = new DetectedEncodingInfo
            {
                Encoding = Encoding.UTF8,
                Name = "utf-8",
                Kind = DetectedEncodingKind.Utf8,
                HasBom = true,
            },
            NewLineSequence = "\r\n",
            CsvKind = CsvDocumentKind.None,
        };
        document.Segments.Add(new TextSegment
        {
            SegmentId = "ERB/Test.ERB:0",
            DocumentId = "ERB/Test.ERB",
            SegmentType = "PRINT",
            AbsoluteStart = 0,
            Length = 4,
            LineNumber = 1,
            OriginalText = "test",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            SymbolNamespace = "CFLAG",
            OriginalSymbolKey = "外見年齢",
            IsReferenceBearingKey = true,
        });
        document.SymbolReferences.Add(new ErbSymbolReference
        {
            DocumentId = "ERB/Test.ERB",
            Namespace = "CFLAG",
            Kind = ErbSymbolReferenceKind.DirectLiteral,
            ResolutionKind = SymbolReferenceResolutionKind.Direct,
            OriginalKey = "外見年齢",
            AbsoluteStart = 0,
            Length = 4,
            LineNumber = 1,
            CandidateKeys = ["外見年齢"],
        });
        document.ScanWarnings.Add("warning");
        session.Documents[document.DocumentId] = document;
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = "ERB/Test.ERB:0",
            DocumentId = "ERB/Test.ERB",
            FileType = "ERB",
            RelativePath = Path.Combine("ERB", "Test.ERB"),
            EncodingName = "utf-8",
            SegmentType = "PRINT",
            LineNumber = 1,
            OriginalText = "test",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            SymbolNamespace = "CFLAG",
            OriginalSymbolKey = "外見年齢",
            IsReferenceBearingKey = true,
            ReferenceImpactCount = 1,
            RequiresReferenceRewrite = true,
            ReferenceResolutionStatus = "직접 참조만",
            WarningText = "warning",
        });
        session.Metrics["Documents"] = 1;
        session.Metrics["Items"] = 1;
        return session;
    }
}
