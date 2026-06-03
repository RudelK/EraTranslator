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
            var referenceReplacement = GetReferenceReplacement(entry);
            foreach (var symbolNamespace in GetNamespaceAliases(entry.Namespace))
            {
                foreach (var inputKey in entry.InputKeys)
                {
                    plan.RenameMap[(symbolNamespace, inputKey)] = referenceReplacement;
                    plan.StringLookupRenameMap[(symbolNamespace, inputKey)] = entry.NewKey;
                }
            }

            foreach (var pair in entry.RequestedKeysBySegmentId)
            {
                var requestedKey = pair.Value;
                var originalTranslatedText = entry.OriginalTranslatedTextsBySegmentId[pair.Key];
                if (string.Equals(requestedKey, entry.NewKey, StringComparison.Ordinal)
                    && string.Equals(originalTranslatedText, entry.NewKey, StringComparison.Ordinal))
                {
                    continue;
                }

                plan.ItemOverrides[pair.Key] = plan.GetOverride(pair.Key) with
                {
                    TranslatedText = entry.NewKey,
                };
            }
        }

        foreach (var document in session.Documents.Values.Where(document => DocumentFileTypes.IsErbLike(document.FileType)))
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
                    ReviewEntry(plan, entry, $"동적 {entry.Namespace} 참조 중 일부를 해석하지 못했습니다. 자동 저장은 진행하지만 사용처를 확인하세요.");
                    continue;
                }
            }

            foreach (var reference in document.SymbolReferences.Where(reference => reference.Kind == ErbSymbolReferenceKind.DirectLiteral))
            {
                var renameMap = IsStringLookupReference(document.OriginalText, reference)
                    ? plan.StringLookupRenameMap
                    : plan.RenameMap;
                if (!renameMap.TryGetValue((reference.Namespace, reference.OriginalKey), out var replacementValue))
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
                            BlockEntry(plan, impactedEntry, $"간접 참조 변수 '{reference.VariableName}'의 리터럴 생산 지점을 찾지 못했습니다.");
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
                            ReviewEntry(plan, impactedEntry, $"간접 참조 변수 '{reference.VariableName}'가 여러 후보를 가집니다.");
                        }
                    }
                }
            }

            if (replacements.Count > 0)
            {
                plan.DocumentReplacements[document.DocumentId] = replacements
                    .ResolveOverlaps()
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
                && item.GetReferenceLookupKeys().Any()
                && !string.IsNullOrWhiteSpace(item.TranslatedSymbolKey)
                && item.CanSave
                && string.Equals(item.ValidationStatus, "통과", StringComparison.Ordinal))
            .GroupBy(item => (
                item.SymbolNamespace,
                !string.IsNullOrWhiteSpace(item.ReferenceOriginalSymbolKey)
                    ? item.ReferenceOriginalSymbolKey
                    : item.OriginalSymbolKey))
            .Select(group =>
            {
                var ordered = group
                    .OrderBy(item => CountCollisionSuffixes(item.TranslatedSymbolKey))
                    .ThenBy(item => item.TranslatedSymbolKey.Length)
                    .ThenBy(item => item.RelativePath, StringComparer.Ordinal)
                    .ThenBy(item => item.LineNumber)
                    .ThenBy(item => item.SegmentId, StringComparer.Ordinal)
                    .ToList();
                var preferred = ChoosePreferredRenameItem(ordered);
                var requestedKeysBySegmentId = ordered.ToDictionary(
                    item => item.SegmentId,
                    item => item.TranslatedSymbolKey,
                    StringComparer.Ordinal);
                var originalTranslatedTextsBySegmentId = ordered.ToDictionary(
                    item => item.SegmentId,
                    item => item.TranslatedText,
                    StringComparer.Ordinal);
                var inputKeys = ordered
                    .SelectMany(item => item.GetReferenceLookupKeys())
                    .Concat(ordered.Select(item => item.TranslatedText))
                    .Concat(requestedKeysBySegmentId.Values)
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                var numericKey = ordered
                    .Select(item => item.SourceKey)
                    .FirstOrDefault(sourceKey => !string.IsNullOrWhiteSpace(sourceKey)
                        && int.TryParse(sourceKey, out _))
                    ?? string.Empty;
                return new SymbolRenameEntry(
                    preferred,
                    preferred.SymbolNamespace,
                    !string.IsNullOrWhiteSpace(preferred.ReferenceOriginalSymbolKey)
                        ? preferred.ReferenceOriginalSymbolKey
                        : preferred.OriginalSymbolKey,
                    preferred.TranslatedSymbolKey,
                    numericKey,
                    inputKeys,
                    requestedKeysBySegmentId,
                    originalTranslatedTextsBySegmentId);
            })
            .Where(entry => !string.Equals(entry.OriginalKey, entry.NewKey, StringComparison.Ordinal))
            .ToList();
    }

    private static ExtractedTextItem ChoosePreferredRenameItem(IReadOnlyList<ExtractedTextItem> ordered)
    {
        return ordered
            .OrderBy(GetRenameSourcePriority)
            .ThenBy(item => CountCollisionSuffixes(item.TranslatedSymbolKey))
            .ThenBy(item => item.TranslatedSymbolKey.Length)
            .ThenBy(item => item.RelativePath, StringComparer.Ordinal)
            .ThenBy(item => item.LineNumber)
            .ThenBy(item => item.SegmentId, StringComparer.Ordinal)
            .First();
    }

    private static int GetRenameSourcePriority(ExtractedTextItem item)
    {
        if (DocumentFileTypes.IsCsvLike(item.FileType) && IsPrimaryCsvNamespaceSource(item))
        {
            return 0;
        }

        if (DocumentFileTypes.IsCsvLike(item.FileType) && !IsUnderscoreCsvFile(item.RelativePath))
        {
            return 1;
        }

        return 2;
    }

    private static bool IsPrimaryCsvNamespaceSource(ExtractedTextItem item)
    {
        if (IsUnderscoreCsvFile(item.RelativePath))
        {
            return false;
        }

        var fileNamespace = SymbolNamespaceRegistry.Default.ResolveFileNamespace(item.RelativePath);
        return !string.IsNullOrWhiteSpace(fileNamespace)
            && string.Equals(fileNamespace, item.SymbolNamespace, StringComparison.Ordinal);
    }

    private static bool IsUnderscoreCsvFile(string relativePath)
    {
        return Path.GetFileName(relativePath).StartsWith('_');
    }

    private static void ResolveCollisions(List<SymbolRenameEntry> entries)
    {
        foreach (var group in entries
                     .GroupBy(entry => (entry.Namespace, entry.NewKey))
                     .Where(group => group.Count() > 1))
        {
            var ordered = group.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
                .ThenBy(entry => entry.LineNumber)
                .ThenBy(entry => entry.SegmentId, StringComparer.Ordinal)
                .ToList();

            for (var index = 0; index < ordered.Count; index++)
            {
                var entry = ordered[index];
                if (index == 0)
                {
                    continue;
                }

                entry.NewKey = $"{entry.NewKey}__{TranslationQualityRules.NormalizeTranslatedText(entry.FileType, entry.OriginalKey, entry.PreserveWhitespace)}";
            }
        }
    }

    private static void ReviewEntry(SymbolRewritePlan plan, SymbolRenameEntry entry, string message)
    {
        foreach (var segmentId in entry.RequestedKeysBySegmentId.Keys)
        {
            plan.ItemOverrides[segmentId] = plan.GetOverride(segmentId) with
            {
                TranslatedText = entry.NewKey,
                CanSave = true,
                ValidationStatus = "통과",
                Status = "검수 필요",
                TranslationError = message,
            };
        }
    }

    private static void BlockEntry(SymbolRewritePlan plan, SymbolRenameEntry entry, string message)
    {
        foreach (var segmentId in entry.RequestedKeysBySegmentId.Keys)
        {
            plan.ItemOverrides[segmentId] = plan.GetOverride(segmentId) with
            {
                TranslatedText = entry.NewKey,
                CanSave = false,
                ValidationStatus = "참조 해석 실패",
                Status = "검수 필요",
                TranslationError = message,
            };
        }
    }

    private static int CountCollisionSuffixes(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? int.MaxValue
            : value.Count(static character => character == '_') / 2;
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

    private static string GetReferenceReplacement(SymbolRenameEntry entry)
    {
        return ShouldUseNumericReference(entry.NewKey) && !string.IsNullOrWhiteSpace(entry.NumericKey)
            ? entry.NumericKey
            : entry.NewKey;
    }

    private static bool ShouldUseNumericReference(string key)
    {
        return key.Any(static ch => char.IsWhiteSpace(ch)
            || ch is ':' or '(' or ')' or '{' or '}' or '[' or ']' or ',' or '"' or '\'' or '+' or '-' or '*' or '/' or '<' or '>' or '=' or '!' or '&' or '|' or '%');
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

internal static class PlannedTextReplacementExtensions
{
    public static IEnumerable<PlannedTextReplacement> ResolveOverlaps(this IEnumerable<PlannedTextReplacement> replacements)
    {
        var accepted = new List<PlannedTextReplacement>();
        foreach (var replacement in replacements
                     .GroupBy(replacement => (replacement.Start, replacement.Length))
                     .Select(group => group.Last())
                     .OrderBy(replacement => replacement.Start)
                     .ThenByDescending(replacement => replacement.Length))
        {
            if (accepted.Any(existing => RangesOverlap(existing.Start, existing.Length, replacement.Start, replacement.Length)))
            {
                continue;
            }

            accepted.Add(replacement);
        }

        return accepted;
    }

    private static bool RangesOverlap(int leftStart, int leftLength, int rightStart, int rightLength)
    {
        var leftEnd = leftStart + leftLength;
        var rightEnd = rightStart + rightLength;
        return leftStart < rightEnd && rightStart < leftEnd;
    }
}

public sealed class SymbolRewritePlan
{
    public Dictionary<(string Namespace, string OriginalKey), string> RenameMap { get; } = new();

    public Dictionary<(string Namespace, string OriginalKey), string> StringLookupRenameMap { get; } = new();

    public Dictionary<string, List<PlannedTextReplacement>> DocumentReplacements { get; } = [];

    public List<SymbolRenameEntry> RenameEntries { get; } = [];

    public Dictionary<string, SymbolRewriteItemOverride> ItemOverrides { get; } = new(StringComparer.Ordinal);

    public SymbolRewriteItemOverride GetOverride(string segmentId)
    {
        return ItemOverrides.TryGetValue(segmentId, out var itemOverride)
            ? itemOverride
            : SymbolRewriteItemOverride.Empty;
    }

    public string GetOutputTranslatedText(ExtractedTextItem item)
    {
        var itemOverride = GetOverride(item.SegmentId);
        if (itemOverride.TranslatedText is not null)
        {
            return itemOverride.TranslatedText;
        }

        return item.IsExcluded ? item.OriginalText : item.TranslatedText;
    }

    public bool CanWriteItem(ExtractedTextItem item)
    {
        var itemOverride = GetOverride(item.SegmentId);
        var canSave = itemOverride.CanSave ?? item.CanSave;
        var validationStatus = itemOverride.ValidationStatus ?? item.ValidationStatus;

        if (item.IsExcluded)
        {
            return canSave
                && (string.Equals(validationStatus, "언어 제외", StringComparison.Ordinal)
                    || string.Equals(validationStatus, "수동 제외", StringComparison.Ordinal))
                && !string.IsNullOrWhiteSpace(item.OriginalText);
        }

        var translatedText = itemOverride.TranslatedText ?? item.TranslatedText;
        return !string.IsNullOrWhiteSpace(translatedText)
            && canSave
            && string.Equals(validationStatus, "통과", StringComparison.Ordinal);
    }
}

public sealed class SymbolRenameEntry
{
    public SymbolRenameEntry(
        ExtractedTextItem item,
        string symbolNamespace,
        string originalKey,
        string newKey,
        string numericKey,
        IReadOnlyList<string> inputKeys,
        IReadOnlyDictionary<string, string> requestedKeysBySegmentId,
        IReadOnlyDictionary<string, string> originalTranslatedTextsBySegmentId)
    {
        SegmentId = item.SegmentId;
        RelativePath = item.RelativePath;
        LineNumber = item.LineNumber;
        FileType = item.FileType;
        PreserveWhitespace = item.PreserveWhitespace;
        Namespace = symbolNamespace;
        OriginalKey = originalKey;
        RequestedKey = newKey;
        NewKey = newKey;
        NumericKey = numericKey;
        InputKeys = inputKeys;
        RequestedKeysBySegmentId = requestedKeysBySegmentId;
        OriginalTranslatedTextsBySegmentId = originalTranslatedTextsBySegmentId;
    }

    public string SegmentId { get; }

    public string RelativePath { get; }

    public int LineNumber { get; }

    public string FileType { get; }

    public bool PreserveWhitespace { get; }

    public string Namespace { get; }

    public string OriginalKey { get; }

    public string RequestedKey { get; }

    public string NewKey { get; set; }

    public string NumericKey { get; }

    public IReadOnlyList<string> InputKeys { get; }

    public IReadOnlyDictionary<string, string> RequestedKeysBySegmentId { get; }

    public IReadOnlyDictionary<string, string> OriginalTranslatedTextsBySegmentId { get; }
}

public readonly record struct SymbolRewriteItemOverride(
    string? TranslatedText,
    bool? CanSave,
    string? ValidationStatus,
    string? Status,
    string? TranslationError)
{
    public static SymbolRewriteItemOverride Empty { get; } = new(null, null, null, null, null);
}

public readonly record struct PlannedTextReplacement(int Start, int Length, string Value);
