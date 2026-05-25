using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class SymbolReferenceAnalyzer
{
    public void Analyze(ScanSession session)
    {
        var references = session.Documents.Values
            .SelectMany(document => document.SymbolReferences)
            .ToList();

        foreach (var item in session.Items)
        {
            if (!item.IsReferenceBearingKey || string.IsNullOrWhiteSpace(item.SymbolNamespace) || string.IsNullOrWhiteSpace(item.OriginalSymbolKey))
            {
                item.ReferenceImpactCount = 0;
                item.RequiresReferenceRewrite = false;
                item.ReferenceResolutionStatus = string.Empty;
                continue;
            }

            var matchingReferences = references
                .Where(reference => string.Equals(reference.Namespace, item.SymbolNamespace, StringComparison.Ordinal)
                    && (string.Equals(reference.OriginalKey, item.OriginalSymbolKey, StringComparison.Ordinal)
                        || reference.CandidateKeys.Contains(item.OriginalSymbolKey, StringComparer.Ordinal)))
                .ToList();

            item.ReferenceImpactCount = matchingReferences.Count;
            item.RequiresReferenceRewrite = matchingReferences.Count > 0;
            item.ReferenceResolutionStatus = BuildResolutionStatus(matchingReferences);
        }
    }

    private static string BuildResolutionStatus(IReadOnlyCollection<ErbSymbolReference> references)
    {
        if (references.Count == 0)
        {
            return "참조 없음";
        }

        if (references.Any(reference => reference.ResolutionKind == SymbolReferenceResolutionKind.Unresolved))
        {
            return "해석 불가 참조 있음";
        }

        if (references.Any(reference => reference.ResolutionKind == SymbolReferenceResolutionKind.Ambiguous))
        {
            return "후보 다중 참조";
        }

        if (references.Any(reference => reference.Kind == ErbSymbolReferenceKind.IndirectVariable))
        {
            return "간접 참조 있음";
        }

        return "직접 참조만";
    }
}
