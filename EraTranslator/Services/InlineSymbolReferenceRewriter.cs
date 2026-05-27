using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class InlineSymbolReferenceRewriter
{
    private readonly ErbReferenceExtractor _erbReferenceExtractor = new();

    public string Rewrite(
        string text,
        IReadOnlyDictionary<(string Namespace, string OriginalKey), string> renameMap,
        IReadOnlyDictionary<(string Namespace, string OriginalKey), string>? stringLookupRenameMap = null)
    {
        stringLookupRenameMap ??= renameMap;
        if ((renameMap.Count == 0 && stringLookupRenameMap.Count == 0) || string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var extracted = _erbReferenceExtractor.Extract("inline", text);
        var directReferences = extracted.references
            .Where(reference => reference.Kind == ErbSymbolReferenceKind.DirectLiteral
                && GetReplacementMap(text, reference, renameMap, stringLookupRenameMap).ContainsKey((reference.Namespace, reference.OriginalKey)))
            .OrderByDescending(reference => reference.AbsoluteStart)
            .ToList();

        var buffer = text;
        foreach (var reference in directReferences)
        {
            var replacementMap = GetReplacementMap(text, reference, renameMap, stringLookupRenameMap);
            buffer = buffer.Remove(reference.AbsoluteStart, reference.Length)
                .Insert(reference.AbsoluteStart, replacementMap[(reference.Namespace, reference.OriginalKey)]);
        }

        return buffer;
    }

    private static IReadOnlyDictionary<(string Namespace, string OriginalKey), string> GetReplacementMap(
        string text,
        ErbSymbolReference reference,
        IReadOnlyDictionary<(string Namespace, string OriginalKey), string> renameMap,
        IReadOnlyDictionary<(string Namespace, string OriginalKey), string> stringLookupRenameMap)
    {
        return IsStringLookupReference(text, reference) ? stringLookupRenameMap : renameMap;
    }

    private static bool IsStringLookupReference(string content, ErbSymbolReference reference)
    {
        var searchStart = Math.Max(0, reference.AbsoluteStart - 64);
        var prefix = content[searchStart..reference.AbsoluteStart];
        var getNumIndex = prefix.LastIndexOf("GETNUM", StringComparison.OrdinalIgnoreCase);
        if (getNumIndex < 0)
        {
            return false;
        }

        var between = prefix[(getNumIndex + "GETNUM".Length)..];
        return between.Contains('(') && between.Contains(',') && !between.Contains('\n');
    }
}
