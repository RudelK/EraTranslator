using EraTranslator.Models;

namespace EraTranslator.Services;

public static class ErbReferenceSessionRefresher
{
    public static void Refresh(ScanSession session)
    {
        var extractor = new ErbReferenceExtractor();
        foreach (var document in session.Documents.Values.Where(document =>
                     string.Equals(document.FileType, "ERB", StringComparison.OrdinalIgnoreCase)))
        {
            var extracted = extractor.Extract(document.DocumentId, document.OriginalText);
            document.SymbolReferences.Clear();
            document.SymbolReferences.AddRange(extracted.references);
            document.VariableLiteralOccurrences.Clear();
            document.VariableLiteralOccurrences.AddRange(extracted.variableLiterals);
        }
    }
}
