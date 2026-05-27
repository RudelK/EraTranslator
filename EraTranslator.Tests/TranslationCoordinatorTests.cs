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

        Assert.Equal("공통 번역", items[0].TranslatedText);
        Assert.Equal("공통 번역", items[1].TranslatedText);
        Assert.Equal("번역 완료", items[1].Status);
        Assert.Equal(["id-1"], provider.RequestHistory[0]);
        Assert.Equal(["id-3"], provider.RequestHistory[1]);
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
            result.Translations["id-1"] = "__PH0__……그런 얼굴 하지 마.__PH1__도와달라고 할 생각은 없어.__PH2_";
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

    private static ExtractedTextItem BuildItem(string segmentId, string originalText)
    {
        return new ExtractedTextItem
        {
            SegmentId = segmentId,
            DocumentId = "doc",
            FileType = "ERB",
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

        public Task<TranslationProviderResult> TranslateAsync(
            IReadOnlyList<ProtectedSegment> requests,
            ProviderSettings settings,
            CancellationToken cancellationToken)
        {
            RequestHistory.Add(requests.Select(request => request.Id).ToList());
            var step = _steps.Dequeue();
            return Task.FromResult(step(requests));
        }
    }
}
