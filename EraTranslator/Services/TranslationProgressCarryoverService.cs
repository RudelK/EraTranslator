using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class TranslationProgressCarryoverService
{
    private const string HeuristicCarryoverMessage = "업데이트 승계 항목, 검토 권장";

    public TranslationProgressCarryoverResult Apply(
        ScanSession? previousSession,
        TranslationProgressState? previousProgress,
        IList<ExtractedTextItem> currentItems)
    {
        if (previousSession is null || previousProgress is null || currentItems.Count == 0)
        {
            return new TranslationProgressCarryoverResult(0, 0, currentItems.Count);
        }

        var previousItemsBySegmentId = previousSession.Items.ToDictionary(item => item.SegmentId, StringComparer.Ordinal);
        var previousStatesBySegmentId = previousProgress.Items.ToDictionary(item => item.SegmentId, StringComparer.Ordinal);
        var matchedCurrentIds = new HashSet<string>(StringComparer.Ordinal);
        var usedPreviousIds = new HashSet<string>(StringComparer.Ordinal);
        var blockedFromWeakMatching = new HashSet<string>(StringComparer.Ordinal);
        var exactRestoredCount = 0;
        var heuristicRestoredCount = 0;

        foreach (var currentItem in currentItems)
        {
            if (!previousItemsBySegmentId.TryGetValue(currentItem.SegmentId, out var previousItem)
                || !previousStatesBySegmentId.TryGetValue(currentItem.SegmentId, out var previousState)
                || !CanExactlyRestore(previousItem, previousState, currentItem))
            {
                continue;
            }

            ApplyExactRestore(currentItem, previousItem, previousState);
            matchedCurrentIds.Add(currentItem.SegmentId);
            usedPreviousIds.Add(previousItem.SegmentId);
            exactRestoredCount++;
        }

        var carryoverCandidates = previousSession.Items
            .Where(item => !usedPreviousIds.Contains(item.SegmentId)
                && previousStatesBySegmentId.TryGetValue(item.SegmentId, out var state)
                && CanHeuristicallyCarryOver(state))
            .Select(item => new CarryoverCandidate(item, previousStatesBySegmentId[item.SegmentId]))
            .ToList();

        heuristicRestoredCount += ApplyStrongKeyMatches(
            currentItems,
            carryoverCandidates,
            matchedCurrentIds,
            usedPreviousIds,
            blockedFromWeakMatching,
            BuildSourceKey);
        heuristicRestoredCount += ApplyStrongKeyMatches(
            currentItems,
            carryoverCandidates,
            matchedCurrentIds,
            usedPreviousIds,
            blockedFromWeakMatching,
            BuildSymbolKey);
        heuristicRestoredCount += ApplyOccurrenceMatches(
            currentItems,
            carryoverCandidates,
            matchedCurrentIds,
            usedPreviousIds,
            blockedFromWeakMatching);

        return new TranslationProgressCarryoverResult(
            exactRestoredCount,
            heuristicRestoredCount,
            Math.Max(0, currentItems.Count - exactRestoredCount - heuristicRestoredCount));
    }

    private static int ApplyStrongKeyMatches(
        IEnumerable<ExtractedTextItem> currentItems,
        IEnumerable<CarryoverCandidate> previousCandidates,
        ISet<string> matchedCurrentIds,
        ISet<string> usedPreviousIds,
        ISet<string> blockedFromWeakMatching,
        Func<ExtractedTextItem, string?> keySelector)
    {
        var currentGroups = currentItems
            .Where(item => !matchedCurrentIds.Contains(item.SegmentId))
            .Select(item => (item, key: keySelector(item)))
            .Where(tuple => !string.IsNullOrWhiteSpace(tuple.key))
            .GroupBy(tuple => tuple.key!, tuple => tuple.item, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var previousGroups = previousCandidates
            .Where(candidate => !usedPreviousIds.Contains(candidate.Item.SegmentId))
            .Select(candidate => (candidate, key: keySelector(candidate.Item)))
            .Where(tuple => !string.IsNullOrWhiteSpace(tuple.key))
            .GroupBy(tuple => tuple.key!, tuple => tuple.candidate, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var restoredCount = 0;

        foreach (var (key, currentGroup) in currentGroups)
        {
            if (!previousGroups.TryGetValue(key, out var previousGroup) || previousGroup.Count == 0)
            {
                continue;
            }

            if (currentGroup.Count != 1 || previousGroup.Count != 1)
            {
                foreach (var blockedItem in currentGroup)
                {
                    blockedFromWeakMatching.Add(blockedItem.SegmentId);
                }

                continue;
            }

            var currentItem = currentGroup[0];
            var previousCandidate = previousGroup[0];
            if (!HasSameRestorableOriginalText(previousCandidate.Item, previousCandidate.State, currentItem))
            {
                blockedFromWeakMatching.Add(currentItem.SegmentId);
                continue;
            }

            ApplyHeuristicRestore(currentItem, previousCandidate.Item, previousCandidate.State);
            matchedCurrentIds.Add(currentItem.SegmentId);
            usedPreviousIds.Add(previousCandidate.Item.SegmentId);
            restoredCount++;
        }

        return restoredCount;
    }

    private static int ApplyOccurrenceMatches(
        IEnumerable<ExtractedTextItem> currentItems,
        IEnumerable<CarryoverCandidate> previousCandidates,
        ISet<string> matchedCurrentIds,
        ISet<string> usedPreviousIds,
        ISet<string> blockedFromWeakMatching)
    {
        var currentOccurrenceKeys = BuildOccurrenceKeys(
            currentItems
                .Where(item => !matchedCurrentIds.Contains(item.SegmentId) && !blockedFromWeakMatching.Contains(item.SegmentId))
                .ToList());
        var previousOccurrenceKeys = BuildOccurrenceKeys(
            previousCandidates
                .Where(candidate => !usedPreviousIds.Contains(candidate.Item.SegmentId))
                .Select(candidate => candidate.Item)
                .ToList());
        var previousByOccurrenceKey = previousCandidates
            .Where(candidate => !usedPreviousIds.Contains(candidate.Item.SegmentId))
            .ToDictionary(candidate => previousOccurrenceKeys[candidate.Item.SegmentId], StringComparer.Ordinal);
        var restoredCount = 0;

        foreach (var currentItem in currentItems.Where(item => !matchedCurrentIds.Contains(item.SegmentId) && !blockedFromWeakMatching.Contains(item.SegmentId)))
        {
            if (!currentOccurrenceKeys.TryGetValue(currentItem.SegmentId, out var occurrenceKey)
                || !previousByOccurrenceKey.TryGetValue(occurrenceKey, out var previousCandidate))
            {
                continue;
            }

            ApplyHeuristicRestore(currentItem, previousCandidate.Item, previousCandidate.State);
            matchedCurrentIds.Add(currentItem.SegmentId);
            usedPreviousIds.Add(previousCandidate.Item.SegmentId);
            previousByOccurrenceKey.Remove(occurrenceKey);
            restoredCount++;
        }

        return restoredCount;
    }

    private static Dictionary<string, string> BuildOccurrenceKeys(IReadOnlyList<ExtractedTextItem> items)
    {
        var counters = new Dictionary<string, int>(StringComparer.Ordinal);
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var item in items
                     .OrderBy(candidate => NormalizePath(candidate.RelativePath), StringComparer.Ordinal)
                     .ThenBy(candidate => candidate.LineNumber)
                     .ThenBy(candidate => candidate.SegmentId, StringComparer.Ordinal))
        {
            var baseKey = string.Join(
                "\u001F",
                NormalizePath(item.RelativePath),
                item.SegmentType,
                NormalizeText(item.OriginalText));
            counters.TryGetValue(baseKey, out var occurrenceIndex);
            occurrenceIndex++;
            counters[baseKey] = occurrenceIndex;
            keys[item.SegmentId] = $"{baseKey}\u001F{occurrenceIndex}";
        }

        return keys;
    }

    private static bool CanExactlyRestore(
        ExtractedTextItem previousItem,
        TranslationProgressItemState previousState,
        ExtractedTextItem currentItem)
    {
        return string.Equals(previousItem.RelativePath, currentItem.RelativePath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(previousItem.SegmentType, currentItem.SegmentType, StringComparison.Ordinal)
            && HasSameRestorableOriginalText(previousItem, previousState, currentItem)
            && string.Equals(previousItem.SourceKey ?? string.Empty, currentItem.SourceKey ?? string.Empty, StringComparison.Ordinal)
            && previousItem.FieldIndex == currentItem.FieldIndex
            && previousItem.CsvFieldRole == currentItem.CsvFieldRole
            && string.Equals(previousItem.SymbolNamespace, currentItem.SymbolNamespace, StringComparison.Ordinal)
            && HasSameRestorableSymbolKey(previousItem, previousState, currentItem);
    }

    private static bool CanHeuristicallyCarryOver(TranslationProgressItemState state)
    {
        return state.CanSave
            && !string.IsNullOrWhiteSpace(state.TranslatedText)
            && state.Status is "번역 완료" or "검수 필요" or "수동 수정";
    }

    private static void ApplyExactRestore(ExtractedTextItem item, ExtractedTextItem previousItem, TranslationProgressItemState state)
    {
        item.ApplyPersistedState(state);
        item.ReferenceOriginalSymbolKey = ResolveReferenceOriginalSymbolKey(previousItem, state, item);
    }

    private static void ApplyHeuristicRestore(ExtractedTextItem item, ExtractedTextItem previousItem, TranslationProgressItemState state)
    {
        item.ApplyTranslationState(
            "검수 필요",
            "통과",
            HeuristicCarryoverMessage,
            true,
            state.TranslatedText);
        item.ReferenceOriginalSymbolKey = ResolveReferenceOriginalSymbolKey(previousItem, state, item);
    }

    private static bool HasSameOriginalText(string left, string right)
    {
        return string.Equals(NormalizeText(left), NormalizeText(right), StringComparison.Ordinal);
    }

    private static bool HasSameRestorableOriginalText(
        ExtractedTextItem previousItem,
        TranslationProgressItemState previousState,
        ExtractedTextItem currentItem)
    {
        if (HasSameOriginalText(previousItem.OriginalText, currentItem.OriginalText))
        {
            return true;
        }

        if (!previousItem.IsReferenceBearingKey || !currentItem.IsReferenceBearingKey || string.IsNullOrWhiteSpace(previousState.TranslatedText))
        {
            return false;
        }

        var normalizedTranslatedKey = TranslationQualityRules.NormalizeTranslatedText(
            previousItem.FileType,
            previousState.TranslatedText,
            previousItem.PreserveWhitespace);
        return HasSameOriginalText(normalizedTranslatedKey, currentItem.OriginalText);
    }

    private static bool HasSameRestorableSymbolKey(
        ExtractedTextItem previousItem,
        TranslationProgressItemState previousState,
        ExtractedTextItem currentItem)
    {
        if (string.Equals(previousItem.OriginalSymbolKey, currentItem.OriginalSymbolKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (!previousItem.IsReferenceBearingKey || !currentItem.IsReferenceBearingKey || string.IsNullOrWhiteSpace(previousState.TranslatedText))
        {
            return false;
        }

        var normalizedTranslatedKey = TranslationQualityRules.NormalizeTranslatedText(
            previousItem.FileType,
            previousState.TranslatedText,
            previousItem.PreserveWhitespace);
        return string.Equals(normalizedTranslatedKey, currentItem.OriginalSymbolKey, StringComparison.Ordinal);
    }

    private static string ResolveReferenceOriginalSymbolKey(
        ExtractedTextItem previousItem,
        TranslationProgressItemState previousState,
        ExtractedTextItem currentItem)
    {
        if (!currentItem.IsReferenceBearingKey)
        {
            return currentItem.OriginalSymbolKey;
        }

        if (!string.IsNullOrWhiteSpace(previousState.ReferenceOriginalSymbolKey))
        {
            return previousState.ReferenceOriginalSymbolKey;
        }

        if (!string.IsNullOrWhiteSpace(previousItem.ReferenceOriginalSymbolKey))
        {
            return previousItem.ReferenceOriginalSymbolKey;
        }

        return previousItem.OriginalSymbolKey;
    }

    private static string NormalizeText(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
    }

    private static string NormalizePath(string relativePath)
    {
        return relativePath.Replace('\\', '/').ToUpperInvariant();
    }

    private static string? BuildSourceKey(ExtractedTextItem item)
    {
        return string.IsNullOrWhiteSpace(item.SourceKey)
            ? null
            : string.Join(
                "\u001F",
                NormalizePath(item.RelativePath),
                item.CsvFieldRole,
                item.SourceKey.Trim());
    }

    private static string? BuildSymbolKey(ExtractedTextItem item)
    {
        return string.IsNullOrWhiteSpace(item.SymbolNamespace) || string.IsNullOrWhiteSpace(item.OriginalSymbolKey)
            ? null
            : string.Join(
                "\u001F",
                NormalizePath(item.RelativePath),
                item.SymbolNamespace.Trim(),
                item.OriginalSymbolKey.Trim());
    }

    private sealed record CarryoverCandidate(ExtractedTextItem Item, TranslationProgressItemState State);
}

public sealed record TranslationProgressCarryoverResult(
    int ExactRestoredCount,
    int HeuristicRestoredCount,
    int UnmatchedCount)
{
    public int RestoredCount => ExactRestoredCount + HeuristicRestoredCount;
}
