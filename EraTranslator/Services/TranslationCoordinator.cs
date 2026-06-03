using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class TranslationCoordinator : IDisposable
{
    private readonly UserDictionaryApplier _dictionaryApplier = new();
    private readonly PhaseScopedGlossaryBuilder _glossaryBuilder = new();
    private readonly IDictionaryFirstTranslationService _dictionaryFirstTranslationService;
    private readonly IDictionaryHitLogger _dictionaryHitLogger;
    private readonly ITranslationProviderFactory _providerFactory;

    public TranslationCoordinator(
        ITranslationProviderFactory? providerFactory = null,
        IDictionaryFirstTranslationService? dictionaryFirstTranslationService = null,
        IDictionaryHitLogger? dictionaryHitLogger = null)
    {
        _providerFactory = providerFactory ?? new TranslationProviderFactory();
        _dictionaryFirstTranslationService = dictionaryFirstTranslationService ?? new DictionaryFirstTranslationService();
        _dictionaryHitLogger = dictionaryHitLogger ?? new FileDictionaryHitLogger();
    }

    public async Task TranslateAsync(
        IReadOnlyList<ExtractedTextItem> items,
        ProviderSettings settings,
        IReadOnlyList<UserDictionaryEntry> dictionaryEntries,
        IProgress<(double value, string status, string detail)> progress,
        Action<IReadOnlyList<ExtractedTextItem>>? persistState,
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
        Action<IReadOnlyList<ExtractedTextItem>>? persistState,
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
        Action<IReadOnlyList<ExtractedTextItem>>? persistState,
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
            PersistChangedItems(persistState, seededItems);
        }

        var representatives = activeItems
            .Where(item => item.NeedsTranslation)
            .GroupBy(item => item.OriginalText, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        var targetLanguageRepresentatives = representatives
            .Where(item => ShouldReuseOriginalAsTranslation(item.OriginalText, settings))
            .ToList();
        var targetLanguageResolvedItems = new List<ExtractedTextItem>();
        foreach (var item in targetLanguageRepresentatives)
        {
            var affectedItems = ApplyResolvedTranslationToGroup(
                activeGroupedByOriginal,
                propagationGroupedByOriginal,
                activeSegmentIds,
                item.OriginalText,
                item.OriginalText,
                settings.SourceLanguage,
                settings.TargetLanguage,
                string.Empty,
                forceReview: false,
                reviewReason: string.Empty);
            processedCount += MarkProcessed(affectedItems);
            targetLanguageResolvedItems.AddRange(affectedItems);
        }

        if (targetLanguageRepresentatives.Count > 0)
        {
            PersistChangedItems(persistState, targetLanguageResolvedItems);
            representatives = representatives
                .Except(targetLanguageRepresentatives)
                .ToList();
        }

        var dictionaryResolvedRepresentatives = new List<ExtractedTextItem>();
        var dictionaryResolvedItems = new List<ExtractedTextItem>();
        foreach (var item in representatives)
        {
            var dictionaryMatch = await _dictionaryFirstTranslationService.TryResolveAsync(item, settings, cancellationToken).ConfigureAwait(false);
            if (dictionaryMatch is null)
            {
                continue;
            }

            dictionaryResolvedRepresentatives.Add(item);
            var affectedItems = ApplyResolvedTranslationToGroup(
                activeGroupedByOriginal,
                propagationGroupedByOriginal,
                activeSegmentIds,
                item.OriginalText,
                dictionaryMatch.Value.TranslatedText,
                settings.SourceLanguage,
                settings.TargetLanguage,
                dictionaryMatch.Value.TranslationSource,
                dictionaryMatch.Value.ForceReview,
                dictionaryMatch.Value.ReviewReason);
            processedCount += MarkProcessed(affectedItems);
            dictionaryResolvedItems.AddRange(affectedItems);
            LogDictionaryHitIfEnabled(settings, item, dictionaryMatch.Value, affectedItems.Count);
        }

        if (dictionaryResolvedRepresentatives.Count > 0)
        {
            PersistChangedItems(persistState, dictionaryResolvedItems);
            representatives = representatives
                .Except(dictionaryResolvedRepresentatives)
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
        var supportsPromptingDictionary = SupportsPromptingDictionary(settings.ProviderType);
        var replacementDictionaryEntries = supportsPromptingDictionary
            ? dictionaryEntries
                .Where(entry => entry.ApplyMode != UserDictionaryApplyMode.Prompting)
                .ToList()
            : dictionaryEntries.ToList();
        var promptDictionaryHints = supportsPromptingDictionary
            ? BuildPromptDictionaryHints(dictionaryEntries)
            : [];
        var availableGlossaryHints = MergeGlossaryHints(glossaryHints, promptDictionaryHints);
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

            var remaining = batch.ToDictionary(item => item.SegmentId, StringComparer.Ordinal);

            for (var attempt = 0; attempt <= retryCount && remaining.Count > 0; attempt++)
            {
                var currentBatch = remaining.Values.ToList();
                var batchGlossaryHints = _glossaryBuilder.SelectForBatch(availableGlossaryHints, currentBatch);
                var protectedBatch = currentBatch
                    .Select(item =>
                    {
                        var protectedText = protector.Protect(item.OriginalText);
                        var dictionaryApplied = _dictionaryApplier.Apply(
                            protectedText.Text,
                            protectedText.Placeholders,
                            replacementDictionaryEntries);
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
                    var changedItems = new List<ExtractedTextItem>();

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
                                var failureItems = ApplyFailureToGroup(
                                    activeGroupedByOriginal,
                                    item.OriginalText,
                                    "번역 실패",
                                    MapErrorKind(providerError.Kind, providerError.HttpStatusCode),
                                    providerError.Message);
                                processedCount += MarkProcessed(failureItems);
                                changedItems.AddRange(failureItems);
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
                                var missingResultItems = ApplyFailureToGroup(
                                    activeGroupedByOriginal,
                                    item.OriginalText,
                                    "번역 실패",
                                    "응답 누락",
                                    "번역 결과가 반환되지 않았습니다.");
                                processedCount += MarkProcessed(missingResultItems);
                                changedItems.AddRange(missingResultItems);
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
                                var failureItems = ApplyFailureToGroup(
                                    activeGroupedByOriginal,
                                    item.OriginalText,
                                    "번역 실패",
                                    validationLabel,
                                    tokenError);
                                processedCount += MarkProcessed(failureItems);
                                changedItems.AddRange(failureItems);
                            }

                            continue;
                        }

                        var restoredTranslation = protector.Restore(normalizedTranslated, protectedSegment.Placeholders);
                        var affectedItems = ApplyResolvedTranslationToGroup(
                            activeGroupedByOriginal,
                            propagationGroupedByOriginal,
                            activeSegmentIds,
                            item.OriginalText,
                            restoredTranslation,
                            settings.SourceLanguage,
                            settings.TargetLanguage,
                            string.Empty,
                            forceReview: false,
                            reviewReason: string.Empty);
                        processedCount += MarkProcessed(affectedItems);
                        changedItems.AddRange(affectedItems);
                    }

                    remaining = nextRemaining;
                    PersistChangedItems(persistState, changedItems);
                }
                catch (OperationCanceledException)
                {
                    var changedItems = new List<ExtractedTextItem>();
                    foreach (var item in currentBatch)
                    {
                        changedItems.AddRange(ApplyToGroup(activeGroupedByOriginal, item.OriginalText, static groupItem =>
                        {
                            if (groupItem.NeedsTranslation)
                            {
                                groupItem.MarkStopped();
                            }
                        }));
                    }

                    PersistChangedItems(persistState, changedItems);
                    throw;
                }
                catch (TranslationProviderException ex)
                {
                    if (attempt < retryCount)
                    {
                        continue;
                    }

                    var changedItems = new List<ExtractedTextItem>();
                    foreach (var item in currentBatch)
                    {
                        var affectedItems = ApplyFailureToGroup(
                            activeGroupedByOriginal,
                            item.OriginalText,
                            "번역 실패",
                            MapErrorKind(ex.Kind, ex.StatusCode is null ? null : (int)ex.StatusCode.Value),
                            ex.Message);
                        processedCount += MarkProcessed(affectedItems);
                        changedItems.AddRange(affectedItems);
                    }
                    PersistChangedItems(persistState, changedItems);
                }
                catch (Exception ex)
                {
                    if (attempt < retryCount)
                    {
                        continue;
                    }

                    var changedItems = new List<ExtractedTextItem>();
                    foreach (var item in currentBatch)
                    {
                        var affectedItems = ApplyFailureToGroup(
                            activeGroupedByOriginal,
                            item.OriginalText,
                            "번역 실패",
                            "배치 실패",
                            ex.Message);
                        processedCount += MarkProcessed(affectedItems);
                        changedItems.AddRange(affectedItems);
                    }
                    PersistChangedItems(persistState, changedItems);
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

    private void LogDictionaryHitIfEnabled(
        ProviderSettings settings,
        ExtractedTextItem item,
        DictionaryFirstTranslationMatch match,
        int affectedItemCount)
    {
        if (!settings.EnableDictionaryHitLogging)
        {
            return;
        }

        _dictionaryHitLogger.LogHit(new DictionaryHitLogEntry(
            item.SegmentId,
            item.RelativePath,
            item.LineNumber,
            item.OriginalText,
            match.TranslatedText,
            match.TranslationSource,
            match.MatchKind,
            match.MatchedTerm,
            item.SourceKey,
            item.SymbolNamespace,
            item.OriginalSymbolKey,
            affectedItemCount,
            match.ForceReview,
            match.ReviewReason,
            match.DictionaryStore,
            match.PersistedToNaverDictionary,
            match.SourceUrl,
            match.ReviewRequired));
    }

    private static void PersistChangedItems(
        Action<IReadOnlyList<ExtractedTextItem>>? persistState,
        IReadOnlyList<ExtractedTextItem> changedItems)
    {
        if (persistState is null || changedItems.Count == 0)
        {
            return;
        }

        var distinctItems = changedItems
            .GroupBy(item => item.SegmentId, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToList();
        if (distinctItems.Count == 0)
        {
            return;
        }

        persistState(distinctItems);
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

    private static IReadOnlyList<ExtractedTextItem> ApplyResolvedTranslationToGroup(
        IReadOnlyDictionary<string, List<ExtractedTextItem>> activeGroupedByOriginal,
        IReadOnlyDictionary<string, List<ExtractedTextItem>> propagationGroupedByOriginal,
        IReadOnlySet<string> activeSegmentIds,
        string originalText,
        string translatedText,
        string sourceLanguage,
        string targetLanguage,
        string translationSource,
        bool forceReview,
        string reviewReason)
    {
        var affectedItems = ApplyToGroup(
            activeGroupedByOriginal,
            originalText,
            item =>
            {
                var normalizedTranslation = NormalizeTranslationForItem(item, translatedText);
                var hardFailureReason = GetHardFailureReasonForItem(
                    item,
                    normalizedTranslation,
                    sourceLanguage,
                    targetLanguage);
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

                var resolvedReviewReason = MergeReviewReason(
                    forceReview,
                    reviewReason,
                    IdentifierSegmentTypes.IsIdentifier(item.SegmentType)
                        ? null
                        : TranslationQualityRules.GetReviewReason(item.OriginalText, normalizedTranslation));
                item.ApplyTranslationState(
                    resolvedReviewReason is null ? "번역 완료" : "검수 필요",
                    "통과",
                    resolvedReviewReason ?? string.Empty,
                    true,
                    normalizedTranslation);
                item.TranslationSource = translationSource;
            });

        if (!propagationGroupedByOriginal.TryGetValue(originalText, out var propagationGroup))
        {
            return affectedItems;
        }

        foreach (var item in propagationGroup.Where(item => !activeSegmentIds.Contains(item.SegmentId) && item.NeedsTranslation))
        {
            var normalizedTranslation = NormalizeTranslationForItem(item, translatedText);
            var hardFailureReason = GetHardFailureReasonForItem(
                item,
                normalizedTranslation,
                sourceLanguage,
                targetLanguage);
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

            var resolvedReviewReason = MergeReviewReason(
                forceReview,
                reviewReason,
                IdentifierSegmentTypes.IsIdentifier(item.SegmentType)
                    ? null
                    : TranslationQualityRules.GetReviewReason(item.OriginalText, normalizedTranslation));
            item.ApplyTranslationState(
                resolvedReviewReason is null ? "번역 완료" : "검수 필요",
                "통과",
                resolvedReviewReason ?? string.Empty,
                true,
                normalizedTranslation);
            item.TranslationSource = translationSource;
            affectedItems.Add(item);
        }

        return affectedItems;
    }

    private static string NormalizeTranslationForItem(ExtractedTextItem item, string translatedText)
    {
        return IdentifierSegmentTypes.IsIdentifier(item.SegmentType)
            ? TranslationQualityRules.NormalizeIdentifierText(translatedText)
            : TranslationQualityRules.NormalizeTranslatedText(item.FileType, translatedText, item.PreserveWhitespace);
    }

    private static TranslationQualityRules.HardFailureReason? GetHardFailureReasonForItem(
        ExtractedTextItem item,
        string normalizedTranslation,
        string sourceLanguage,
        string targetLanguage)
    {
        return IdentifierSegmentTypes.IsIdentifier(item.SegmentType)
            ? TranslationQualityRules.GetIdentifierHardFailureReason(normalizedTranslation)
            : TranslationQualityRules.GetHardFailureReason(
                normalizedTranslation,
                sourceLanguage,
                targetLanguage,
                item.OriginalText);
    }

    private static string? MergeReviewReason(bool forceReview, string configuredReason, string? qualityReason)
    {
        if (!string.IsNullOrWhiteSpace(configuredReason))
        {
            return configuredReason;
        }

        if (forceReview)
        {
            return "사전 fallback 결과입니다. 검토가 필요합니다.";
        }

        return qualityReason;
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

    private static bool SupportsPromptingDictionary(TranslationProviderType providerType)
    {
        return providerType is TranslationProviderType.OpenAi
            or TranslationProviderType.XiaomiMiMo
            or TranslationProviderType.LmStudio
            or TranslationProviderType.Lemonade;
    }

    private static IReadOnlyList<GlossaryHint> BuildPromptDictionaryHints(IReadOnlyList<UserDictionaryEntry> dictionaryEntries)
    {
        return dictionaryEntries
            .Where(entry => entry.IsEnabled && entry.ApplyMode == UserDictionaryApplyMode.Prompting)
            .Select(entry => new GlossaryHint(
                entry.Source.Trim(),
                entry.Target.Trim(),
                "USER"))
            .Where(static hint =>
                !string.IsNullOrWhiteSpace(hint.Source)
                && !string.IsNullOrWhiteSpace(hint.Target)
                && !hint.Source.Contains('\r')
                && !hint.Source.Contains('\n')
                && !hint.Target.Contains('\r')
                && !hint.Target.Contains('\n'))
            .GroupBy(static hint => hint.Source, StringComparer.Ordinal)
            .Select(static group => group.Last())
            .OrderByDescending(static hint => hint.Source.Length)
            .ThenBy(static hint => hint.Source, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<GlossaryHint> MergeGlossaryHints(
        IReadOnlyList<GlossaryHint> glossaryHints,
        IReadOnlyList<GlossaryHint> promptDictionaryHints)
    {
        if (glossaryHints.Count == 0)
        {
            return promptDictionaryHints;
        }

        if (promptDictionaryHints.Count == 0)
        {
            return glossaryHints;
        }

        var merged = new Dictionary<string, GlossaryHint>(StringComparer.Ordinal);
        foreach (var hint in glossaryHints)
        {
            merged[hint.Source] = hint;
        }

        foreach (var hint in promptDictionaryHints)
        {
            merged[hint.Source] = hint;
        }

        return merged.Values
            .OrderByDescending(static hint => hint.Source.Length)
            .ThenBy(static hint => hint.Source, StringComparer.Ordinal)
            .ToList();
    }
}
