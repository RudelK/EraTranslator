namespace EraTranslator.Services;

public sealed class CsvFieldInfo
{
    public required int FieldIndex { get; init; }

    public required int RawStart { get; init; }

    public required int RawLength { get; init; }

    public required string RawText { get; init; }

    public required string LeadingTrivia { get; init; }

    public required string TrailingTrivia { get; init; }

    public required bool WasQuoted { get; init; }

    public required string Value { get; init; }

    public int ValueStartWithinLine => RawStart + LeadingTrivia.Length + (WasQuoted ? 1 : 0);
}
