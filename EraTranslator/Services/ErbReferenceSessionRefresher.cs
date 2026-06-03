using EraTranslator.Models;

namespace EraTranslator.Services;

public static class ErbReferenceSessionRefresher
{
    public static void Refresh(ScanSession session)
    {
        var dimsLookupRegistry = ErbDimsLookupRegistry.BuildFromDocuments(session.Documents.Values.Select(document => document.OriginalText));
        var extractor = new ErbReferenceExtractor(SymbolNamespaceRegistry.CreateFromDocuments(session.Documents.Values), dimsLookupRegistry);
        foreach (var document in session.Documents.Values.Where(document =>
                     DocumentFileTypes.IsErbLike(document.FileType)))
        {
            var extracted = extractor.Extract(document.DocumentId, document.OriginalText);
            document.SymbolReferences.Clear();
            document.SymbolReferences.AddRange(extracted.references);
            document.VariableLiteralOccurrences.Clear();
            document.VariableLiteralOccurrences.AddRange(extracted.variableLiterals);
        }
    }
}
