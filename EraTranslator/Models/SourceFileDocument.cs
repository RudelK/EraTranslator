namespace EraTranslator.Models;

public sealed class SourceFileDocument
{
    public required string DocumentId { get; init; }

    public required string FullPath { get; init; }

    public required string RelativePath { get; init; }

    public required string FileType { get; init; }

    public required string OriginalText { get; init; }

    public required DetectedEncodingInfo EncodingInfo { get; init; }

    public required string NewLineSequence { get; init; }

    public CsvDocumentKind CsvKind { get; set; }

    public List<TextSegment> Segments { get; } = [];

    public List<ErbSymbolReference> SymbolReferences { get; } = [];

    public List<ErbVariableLiteralOccurrence> VariableLiteralOccurrences { get; } = [];

    public List<ErbIdentifierOccurrence> IdentifierOccurrences { get; } = [];

    public List<string> ScanWarnings { get; } = [];

    public JosaDocumentAnalysis JosaAnalysis { get; set; } = new();
}
