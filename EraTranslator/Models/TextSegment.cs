namespace EraTranslator.Models;

public sealed class TextSegment
{
    public required string SegmentId { get; init; }

    public required string DocumentId { get; init; }

    public required string SegmentType { get; init; }

    public required int AbsoluteStart { get; init; }

    public required int Length { get; init; }

    public required int LineNumber { get; init; }

    public required string OriginalText { get; init; }

    public int? FieldIndex { get; init; }

    public string? SourceKey { get; init; }

    public CsvFieldRole CsvFieldRole { get; init; }

    public bool PreserveWhitespace { get; init; }

    public string SymbolNamespace { get; init; } = string.Empty;

    public string OriginalSymbolKey { get; init; } = string.Empty;

    public bool IsReferenceBearingKey { get; init; }
}
