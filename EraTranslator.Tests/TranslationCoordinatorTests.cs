using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class TranslationCoordinatorTests
{
    [Fact]
    public async Task TranslateAsync_RetriesFailedItemsOnlyAndSkipsCompletedItems()
    {
        var provider = new SequencedProvider(
            requests =>
            {
                var result = new TranslationProviderResult();
                result.Errors["id-1"] = new TranslationErrorDetail(TranslationErrorKind.Http, "temporary", 500);
                result.Translations["id-2"] = "둘째 번역";
                return result;
            },
            requests =>
            {
                var result = new TranslationProviderResult();
                result.Translations["id-1"] = "첫째 번역";
                return result;
            });
        var coordinator = new TranslationCoordinator(new FakeTranslationProviderFactory(provider));
        var items = new[]
        {
            BuildItem("id-1", "첫째"),
            BuildItem("id-2", "둘째"),
            BuildCompletedItem("id-3", "셋째", "이미 완료"),
        };
        var persisted = 0;

        await coordinator.TranslateAsync(
            items,
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.OpenAi,
                BatchSize = 2,
                RetryCount = 1,
                ApiKey = "test",
                TargetLanguage = "ko",
            },
            [],
            new Progress<(double value, string status, string detail)>(),
            () => persisted++,
            CancellationToken.None);

        Assert.Equal("첫째 번역", items[0].TranslatedText);
        Assert.Equal("둘째 번역", items[1].TranslatedText);
        Assert.Equal("이미 완료", items[2].TranslatedText);
        Assert.Equal(["id-1", "id-2"], provider.RequestHistory[0]);
        Assert.Equal(["id-1"], provider.RequestHistory[1]);
        Assert.True(persisted >= 2);
    }

    [Fact]
    public async Task TranslateAsync_ReusesSameTranslationForDuplicateOriginalText()
    {
        var provider = new SequencedProvider(
            requests =>
            {
                var result = new TranslationProviderResult();
                result.Translations["id-1"] = "공통 번역";
                return result;
            },
            requests =>
            {
                var result = new TranslationProviderResult();
                result.Translations["id-3"] = "다른 번역";
                return result;
            });
        var coordinator = new TranslationCoordinator(new FakeTranslationProviderFactory(provider));
        var items = new[]
        {
            BuildItem("id-1", "같은 문장"),
            BuildItem("id-2", "같은 문장"),
            BuildItem("id-3", "다른 문장"),
        };
        var progress = new RecordingProgress();

        await coordinator.TranslateAsync(
            items,
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.OpenAi,
                BatchSize = 1,
                RetryCount = 0,
                ApiKey = "test",
                TargetLanguage = "ko",
                EnableBundledDictionaryFirstPass = false,
                EnableKanaTransliterationFallback = false,
                EnableKanjiReadingFallback = false,
            },
            [],
            progress,
            null,
            CancellationToken.None);

        Assert.Equal("공통 번역", items[0].TranslatedText);
        Assert.Equal("공통 번역", items[1].TranslatedText);
        Assert.Equal("번역 완료", items[1].Status);
        Assert.Equal(["id-1"], provider.RequestHistory[0]);
        Assert.Equal(["id-3"], provider.RequestHistory[1]);
        Assert.Contains(progress.Reports, report => report.status.Contains("2/3", StringComparison.Ordinal));
        Assert.Contains(progress.Reports, report => report.status.Contains("3/3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TranslateAsync_UsesExistingCompletedTranslationForDuplicateOriginalText()
    {
        var provider = new SequencedProvider(requests =>
        {
            var result = new TranslationProviderResult();
            result.Translations["id-3"] = "새 번역";
            return result;
        });
        var coordinator = new TranslationCoordinator(new FakeTranslationProviderFactory(provider));
        var completed = BuildCompletedItem("id-1", "같은 문장", "기존 번역");
        var duplicate = BuildItem("id-2", "같은 문장");
        var different = BuildItem("id-3", "다른 문장");

        await coordinator.TranslateAsync(
            [completed, duplicate, different],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.OpenAi,
                BatchSize = 2,
                RetryCount = 0,
                ApiKey = "test",
                TargetLanguage = "ko",
            },
            [],
            new Progress<(double value, string status, string detail)>(),
            null,
            CancellationToken.None);

        Assert.Equal("기존 번역", duplicate.TranslatedText);
        Assert.Equal("번역 완료", duplicate.Status);
        Assert.Equal(["id-3"], provider.RequestHistory[0]);
    }

    [Fact]
    public async Task TranslateAsync_UsesDictionaryFirstTranslationBeforeCallingProvider()
    {
        var provider = new SequencedProvider();
        var coordinator = new TranslationCoordinator(
            new FakeTranslationProviderFactory(provider),
            new StubDictionaryFirstTranslationService(new Dictionary<string, DictionaryFirstTranslationMatch>(StringComparer.Ordinal)
            {
                ["快楽"] = new DictionaryFirstTranslationMatch("쾌락", "사전 번역", false, string.Empty),
            }));
        var item = BuildItem("id-1", "快楽", fileType: "CSV");

        await coordinator.TranslateAsync(
            [item],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.OpenAi,
                BatchSize = 1,
                RetryCount = 0,
                ApiKey = "test",
                SourceLanguage = "ja",
                TargetLanguage = "ko",
                EnableBundledDictionaryFirstPass = true,
            },
            [],
            new Progress<(double value, string status, string detail)>(),
            null,
            CancellationToken.None);

        Assert.Empty(provider.RequestHistory);
        Assert.Equal("쾌락", item.TranslatedText);
        Assert.Equal("사전 번역", item.TranslationSource);
        Assert.Equal("번역 완료", item.Status);
    }

    [Fact]
    public async Task TranslateAsync_LogsDictionaryHitWhenEnabled()
    {
        var provider = new SequencedProvider();
        var logger = new RecordingDictionaryHitLogger();
        var coordinator = new TranslationCoordinator(
            new FakeTranslationProviderFactory(provider),
            new StubDictionaryFirstTranslationService(new Dictionary<string, DictionaryFirstTranslationMatch>(StringComparer.Ordinal)
            {
                ["快楽"] = new DictionaryFirstTranslationMatch("쾌락", "사전 번역", false, string.Empty, "surface", "快楽"),
            }),
            logger);
        var first = BuildItem("id-1", "快楽", fileType: "CSV");
        var duplicate = BuildItem("id-2", "快楽", fileType: "CSV");

        await coordinator.TranslateAsync(
            [first, duplicate],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.OpenAi,
                BatchSize = 1,
                RetryCount = 0,
                ApiKey = "test",
                SourceLanguage = "ja",
                TargetLanguage = "ko",
                EnableBundledDictionaryFirstPass = true,
                EnableDictionaryHitLogging = true,
            },
            [],
            new Progress<(double value, string status, string detail)>(),
            null,
            CancellationToken.None);

        Assert.Empty(provider.RequestHistory);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal("id-1", entry.SegmentId);
        Assert.Equal("快楽", entry.OriginalText);
        Assert.Equal("쾌락", entry.TranslatedText);
        Assert.Equal("surface", entry.MatchKind);
        Assert.Equal("快楽", entry.MatchedTerm);
        Assert.Equal(2, entry.AffectedItemCount);
    }

    [Fact]
    public async Task TranslateAsync_ReusesCompletedTranslationFromPropagationScopeWithoutApiRequest()
    {
        var provider = new SequencedProvider();
        var coordinator = new TranslationCoordinator(new FakeTranslationProviderFactory(provider));
        var active = BuildItem("id-1", "같은 문장");
        var completedOutsideFilter = BuildCompletedItem("id-2", "같은 문장", "기존 번역");
        var progress = new RecordingProgress();

        await coordinator.TranslateAsync(
            [active],
            [active, completedOutsideFilter],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.OpenAi,
                BatchSize = 1,
                RetryCount = 0,
                ApiKey = "test",
                TargetLanguage = "ko",
                EnableBundledDictionaryFirstPass = false,
                EnableKanaTransliterationFallback = false,
                EnableKanjiReadingFallback = false,
            },
            [],
            progress,
            null,
            CancellationToken.None);

        Assert.Empty(provider.RequestHistory);
        Assert.Equal("기존 번역", active.TranslatedText);
        Assert.Equal("번역 완료", active.Status);
        Assert.Contains(progress.Reports, report => report.status.Contains("1/1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TranslateAsync_PropagatesApiTranslationToPendingItemsOutsideActiveScope()
    {
        var provider = new SequencedProvider(requests =>
        {
            var result = new TranslationProviderResult();
            result.Translations["id-1"] = "공통 번역";
            return result;
        });
        var coordinator = new TranslationCoordinator(new FakeTranslationProviderFactory(provider));
        var active = BuildItem("id-1", "같은 문장");
        var pendingOutsideFilter = BuildItem("id-2", "같은 문장");
        var excludedOutsideFilter = BuildItem("id-3", "같은 문장");
        excludedOutsideFilter.ApplyManualStatusOverride("제외됨");
        var progress = new RecordingProgress();

        await coordinator.TranslateAsync(
            [active],
            [active, pendingOutsideFilter, excludedOutsideFilter],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.OpenAi,
                BatchSize = 1,
                RetryCount = 0,
                ApiKey = "test",
                TargetLanguage = "ko",
                EnableBundledDictionaryFirstPass = false,
                EnableKanaTransliterationFallback = false,
                EnableKanjiReadingFallback = false,
            },
            [],
            progress,
            null,
            CancellationToken.None);

        Assert.Equal(["id-1"], provider.RequestHistory[0]);
        Assert.Equal("공통 번역", active.TranslatedText);
        Assert.Equal("공통 번역", pendingOutsideFilter.TranslatedText);
        Assert.Equal("번역 완료", pendingOutsideFilter.Status);
        Assert.Equal(string.Empty, excludedOutsideFilter.TranslatedText);
        Assert.Equal("제외됨", excludedOutsideFilter.Status);
        Assert.Contains(progress.Reports, report => report.status.Contains("2/2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TranslateAsync_UsesFirstExistingCompletedTranslationAndPreservesExistingRows()
    {
        var provider = new SequencedProvider();
        var coordinator = new TranslationCoordinator(new FakeTranslationProviderFactory(provider));
        var active = BuildItem("id-1", "같은 문장");
        var firstCompleted = BuildCompletedItem("id-2", "같은 문장", "첫 번역");
        var secondCompleted = BuildCompletedItem("id-3", "같은 문장", "둘째 번역");
        var manual = BuildItem("id-4", "같은 문장");
        manual.ApplyTranslationState("수동 수정", "통과", string.Empty, true, "수동 번역");
        var review = BuildItem("id-5", "같은 문장");
        review.ApplyTranslationState("검수 필요", "통과", "검수 유지", true, "검수 번역");

        await coordinator.TranslateAsync(
            [active],
            [active, firstCompleted, secondCompleted, manual, review],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.OpenAi,
                BatchSize = 1,
                RetryCount = 0,
                ApiKey = "test",
                TargetLanguage = "ko",
                EnableBundledDictionaryFirstPass = false,
            },
            [],
            new Progress<(double value, string status, string detail)>(),
            null,
            CancellationToken.None);

        Assert.Empty(provider.RequestHistory);
        Assert.Equal("첫 번역", active.TranslatedText);
        Assert.Equal("첫 번역", firstCompleted.TranslatedText);
        Assert.Equal("둘째 번역", secondCompleted.TranslatedText);
        Assert.Equal("수동 번역", manual.TranslatedText);
        Assert.Equal("검수 번역", review.TranslatedText);
    }

    [Fact]
    public async Task TranslateAsync_MarksPercentPlaceholderDamageAsReviewNeededWithoutSaving()
    {
        var provider = new SequencedProvider(requests =>
        {
            var result = new TranslationProviderResult();
            result.Translations["id-1"] = "__PH1__ 마을 __PH0__";
            return result;
        });
        var coordinator = new TranslationCoordinator(new FakeTranslationProviderFactory(provider));
        var items = new[]
        {
            BuildItem("id-1", "%CALLNAME%の町%MASTERNAME%"),
        };

        await coordinator.TranslateAsync(
            items,
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.OpenAi,
                BatchSize = 1,
                RetryCount = 0,
                ApiKey = "test",
                TargetLanguage = "ko",
                EnableBundledDictionaryFirstPass = false,
            },
            [],
            new Progress<(double value, string status, string detail)>(),
            null,
            CancellationToken.None);

        Assert.Equal("검수 필요", items[0].Status);
        Assert.Equal("변수 삽입 손상", items[0].ValidationStatus);
        Assert.False(items[0].CanSave);
        Assert.Equal("__PH1__ 마을 __PH0__", items[0].TranslatedText);
    }

    [Fact]
    public async Task TranslateAsync_MarksPlaceholderCountMismatchAsReviewNeeded()
    {
        var provider = new SequencedProvider(requests =>
        {
            var result = new TranslationProviderResult();
            result.Translations["id-1"] = "__PH0__ 마을";
            return result;
        });
        var coordinator = new TranslationCoordinator(new FakeTranslationProviderFactory(provider));
        var items = new[]
        {
            BuildItem("id-1", "%CALLNAME%の町%MASTERNAME%"),
        };

        await coordinator.TranslateAsync(
            items,
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.OpenAi,
                BatchSize = 1,
                RetryCount = 0,
                ApiKey = "test",
                TargetLanguage = "ko",
                EnableBundledDictionaryFirstPass = false,
            },
            [],
            new Progress<(double value, string status, string detail)>(),
            null,
            CancellationToken.None);

        Assert.Equal("검수 필요", items[0].Status);
        Assert.Equal("토큰 검토 필요", items[0].ValidationStatus);
        Assert.Contains("보호 토큰 개수가 일치하지 않습니다", items[0].TranslationError, StringComparison.Ordinal);
        Assert.Equal("__PH0__ 마을", items[0].TranslatedText);
        Assert.False(items[0].CanSave);
    }

    [Fact]
    public async Task TranslateAsync_RepairsNearMissPlaceholderTokensBeforeRestore()
    {
        var provider = new SequencedProvider(requests =>
        {
            var result = new TranslationProviderResult();
            result.Translations["id-1"] = "__PH0__……그런 얼굴 하지 마.　도와달라고 할 생각은 없어.__PH1_";
            return result;
        });
        var coordinator = new TranslationCoordinator(new FakeTranslationProviderFactory(provider));
        var items = new[]
        {
            BuildItem("id-1", "「……そんな顔しないでよ。　手伝ってくれなんていう気はないんだ」"),
        };

        await coordinator.TranslateAsync(
            items,
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.OpenAi,
                BatchSize = 1,
                RetryCount = 0,
                ApiKey = "test",
                TargetLanguage = "ko",
                EnableBundledDictionaryFirstPass = false,
            },
            [],
            new Progress<(double value, string status, string detail)>(),
            null,
            CancellationToken.None);

        Assert.Equal("통과", items[0].ValidationStatus);
        Assert.True(items[0].CanSave);
        Assert.Equal("「……그런 얼굴 하지 마.　도와달라고 할 생각은 없어.」", items[0].TranslatedText);
        Assert.DoesNotContain("__PH", items[0].TranslatedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranslateAsync_MarksAlternativeCandidatesAsReviewNeeded()
    {
        var provider = new SequencedProvider(requests =>
        {
            var result = new TranslationProviderResult();
            result.Translations["id-1"] = "데포자/기본 캐릭터";
            return result;
        });
        var coordinator = new TranslationCoordinator(new FakeTranslationProviderFactory(provider));
        var items = new[]
        {
            BuildItem("id-1", "デフォ子"),
        };

        await coordinator.TranslateAsync(
            items,
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.OpenAi,
                BatchSize = 1,
                RetryCount = 0,
                ApiKey = "test",
                TargetLanguage = "ko",
            },
            [],
            new Progress<(double value, string status, string detail)>(),
            null,
            CancellationToken.None);

        Assert.Equal("검수 필요", items[0].Status);
        Assert.Equal("통과", items[0].ValidationStatus);
        Assert.Equal("대체 후보가 함께 출력되어 검토가 필요합니다.", items[0].TranslationError);
        Assert.Equal("데포자/기본 캐릭터", items[0].TranslatedText);
    }

    [Fact]
    public async Task TranslateAsync_MarksExplanationParenthesesAsReviewNeeded()
    {
        var provider = new SequencedProvider(requests =>
        {
            var result = new TranslationProviderResult();
            result.Translations["id-1"] = "파이판(무모/제모 상태)";
            return result;
        });
        var coordinator = new TranslationCoordinator(new FakeTranslationProviderFactory(provider));
        var items = new[]
        {
            BuildItem("id-1", "パイパン"),
        };

        await coordinator.TranslateAsync(
            items,
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.OpenAi,
                BatchSize = 1,
                RetryCount = 0,
                ApiKey = "test",
                TargetLanguage = "ko",
                EnableBundledDictionaryFirstPass = false,
                EnableKanaTransliterationFallback = false,
                EnableKanjiReadingFallback = false,
            },
            [],
            new Progress<(double value, string status, string detail)>(),
            null,
            CancellationToken.None);

        Assert.Equal("검수 필요", items[0].Status);
        Assert.Equal("통과", items[0].ValidationStatus);
        Assert.Equal("대체 후보가 함께 출력되어 검토가 필요합니다.", items[0].TranslationError);
        Assert.Equal("파이판(무모/제모 상태)", items[0].TranslatedText);
    }

    [Fact]
    public async Task TranslateAsync_MarksAsciiNoiseAsReviewNeededInsteadOfFailure()
    {
        var provider = new SequencedProvider(requests =>
        {
            var result = new TranslationProviderResult();
            result.Translations["id-1"] = "나는 당신과 다르게 바빠요 much";
            return result;
        });
        var coordinator = new TranslationCoordinator(new FakeTranslationProviderFactory(provider));
        var items = new[]
        {
            BuildItem("id-1", "あなたと違って忙しいの。"),
        };

        await coordinator.TranslateAsync(
            items,
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.OpenAi,
                BatchSize = 1,
                RetryCount = 0,
                ApiKey = "test",
                TargetLanguage = "ko",
                EnableBundledDictionaryFirstPass = false,
            },
            [],
            new Progress<(double value, string status, string detail)>(),
            null,
            CancellationToken.None);

        Assert.Equal("검수 필요", items[0].Status);
        Assert.Equal("통과", items[0].ValidationStatus);
        Assert.True(items[0].CanSave);
        Assert.Equal("영어 또는 로마자 잡음이 섞여 있어 검토가 필요합니다.", items[0].TranslationError);
        Assert.Equal("나는 당신과 다르게 바빠요 much", items[0].TranslatedText);
    }

    [Fact]
    public async Task TranslateAsync_FailsWhenNormalizedTranslationBecomesBlank()
    {
        var provider = new SequencedProvider(requests =>
        {
            var result = new TranslationProviderResult();
            result.Translations["id-1"] = "   ";
            return result;
        });
        var coordinator = new TranslationCoordinator(new FakeTranslationProviderFactory(provider));
        var items = new[]
        {
            BuildItem("id-1", "空白"),
        };

        await coordinator.TranslateAsync(
            items,
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.OpenAi,
                BatchSize = 1,
                RetryCount = 0,
                ApiKey = "test",
                SourceLanguage = "ja",
                TargetLanguage = "ko",
                EnableBundledDictionaryFirstPass = false,
                EnableKanaTransliterationFallback = false,
                EnableKanjiReadingFallback = false,
            },
            [],
            new Progress<(double value, string status, string detail)>(),
            null,
            CancellationToken.None);

        Assert.Equal("번역 실패", items[0].Status);
        Assert.Equal("빈 번역문", items[0].ValidationStatus);
        Assert.False(items[0].CanSave);
        Assert.Equal(string.Empty, items[0].TranslatedText);
    }

    [Fact]
    public async Task TranslateAsync_FailsWhenJapaneseLeaksIntoJaToKoTranslation()
    {
        var provider = new SequencedProvider(requests =>
        {
            var result = new TranslationProviderResult();
            result.Translations["id-1"] = "쾌락快楽";
            return result;
        });
        var coordinator = new TranslationCoordinator(new FakeTranslationProviderFactory(provider));
        var items = new[]
        {
            BuildItem("id-1", "快楽"),
        };

        await coordinator.TranslateAsync(
            items,
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.OpenAi,
                BatchSize = 1,
                RetryCount = 0,
                ApiKey = "test",
                SourceLanguage = "ja",
                TargetLanguage = "ko",
                EnableBundledDictionaryFirstPass = false,
                EnableKanaTransliterationFallback = false,
                EnableKanjiReadingFallback = false,
            },
            [],
            new Progress<(double value, string status, string detail)>(),
            null,
            CancellationToken.None);

        Assert.Equal("번역 실패", items[0].Status);
        Assert.Equal("대상 언어 불일치", items[0].ValidationStatus);
        Assert.False(items[0].CanSave);
        Assert.Equal("쾌락快楽", items[0].TranslatedText);
    }

    [Fact]
    public async Task TranslateAsync_FailsWhenKanjiOnlyJapaneseIsReturnedUnchanged()
    {
        var provider = new SequencedProvider(requests =>
        {
            var result = new TranslationProviderResult();
            result.Translations["id-1"] = "交渉術";
            return result;
        });
        var coordinator = new TranslationCoordinator(new FakeTranslationProviderFactory(provider));
        var items = new[]
        {
            BuildItem("id-1", "交渉術", fileType: "CSV"),
        };

        await coordinator.TranslateAsync(
            items,
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.OpenAi,
                BatchSize = 1,
                RetryCount = 0,
                ApiKey = "test",
                SourceLanguage = "ja",
                TargetLanguage = "ko",
                EnableBundledDictionaryFirstPass = false,
                EnableKanaTransliterationFallback = false,
                EnableKanjiReadingFallback = false,
            },
            [],
            new Progress<(double value, string status, string detail)>(),
            null,
            CancellationToken.None);

        Assert.Equal("번역 실패", items[0].Status);
        Assert.Equal("대상 언어 불일치", items[0].ValidationStatus);
        Assert.False(items[0].CanSave);
        Assert.Equal("交渉術", items[0].TranslatedText);
    }

    [Fact]
    public async Task TranslateAsync_SkipsProviderWhenOriginalIsEntirelyTargetLanguage()
    {
        var provider = new SequencedProvider();
        var coordinator = new TranslationCoordinator(new FakeTranslationProviderFactory(provider));
        var items = new[]
        {
            BuildItem("id-1", "안녕하세요"),
        };

        await coordinator.TranslateAsync(
            items,
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.OpenAi,
                BatchSize = 1,
                RetryCount = 0,
                ApiKey = "test",
                SourceLanguage = "ja",
                TargetLanguage = "ko",
                ExcludeNonSourceText = true,
            },
            [],
            new Progress<(double value, string status, string detail)>(),
            null,
            CancellationToken.None);

        Assert.Empty(provider.RequestHistory);
        Assert.Equal("번역 완료", items[0].Status);
        Assert.Equal("안녕하세요", items[0].TranslatedText);
        Assert.True(items[0].CanSave);
    }

    [Fact]
    public async Task TranslateAsync_StillAppliesNormalizationWhenOriginalIsEntirelyTargetLanguage()
    {
        var provider = new SequencedProvider();
        var coordinator = new TranslationCoordinator(new FakeTranslationProviderFactory(provider));
        var items = new[]
        {
            BuildItem("id-1", "테 스트", fileType: "CSV"),
        };

        await coordinator.TranslateAsync(
            items,
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.OpenAi,
                BatchSize = 1,
                RetryCount = 0,
                ApiKey = "test",
                SourceLanguage = "ja",
                TargetLanguage = "ko",
                ExcludeNonSourceText = true,
            },
            [],
            new Progress<(double value, string status, string detail)>(),
            null,
            CancellationToken.None);

        Assert.Empty(provider.RequestHistory);
        Assert.Equal("번역 완료", items[0].Status);
        Assert.Equal("테스트", items[0].TranslatedText);
    }

    [Fact]
    public async Task TranslateAsync_DoesNotSkipProviderWhenTargetLanguageIsMixedWithOtherLanguage()
    {
        var provider = new SequencedProvider(requests =>
        {
            var result = new TranslationProviderResult();
            result.Translations["id-1"] = "쾌락";
            return result;
        });
        var coordinator = new TranslationCoordinator(new FakeTranslationProviderFactory(provider));
        var items = new[]
        {
            BuildItem("id-1", "쾌락快楽"),
        };

        await coordinator.TranslateAsync(
            items,
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.OpenAi,
                BatchSize = 1,
                RetryCount = 0,
                ApiKey = "test",
                SourceLanguage = "ja",
                TargetLanguage = "ko",
                ExcludeNonSourceText = true,
            },
            [],
            new Progress<(double value, string status, string detail)>(),
            null,
            CancellationToken.None);

        Assert.Single(provider.RequestHistory);
        Assert.Equal(["id-1"], provider.RequestHistory[0]);
        Assert.Equal("쾌락", items[0].TranslatedText);
    }

    [Fact]
    public async Task TranslateAsync_ForcesSingleItemBatchForTranslateGemma()
    {
        var provider = new SequencedProvider(
            requests =>
            {
                var result = new TranslationProviderResult();
                result.Translations["id-1"] = "첫째 번역";
                return result;
            },
            requests =>
            {
                var result = new TranslationProviderResult();
                result.Translations["id-2"] = "둘째 번역";
                return result;
            });
        var coordinator = new TranslationCoordinator(new FakeTranslationProviderFactory(provider));
        var items = new[]
        {
            BuildItem("id-1", "첫째"),
            BuildItem("id-2", "둘째"),
        };

        await coordinator.TranslateAsync(
            items,
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.LmStudio,
                Model = "google/translategemma-4b-it",
                BatchSize = 5,
                RetryCount = 0,
                SourceLanguage = "ja",
                TargetLanguage = "ko",
            },
            [],
            new Progress<(double value, string status, string detail)>(),
            null,
            CancellationToken.None);

        Assert.Equal(2, provider.RequestHistory.Count);
        Assert.Equal(["id-1"], provider.RequestHistory[0]);
        Assert.Equal(["id-2"], provider.RequestHistory[1]);
    }

    [Fact]
    public async Task TranslateAsync_EzTransUsesProcessCountToExpandBatch()
    {
        var provider = new SequencedProvider(requests =>
        {
            var result = new TranslationProviderResult();
            foreach (var request in requests)
            {
                result.Translations[request.Id] = $"{request.Text}-번역";
            }

            return result;
        });
        var coordinator = new TranslationCoordinator(new FakeTranslationProviderFactory(provider));
        var items = new[]
        {
            BuildItem("id-1", "첫째"),
            BuildItem("id-2", "둘째"),
            BuildItem("id-3", "셋째"),
        };

        await coordinator.TranslateAsync(
            items,
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.EzTransXp,
                BatchSize = 1,
                EzTransProcessCount = 3,
                RetryCount = 0,
                SourceLanguage = "ja",
                TargetLanguage = "ko",
            },
            [],
            new Progress<(double value, string status, string detail)>(),
            null,
            CancellationToken.None);

        Assert.Single(provider.RequestHistory);
        Assert.Equal(["id-1", "id-2", "id-3"], provider.RequestHistory[0]);
        Assert.All(items, item =>
        {
            Assert.False(item.NeedsTranslation);
            Assert.True(item.CanSave);
            Assert.False(string.IsNullOrWhiteSpace(item.TranslatedText));
        });
    }

    [Fact]
    public async Task TranslateAsync_SelectsOnlyOverlappingGlossaryHintsForCurrentBatch()
    {
        var provider = new SequencedProvider(requests =>
        {
            var result = new TranslationProviderResult();
            result.Translations["id-1"] = "쾌락치가 상승했다";
            return result;
        });
        var coordinator = new TranslationCoordinator(new FakeTranslationProviderFactory(provider));
        var item = BuildItem("id-1", "快楽値が上がった");
        var glossaryHints = new[]
        {
            new GlossaryHint("快楽", "쾌락", "CSV"),
            new GlossaryHint("快楽値", "쾌락치", "ERH"),
            new GlossaryHint("ご主人さま", "주인님", "CSV"),
        };

        await coordinator.TranslateAsync(
            [item],
            [item],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.OpenAi,
                BatchSize = 1,
                RetryCount = 0,
                ApiKey = "test",
                TargetLanguage = "ko",
                EnableBundledDictionaryFirstPass = false,
            },
            [],
            glossaryHints,
            new Progress<(double value, string status, string detail)>(),
            null,
            CancellationToken.None);

        Assert.Single(provider.GlossaryHistory);
        Assert.Equal(["快楽値", "快楽"], provider.GlossaryHistory[0].Select(static hint => hint.Source).ToList());
    }

    [Fact]
    public async Task TranslateAsync_UsesPromptingDictionaryAsGlossaryHintForLlmProviders()
    {
        var provider = new SequencedProvider(requests =>
        {
            var result = new TranslationProviderResult();
            result.Translations["id-1"] = "주인님";
            return result;
        });
        var coordinator = new TranslationCoordinator(new FakeTranslationProviderFactory(provider));
        var item = BuildItem("id-1", "ご主人さま");

        await coordinator.TranslateAsync(
            [item],
            new ProviderSettings
            {
                ProviderType = TranslationProviderType.OpenAi,
                BatchSize = 1,
                RetryCount = 0,
                ApiKey = "test",
                TargetLanguage = "ko",
                EnableBundledDictionaryFirstPass = false,
            },
            [
                new UserDictionaryEntry
                {
                    IsEnabled = true,
                    Source = "ご主人さま",
                    Target = "주인님",
                    ApplyMode = UserDictionaryApplyMode.Prompting,
                },
            ],
            new Progress<(double value, string status, string detail)>(),
            null,
            CancellationToken.None);

        Assert.Single(provider.RequestTextsHistory);
        Assert.Equal(["ご主人さま"], provider.RequestTextsHistory[0]);
        Assert.Single(provider.GlossaryHistory);
        Assert.Equal(["ご主人さま"], provider.GlossaryHistory[0].Select(static hint => hint.Source).ToList());
        Assert.Equal(["주인님"], provider.GlossaryHistory[0].Select(static hint => hint.Target).ToList());
    }

    [Theory]
    [InlineData(TranslationProviderType.DeepLFree)]
    [InlineData(TranslationProviderType.Papago)]
    [InlineData(TranslationProviderType.EzTransXp)]
    public async Task TranslateAsync_TreatsPromptingDictionaryAsReplacementForNonLlmProviders(TranslationProviderType providerType)
    {
        var provider = new SequencedProvider(requests =>
        {
            var result = new TranslationProviderResult();
            result.Translations["id-1"] = "__PH0__";
            return result;
        });
        var coordinator = new TranslationCoordinator(new FakeTranslationProviderFactory(provider));
        var item = BuildItem("id-1", "勇者");

        await coordinator.TranslateAsync(
            [item],
            new ProviderSettings
            {
                ProviderType = providerType,
                BatchSize = 1,
                RetryCount = 0,
                ApiKey = "test",
                TargetLanguage = "ko",
                EnableBundledDictionaryFirstPass = false,
                EnableKanaTransliterationFallback = false,
                EnableKanjiReadingFallback = false,
            },
            [
                new UserDictionaryEntry
                {
                    IsEnabled = true,
                    Source = "勇者",
                    Target = "용사",
                    ApplyMode = UserDictionaryApplyMode.Prompting,
                },
            ],
            new Progress<(double value, string status, string detail)>(),
            null,
            CancellationToken.None);

        Assert.Single(provider.RequestTextsHistory);
        Assert.Equal(["__PH0__"], provider.RequestTextsHistory[0]);
        Assert.Single(provider.GlossaryHistory);
        Assert.Empty(provider.GlossaryHistory[0]);
        Assert.Equal("용사", item.TranslatedText);
    }

    private static ExtractedTextItem BuildItem(string segmentId, string originalText, string fileType = "ERB")
    {
        return new ExtractedTextItem
        {
            SegmentId = segmentId,
            DocumentId = "doc",
            FileType = fileType,
            RelativePath = "A.ERB",
            EncodingName = "utf-8",
            SegmentType = "PRINT",
            LineNumber = 1,
            OriginalText = originalText,
            CsvFieldRole = CsvFieldRole.TranslatableValue,
        };
    }

    private static ExtractedTextItem BuildCompletedItem(string segmentId, string originalText, string translatedText)
    {
        var item = BuildItem(segmentId, originalText);
        item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, translatedText);
        return item;
    }

    private sealed class FakeTranslationProviderFactory(ITranslationProvider provider) : ITranslationProviderFactory
    {
        public ITranslationProvider Create(ProviderSettings settings) => provider;
    }

    private sealed class SequencedProvider(params Func<IReadOnlyList<ProtectedSegment>, TranslationProviderResult>[] steps) : ITranslationProvider
    {
        private readonly Queue<Func<IReadOnlyList<ProtectedSegment>, TranslationProviderResult>> _steps = new(steps);

        public List<IReadOnlyList<string>> RequestHistory { get; } = [];
        public List<IReadOnlyList<string>> RequestTextsHistory { get; } = [];
        public List<IReadOnlyList<GlossaryHint>> GlossaryHistory { get; } = [];

        public Task<TranslationProviderResult> TranslateAsync(
            IReadOnlyList<ProtectedSegment> requests,
            ProviderSettings settings,
            CancellationToken cancellationToken,
            IReadOnlyList<GlossaryHint>? glossaryHints = null)
        {
            RequestHistory.Add(requests.Select(request => request.Id).ToList());
            RequestTextsHistory.Add(requests.Select(request => request.Text).ToList());
            GlossaryHistory.Add((glossaryHints ?? []).ToList());
            var step = _steps.Dequeue();
            return Task.FromResult(step(requests));
        }
    }

    private sealed class StubDictionaryFirstTranslationService(
        IReadOnlyDictionary<string, DictionaryFirstTranslationMatch> matches) : IDictionaryFirstTranslationService
    {
        public Task<DictionaryFirstTranslationMatch?> TryResolveAsync(
            ExtractedTextItem item,
            ProviderSettings settings,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(matches.TryGetValue(item.OriginalText, out var match)
                ? match
                : (DictionaryFirstTranslationMatch?)null);
        }
    }

    private sealed class RecordingDictionaryHitLogger : IDictionaryHitLogger
    {
        public List<DictionaryHitLogEntry> Entries { get; } = [];

        public void LogHit(DictionaryHitLogEntry entry)
        {
            Entries.Add(entry);
        }
    }

    private sealed class RecordingProgress : IProgress<(double value, string status, string detail)>
    {
        public List<(double value, string status, string detail)> Reports { get; } = [];

        public void Report((double value, string status, string detail) value)
        {
            Reports.Add(value);
        }
    }
}
