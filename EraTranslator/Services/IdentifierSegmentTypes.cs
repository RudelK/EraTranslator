using EraTranslator.Models;

namespace EraTranslator.Services;

public static class IdentifierSegmentTypes
{
    public const string Function = "erb-function-identifier";
    public const string Variable = "erb-variable-identifier";

    public static bool IsIdentifier(string segmentType)
    {
        return string.Equals(segmentType, Function, StringComparison.Ordinal)
            || string.Equals(segmentType, Variable, StringComparison.Ordinal);
    }

    public static bool TryGetKind(string segmentType, out ErbIdentifierKind kind)
    {
        if (string.Equals(segmentType, Function, StringComparison.Ordinal))
        {
            kind = ErbIdentifierKind.Function;
            return true;
        }

        if (string.Equals(segmentType, Variable, StringComparison.Ordinal))
        {
            kind = ErbIdentifierKind.Variable;
            return true;
        }

        kind = default;
        return false;
    }

    public static string ForKind(ErbIdentifierKind kind)
    {
        return kind == ErbIdentifierKind.Function ? Function : Variable;
    }

    public static string SourceKeyFor(ErbIdentifierKind kind, string originalName)
    {
        var prefix = kind == ErbIdentifierKind.Function ? "function" : "variable";
        return $"identifier:{prefix}:{originalName}";
    }
}
