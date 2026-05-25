namespace EraTranslator.Models;

public sealed class SymbolKeyInfo
{
    public string Namespace { get; init; } = string.Empty;

    public string OriginalKey { get; init; } = string.Empty;

    public bool IsReferenceBearingKey { get; init; }
}
