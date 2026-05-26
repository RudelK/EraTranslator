namespace EraTranslator.Models;

public sealed class CsvFieldClassification
{
    public CsvFieldRole Role { get; init; }

    public bool ShouldExtract { get; init; }

    public bool PreserveWhitespace { get; init; }

    public string SymbolNamespace { get; init; } = string.Empty;

    public string OriginalSymbolKey { get; init; } = string.Empty;

    public bool IsReferenceBearingKey { get; init; }
}
