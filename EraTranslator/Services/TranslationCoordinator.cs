using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class TranslationCoordinator
{
    private readonly PlaceholderProtector _protector = new();
    private readonly UserDictionaryApplier _dictionaryApplier = new();
    private readonly ITranslationProviderFactory _providerFactory;

    public TranslationCoordinator(ITranslationProviderFactory? providerFactory = null)
    {
        _providerFactory = providerFactory ?? new TranslationProviderFactory();
    }

    public async Task TranslateAsync(
        IReadOnlyList<ExtractedTextItem> items,
        ProviderSettings settings,
        IReadOnlyList<UserDictionaryEntry> dictionaryEntries,
        IProgress<(double value, string status, string detail)> progress,
        Action? persistState,
        CancellationToken cancellationToken)
    {
        var groupedByOriginal = items
            .GroupBy(item => item.OriginalText, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var seededCount = ApplyExistingSharedTranslations(groupedByOriginal);
        if (seededCount > 0)
        {
            persistState?.Invoke();
        }

        var representatives = items
            .Where(item => item.NeedsTranslation)
            .GroupBy(item => item.OriginalText, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        if (representatives.Count == 0)
        {
            return;
        }

        var provider = _providerFactory.Create(settings);
        try
        {
            var batchSize = Math.Clamp(
                settings.ProviderType == TranslationProviderType.EzTransXp
                    ? Math.Max(settings.BatchSize, settings.EzTransProcessCount)
                    : settings.BatchSize,
                1,
                100);
            var retryCount = Math.Clamp(settings.RetryCount, 0, 10);
            var queue = new Queue<ExtractedTextItem>(representatives);
            var totalCount = representatives.Count;
            var processedCount = 0;

            while (queue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = new List<ExtractedTextItem>(batchSize);
                while (queue.Count > 0 && batch.Count < batchSize)
                {
                    var candidate = queue.Dequeue();
                    if (candidate.NeedsTranslation)
                    {
                        batch.Add(candidate);
                    }
                }

                if (batch.Count == 0)
                {
                    break;
                }

                var currentFiles = string.Join(", ", batch.Select(item => item.RelativePath).Distinct().Take(2));
                if (batch.Select(item => item.RelativePath).Distinct().Count() > 2)
                {
                    currentFiles += " 외";
                }

                foreach (var item in batch)
                {
                    ApplyToGroup(groupedByOriginal, item.OriginalText, static groupItem => groupItem.MarkTranslating());
                }
                persistState?.Invoke();

                var remaining = batch.ToDictionary(item => item.SegmentId, StringComparer.Ordinal);

                for (var attempt = 0; attempt <= retryCount && remaining.Count > 0; attempt++)
                {
                    var currentBatch = remaining.Values.ToList();
                    var protectedBatch = currentBatch
                        .Select(item =>
                        {
                            var protectedText = _protector.Protect(item.OriginalText);
                            var dictionaryApplied = _dictionaryApplier.Apply(
                                protectedText.Text,
                                protectedText.Placeholders,
                                dictionaryEntries);
                            return new ProtectedSegment(
                                item.SegmentId,
                                dictionaryApplied.Text,
                                item.OriginalText,
                                dictionaryApplied.Placeholders);
                        })
                        .ToList();

                    try
                    {
                        var providerResult = await provider.TranslateAsync(protectedBatch, settings, cancellationToken);
                        var nextRemaining = new Dictionary<string, ExtractedTextItem>(StringComparer.Ordinal);

                        foreach (var item in currentBatch)
                        {
                            if (providerResult.Errors.TryGetValue(item.SegmentId, out var providerError))
                            {
                                if (attempt < retryCount)
                                {
                                    ApplyToGroup(groupedByOriginal, item.OriginalText, static groupItem => groupItem.MarkRetrying());
                                    nextRemaining[item.SegmentId] = item;
                                }
                                else
                                {
                                    ApplyFailureToGroup(
                                        groupedByOriginal,
                                        item.OriginalText,
                                        "번역 실패",
                                        MapErrorKind(providerError.Kind, providerError.HttpStatusCode),
                                        providerError.Message);
                                }

                                continue;
                            }

                            var protectedSegment = protectedBatch.First(segment => segment.Id == item.SegmentId);
                            if (!providerResult.Translations.TryGetValue(item.SegmentId, out var translated))
                            {
                                if (attempt < retryCount)
                                {
                                    ApplyToGroup(groupedByOriginal, item.OriginalText, static groupItem => groupItem.MarkRetrying());
                                    nextRemaining[item.SegmentId] = item;
                                }
                                else
                                {
                                    ApplyFailureToGroup(
                                        groupedByOriginal,
                                        item.OriginalText,
                                        "번역 실패",
                                        "응답 누락",
                                        "번역 결과가 반환되지 않았습니다.");
                                }

                                continue;
                            }

                            if (!_protector.HasAllTokens(translated, protectedSegment.Placeholders, out var tokenError))
                            {
                                var validationLabel = HasPercentInsertionPlaceholder(protectedSegment.Placeholders)
                                    ? "변수 삽입 손상"
                                    : "토큰 손실";

                                if (attempt < retryCount)
                                {
                                    ApplyToGroup(groupedByOriginal, item.OriginalText, static groupItem => groupItem.MarkRetrying());
                                    nextRemaining[item.SegmentId] = item;
                                }
                                else
                                {
                                    if (IsPlaceholderCountMismatch(tokenError))
                                    {
                                        ApplyReviewToGroup(
                                            groupedByOriginal,
                                            item.OriginalText,
                                            translated,
                                            "토큰 검토 필요",
                                            tokenError);
                                    }
                                    else
                                    {
                                        ApplyReviewToGroup(
                                            groupedByOriginal,
                                            item.OriginalText,
                                            translated,
                                            validationLabel,
                                            tokenError);
                                    }
                                }

                                continue;
                            }

                            var restoredTranslation = _protector.Restore(translated, protectedSegment.Placeholders);
                            ApplySuccessToGroup(groupedByOriginal, item.OriginalText, restoredTranslation);
                        }

                        remaining = nextRemaining;
                        persistState?.Invoke();
                    }
                    catch (OperationCanceledException)
                    {
                        foreach (var item in currentBatch)
                        {
                            ApplyToGroup(groupedByOriginal, item.OriginalText, static groupItem =>
                            {
                                if (groupItem.NeedsTranslation)
                                {
                                    groupItem.MarkStopped();
                                }
                            });
                        }

                        persistState?.Invoke();
                        throw;
                    }
                    catch (TranslationProviderException ex)
                    {
                        if (attempt < retryCount)
                        {
                            continue;
                        }

                        foreach (var item in currentBatch)
                        {
                            ApplyFailureToGroup(
                                groupedByOriginal,
                                item.OriginalText,
                                "번역 실패",
                                MapErrorKind(ex.Kind, ex.StatusCode is null ? null : (int)ex.StatusCode.Value),
                                ex.Message);
                        }
                        persistState?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        if (attempt < retryCount)
                        {
                            continue;
                        }

                        foreach (var item in currentBatch)
                        {
                            ApplyFailureToGroup(
                                groupedByOriginal,
                                item.OriginalText,
                                "번역 실패",
                                "배치 실패",
                                ex.Message);
                        }
                        persistState?.Invoke();
                    }
                }

                processedCount += batch.Count;
                progress.Report((processedCount / (double)totalCount, $"번역 진행 중... {processedCount}/{totalCount}", currentFiles));
                await Task.Yield();
            }
        }
        finally
        {
            if (provider is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (provider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private static int ApplyExistingSharedTranslations(IReadOnlyDictionary<string, List<ExtractedTextItem>> groupedByOriginal)
    {
        var appliedCount = 0;

        foreach (var group in groupedByOriginal.Values)
        {
            var existing = group.FirstOrDefault(item => item.IsTranslatedSuccessfully);
            if (existing is null || string.IsNullOrWhiteSpace(existing.TranslatedText))
            {
                continue;
            }

            foreach (var item in group.Where(item => item.NeedsTranslation))
            {
                item.ApplyTranslationState(
                    existing.Status,
                    "통과",
                    string.Empty,
                    true,
                    existing.TranslatedText);
                appliedCount++;
            }
        }

        return appliedCount;
    }

    private static void ApplySuccessToGroup(
        IReadOnlyDictionary<string, List<ExtractedTextItem>> groupedByOriginal,
        string originalText,
        string translatedText)
    {
        ApplyToGroup(
            groupedByOriginal,
            originalText,
            item =>
            {
                var normalizedTranslation = TranslationQualityRules.NormalizeTranslatedText(item.FileType, translatedText);
                var reviewReason = TranslationQualityRules.GetReviewReason(item.OriginalText, normalizedTranslation);
                item.ApplyTranslationState(
                    reviewReason is null ? "번역 완료" : "검수 필요",
                    "통과",
                    reviewReason ?? string.Empty,
                    true,
                    normalizedTranslation);
            });
    }

    private static void ApplyFailureToGroup(
        IReadOnlyDictionary<string, List<ExtractedTextItem>> groupedByOriginal,
        string originalText,
        string status,
        string validationStatus,
        string error)
    {
        ApplyToGroup(
            groupedByOriginal,
            originalText,
            item => item.ApplyTranslationState(
                status,
                validationStatus,
                error,
                false));
    }

    private static void ApplyReviewToGroup(
        IReadOnlyDictionary<string, List<ExtractedTextItem>> groupedByOriginal,
        string originalText,
        string translatedText,
        string validationStatus,
        string reviewReason)
    {
        ApplyToGroup(
            groupedByOriginal,
            originalText,
            item => item.ApplyTranslationState(
                "검수 필요",
                validationStatus,
                reviewReason,
                false,
                translatedText));
    }

    private static void ApplyToGroup(
        IReadOnlyDictionary<string, List<ExtractedTextItem>> groupedByOriginal,
        string originalText,
        Action<ExtractedTextItem> apply)
    {
        if (!groupedByOriginal.TryGetValue(originalText, out var group))
        {
            return;
        }

        foreach (var item in group)
        {
            apply(item);
        }
    }

    private static string MapErrorKind(TranslationErrorKind kind, int? httpStatusCode)
    {
        return kind switch
        {
            TranslationErrorKind.Configuration => "설정 오류",
            TranslationErrorKind.Timeout => "시간 초과",
            TranslationErrorKind.Http when httpStatusCode.HasValue => $"HTTP {httpStatusCode.Value}",
            TranslationErrorKind.Http => "HTTP 오류",
            TranslationErrorKind.Json => "JSON 오류",
            TranslationErrorKind.MissingResult => "응답 누락",
            TranslationErrorKind.Validation => "검증 실패",
            _ => "공급자 오류",
        };
    }

    private static bool HasPercentInsertionPlaceholder(IReadOnlyList<string> placeholders)
    {
        return placeholders.Any(static placeholder =>
            placeholder.Length >= 2
            && placeholder.StartsWith('%')
            && placeholder.EndsWith('%'));
    }

    private static bool IsPlaceholderCountMismatch(string error)
    {
        return error.Contains("보호 토큰 개수가 일치하지 않습니다", StringComparison.Ordinal);
    }
}
