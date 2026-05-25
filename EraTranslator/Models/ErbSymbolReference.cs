namespace EraTranslator.Models;

public enum ErbSymbolReferenceKind
{
    DirectLiteral,
    IndirectVariable,
}

public enum SymbolReferenceResolutionKind
{
    Direct,
    Resolved,
    Ambiguous,
    Unresolved,
}

public sealed class ErbSymbolReference
{
    public required string DocumentId { get; init; }

    public required string Namespace { get; init; }

    public required ErbSymbolReferenceKind Kind { get; init; }

    public required SymbolReferenceResolutionKind ResolutionKind { get; init; }

    public string OriginalKey { get; init; } = string.Empty;

    public string VariableName { get; init; } = string.Empty;

    public string ExpressionText { get; init; } = string.Empty;

    public int AbsoluteStart { get; init; }

    public int Length { get; init; }

    public int LineNumber { get; init; }

    public List<string> CandidateKeys { get; init; } = [];
}

public sealed class ErbVariableLiteralOccurrence
{
    public required string DocumentId { get; init; }

    public required string VariableName { get; init; }

    public required string LiteralValue { get; init; }

    public int AbsoluteStart { get; init; }

    public int Length { get; init; }

    public int LineNumber { get; init; }

    public bool IsExactValue { get; init; }
}
