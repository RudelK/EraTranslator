using System.Text;
using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class ScanSessionStateServiceTests : IDisposable
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
    public void SaveAndLoad_RestoresLastScanSession()
    {
        Directory.CreateDirectory(_gameRoot);
        var service = new ScanSessionStateService();
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

        service.Save(session);
        var restored = service.Load(_gameRoot);

        Assert.NotNull(restored);
        Assert.Equal(_gameRoot, restored!.GameRoot);
        Assert.Single(restored.Documents);
        Assert.Single(restored.Items);
        Assert.Equal("warning", restored.Items[0].WarningText);
        Assert.Equal("CFLAG", restored.Items[0].SymbolNamespace);
        Assert.True(restored.Items[0].RequiresReferenceRewrite);
        Assert.Single(restored.Documents.Values.Single().SymbolReferences);
        Assert.Equal(1, restored.Metrics["Items"]);
        Assert.Equal(Path.Combine(_gameRoot, ".era-translator", "last-scan-session.json"), service.GetStateFilePath(_gameRoot));
    }
}
