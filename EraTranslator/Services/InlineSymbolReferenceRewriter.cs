using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class InlineSymbolReferenceRewriter
{
    private readonly ErbReferenceExtractor _erbReferenceExtractor = new();

    public string Rewrite(
        string text,
        IReadOnlyDictionary<(string Namespace, string OriginalKey), string> renameMap)
    {
        if (renameMap.Count == 0 || string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var extracted = _erbReferenceExtractor.Extract("inline", text);
        var directReferences = extracted.references
            .Where(reference => reference.Kind == ErbSymbolReferenceKind.DirectLiteral
                && renameMap.ContainsKey((reference.Namespace, reference.OriginalKey)))
            .OrderByDescending(reference => reference.AbsoluteStart)
            .ToList();

        var buffer = text;
        foreach (var reference in directReferences)
        {
            buffer = buffer.Remove(reference.AbsoluteStart, reference.Length)
                .Insert(reference.AbsoluteStart, renameMap[(reference.Namespace, reference.OriginalKey)]);
        }

        return buffer;
    }
}
