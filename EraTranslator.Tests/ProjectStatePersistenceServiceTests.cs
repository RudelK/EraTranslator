using System.Text;
using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class ProjectStatePersistenceServiceTests : IDisposable
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
    public void LoadAndApply_MigratesLegacyJsonIntoSqliteAndBacksUpJson()
    {
        Directory.CreateDirectory(_gameRoot);
        var scanStateService = new ScanSessionStateService();
        var progressStateService = new TranslationProgressStateService();
        var sqliteStore = new SqliteProjectStateStore();

        var session = BuildScanSession();
        scanStateService.Save(session, _gameRoot);

        session.Items[0].ApplyTranslationState("번역 완료", "통과", string.Empty, true, "테스트 번역");
        progressStateService.Save(_gameRoot, session.Items);

        var persistenceService = new ProjectStatePersistenceService(
            scanStateService,
            progressStateService,
            sqliteStore);

        var restoredSession = persistenceService.LoadScanSession(_gameRoot);
        var freshItems = new[]
        {
            new ExtractedTextItem
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
                WarningText = "warning",
            },
        };
        var restoredCount = persistenceService.ApplyTranslationProgress(_gameRoot, freshItems);

        Assert.NotNull(restoredSession);
        Assert.True(sqliteStore.Exists(_gameRoot));
        Assert.Equal(1, restoredCount);
        Assert.Equal("테스트 번역", freshItems[0].TranslatedText);
        Assert.False(File.Exists(scanStateService.GetStateFilePath(_gameRoot)));
        Assert.False(File.Exists(progressStateService.GetProgressFilePath(_gameRoot)));

        var backupRoot = Path.Combine(_gameRoot, ".era-translator", "legacy-json-backup");
        Assert.True(Directory.Exists(backupRoot));
        Assert.NotEmpty(Directory.EnumerateFiles(backupRoot, "*.json", SearchOption.AllDirectories));
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
