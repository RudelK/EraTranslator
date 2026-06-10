using System.Security.Cryptography;
using System.Text;
using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class PhaseScopedGlossaryBuilder
{
    private const int MaxGlossarySourceLength = 40;
    private const int MaxGlossaryTargetLength = 80;
    private const int MaxGlossaryHintsPerBatch = 8;
    private const int DefaultGlossaryCharacterBudget = 360;
    private const int DefaultGlossaryMinSourceLength = 2;
    private const int CsvGeneralPhaseMask = 1;
    private const int ErbIdentifiersPhaseMask = 2;
    private const int ErhPhaseMask = 4;
    private const int ErbPhaseMask = 8;
    public const int GlossaryEligibilityPolicyVersion = 2;

    private readonly PlaceholderProtector _placeholderProtector = new();

    public IReadOnlyList<GlossaryHint> BuildForPhase(IEnumerable<ExtractedTextItem> items, TranslationPhaseKind phase)
    {
        var refreshResult = BuildOrRefreshCacheEntries(items, new Dictionary<string, GlossaryCacheEntry>(StringComparer.Ordinal));
        return BuildHintsForPhase(refreshResult.Entries, phase);
    }

    public GlossaryCacheRefreshResult BuildOrRefreshCacheEntries(
        IEnumerable<ExtractedTextItem> items,
        IReadOnlyDictionary<string, GlossaryCacheEntry> cachedEntries)
    {
        var now = DateTimeOffset.UtcNow;
        var currentSegmentIds = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<GlossaryCacheEntry>();
        var upsertEntries = new List<GlossaryCacheEntry>();
        var deleteSegmentIds = new HashSet<string>(StringComparer.Ordinal);
        var hitCount = 0;
        var missCount = 0;
        var updatedCount = 0;
        var deletedCount = 0;

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.SegmentId))
            {
                continue;
            }

            currentSegmentIds.Add(item.SegmentId);
            var eligibilityHash = ComputeEligibilityHash(item);
            if (cachedEntries.TryGetValue(item.SegmentId, out var cached)
                && string.Equals(cached.EligibilityHash, eligibilityHash, StringComparison.Ordinal))
            {
                hitCount++;
                entries.Add(cached);
                continue;
            }

            missCount++;
            var refreshed = TryBuildCacheEntry(item, eligibilityHash, now);
            if (refreshed is null)
            {
                if (cached is not null)
                {
                    deleteSegmentIds.Add(item.SegmentId);
                    deletedCount++;
                }

                continue;
            }

            entries.Add(refreshed);
            upsertEntries.Add(refreshed);
            updatedCount++;
        }

        foreach (var cachedSegmentId in cachedEntries.Keys)
        {
            if (currentSegmentIds.Contains(cachedSegmentId))
            {
                continue;
            }

            if (deleteSegmentIds.Add(cachedSegmentId))
            {
                deletedCount++;
            }
        }

        return new GlossaryCacheRefreshResult(
            entries,
            upsertEntries,
            deleteSegmentIds.ToList(),
            hitCount,
            missCount,
            updatedCount,
            deletedCount);
    }

    public IReadOnlyList<GlossaryHint> BuildHintsForPhase(
        IReadOnlyList<GlossaryCacheEntry> entries,
        TranslationPhaseKind phase)
    {
        if (phase == TranslationPhaseKind.CsvReferenceKeys)
        {
            return [];
        }

        var phaseMask = GetPhaseMask(phase);
        return entries
            .Where(entry => (entry.PhaseMask & phaseMask) != 0)
            .GroupBy(static entry => entry.Source, StringComparer.Ordinal)
            .SelectMany(group =>
            {
                var targets = group
                    .Select(static entry => entry.Target)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (targets.Count != 1)
                {
                    return Enumerable.Empty<GlossaryHint>();
                }

                return new[]
                {
                    group
                        .OrderByDescending(static entry => entry.Source.Length)
                        .ThenByDescending(static entry => entry.IsReferenceBearingKey)
                        .First()
                        .ToHint() with { Target = targets[0] },
                };
            })
            .OrderByDescending(static hint => hint.Source.Length)
            .ThenBy(static hint => hint.Source, StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyList<GlossaryHint> SelectForBatch(
        IReadOnlyList<GlossaryHint> availableHints,
        IReadOnlyList<ExtractedTextItem> batchItems)
    {
        return CreateBatchSelector(availableHints).SelectForBatch(batchItems);
    }

    public GlossaryBatchSelector CreateBatchSelector(IReadOnlyList<GlossaryHint> availableHints)
    {
        return new GlossaryBatchSelector(availableHints, GlossarySelectionOptions.Default);
    }

    public GlossaryBatchSelector CreateBatchSelector(
        IReadOnlyList<GlossaryHint> availableHints,
        ProviderSettings settings)
    {
        return new GlossaryBatchSelector(
            settings.EnableGlossaryHints ? availableHints : [],
            new GlossarySelectionOptions(
                Math.Clamp(settings.GlossaryMaxHintsPerBatch, 0, 50),
                Math.Clamp(settings.GlossaryCharacterBudget, 0, 4000),
                Math.Clamp(settings.GlossaryMinSourceLength, 1, 20)));
    }

    public GlossaryBatchSelector CreateBatchSelector(
        IReadOnlyList<GlossaryCacheEntry> entries,
        TranslationPhaseKind phase,
        ProviderSettings settings)
    {
        return CreateBatchSelector(BuildHintsForPhase(entries, phase), settings);
    }

    public sealed class GlossaryBatchSelector
    {
        private readonly Dictionary<char, List<GlossaryCandidate>> _candidatesByFirstChar = new();
        private readonly Dictionary<string, IReadOnlyList<OriginalGlossaryMatch>> _matchCache = new(StringComparer.Ordinal);
        private readonly GlossarySelectionOptions _options;

        public GlossaryBatchSelector(IReadOnlyList<GlossaryHint> availableHints, GlossarySelectionOptions options)
        {
            _options = options;
            if (_options.MaxHintsPerBatch <= 0 || _options.CharacterBudget <= 0)
            {
                return;
            }

            foreach (var hint in availableHints)
            {
                var source = hint.Source.Trim();
                var target = hint.Target.Trim();
                if (string.IsNullOrWhiteSpace(source)
                    || string.IsNullOrWhiteSpace(target)
                    || IsTooShortGeneralCandidate(hint, source, _options.MinSourceLength))
                {
                    continue;
                }

                var candidate = new GlossaryCandidate(
                    hint with { Source = source, Target = target },
                    EstimateRenderedLength(source, target),
                    GetStaticScore(hint, source));
                var firstChar = source[0];
                if (!_candidatesByFirstChar.TryGetValue(firstChar, out var bucket))
                {
                    bucket = [];
                    _candidatesByFirstChar[firstChar] = bucket;
                }

                bucket.Add(candidate);
            }

            foreach (var bucket in _candidatesByFirstChar.Values)
            {
                bucket.Sort(static (left, right) =>
                {
                    var lengthComparison = right.Hint.Source.Length.CompareTo(left.Hint.Source.Length);
                    if (lengthComparison != 0)
                    {
                        return lengthComparison;
                    }

                    var scoreComparison = right.StaticScore.CompareTo(left.StaticScore);
                    return scoreComparison != 0
                        ? scoreComparison
                        : StringComparer.Ordinal.Compare(left.Hint.Source, right.Hint.Source);
                });
            }
        }

        public IReadOnlyList<GlossaryHint> SelectForBatch(IReadOnlyList<ExtractedTextItem> batchItems)
        {
            if (_candidatesByFirstChar.Count == 0 || batchItems.Count == 0)
            {
                return [];
            }

            var originals = batchItems
                .Select(item => item.OriginalText)
                .Where(static text => !string.IsNullOrWhiteSpace(text))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (originals.Count == 0)
            {
                return [];
            }

            var scoredBySource = new Dictionary<string, ScoredGlossaryMatch>(StringComparer.Ordinal);
            foreach (var original in originals)
            {
                foreach (var match in GetMatchesForOriginal(original))
                {
                    if (scoredBySource.TryGetValue(match.Candidate.Hint.Source, out var current))
                    {
                        scoredBySource[match.Candidate.Hint.Source] = current with
                        {
                            OccurrenceCount = current.OccurrenceCount + match.OccurrenceCount,
                            Ranges = current.Ranges.Concat(match.Ranges).ToList(),
                        };
                        continue;
                    }

                    scoredBySource[match.Candidate.Hint.Source] = new ScoredGlossaryMatch(
                        match.Candidate,
                        match.OccurrenceCount,
                        match.Ranges,
                        match.Candidate.StaticScore + (20 * match.OccurrenceCount));
                }
            }

            var nonOverlapping = SelectLongestNonOverlapping(scoredBySource.Values);
            var selected = new List<GlossaryHint>(_options.MaxHintsPerBatch);
            var selectedTargets = new HashSet<string>(StringComparer.Ordinal);
            var usedCharacters = 0;
            foreach (var match in nonOverlapping
                         .OrderByDescending(static match => match.Score)
                         .ThenByDescending(static match => match.Candidate.Hint.Source.Length)
                         .ThenBy(static match => match.Candidate.Hint.Source, StringComparer.Ordinal))
            {
                var hint = match.Candidate.Hint;
                if (!selectedTargets.Add(hint.Target))
                {
                    continue;
                }

                if (usedCharacters + match.Candidate.RenderedLength > _options.CharacterBudget)
                {
                    continue;
                }

                selected.Add(hint);
                usedCharacters += match.Candidate.RenderedLength;
                if (selected.Count >= _options.MaxHintsPerBatch)
                {
                    break;
                }
            }

            return selected;
        }

        private IReadOnlyList<OriginalGlossaryMatch> GetMatchesForOriginal(string original)
        {
            if (_matchCache.TryGetValue(original, out var cached))
            {
                return cached;
            }

            var bySource = new Dictionary<string, (GlossaryCandidate Candidate, List<GlossaryMatchRange> Ranges)>(StringComparer.Ordinal);
            for (var index = 0; index < original.Length; index++)
            {
                if (!_candidatesByFirstChar.TryGetValue(original[index], out var bucket))
                {
                    continue;
                }

                foreach (var candidate in bucket)
                {
                    var source = candidate.Hint.Source;
                    if (index + source.Length > original.Length
                        || !original.AsSpan(index, source.Length).SequenceEqual(source.AsSpan()))
                    {
                        continue;
                    }

                    if (!bySource.TryGetValue(source, out var entry))
                    {
                        entry = (candidate, []);
                        bySource[source] = entry;
                    }

                    entry.Ranges.Add(new GlossaryMatchRange(original, index, index + source.Length));
                }
            }

            var matches = bySource.Values
                .Select(static entry => new OriginalGlossaryMatch(entry.Candidate, entry.Ranges.Count, entry.Ranges))
                .ToList();
            _matchCache[original] = matches;
            return matches;
        }

        private static IReadOnlyList<ScoredGlossaryMatch> SelectLongestNonOverlapping(IEnumerable<ScoredGlossaryMatch> matches)
        {
            var occupied = new Dictionary<string, List<(int Start, int End)>>(StringComparer.Ordinal);
            var accepted = new List<ScoredGlossaryMatch>();
            foreach (var match in matches
                         .OrderByDescending(static match => GetOverlapPriority(match.Candidate.Hint))
                         .ThenByDescending(static match => match.Candidate.Hint.Source.Length)
                         .ThenByDescending(static match => match.Score)
                         .ThenBy(static match => match.Candidate.Hint.Source, StringComparer.Ordinal))
            {
                if (match.Ranges.All(range => IsRangeOverlapping(occupied, range)))
                {
                    continue;
                }

                accepted.Add(match);
                foreach (var range in match.Ranges)
                {
                    if (!occupied.TryGetValue(range.Original, out var ranges))
                    {
                        ranges = [];
                        occupied[range.Original] = ranges;
                    }

                    ranges.Add((range.Start, range.End));
                }
            }

            return accepted;
        }

        private static bool IsRangeOverlapping(
            Dictionary<string, List<(int Start, int End)>> occupied,
            GlossaryMatchRange range)
        {
            return occupied.TryGetValue(range.Original, out var ranges)
                && ranges.Any(existing => range.Start < existing.End && range.End > existing.Start);
        }

        private static bool IsTooShortGeneralCandidate(GlossaryHint hint, string source, int minSourceLength)
        {
            if (source.Length >= minSourceLength)
            {
                return false;
            }

            return !hint.IsUserPromptingDictionary
                && !hint.IsReferenceBearingKey
                && !hint.IsBundledDictionary
                && !IdentifierSegmentTypes.IsIdentifier(hint.SourceSegmentType);
        }

        internal static int GetStaticScore(GlossaryHint hint, string source)
        {
            var score = hint.IsBundledDictionary
                ? 55 + (source.Length * 3)
                : 100 + (source.Length * 3);
            if (hint.IsUserPromptingDictionary)
            {
                score += 30;
            }

            if (hint.IsReferenceBearingKey)
            {
                score += 20;
            }

            if (IdentifierSegmentTypes.IsIdentifier(hint.SourceSegmentType))
            {
                score += 15;
            }

            if (hint.IsBundledDictionary)
            {
                score += Math.Clamp(hint.BundledDictionaryPriority / 500, 0, 20);
                if (source.Any(static ch => ch is >= '\u3400' and <= '\u9fff'))
                {
                    score += 10;
                }

                if (hint.IsBundledDictionaryName)
                {
                    score -= 20;
                }
            }

            if (string.Equals(hint.SourceStatus, "수동 수정", StringComparison.Ordinal))
            {
                score += 20;
            }
            else if (string.Equals(hint.SourceStatus, "검수 필요", StringComparison.Ordinal))
            {
                score -= 25;
            }

            return score;
        }

        private static int GetOverlapPriority(GlossaryHint hint)
        {
            if (hint.IsUserPromptingDictionary)
            {
                return 3;
            }

            return hint.IsBundledDictionary ? 1 : 2;
        }

        internal static int EstimateRenderedLength(string source, string target)
        {
            return source.Length + target.Length + 4;
        }
    }

    public sealed record GlossarySelectionOptions(
        int MaxHintsPerBatch,
        int CharacterBudget,
        int MinSourceLength)
    {
        public static GlossarySelectionOptions Default { get; } = new(
            MaxGlossaryHintsPerBatch,
            DefaultGlossaryCharacterBudget,
            DefaultGlossaryMinSourceLength);
    }

    private sealed record GlossaryCandidate(
        GlossaryHint Hint,
        int RenderedLength,
        int StaticScore);

    private sealed record OriginalGlossaryMatch(
        GlossaryCandidate Candidate,
        int OccurrenceCount,
        IReadOnlyList<GlossaryMatchRange> Ranges);

    private sealed record ScoredGlossaryMatch(
        GlossaryCandidate Candidate,
        int OccurrenceCount,
        IReadOnlyList<GlossaryMatchRange> Ranges,
        int Score);

    private sealed record GlossaryMatchRange(
        string Original,
        int Start,
        int End);

    private GlossaryCacheEntry? TryBuildCacheEntry(
        ExtractedTextItem item,
        string eligibilityHash,
        DateTimeOffset updatedAtUtc)
    {
        var phaseMask = GetAllowedPhaseMask(item);
        if (phaseMask == 0 || !IsEligibleSource(item))
        {
            return null;
        }

        var source = Normalize(item.OriginalText);
        var target = Normalize(item.TranslatedText);
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        var hint = new GlossaryHint(source, target, item.FileType)
        {
            SourceStatus = item.Status,
            SourceSegmentType = item.SegmentType,
            SourceNamespace = item.SymbolNamespace,
            IsReferenceBearingKey = item.IsReferenceBearingKey,
        };
        return new GlossaryCacheEntry(
            item.SegmentId,
            source,
            target,
            item.FileType,
            item.Status,
            item.SegmentType,
            item.SymbolNamespace,
            item.IsReferenceBearingKey,
            GlossaryBatchSelector.EstimateRenderedLength(source, target),
            GlossaryBatchSelector.GetStaticScore(hint, source),
            source[0].ToString(),
            phaseMask,
            eligibilityHash,
            GlossaryEligibilityPolicyVersion,
            updatedAtUtc);
    }

    public static string ComputeEligibilityHash(ExtractedTextItem item)
    {
        var builder = new StringBuilder();
        AppendHashPart(builder, item.OriginalText);
        AppendHashPart(builder, item.TranslatedText);
        AppendHashPart(builder, item.Status);
        AppendHashPart(builder, item.ValidationStatus);
        AppendHashPart(builder, item.CanSave ? "1" : "0");
        AppendHashPart(builder, item.FileType);
        AppendHashPart(builder, item.SegmentType);
        AppendHashPart(builder, ((int)item.CsvFieldRole).ToString(System.Globalization.CultureInfo.InvariantCulture));
        AppendHashPart(builder, item.IsReferenceBearingKey ? "1" : "0");
        AppendHashPart(builder, item.SymbolNamespace);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes);
    }

    private static void AppendHashPart(StringBuilder builder, string? value)
    {
        builder.Append(value?.Length ?? 0);
        builder.Append(':');
        builder.Append(value);
        builder.Append('|');
    }

    private bool IsEligibleSource(ExtractedTextItem item)
    {
        if (!item.IsTranslatedSuccessfully
            || string.IsNullOrWhiteSpace(item.OriginalText)
            || string.IsNullOrWhiteSpace(item.TranslatedText))
        {
            return false;
        }

        if (ContainsLineBreak(item.OriginalText)
            || ContainsLineBreak(item.TranslatedText)
            || item.OriginalText.Trim().Length > MaxGlossarySourceLength
            || item.TranslatedText.Trim().Length > MaxGlossaryTargetLength)
        {
            return false;
        }

        if (_placeholderProtector.Protect(item.OriginalText).Placeholders.Count > 0
            || TextHeuristics.LooksLikeCodeOnly(item.OriginalText)
            || TextHeuristics.LooksLikeErbSymbolExpression(item.OriginalText)
            || TextHeuristics.IsNumericLike(item.OriginalText))
        {
            return false;
        }

        return true;
    }

    private static int GetAllowedPhaseMask(ExtractedTextItem item)
    {
        if (DocumentFileTypes.IsCsvLike(item.FileType))
        {
            var mask = 0;
            if (item.IsReferenceBearingKey)
            {
                mask |= CsvGeneralPhaseMask | ErbIdentifiersPhaseMask | ErhPhaseMask | ErbPhaseMask;
            }

            return mask;
        }

        if (IdentifierSegmentTypes.IsIdentifier(item.SegmentType))
        {
            return ErhPhaseMask | ErbPhaseMask;
        }

        return string.Equals(item.FileType, "ERH", StringComparison.OrdinalIgnoreCase)
            ? ErbPhaseMask
            : 0;
    }

    internal static int GetPhaseMask(TranslationPhaseKind phase)
    {
        return phase switch
        {
            TranslationPhaseKind.CsvGeneral => CsvGeneralPhaseMask,
            TranslationPhaseKind.ErbIdentifiers => ErbIdentifiersPhaseMask,
            TranslationPhaseKind.Erh => ErhPhaseMask,
            TranslationPhaseKind.Erb => ErbPhaseMask,
            _ => 0,
        };
    }

    private static bool ContainsLineBreak(string value)
    {
        return value.Contains('\r') || value.Contains('\n');
    }

    private static string Normalize(string value)
    {
        return value.Trim();
    }
}
