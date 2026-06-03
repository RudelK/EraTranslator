using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class IdentifierRewritePlanner
{
    public IdentifierRewritePlan CreatePlan(ScanSession session)
    {
        var candidateMap = session.Items
            .Where(item => IdentifierSegmentTypes.IsIdentifier(item.SegmentType)
                && item.IsTranslatedSuccessfully
                && !item.IsExcluded
                && IdentifierSegmentTypes.TryGetKind(item.SegmentType, out _))
            .Select(item =>
            {
                IdentifierSegmentTypes.TryGetKind(item.SegmentType, out var kind);
                return new
                {
                    Kind = kind,
                    OriginalName = item.OriginalText,
                    TranslatedName = TranslationQualityRules.NormalizeIdentifierText(item.TranslatedText),
                };
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.TranslatedName)
                && !string.Equals(item.OriginalName, item.TranslatedName, StringComparison.Ordinal)
                && TranslationQualityRules.GetIdentifierHardFailureReason(item.TranslatedName) is null)
            .GroupBy(item => (item.Kind, item.OriginalName), IdentifierKeyComparer.Instance)
            .ToDictionary(
                group => group.Key,
                group => group.Last().TranslatedName,
                IdentifierKeyComparer.Instance);

        var collisions = candidateMap
            .GroupBy(pair => (pair.Key.Kind, TranslatedName: pair.Value), IdentifierCollisionKeyComparer.Instance)
            .Where(group => group.Select(pair => pair.Key.OriginalName).Distinct(StringComparer.Ordinal).Count() > 1)
            .SelectMany(group => group.Select(pair => pair.Key))
            .ToHashSet(IdentifierKeyComparer.Instance);

        foreach (var key in collisions)
        {
            candidateMap.Remove(key);
        }

        var replacements = new Dictionary<string, List<PlannedIdentifierReplacement>>(StringComparer.Ordinal);
        foreach (var document in session.Documents.Values.Where(document => DocumentFileTypes.IsErbLike(document.FileType)))
        {
            foreach (var occurrence in document.IdentifierOccurrences)
            {
                if (!candidateMap.TryGetValue((occurrence.Kind, occurrence.OriginalName), out var translatedName))
                {
                    continue;
                }

                if (!replacements.TryGetValue(document.DocumentId, out var documentReplacements))
                {
                    documentReplacements = [];
                    replacements[document.DocumentId] = documentReplacements;
                }

                documentReplacements.Add(new PlannedIdentifierReplacement(
                    occurrence.AbsoluteStart,
                    occurrence.Length,
                    translatedName,
                    occurrence.Kind,
                    occurrence.Role));
            }
        }

        return new IdentifierRewritePlan(replacements);
    }

    private sealed class IdentifierKeyComparer : IEqualityComparer<(ErbIdentifierKind Kind, string OriginalName)>
    {
        public static IdentifierKeyComparer Instance { get; } = new();

        public bool Equals((ErbIdentifierKind Kind, string OriginalName) x, (ErbIdentifierKind Kind, string OriginalName) y)
        {
            return x.Kind == y.Kind && string.Equals(x.OriginalName, y.OriginalName, StringComparison.Ordinal);
        }

        public int GetHashCode((ErbIdentifierKind Kind, string OriginalName) obj)
        {
            return HashCode.Combine(obj.Kind, StringComparer.Ordinal.GetHashCode(obj.OriginalName));
        }
    }

    private sealed class IdentifierCollisionKeyComparer : IEqualityComparer<(ErbIdentifierKind Kind, string TranslatedName)>
    {
        public static IdentifierCollisionKeyComparer Instance { get; } = new();

        public bool Equals((ErbIdentifierKind Kind, string TranslatedName) x, (ErbIdentifierKind Kind, string TranslatedName) y)
        {
            return x.Kind == y.Kind && string.Equals(x.TranslatedName, y.TranslatedName, StringComparison.Ordinal);
        }

        public int GetHashCode((ErbIdentifierKind Kind, string TranslatedName) obj)
        {
            return HashCode.Combine(obj.Kind, StringComparer.Ordinal.GetHashCode(obj.TranslatedName));
        }
    }
}

public sealed class IdentifierRewritePlan(
    IReadOnlyDictionary<string, List<PlannedIdentifierReplacement>> documentReplacements)
{
    public IReadOnlyDictionary<string, List<PlannedIdentifierReplacement>> DocumentReplacements { get; } = documentReplacements;
}

public sealed record PlannedIdentifierReplacement(
    int Start,
    int Length,
    string Value,
    ErbIdentifierKind Kind,
    ErbIdentifierRole Role);
