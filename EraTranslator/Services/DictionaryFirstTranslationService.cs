using EraTranslator.Models;

namespace EraTranslator.Services;

public interface IDictionaryFirstTranslationService
{
    Task<DictionaryFirstTranslationMatch?> TryResolveAsync(ExtractedTextItem item, ProviderSettings settings, CancellationToken cancellationToken);
}

public sealed class DictionaryFirstTranslationService(
    IBundledJapaneseLexiconService? lexiconService = null,
    JapaneseReadingFallbackService? readingFallbackService = null,
    INaverJapaneseDictionaryStore? naverDictionaryStore = null,
    INaverJapaneseDictionaryLookupService? naverLookupService = null) : IDictionaryFirstTranslationService
{
    private static readonly HashSet<string> CharacterNameValueSourceKeys =
    [
        "名前",
        "呼び名",
        "彼氏姓",
        "彼氏名",
    ];

    private readonly IBundledJapaneseLexiconService _lexiconService = lexiconService ?? new BundledJapaneseLexiconService();
    private readonly JapaneseReadingFallbackService _readingFallbackService = readingFallbackService ?? new JapaneseReadingFallbackService();
    private readonly INaverJapaneseDictionaryStore _naverDictionaryStore = naverDictionaryStore ?? new NaverJapaneseDictionaryStore();
    private readonly INaverJapaneseDictionaryLookupService _naverLookupService = naverLookupService ?? new NaverJapaneseDictionaryLookupService();

    public async Task<DictionaryFirstTranslationMatch?> TryResolveAsync(
        ExtractedTextItem item,
        ProviderSettings settings,
        CancellationToken cancellationToken)
    {
        if (!IsSupportedLanguagePair(settings)
            || IsCharacterSheetNameValue(item)
            || !IsEligible(item, settings.DictionaryFirstMaxTermLength))
        {
            return null;
        }

        if (!settings.EnableBundledDictionaryFirstPass
            && !settings.EnableKanaTransliterationFallback
            && !settings.EnableNaverJapaneseDictionaryLookup
            && !settings.EnableKanjiReadingFallback)
        {
            return null;
        }

        var original = (item.OriginalText ?? string.Empty).Trim();
        if (settings.EnableBundledDictionaryFirstPass
            && TryResolveExactOrReading(original, out var bundledMatch))
        {
            return bundledMatch;
        }

        if (settings.EnableKanaTransliterationFallback
            && _readingFallbackService.TryTransliterateKatakana(original, out var kanaFallback))
        {
            return new DictionaryFirstTranslationMatch(kanaFallback, "카타카나 음독", false, string.Empty, "katakana", original, "fallback");
        }

        if (settings.EnableNaverJapaneseDictionaryLookup
            && TryResolveNaverLocal(original, out var naverLocalMatch))
        {
            return naverLocalMatch;
        }

        if (settings.EnableNaverJapaneseDictionaryLookup)
        {
            var naverEntry = await _naverLookupService.TryLookupAsync(original, cancellationToken).ConfigureAwait(false);
            if (naverEntry is not null)
            {
                _naverDictionaryStore.Upsert(naverEntry);
                return BuildNaverMatch(naverEntry, online: true);
            }
        }

        if (settings.EnableKanjiReadingFallback
            && _readingFallbackService.TryBuildKanjiReading(original, _lexiconService, out var kanjiFallback))
        {
            return new DictionaryFirstTranslationMatch(
                kanjiFallback,
                "한자 음독",
                true,
                "한자 음독 fallback 결과입니다. 의미 번역과 다를 수 있어 검토가 필요합니다.",
                "kanji",
                original,
                "fallback");
        }

        return null;
    }

    private bool TryResolveExactOrReading(string original, out DictionaryFirstTranslationMatch match)
    {
        match = default;
        if (_lexiconService.TryGetSurfaceEntry(original, out var surfaceEntry))
        {
            if (!string.IsNullOrWhiteSpace(surfaceEntry.KoTarget))
            {
                match = new DictionaryFirstTranslationMatch(surfaceEntry.KoTarget!, "사전 번역", false, string.Empty, "surface", surfaceEntry.Surface, "bundled");
                return true;
            }
        }

        if (_lexiconService.TryGetReadingEntry(original, out var readingEntry)
            && !string.IsNullOrWhiteSpace(readingEntry.KoTarget))
        {
            match = new DictionaryFirstTranslationMatch(readingEntry.KoTarget!, "사전 번역", false, string.Empty, "reading", readingEntry.ReadingKana, "bundled");
            return true;
        }

        return false;
    }

    private bool TryResolveNaverLocal(string original, out DictionaryFirstTranslationMatch match)
    {
        match = default;
        if (!_naverDictionaryStore.TryGet(original, out var entry))
        {
            return false;
        }

        match = BuildNaverMatch(entry, online: false);
        return true;
    }

    private static DictionaryFirstTranslationMatch BuildNaverMatch(NaverJapaneseDictionaryEntry entry, bool online)
    {
        var reviewReason = entry.ReviewRequired
            ? "네이버 사전 결과가 다의어 또는 설명형일 수 있어 검토가 필요합니다."
            : string.Empty;
        return new DictionaryFirstTranslationMatch(
            entry.KoTarget,
            "네이버 사전",
            entry.ReviewRequired,
            reviewReason,
            online ? "naver-online" : "naver-local",
            entry.Surface,
            "naver",
            online,
            entry.SourceUrl,
            entry.ReviewRequired);
    }

    private static bool IsSupportedLanguagePair(ProviderSettings settings)
    {
        var source = (settings.SourceLanguage ?? string.Empty).Trim().ToLowerInvariant();
        var target = (settings.TargetLanguage ?? string.Empty).Trim().ToLowerInvariant();
        return source is "ja" or "jp" && target == "ko";
    }

    private static bool IsEligible(ExtractedTextItem item, int maxMeaningfulLength)
    {
        var original = (item.OriginalText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(original)
            || original.Contains('\r')
            || original.Contains('\n')
            || TextHeuristics.IsNumericLike(original)
            || TextHeuristics.LooksLikeCodeOnly(original)
            || TextHeuristics.LooksLikeErbSymbolExpression(original)
            || !TextHeuristics.ContainsTranslatableText(original))
        {
            return false;
        }

        if (item.IsReferenceBearingKey)
        {
            return true;
        }

        if (HasSentenceLikePunctuation(original) || original.Any(char.IsWhiteSpace))
        {
            return false;
        }

        return CountMeaningfulLength(original) <= Math.Clamp(maxMeaningfulLength, 1, 12);
    }

    private static bool IsCharacterSheetNameValue(ExtractedTextItem item)
    {
        return DocumentFileTypes.IsCsvLike(item.FileType)
            && string.Equals(item.SegmentType, "csv-CharacterSheet-field-1", StringComparison.Ordinal)
            && item.CsvFieldRole == CsvFieldRole.TranslatableValue
            && CharacterNameValueSourceKeys.Contains((item.SourceKey ?? string.Empty).Trim());
    }

    private static int CountMeaningfulLength(string text)
    {
        return text.Count(static ch => !char.IsWhiteSpace(ch));
    }

    private static bool HasSentenceLikePunctuation(string text)
    {
        return text.IndexOfAny(['。', '！', '？', '!', '?', '\'', '"']) >= 0;
    }
}

public readonly record struct DictionaryFirstTranslationMatch(
    string TranslatedText,
    string TranslationSource,
    bool ForceReview,
    string ReviewReason,
    string MatchKind = "",
    string MatchedTerm = "",
    string DictionaryStore = "",
    bool PersistedToNaverDictionary = false,
    string SourceUrl = "",
    bool ReviewRequired = false);
