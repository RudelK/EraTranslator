using System.Text;
using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class TeamScanManifestBuilderTests
{
    [Fact]
    public void Build_IncludesDocumentsItemsReferencesAndIdentifiers()
    {
        var session = new ScanSession
        {
            GameRoot = @"D:\Game",
        };
        var document = new SourceFileDocument
        {
            DocumentId = "doc-1",
            FullPath = @"D:\Game\ERB\Test.ERB",
            RelativePath = @"ERB\Test.ERB",
            FileType = "ERB",
            OriginalText = "PRINTL テスト",
            EncodingInfo = new DetectedEncodingInfo
            {
                Encoding = Encoding.UTF8,
                Name = "UTF-8",
                Kind = DetectedEncodingKind.Utf8,
            },
            NewLineSequence = "\n",
        };
        document.SymbolReferences.Add(new ErbSymbolReference
        {
            DocumentId = "doc-1",
            Namespace = "TALENT",
            Kind = ErbSymbolReferenceKind.DirectLiteral,
            ResolutionKind = SymbolReferenceResolutionKind.Direct,
            OriginalKey = "素質",
            AbsoluteStart = 7,
            Length = 2,
            LineNumber = 1,
        });
        document.IdentifierOccurrences.Add(new ErbIdentifierOccurrence
        {
            DocumentId = "doc-1",
            Kind = ErbIdentifierKind.Function,
            Role = ErbIdentifierRole.Call,
            OriginalName = "キャラ検索",
            AbsoluteStart = 0,
            Length = 4,
            LineNumber = 1,
        });
        session.Documents[document.DocumentId] = document;
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = "seg-1",
            DocumentId = "doc-1",
            FileType = "ERB",
            RelativePath = @"ERB\Test.ERB",
            EncodingName = "UTF-8",
            SegmentType = "quoted-string",
            LineNumber = 1,
            OriginalText = "テスト",
            SourceKey = "ERB\\Test.ERB:1:0",
        });

        var manifest = new TeamScanManifestBuilder().Build(session, "scan-1", "abc123");

        Assert.Equal("scan-1", manifest.ScanRevisionId);
        Assert.Equal("abc123", manifest.SourceArchiveSha256);
        var manifestDocument = Assert.Single(manifest.Documents);
        Assert.Equal("doc-1", manifestDocument.DocumentId);
        Assert.Equal("UTF-8", manifestDocument.EncodingName);
        var item = Assert.Single(manifest.Items);
        Assert.Equal("seg-1", item.SegmentId);
        Assert.Equal("テスト", item.OriginalText);
        var reference = Assert.Single(manifest.SymbolReferences);
        Assert.Equal("TALENT", reference.Namespace);
        Assert.Equal("DirectLiteral", reference.Kind);
        var identifier = Assert.Single(manifest.IdentifierOccurrences);
        Assert.Equal("キャラ検索", identifier.OriginalName);
        Assert.Equal("Function", identifier.Kind);
    }
}
