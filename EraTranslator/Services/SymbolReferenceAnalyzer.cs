using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class SymbolReferenceAnalyzer
{
    public void Analyze(ScanSession session)
    {
        var referenceIndex = BuildReferenceIndex(session.Documents.Values.SelectMany(document => document.SymbolReferences));

        foreach (var item in session.Items)
        {
            if (!item.IsReferenceBearingKey || string.IsNullOrWhiteSpace(item.SymbolNamespace) || string.IsNullOrWhiteSpace(item.OriginalSymbolKey))
            {
                item.ReferenceImpactCount = 0;
                item.RequiresReferenceRewrite = false;
                item.ReferenceResolutionStatus = string.Empty;
                continue;
            }

            var lookupKeys = item.GetReferenceLookupKeys().ToHashSet(StringComparer.Ordinal);
            var matchingNamespaces = GetNamespaceAliases(item.SymbolNamespace);
            var matchingReferences = new HashSet<ErbSymbolReference>();
            foreach (var matchingNamespace in matchingNamespaces)
            {
                if (!referenceIndex.TryGetValue(matchingNamespace, out var namespaceIndex))
                {
                    continue;
                }

                foreach (var lookupKey in lookupKeys)
                {
                    if (!namespaceIndex.TryGetValue(lookupKey, out var referencesForKey))
                    {
                        continue;
                    }

                    foreach (var reference in referencesForKey)
                    {
                        matchingReferences.Add(reference);
                    }
                }
            }

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

    private static IReadOnlyList<string> GetNamespaceAliases(string symbolNamespace)
    {
        return symbolNamespace switch
        {
            "BASE" => ["BASE", "MAXBASE", "DOWNBASE"],
            "DOWNBASE" => ["DOWNBASE", "BASE", "MAXBASE"],
            "STRNAME" => ["STRNAME", "STR"],
            "STR" => ["STR", "STRNAME"],
            "ITEM" => ["ITEM", "ITEMPRICE", "ITEMSALES"],
            "ITEMPRICE" => ["ITEMPRICE", "ITEM", "ITEMSALES"],
            "ITEMSALES" => ["ITEMSALES", "ITEM", "ITEMPRICE"],
            "PALAM" => ["PALAM", "JUEL", "CUP", "CDOWN"],
            "JUEL" => ["JUEL", "PALAM", "CUP", "CDOWN"],
            "SOURCE" => ["SOURCE", "CUP", "CDOWN"],
            "CDOWN" => ["CDOWN", "PALAM", "JUEL", "CUP"],
            "EX" => ["EX", "NOWEX"],
            "NOWEX" => ["NOWEX", "EX"],
            _ => [symbolNamespace],
        };
    }

    private static Dictionary<string, Dictionary<string, List<ErbSymbolReference>>> BuildReferenceIndex(
        IEnumerable<ErbSymbolReference> references)
    {
        var index = new Dictionary<string, Dictionary<string, List<ErbSymbolReference>>>(StringComparer.Ordinal);

        foreach (var reference in references)
        {
            if (string.IsNullOrWhiteSpace(reference.Namespace))
            {
                continue;
            }

            if (!index.TryGetValue(reference.Namespace, out var namespaceIndex))
            {
                namespaceIndex = new Dictionary<string, List<ErbSymbolReference>>(StringComparer.Ordinal);
                index[reference.Namespace] = namespaceIndex;
            }

            AddReferenceKey(namespaceIndex, reference.OriginalKey, reference);
            foreach (var candidateKey in reference.CandidateKeys)
            {
                AddReferenceKey(namespaceIndex, candidateKey, reference);
            }
        }

        return index;
    }

    private static void AddReferenceKey(
        Dictionary<string, List<ErbSymbolReference>> namespaceIndex,
        string key,
        ErbSymbolReference reference)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (!namespaceIndex.TryGetValue(key, out var referencesForKey))
        {
            referencesForKey = [];
            namespaceIndex[key] = referencesForKey;
        }

        referencesForKey.Add(reference);
    }
}
