using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class TranslationCoordinator : IDisposable
{
    private readonly UserDictionaryApplier _dictionaryApplier = new();
    private readonly PhaseScopedGlossaryBuilder _glossaryBuilder = new();
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
        await TranslateAsync(
            items,
            items,
            settings,
            dictionaryEntries,
            [],
            progress,
            persistState,
            cancellationToken);
    }

    public async Task TranslateAsync(
        IReadOnlyList<ExtractedTextItem> items,
        IReadOnlyList<ExtractedTextItem> propagationItems,
        ProviderSettings settings,
        IReadOnlyList<UserDictionaryEntry> dictionaryEntries,
        IProgress<(double value, string status, string detail)> progress,
        Action? persistState,
        CancellationToken cancellationToken)
    {
        await TranslateAsync(
            items,
            propagationItems,
            settings,
            dictionaryEntries,
            [],
            progress,
            persistState,
            cancellationToken);
    }

    public async Task TranslateAsync(
        IReadOnlyList<ExtractedTextItem> items,
        IReadOnlyList<ExtractedTextItem> propagationItems,
        ProviderSettings settings,
        IReadOnlyList<UserDictionaryEntry> dictionaryEntries,
        IReadOnlyList<GlossaryHint> glossaryHints,
        IProgress<(double value, string status, string detail)> progress,
        Action? persistState,
        CancellationToken cancellationToken)
    {
        var activeItems = items.ToList();
        var activeSegmentIds = activeItems
            .Select(item => item.SegmentId)
            .ToHashSet(StringComparer.Ordinal);
        var allItems = activeItems
            .Concat(propagationItems)
            .GroupBy(item => item.SegmentId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        var activeGroupedByOriginal = activeItems
            .GroupBy(item => item.OriginalText, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var propagationGroupedByOriginal = allItems
            .GroupBy(item => item.OriginalText, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var targetOriginals = activeItems
            .Where(item => item.NeedsTranslation)
            .Select(item => item.OriginalText)
            .ToHashSet(StringComparer.Ordinal);
        var initiallyPendingSegmentIds = allItems
            .Where(item => item.NeedsTranslation && targetOriginals.Contains(item.OriginalText))
            .Select(item => item.SegmentId)
            .ToHashSet(StringComparer.Ordinal);
        var totalCount = initiallyPendingSegmentIds.Count;
        var processedSegmentIds = new HashSet<string>(StringComparer.Ordinal);
        var processedCount = 0;

        var seededItems = ApplyExistingSharedTranslations(propagationGroupedByOriginal, targetOriginals);
        var seededCount = MarkProcessed(seededItems);
        processedCount = seededCount;
        if (seededCount > 0)
        {
            persistState?.Invoke();
        }

        var representatives = activeItems
            .Where(item => item.NeedsTranslation)
            .GroupBy(item => item.OriginalText, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        var targetLanguageRepresentatives = representatives
            .Where(item => ShouldReuseOriginalAsTranslation(item.OriginalText, settings))
            .ToList();
        foreach (var item in targetLanguageRepresentatives)
        {
            processedCount += MarkProcessed(ApplySuccessToGroup(
                activeGroupedByOriginal,
                propagationGroupedByOriginal,
                activeSegmentIds,
                item.OriginalText,
                item.OriginalText,
                settings.SourceLanguage,
                settings.TargetLanguage));
        }

        if (targetLanguageRepresentatives.Count > 0)
        {
            persistState?.Invoke();
            representatives = representatives
                .Except(targetLanguageRepresentatives)
                .ToList();
        }

        if (representatives.Count == 0)
        {
            if (totalCount > 0 && processedCount > 0)
            {
                progress.Report((1.0, $"번역 진행 중... {processedCount}/{totalCount}", string.Empty));
            }

            return;
        }

        var provider = _providerFactory.Create(settings);
        var protector = new PlaceholderProtector(settings.ProtectedFullWidthCharacters);
        var batchSize = Math.Clamp(
            settings.ProviderType == TranslationProviderType.EzTransXp
                ? Math.Max(settings.BatchSize, settings.EzTransProcessCount)
                : settings.BatchSize,
            1,
            100);
        if (settings.ProviderType is TranslationProviderType.LmStudio or TranslationProviderType.Lemonade
            && LmStudioSamplingDefaults.DetectModelFamily(settings.Model) == LmStudioModelFamily.TranslateGemma)
        {
            batchSize = 1;
        }
        var retryCount = Math.Clamp(settings.RetryCount, 0, 10);
        var queue = new Queue<ExtractedTextItem>(representatives);

        int MarkProcessed(IEnumerable<ExtractedTextItem> affectedItems)
        {
            var count = 0;
            foreach (var item in affectedItems)
            {
                if (initiallyPendingSegmentIds.Contains(item.SegmentId)
                    && processedSegmentIds.Add(item.SegmentId))
                {
                    count++;
                }
            }

            return count;
        }

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
                ApplyToGroup(activeGroupedByOriginal, item.OriginalText, static groupItem => groupItem.MarkTranslating());
            }
            persistState?.Invoke();

            var remaining = batch.ToDictionary(item => item.SegmentId, StringComparer.Ordinal);

            for (var attempt = 0; attempt <= retryCount && remaining.Count > 0; attempt++)
            {
                var currentBatch = remaining.Values.ToList();
                var batchGlossaryHints = _glossaryBuilder.SelectForBatch(glossaryHints, currentBatch);
                var protectedBatch = currentBatch
                    .Select(item =>
                    {
                        var protectedText = protector.Protect(item.OriginalText);
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
                    var providerResult = await provider.TranslateAsync(
                        protectedBatch,
                        settings,
                        cancellationToken,
                        batchGlossaryHints);
                    var nextRemaining = new Dictionary<string, ExtractedTextItem>(StringComparer.Ordinal);

                    foreach (var item in currentBatch)
                    {
                        if (providerResult.Errors.TryGetValue(item.SegmentId, out var providerError))
                        {
                            if (attempt < retryCount)
                            {
                                ApplyToGroup(activeGroupedByOriginal, item.OriginalText, static groupItem => groupItem.MarkRetrying());
                                nextRemaining[item.SegmentId] = item;
                            }
                            else
                            {
                                processedCount += MarkProcessed(ApplyFailureToGroup(
                                    activeGroupedByOriginal,
                                    item.OriginalText,
                                    "번역 실패",
                                    MapErrorKind(providerError.Kind, providerError.HttpStatusCode),
                                    providerError.Message));
                            }

                            continue;
                        }

                        var protectedSegment = protectedBatch.First(segment => segment.Id == item.SegmentId);
                        if (!providerResult.Translations.TryGetValue(item.SegmentId, out var translated))
                        {
                            if (attempt < retryCount)
                            {
                                ApplyToGroup(activeGroupedByOriginal, item.OriginalText, static groupItem => groupItem.MarkRetrying());
                                nextRemaining[item.SegmentId] = item;
                            }
                            else
                            {
                                processedCount += MarkProcessed(ApplyFailureToGroup(
                                    activeGroupedByOriginal,
                                    item.OriginalText,
                                    "번역 실패",
                                    "응답 누락",
                                    "번역 결과가 반환되지 않았습니다."));
                            }

                            continue;
                        }

                        var normalizedTranslated = protector.NormalizeTokenCandidates(translated, protectedSegment.Placeholders);
                        if (!protector.HasAllTokens(normalizedTranslated, protectedSegment.Placeholders, out var tokenError))
                        {
                            var validationLabel = HasPercentInsertionPlaceholder(protectedSegment.Placeholders)
                                ? "변수 삽입 손상"
                                : "토큰 손실";

                            if (attempt < retryCount)
                            {
                                ApplyToGroup(activeGroupedByOriginal, item.OriginalText, static groupItem => groupItem.MarkRetrying());
                                nextRemaining[item.SegmentId] = item;
                            }
                            else
                            {
                                if (IsPlaceholderCountMismatch(tokenError))
                                {
                                    processedCount += MarkProcessed(ApplyReviewToGroup(
                                        activeGroupedByOriginal,
                                        item.OriginalText,
                                        normalizedTranslated,
                                        "토큰 검토 필요",
                                        tokenError));
                                }
                                else
                                {
                                    processedCount += MarkProcessed(ApplyReviewToGroup(
                                        activeGroupedByOriginal,
                                        item.OriginalText,
                                        normalizedTranslated,
                                        validationLabel,
                                        tokenError));
                                }
                            }

                            continue;
                        }

                        var restoredTranslation = protector.Restore(normalizedTranslated, protectedSegment.Placeholders);
                        processedCount += MarkProcessed(ApplySuccessToGroup(
                            activeGroupedByOriginal,
                            propagationGroupedByOriginal,
                            activeSegmentIds,
                            item.OriginalText,
                            restoredTranslation,
                            settings.SourceLanguage,
                            settings.TargetLanguage));
                    }

                    remaining = nextRemaining;
                    persistState?.Invoke();
                }
                catch (OperationCanceledException)
                {
                    foreach (var item in currentBatch)
                    {
                        ApplyToGroup(activeGroupedByOriginal, item.OriginalText, static groupItem =>
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
                        processedCount += MarkProcessed(ApplyFailureToGroup(
                            activeGroupedByOriginal,
                            item.OriginalText,
                            "번역 실패",
                            MapErrorKind(ex.Kind, ex.StatusCode is null ? null : (int)ex.StatusCode.Value),
                            ex.Message));
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
                        processedCount += MarkProcessed(ApplyFailureToGroup(
                            activeGroupedByOriginal,
                            item.OriginalText,
                            "번역 실패",
                            "배치 실패",
                            ex.Message));
                    }
                    persistState?.Invoke();
                }
            }

            progress.Report((processedCount / (double)totalCount, $"번역 진행 중... {processedCount}/{totalCount}", currentFiles));
            await Task.Yield();
        }
    }

    public void Dispose()
    {
        if (_providerFactory is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static IReadOnlyList<ExtractedTextItem> ApplyExistingSharedTranslations(
        IReadOnlyDictionary<string, List<ExtractedTextItem>> groupedByOriginal,
        IReadOnlySet<string> targetOriginals)
    {
        var affectedItems = new List<ExtractedTextItem>();

        foreach (var originalText in targetOriginals)
        {
            if (!groupedByOriginal.TryGetValue(originalText, out var group))
            {
                continue;
            }

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
                affectedItems.Add(item);
            }
        }

        return affectedItems;
    }

    private static IReadOnlyList<ExtractedTextItem> ApplySuccessToGroup(
        IReadOnlyDictionary<string, List<ExtractedTextItem>> activeGroupedByOriginal,
        IReadOnlyDictionary<string, List<ExtractedTextItem>> propagationGroupedByOriginal,
        IReadOnlySet<string> activeSegmentIds,
        string originalText,
        string translatedText,
        string sourceLanguage,
        string targetLanguage)
    {
        var affectedItems = ApplyToGroup(
            activeGroupedByOriginal,
            originalText,
            item =>
            {
                var normalizedTranslation = TranslationQualityRules.NormalizeTranslatedText(item.FileType, translatedText, item.PreserveWhitespace);
                var hardFailureReason = TranslationQualityRules.GetHardFailureReason(
                    normalizedTranslation,
                    sourceLanguage,
                    targetLanguage,
                    item.OriginalText);
                if (hardFailureReason is not null)
                {
                    item.ApplyTranslationState(
                        "번역 실패",
                        hardFailureReason.Value.ValidationStatus,
                        hardFailureReason.Value.Message,
                        false,
                        hardFailureReason.Value.ValidationStatus == "빈 번역문" ? string.Empty : normalizedTranslation);
                    return;
                }

                var reviewReason = TranslationQualityRules.GetReviewReason(item.OriginalText, normalizedTranslation);
                item.ApplyTranslationState(
                    reviewReason is null ? "번역 완료" : "검수 필요",
                    "통과",
                    reviewReason ?? string.Empty,
                    true,
                    normalizedTranslation);
            });

        if (!propagationGroupedByOriginal.TryGetValue(originalText, out var propagationGroup))
        {
            return affectedItems;
        }

        foreach (var item in propagationGroup.Where(item => !activeSegmentIds.Contains(item.SegmentId) && item.NeedsTranslation))
        {
            var normalizedTranslation = TranslationQualityRules.NormalizeTranslatedText(item.FileType, translatedText, item.PreserveWhitespace);
            var hardFailureReason = TranslationQualityRules.GetHardFailureReason(
                normalizedTranslation,
                sourceLanguage,
                targetLanguage,
                item.OriginalText);
            if (hardFailureReason is not null)
            {
                item.ApplyTranslationState(
                    "번역 실패",
                    hardFailureReason.Value.ValidationStatus,
                    hardFailureReason.Value.Message,
                    false,
                    hardFailureReason.Value.ValidationStatus == "빈 번역문" ? string.Empty : normalizedTranslation);
                affectedItems.Add(item);
                continue;
            }

            var reviewReason = TranslationQualityRules.GetReviewReason(item.OriginalText, normalizedTranslation);
            item.ApplyTranslationState(
                reviewReason is null ? "번역 완료" : "검수 필요",
                "통과",
                reviewReason ?? string.Empty,
                true,
                normalizedTranslation);
            affectedItems.Add(item);
        }

        return affectedItems;
    }

    private static IReadOnlyList<ExtractedTextItem> ApplyFailureToGroup(
        IReadOnlyDictionary<string, List<ExtractedTextItem>> groupedByOriginal,
        string originalText,
        string status,
        string validationStatus,
        string error)
    {
        return ApplyToGroup(
            groupedByOriginal,
            originalText,
            item => item.ApplyTranslationState(
                status,
                validationStatus,
                error,
                false));
    }

    private static IReadOnlyList<ExtractedTextItem> ApplyReviewToGroup(
        IReadOnlyDictionary<string, List<ExtractedTextItem>> groupedByOriginal,
        string originalText,
        string translatedText,
        string validationStatus,
        string reviewReason)
    {
        return ApplyToGroup(
            groupedByOriginal,
            originalText,
            item => item.ApplyTranslationState(
                "검수 필요",
                validationStatus,
                reviewReason,
                false,
                translatedText));
    }

    private static bool ShouldReuseOriginalAsTranslation(string originalText, ProviderSettings settings)
    {
        if (!settings.ExcludeNonSourceText)
        {
            return false;
        }

        return SourceLanguageHeuristics.IsEntirelyMeaningfulLanguageText(originalText, settings.TargetLanguage);
    }

    private static List<ExtractedTextItem> ApplyToGroup(
        IReadOnlyDictionary<string, List<ExtractedTextItem>> groupedByOriginal,
        string originalText,
        Action<ExtractedTextItem> apply)
    {
        if (!groupedByOriginal.TryGetValue(originalText, out var group))
        {
            return [];
        }

        var affectedItems = new List<ExtractedTextItem>(group.Count);
        foreach (var item in group)
        {
            apply(item);
            affectedItems.Add(item);
        }

        return affectedItems;
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
