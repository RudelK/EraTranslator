using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class PhaseScopedGlossaryBuilder
{
    private const int MaxGlossarySourceLength = 40;
    private const int MaxGlossaryHintsPerBatch = 8;

    private readonly PlaceholderProtector _placeholderProtector = new();

    public IReadOnlyList<GlossaryHint> BuildForPhase(IEnumerable<ExtractedTextItem> items, TranslationPhaseKind phase)
    {
        if (phase == TranslationPhaseKind.CsvReferenceKeys)
        {
            return [];
        }

        var hints = items
            .Where(item => IsEligibleSource(item, phase))
            .Select(item => new GlossaryHint(
                Normalize(item.OriginalText),
                Normalize(item.TranslatedText),
                item.FileType))
            .Where(static hint => !string.IsNullOrWhiteSpace(hint.Source) && !string.IsNullOrWhiteSpace(hint.Target))
            .GroupBy(static hint => hint.Source, StringComparer.Ordinal)
            .SelectMany(group =>
            {
                var targets = group
                    .Select(static hint => hint.Target)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (targets.Count != 1)
                {
                    return Enumerable.Empty<GlossaryHint>();
                }

                return new[]
                {
                    group.OrderByDescending(static hint => hint.Source.Length).First() with { Target = targets[0] },
                };
            })
            .OrderByDescending(static hint => hint.Source.Length)
            .ThenBy(static hint => hint.Source, StringComparer.Ordinal)
            .ToList();

        return hints;
    }

    public IReadOnlyList<GlossaryHint> SelectForBatch(
        IReadOnlyList<GlossaryHint> availableHints,
        IReadOnlyList<ExtractedTextItem> batchItems)
    {
        if (availableHints.Count == 0 || batchItems.Count == 0)
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

        return availableHints
            .Where(hint => originals.Any(original => original.Contains(hint.Source, StringComparison.Ordinal)))
            .OrderByDescending(static hint => hint.Source.Length)
            .ThenBy(static hint => hint.Source, StringComparer.Ordinal)
            .GroupBy(static hint => hint.Target, StringComparer.Ordinal)
            .Select(static group => group.First())
            .Take(MaxGlossaryHintsPerBatch)
            .ToList();
    }

    private bool IsEligibleSource(ExtractedTextItem item, TranslationPhaseKind phase)
    {
        if (!item.IsTranslatedSuccessfully
            || string.IsNullOrWhiteSpace(item.OriginalText)
            || string.IsNullOrWhiteSpace(item.TranslatedText))
        {
            return false;
        }

        if (!IsAllowedFileType(item, phase)
            || ContainsLineBreak(item.OriginalText)
            || ContainsLineBreak(item.TranslatedText)
            || item.OriginalText.Trim().Length > MaxGlossarySourceLength)
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

    private static bool IsAllowedFileType(ExtractedTextItem item, TranslationPhaseKind phase)
    {
        if (string.Equals(item.FileType, "CSV", StringComparison.OrdinalIgnoreCase))
        {
            return phase switch
            {
                TranslationPhaseKind.CsvGeneral => item.IsReferenceBearingKey,
                TranslationPhaseKind.Erh or TranslationPhaseKind.Erb => item.IsReferenceBearingKey
                    || item.CsvFieldRole == CsvFieldRole.TranslatableValue,
                _ => false,
            };
        }

        return phase == TranslationPhaseKind.Erb
            && string.Equals(item.FileType, "ERH", StringComparison.OrdinalIgnoreCase);
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
