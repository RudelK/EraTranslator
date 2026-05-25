using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class SymbolRewritePlanner
{
    public SymbolRewritePlan CreatePlan(ScanSession session)
    {
        var plan = new SymbolRewritePlan();
        var renameEntries = BuildRenameEntries(session.Items);
        ResolveCollisions(renameEntries);

        foreach (var entry in renameEntries)
        {
            plan.RenameMap[(entry.Namespace, entry.OriginalKey)] = entry.NewKey;
        }

        foreach (var document in session.Documents.Values.Where(document => string.Equals(document.FileType, "ERB", StringComparison.OrdinalIgnoreCase)))
        {
            var replacements = new List<PlannedTextReplacement>();
            var unresolvedNamespaces = document.SymbolReferences
                .Where(reference => reference.Kind == ErbSymbolReferenceKind.IndirectVariable
                    && reference.ResolutionKind == SymbolReferenceResolutionKind.Unresolved)
                .Select(reference => reference.Namespace)
                .Distinct(StringComparer.Ordinal)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var entry in renameEntries)
            {
                if (unresolvedNamespaces.Contains(entry.Namespace))
                {
                    BlockEntry(entry, $"동적 {entry.Namespace} 참조를 해석하지 못해 자동 저장을 막았습니다.");
                    continue;
                }
            }

            foreach (var reference in document.SymbolReferences.Where(reference => reference.Kind == ErbSymbolReferenceKind.DirectLiteral))
            {
                if (!plan.RenameMap.TryGetValue((reference.Namespace, reference.OriginalKey), out var replacementValue))
                {
                    continue;
                }

                replacements.Add(new PlannedTextReplacement(reference.AbsoluteStart, reference.Length, replacementValue));
            }

            foreach (var reference in document.SymbolReferences.Where(reference => reference.Kind == ErbSymbolReferenceKind.IndirectVariable))
            {
                if (reference.CandidateKeys.Count == 0)
                {
                    continue;
                }

                var variableOccurrences = document.VariableLiteralOccurrences
                    .Where(occurrence => string.Equals(occurrence.VariableName, reference.VariableName, StringComparison.OrdinalIgnoreCase)
                        && occurrence.IsExactValue)
                    .ToList();

                foreach (var candidateKey in reference.CandidateKeys)
                {
                    if (!plan.RenameMap.TryGetValue((reference.Namespace, candidateKey), out var replacementValue))
                    {
                        continue;
                    }

                    var matchingOccurrences = variableOccurrences
                        .Where(occurrence => string.Equals(occurrence.LiteralValue, candidateKey, StringComparison.Ordinal))
                        .ToList();

                    var impactedEntries = renameEntries
                        .Where(entry => string.Equals(entry.Namespace, reference.Namespace, StringComparison.Ordinal)
                            && string.Equals(entry.OriginalKey, candidateKey, StringComparison.Ordinal))
                        .ToList();

                    if (matchingOccurrences.Count == 0)
                    {
                        foreach (var impactedEntry in impactedEntries)
                        {
                            BlockEntry(impactedEntry, $"간접 참조 변수 '{reference.VariableName}'의 리터럴 생산 지점을 찾지 못했습니다.");
                        }

                        continue;
                    }

                    foreach (var occurrence in matchingOccurrences)
                    {
                        replacements.Add(new PlannedTextReplacement(occurrence.AbsoluteStart, occurrence.Length, replacementValue));
                    }

                    if (reference.ResolutionKind == SymbolReferenceResolutionKind.Ambiguous)
                    {
                        foreach (var impactedEntry in impactedEntries)
                        {
                            ReviewEntry(impactedEntry, $"간접 참조 변수 '{reference.VariableName}'가 여러 후보를 가집니다.");
                        }
                    }
                }
            }

            if (replacements.Count > 0)
            {
                plan.DocumentReplacements[document.DocumentId] = replacements
                    .GroupBy(replacement => (replacement.Start, replacement.Length))
                    .Select(group => group.Last())
                    .OrderByDescending(replacement => replacement.Start)
                    .ToList();
            }
        }

        plan.RenameEntries.AddRange(renameEntries);
        return plan;
    }

    private static List<SymbolRenameEntry> BuildRenameEntries(IEnumerable<ExtractedTextItem> items)
    {
        return items
            .Where(item => item.IsReferenceBearingKey
                && !string.IsNullOrWhiteSpace(item.SymbolNamespace)
                && !string.IsNullOrWhiteSpace(item.OriginalSymbolKey)
                && !string.IsNullOrWhiteSpace(item.TranslatedSymbolKey)
                && item.CanSave
                && string.Equals(item.ValidationStatus, "통과", StringComparison.Ordinal))
            .Select(item => new SymbolRenameEntry(item, item.SymbolNamespace, item.OriginalSymbolKey, item.TranslatedSymbolKey))
            .Where(entry => !string.Equals(entry.OriginalKey, entry.NewKey, StringComparison.Ordinal))
            .ToList();
    }

    private static void ResolveCollisions(List<SymbolRenameEntry> entries)
    {
        foreach (var group in entries
                     .GroupBy(entry => (entry.Namespace, entry.NewKey))
                     .Where(group => group.Count() > 1))
        {
            var ordered = group.OrderBy(entry => entry.Item.RelativePath, StringComparer.Ordinal)
                .ThenBy(entry => entry.Item.LineNumber)
                .ThenBy(entry => entry.Item.SegmentId, StringComparer.Ordinal)
                .ToList();

            for (var index = 0; index < ordered.Count; index++)
            {
                var entry = ordered[index];
                ReviewEntry(entry, "같은 네임스페이스 안에서 번역 키가 충돌해 검토가 필요합니다.");
                if (index == 0)
                {
                    continue;
                }

                entry.NewKey = $"{entry.NewKey}__{TranslationQualityRules.NormalizeTranslatedText(entry.Item.FileType, entry.OriginalKey)}";
                entry.Item.ApplyTranslationState("검수 필요", "통과", "번역 키 충돌로 원문 접미사를 덧붙였습니다.", true, entry.NewKey);
            }
        }
    }

    private static void ReviewEntry(SymbolRenameEntry entry, string message)
    {
        if (entry.Item.CanSave)
        {
            entry.Item.ApplyTranslationState("검수 필요", "통과", message, true, entry.NewKey);
        }
    }

    private static void BlockEntry(SymbolRenameEntry entry, string message)
    {
        entry.Item.ApplyTranslationState("검수 필요", "참조 해석 실패", message, false, entry.NewKey);
    }
}

public sealed class SymbolRewritePlan
{
    public Dictionary<(string Namespace, string OriginalKey), string> RenameMap { get; } = new();

    public Dictionary<string, List<PlannedTextReplacement>> DocumentReplacements { get; } = [];

    public List<SymbolRenameEntry> RenameEntries { get; } = [];
}

public sealed class SymbolRenameEntry
{
    public SymbolRenameEntry(ExtractedTextItem item, string symbolNamespace, string originalKey, string newKey)
    {
        Item = item;
        Namespace = symbolNamespace;
        OriginalKey = originalKey;
        NewKey = newKey;
    }

    public ExtractedTextItem Item { get; }

    public string Namespace { get; }

    public string OriginalKey { get; }

    public string NewKey { get; set; }
}

public readonly record struct PlannedTextReplacement(int Start, int Length, string Value);
