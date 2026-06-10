using System.Diagnostics;
using System.Globalization;
using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class BundledJapaneseLexiconGlossaryProvider(
    IBundledJapaneseLexiconService lexiconService,
    PhaseScopedGlossaryBuilder glossaryBuilder,
    ProviderSettings settings,
    Action<string, string, IReadOnlyDictionary<string, string>?>? logPerformanceDebug = null) : IGlossaryCandidateProvider
{
    public IReadOnlyList<GlossaryHint> LoadCandidates(IReadOnlyList<ExtractedTextItem> batchItems)
    {
        if (!CanUseBundledGlossary(settings) || batchItems.Count == 0)
        {
            return [];
        }

        var originals = batchItems
            .Select(static item => item.OriginalText)
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (originals.Count == 0)
        {
            return [];
        }

        var stopwatch = Stopwatch.StartNew();
        var maxLookupCandidates = Math.Clamp(settings.BundledDictionaryGlossaryMaxHintsPerBatch * 12, 16, 300);
        var lookup = lexiconService.FindGlossaryCandidates(
            originals,
            settings.BundledDictionaryGlossaryMinTermLength,
            settings.BundledDictionaryGlossaryMaxTermLength,
            maxLookupCandidates);
        var hints = lookup.Entries
            .Select(static entry => new GlossaryHint(entry.Surface, entry.KoTarget, "BUNDLED-JA")
            {
                SourceStatus = "탑재 일어사전",
                SourceSegmentType = "bundled-japanese-lexicon",
                IsBundledDictionary = true,
                BundledDictionaryPriority = entry.Priority,
                IsBundledDictionaryName = entry.IsName,
            })
            .ToList();
        var selected = glossaryBuilder
            .CreateBatchSelector(
                hints,
                new ProviderSettings
                {
                    EnableGlossaryHints = true,
                    GlossaryMaxHintsPerBatch = settings.BundledDictionaryGlossaryMaxHintsPerBatch,
                    GlossaryCharacterBudget = settings.BundledDictionaryGlossaryCharacterBudget,
                    GlossaryMinSourceLength = settings.BundledDictionaryGlossaryMinTermLength,
                })
            .SelectForBatch(batchItems);
        stopwatch.Stop();

        logPerformanceDebug?.Invoke(
            "BUNDLED_LEXICON_GLOSSARY_LOOKUP",
            "현재 batch 원문 기준으로 탑재 일어사전 glossary 후보를 조회했습니다.",
            new Dictionary<string, string>
            {
                ["elapsed_ms"] = stopwatch.Elapsed.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture),
                ["batch_count"] = batchItems.Count.ToString(CultureInfo.InvariantCulture),
                ["original_count"] = originals.Count.ToString(CultureInfo.InvariantCulture),
                ["substring_candidate_count"] = lookup.SurfaceCandidateCount.ToString(CultureInfo.InvariantCulture),
                ["db_hit_count"] = lookup.DbHitCount.ToString(CultureInfo.InvariantCulture),
                ["candidate_hint_count"] = hints.Count.ToString(CultureInfo.InvariantCulture),
                ["selected_hint_count"] = selected.Count.ToString(CultureInfo.InvariantCulture),
                ["selected_hint_chars"] = selected.Sum(static hint => hint.Source.Length + hint.Target.Length).ToString(CultureInfo.InvariantCulture),
            });

        return selected;
    }

    private static bool CanUseBundledGlossary(ProviderSettings settings)
    {
        if (!settings.EnableGlossaryHints
            || !settings.EnableBundledDictionaryGlossaryHints
            || settings.GlossaryMaxHintsPerBatch <= 0
            || settings.GlossaryCharacterBudget <= 0
            || settings.BundledDictionaryGlossaryMaxHintsPerBatch <= 0
            || settings.BundledDictionaryGlossaryCharacterBudget <= 0)
        {
            return false;
        }

        var source = (settings.SourceLanguage ?? string.Empty).Trim().ToLowerInvariant();
        var target = (settings.TargetLanguage ?? string.Empty).Trim().ToLowerInvariant();
        return source is "ja" or "jp" && target == "ko";
    }
}

public sealed class CompositeGlossaryCandidateProvider(IReadOnlyList<IGlossaryCandidateProvider> providers) : IGlossaryCandidateProvider
{
    public IReadOnlyList<GlossaryHint> LoadCandidates(IReadOnlyList<ExtractedTextItem> batchItems)
    {
        if (providers.Count == 0)
        {
            return [];
        }

        var merged = new Dictionary<string, GlossaryHint>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            foreach (var hint in provider.LoadCandidates(batchItems))
            {
                if (string.IsNullOrWhiteSpace(hint.Source) || string.IsNullOrWhiteSpace(hint.Target))
                {
                    continue;
                }

                if (!merged.TryGetValue(hint.Source, out var current)
                    || GetPrecedence(hint) >= GetPrecedence(current))
                {
                    merged[hint.Source] = hint;
                }
            }
        }

        return merged.Values
            .OrderByDescending(static hint => GetPrecedence(hint))
            .ThenByDescending(static hint => hint.Source.Length)
            .ThenBy(static hint => hint.Source, StringComparer.Ordinal)
            .ToList();
    }

    private static int GetPrecedence(GlossaryHint hint)
    {
        if (hint.IsUserPromptingDictionary)
        {
            return 4;
        }

        if (hint.IsReferenceBearingKey || IdentifierSegmentTypes.IsIdentifier(hint.SourceSegmentType))
        {
            return 3;
        }

        return hint.IsBundledDictionary ? 1 : 2;
    }
}
