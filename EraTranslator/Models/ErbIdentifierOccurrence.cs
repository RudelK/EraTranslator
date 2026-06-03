namespace EraTranslator.Models;

public enum ErbIdentifierKind
{
    Function,
    Variable,
}

public enum ErbIdentifierRole
{
    Definition,
    Call,
    Declaration,
    Assignment,
    Reference,
}

public sealed class ErbIdentifierOccurrence
{
    public required string DocumentId { get; init; }

    public required ErbIdentifierKind Kind { get; init; }

    public required ErbIdentifierRole Role { get; init; }

    public required string OriginalName { get; init; }

    public int AbsoluteStart { get; init; }

    public int Length { get; init; }

    public int LineNumber { get; init; }
}
