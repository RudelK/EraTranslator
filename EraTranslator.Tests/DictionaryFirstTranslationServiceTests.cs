using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class DictionaryFirstTranslationServiceTests
{
    [Fact]
    public void TryResolve_UsesExactKoTargetWhenAvailable()
    {
        var service = new DictionaryFirstTranslationService(
            new FakeLexiconService(
                surfaceEntries: new Dictionary<string, BundledJapaneseLexiconEntry>(StringComparer.Ordinal)
                {
                    ["快楽"] = new("快楽", "かいらく", 100, "noun", false, "쾌락"),
                }));

        var resolved = TryResolve(service, BuildItem("快楽"), BuildSettings(), out var match);

        Assert.True(resolved);
        Assert.Equal("쾌락", match.TranslatedText);
        Assert.Equal("사전 번역", match.TranslationSource);
        Assert.False(match.ForceReview);
    }

    [Fact]
    public void TryResolve_UsesKanaFallbackForShortKatakanaLabels()
    {
        var service = new DictionaryFirstTranslationService(new FakeLexiconService());

        var resolved = TryResolve(service, BuildItem("レディ"), BuildSettings(), out var match);

        Assert.True(resolved);
        Assert.Equal("카타카나 음독", match.TranslationSource);
        Assert.Equal("레디", match.TranslatedText);
        Assert.False(match.ForceReview);
    }

    [Fact]
    public void TryResolve_UsesKanjiReadingFallbackAndMarksReview()
    {
        var service = new DictionaryFirstTranslationService(
            new FakeLexiconService(
                kanjiEntries: new Dictionary<string, BundledKanjiReadingEntry>(StringComparer.Ordinal)
                {
                    ["快"] = new("快", "쾌", "カイ", string.Empty),
                    ["楽"] = new("楽", "락", "ラク", string.Empty),
                }));

        var resolved = TryResolve(service, BuildItem("快楽"), BuildSettings(), out var match);

        Assert.True(resolved);
        Assert.Equal("쾌락", match.TranslatedText);
        Assert.Equal("한자 음독", match.TranslationSource);
        Assert.True(match.ForceReview);
    }

    [Fact]
    public void TryResolve_DoesNotUseKatakanaFallbackForHiraganaOnlyLabels()
    {
        var service = new DictionaryFirstTranslationService(new FakeLexiconService());

        var resolved = TryResolve(service, BuildItem("れでぃ"), BuildSettings(), out _);

        Assert.False(resolved);
    }

    [Fact]
    public void TryResolve_UsesKatakanaFallbackEvenWhenExactMatchIsDisabled()
    {
        var service = new DictionaryFirstTranslationService(new FakeLexiconService());
        var settings = BuildSettings(
            enableExactMatch: false,
            enableKatakanaFallback: true,
            enableKanjiFallback: true);

        var resolved = TryResolve(service, BuildItem("レディ"), settings, out var match);

        Assert.True(resolved);
        Assert.Equal("카타카나 음독", match.TranslationSource);
        Assert.Equal("레디", match.TranslatedText);
    }

    [Fact]
    public void TryResolve_UsesKanjiFallbackEvenWhenExactMatchIsDisabled()
    {
        var service = new DictionaryFirstTranslationService(
            new FakeLexiconService(
                kanjiEntries: new Dictionary<string, BundledKanjiReadingEntry>(StringComparer.Ordinal)
                {
                    ["依"] = new("依", "의", "イ", string.Empty),
                    ["存"] = new("存", "존", "ソン", string.Empty),
                    ["度"] = new("度", "도", "ド", string.Empty),
                }));
        var settings = BuildSettings(
            enableExactMatch: false,
            enableKatakanaFallback: false,
            enableKanjiFallback: true);

        var resolved = TryResolve(service, BuildItem("依存度"), settings, out var match);

        Assert.True(resolved);
        Assert.Equal("한자 음독", match.TranslationSource);
        Assert.Equal("의존도", match.TranslatedText);
        Assert.True(match.ForceReview);
    }

    [Theory]
    [InlineData("楽園", "낙원")]
    [InlineData("露出", "노출")]
    [InlineData("女王", "여왕")]
    [InlineData("龍族", "용족")]
    [InlineData("能力", "능력")]
    [InlineData("楽園／龍族", "낙원／용족")]
    [InlineData("女王・楽園", "여왕・낙원")]
    public void TryResolve_AppliesInitialSoundRuleToKanjiFallbackTokenStarts(string original, string expected)
    {
        var service = new DictionaryFirstTranslationService(
            new FakeLexiconService(
                kanjiEntries: BuildKanjiEntries()));
        var settings = BuildSettings(
            enableExactMatch: false,
            enableKatakanaFallback: false,
            enableKanjiFallback: true);

        var resolved = TryResolve(service, BuildItem(original), settings, out var match);

        Assert.True(resolved);
        Assert.Equal(expected, match.TranslatedText);
        Assert.Equal("한자 음독", match.TranslationSource);
        Assert.True(match.ForceReview);
    }

    [Theory]
    [InlineData("快楽", "쾌락")]
    [InlineData("処女", "처녀")]
    [InlineData("依存度", "의존도")]
    public void TryResolve_DoesNotApplyInitialSoundRuleInsideKanjiFallbackTokens(string original, string expected)
    {
        var service = new DictionaryFirstTranslationService(
            new FakeLexiconService(
                kanjiEntries: BuildKanjiEntries()));
        var settings = BuildSettings(
            enableExactMatch: false,
            enableKatakanaFallback: false,
            enableKanjiFallback: true);

        var resolved = TryResolve(service, BuildItem(original), settings, out var match);

        Assert.True(resolved);
        Assert.Equal(expected, match.TranslatedText);
    }

    [Fact]
    public void TryResolve_DoesNotApplyInitialSoundRuleToExactDictionaryMatch()
    {
        var service = new DictionaryFirstTranslationService(
            new FakeLexiconService(
                surfaceEntries: new Dictionary<string, BundledJapaneseLexiconEntry>(StringComparer.Ordinal)
                {
                    ["楽園"] = new("楽園", "らくえん", 100, "noun", false, "락원"),
                },
                kanjiEntries: BuildKanjiEntries()));

        var resolved = TryResolve(service, BuildItem("楽園"), BuildSettings(), out var match);

        Assert.True(resolved);
        Assert.Equal("락원", match.TranslatedText);
        Assert.Equal("사전 번역", match.TranslationSource);
    }

    [Fact]
    public void TryResolve_DoesNotCallNaverWhenExactDictionaryMatches()
    {
        var store = new FakeNaverDictionaryStore();
        var lookup = new FakeNaverLookupService(new NaverJapaneseDictionaryEntry("快楽", string.Empty, "쾌락-네이버", "https://example.test", false));
        var service = new DictionaryFirstTranslationService(
            new FakeLexiconService(
                surfaceEntries: new Dictionary<string, BundledJapaneseLexiconEntry>(StringComparer.Ordinal)
                {
                    ["快楽"] = new("快楽", "かいらく", 100, "noun", false, "쾌락"),
                }),
            naverDictionaryStore: store,
            naverLookupService: lookup);
        var settings = BuildSettings(enableNaverLookup: true);

        var resolved = TryResolve(service, BuildItem("快楽"), settings, out var match);

        Assert.True(resolved);
        Assert.Equal("사전 번역", match.TranslationSource);
        Assert.Equal(0, store.GetCount);
        Assert.Equal(0, lookup.CallCount);
    }

    [Fact]
    public void TryResolve_DoesNotCallNaverWhenKatakanaFallbackMatches()
    {
        var store = new FakeNaverDictionaryStore();
        var lookup = new FakeNaverLookupService(new NaverJapaneseDictionaryEntry("レディ", string.Empty, "레이디", "https://example.test", false));
        var service = new DictionaryFirstTranslationService(
            new FakeLexiconService(),
            naverDictionaryStore: store,
            naverLookupService: lookup);
        var settings = BuildSettings(
            enableExactMatch: false,
            enableKatakanaFallback: true,
            enableNaverLookup: true,
            enableKanjiFallback: true);

        var resolved = TryResolve(service, BuildItem("レディ"), settings, out var match);

        Assert.True(resolved);
        Assert.Equal("카타카나 음독", match.TranslationSource);
        Assert.Equal(0, store.GetCount);
        Assert.Equal(0, lookup.CallCount);
    }

    [Fact]
    public void TryResolve_UsesNaverLocalDictionaryBeforeKanjiFallback()
    {
        var store = new FakeNaverDictionaryStore();
        store.Upsert(new NaverJapaneseDictionaryEntry("楽園", string.Empty, "낙원", "https://example.test/local", false));
        var lookup = new FakeNaverLookupService(null);
        var service = new DictionaryFirstTranslationService(
            new FakeLexiconService(kanjiEntries: BuildKanjiEntries()),
            naverDictionaryStore: store,
            naverLookupService: lookup);
        var settings = BuildSettings(
            enableExactMatch: false,
            enableKatakanaFallback: false,
            enableNaverLookup: true,
            enableKanjiFallback: true);

        var resolved = TryResolve(service, BuildItem("楽園"), settings, out var match);

        Assert.True(resolved);
        Assert.Equal("네이버 사전", match.TranslationSource);
        Assert.Equal("naver-local", match.MatchKind);
        Assert.Equal("naver", match.DictionaryStore);
        Assert.Equal("낙원", match.TranslatedText);
        Assert.Equal(0, lookup.CallCount);
    }

    [Fact]
    public void TryResolve_PromotesNaverOnlineResultToLocalDictionary()
    {
        var store = new FakeNaverDictionaryStore();
        var lookup = new FakeNaverLookupService(new NaverJapaneseDictionaryEntry("楽園", "らくえん", "낙원", "https://example.test/online", true));
        var service = new DictionaryFirstTranslationService(
            new FakeLexiconService(kanjiEntries: BuildKanjiEntries()),
            naverDictionaryStore: store,
            naverLookupService: lookup);
        var settings = BuildSettings(
            enableExactMatch: false,
            enableKatakanaFallback: false,
            enableNaverLookup: true,
            enableKanjiFallback: true);

        var firstResolved = TryResolve(service, BuildItem("楽園"), settings, out var firstMatch);
        var secondResolved = TryResolve(service, BuildItem("楽園"), settings, out var secondMatch);

        Assert.True(firstResolved);
        Assert.Equal("naver-online", firstMatch.MatchKind);
        Assert.True(firstMatch.PersistedToNaverDictionary);
        Assert.True(firstMatch.ForceReview);
        Assert.True(secondResolved);
        Assert.Equal("naver-local", secondMatch.MatchKind);
        Assert.Equal(1, lookup.CallCount);
    }

    [Fact]
    public void TryResolve_FallsBackToKanjiWhenNaverMisses()
    {
        var service = new DictionaryFirstTranslationService(
            new FakeLexiconService(kanjiEntries: BuildKanjiEntries()),
            naverDictionaryStore: new FakeNaverDictionaryStore(),
            naverLookupService: new FakeNaverLookupService(null));
        var settings = BuildSettings(
            enableExactMatch: false,
            enableKatakanaFallback: false,
            enableNaverLookup: true,
            enableKanjiFallback: true);

        var resolved = TryResolve(service, BuildItem("楽園"), settings, out var match);

        Assert.True(resolved);
        Assert.Equal("한자 음독", match.TranslationSource);
        Assert.Equal("낙원", match.TranslatedText);
    }

    [Fact]
    public void TryResolve_SkipsNaverForCharacterNameValues()
    {
        var store = new FakeNaverDictionaryStore();
        var lookup = new FakeNaverLookupService(new NaverJapaneseDictionaryEntry("処女", string.Empty, "처녀", "https://example.test", false));
        var service = new DictionaryFirstTranslationService(
            new FakeLexiconService(),
            naverDictionaryStore: store,
            naverLookupService: lookup);
        var item = BuildItem(
            "処女",
            sourceKey: "名前",
            segmentType: "csv-CharacterSheet-field-1",
            fileType: "CSV");

        var resolved = TryResolve(service, item, BuildSettings(enableNaverLookup: true), out _);

        Assert.False(resolved);
        Assert.Equal(0, store.GetCount);
        Assert.Equal(0, lookup.CallCount);
    }

    [Fact]
    public void TryResolve_SkipsLongSentenceLikeText()
    {
        var service = new DictionaryFirstTranslationService(new FakeLexiconService());

        var resolved = TryResolve(service, BuildItem("快楽値が上がった。"), BuildSettings(), out _);

        Assert.False(resolved);
    }

    [Fact]
    public void TryResolve_AllowsReferenceBearingKeysEvenBeyondDefaultLength()
    {
        var service = new DictionaryFirstTranslationService(
            new FakeLexiconService(
                surfaceEntries: new Dictionary<string, BundledJapaneseLexiconEntry>(StringComparer.Ordinal)
                {
                    ["あなた呼び方／陥落前"] = new("あなた呼び方／陥落前", "あなたよびかた", 100, "noun", false, "당신 호칭／함락 전"),
                }));

        var item = BuildItem("あなた呼び方／陥落前", isReferenceBearingKey: true);

        var resolved = TryResolve(service, item, BuildSettings(), out var match);

        Assert.True(resolved);
        Assert.Equal("당신 호칭／함락 전", match.TranslatedText);
    }

    [Theory]
    [InlineData("名前")]
    [InlineData("呼び名")]
    [InlineData("彼氏姓")]
    [InlineData("彼氏名")]
    public void TryResolve_SkipsCharacterSheetNameValuesEvenWhenDictionaryCanResolve(string sourceKey)
    {
        var service = new DictionaryFirstTranslationService(
            new FakeLexiconService(
                surfaceEntries: new Dictionary<string, BundledJapaneseLexiconEntry>(StringComparer.Ordinal)
                {
                    ["処女"] = new("処女", "しょじょ", 1200, "noun", false, "처녀"),
                },
                kanjiEntries: new Dictionary<string, BundledKanjiReadingEntry>(StringComparer.Ordinal)
                {
                    ["処"] = new("処", "처", "ショ", string.Empty),
                    ["女"] = new("女", "녀", "ジョ", "おんな"),
                }));

        var item = BuildItem(
            "処女",
            sourceKey: sourceKey,
            segmentType: "csv-CharacterSheet-field-1",
            fileType: "CSV");

        var resolved = TryResolve(service, item, BuildSettings(), out _);

        Assert.False(resolved);
    }

    [Fact]
    public void TryResolve_StillUsesDictionaryForSameTextOutsideCharacterNameValue()
    {
        var service = new DictionaryFirstTranslationService(
            new FakeLexiconService(
                surfaceEntries: new Dictionary<string, BundledJapaneseLexiconEntry>(StringComparer.Ordinal)
                {
                    ["処女"] = new("処女", "しょじょ", 1200, "noun", false, "처녀"),
                }));

        var resolved = TryResolve(service, BuildItem("処女"), BuildSettings(), out var match);

        Assert.True(resolved);
        Assert.Equal("처녀", match.TranslatedText);
        Assert.Equal("사전 번역", match.TranslationSource);
    }

    private static ExtractedTextItem BuildItem(
        string originalText,
        bool isReferenceBearingKey = false,
        string? sourceKey = null,
        string segmentType = "csv-field",
        string fileType = "CSV")
    {
        return new ExtractedTextItem
        {
            SegmentId = Guid.NewGuid().ToString("N"),
            DocumentId = "doc",
            FileType = fileType,
            RelativePath = "CSV/Test.csv",
            EncodingName = "utf-8",
            SegmentType = segmentType,
            LineNumber = 1,
            OriginalText = originalText,
            SourceKey = sourceKey,
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            IsReferenceBearingKey = isReferenceBearingKey,
            WarningText = string.Empty,
        };
    }

    private static ProviderSettings BuildSettings(
        bool enableExactMatch = true,
        bool enableKatakanaFallback = true,
        bool enableNaverLookup = false,
        bool enableKanjiFallback = true)
    {
        return new ProviderSettings
        {
            ProviderType = TranslationProviderType.OpenAi,
            SourceLanguage = "ja",
            TargetLanguage = "ko",
            EnableBundledDictionaryFirstPass = enableExactMatch,
            EnableKanaTransliterationFallback = enableKatakanaFallback,
            EnableNaverJapaneseDictionaryLookup = enableNaverLookup,
            EnableKanjiReadingFallback = enableKanjiFallback,
            DictionaryFirstMaxTermLength = 6,
        };
    }

    private static bool TryResolve(
        DictionaryFirstTranslationService service,
        ExtractedTextItem item,
        ProviderSettings settings,
        out DictionaryFirstTranslationMatch match)
    {
        var resolved = service.TryResolveAsync(item, settings, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        match = resolved ?? default;
        return resolved.HasValue;
    }

    private static Dictionary<string, BundledKanjiReadingEntry> BuildKanjiEntries()
    {
        return new Dictionary<string, BundledKanjiReadingEntry>(StringComparer.Ordinal)
        {
            ["楽"] = new("楽", "락", "ラク", string.Empty),
            ["園"] = new("園", "원", "エン", string.Empty),
            ["露"] = new("露", "로", "ロ", string.Empty),
            ["出"] = new("出", "출", "シュツ", string.Empty),
            ["女"] = new("女", "녀", "ジョ", "おんな"),
            ["王"] = new("王", "왕", "オウ", string.Empty),
            ["龍"] = new("龍", "룡", "リュウ", string.Empty),
            ["族"] = new("族", "족", "ゾク", string.Empty),
            ["能"] = new("能", "능", "ノウ", string.Empty),
            ["力"] = new("力", "력", "リョク", string.Empty),
            ["快"] = new("快", "쾌", "カイ", string.Empty),
            ["処"] = new("処", "처", "ショ", string.Empty),
            ["依"] = new("依", "의", "イ", string.Empty),
            ["存"] = new("存", "존", "ソン", string.Empty),
            ["度"] = new("度", "도", "ド", string.Empty),
        };
    }

    private sealed class FakeLexiconService(
        Dictionary<string, BundledJapaneseLexiconEntry>? surfaceEntries = null,
        Dictionary<string, BundledJapaneseLexiconEntry>? readingEntries = null,
        Dictionary<string, BundledKanjiReadingEntry>? kanjiEntries = null) : IBundledJapaneseLexiconService
    {
        private readonly Dictionary<string, BundledJapaneseLexiconEntry> _surfaceEntries = surfaceEntries ?? new(StringComparer.Ordinal);
        private readonly Dictionary<string, BundledJapaneseLexiconEntry> _readingEntries = readingEntries ?? new(StringComparer.Ordinal);
        private readonly Dictionary<string, BundledKanjiReadingEntry> _kanjiEntries = kanjiEntries ?? new(StringComparer.Ordinal);

        public string NoticeFilePath => string.Empty;

        public string GetAttributionText() => string.Empty;

        public string GetSnapshotSummary() => string.Empty;

        public BundledJapaneseLexiconGlossaryLookupResult FindGlossaryCandidates(
            IReadOnlyList<string> originals,
            int minTermLength,
            int maxTermLength,
            int maxCandidates) => BundledJapaneseLexiconGlossaryLookupResult.Empty;

        public bool TryGetKanjiReading(char kanji, out BundledKanjiReadingEntry entry) => _kanjiEntries.TryGetValue(kanji.ToString(), out entry!);

        public bool TryGetReadingEntry(string term, out BundledJapaneseLexiconEntry entry) => _readingEntries.TryGetValue(term, out entry!);

        public bool TryGetSurfaceEntry(string term, out BundledJapaneseLexiconEntry entry) => _surfaceEntries.TryGetValue(term, out entry!);
    }

    private sealed class FakeNaverDictionaryStore : INaverJapaneseDictionaryStore
    {
        private readonly Dictionary<string, NaverJapaneseDictionaryEntry> _entries = new(StringComparer.Ordinal);

        public int GetCount { get; private set; }

        public bool TryGet(string surface, out NaverJapaneseDictionaryEntry entry)
        {
            GetCount++;
            return _entries.TryGetValue(surface, out entry!);
        }

        public void Upsert(NaverJapaneseDictionaryEntry entry)
        {
            _entries[entry.Surface] = entry;
        }
    }

    private sealed class FakeNaverLookupService(NaverJapaneseDictionaryEntry? result) : INaverJapaneseDictionaryLookupService
    {
        public int CallCount { get; private set; }

        public Task<NaverJapaneseDictionaryEntry?> TryLookupAsync(string surface, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }
}
