using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed record TeamSyncApplyResult(
    int WorkItemMetadataCount,
    int SharedKeyMetadataCount,
    int AppliedWorkItemTranslations,
    int AppliedSharedKeyTranslations,
    bool SourceRevisionMismatch);

public sealed record TeamSubmitBuildResult(
    TeamSubmitRequest Request,
    int WorkItemChangeCount,
    int SharedKeyChangeCount);

public sealed record TeamSubmitApplyResult(
    int AppliedCount,
    int NoopCount,
    int ConflictCount,
    int RejectedCount);

public sealed class TeamCollaborationService(ITeamServerClient? teamServerClient = null, TeamProjectStateService? stateService = null)
{
    private readonly ITeamServerClient _teamServerClient = teamServerClient ?? new TeamServerClient();
    private readonly TeamProjectStateService _stateService = stateService ?? new TeamProjectStateService();

    public async Task<TeamSyncResponse> SyncAsync(
        TeamProjectContext context,
        string bearerToken,
        CancellationToken cancellationToken = default)
    {
        return await _teamServerClient.SyncAsync(context.ServerUrl, bearerToken, context.ProjectId, cancellationToken);
    }

    public async Task<IReadOnlyList<TeamProjectSummary>> GetProjectsAsync(
        string serverUrl,
        string bearerToken,
        CancellationToken cancellationToken = default)
    {
        return await _teamServerClient.GetProjectsAsync(serverUrl, bearerToken, cancellationToken);
    }

    public async Task RegisterClientAsync(
        string serverUrl,
        string bearerToken,
        string clientId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        await _teamServerClient.RegisterClientAsync(serverUrl, bearerToken, clientId, displayName, cancellationToken);
    }

    public async Task<TeamScanManifestValidationResponse> UploadScanManifestAsync(
        TeamProjectContext context,
        string bearerToken,
        TeamScanManifestUploadRequest manifest,
        CancellationToken cancellationToken = default)
    {
        return await _teamServerClient.UploadScanManifestAsync(
            context.ServerUrl,
            bearerToken,
            context.ProjectId,
            manifest,
            cancellationToken);
    }

    public async Task<TeamSubmitResponse> SubmitAsync(
        TeamProjectContext context,
        string bearerToken,
        TeamSubmitRequest request,
        CancellationToken cancellationToken = default)
    {
        return await _teamServerClient.SubmitAsync(context.ServerUrl, bearerToken, context.ProjectId, request, cancellationToken);
    }

    public TeamSyncApplyResult ApplySyncResponse(
        TeamProjectContext context,
        TeamSyncResponse sync,
        IEnumerable<ExtractedTextItem> items,
        TeamProjectState state)
    {
        var workItemsBySegmentId = sync.WorkItems
            .Where(item => !string.IsNullOrWhiteSpace(item.SegmentId))
            .GroupBy(item => item.SegmentId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var sharedKeysByLookupKey = sync.SharedKeys
            .Where(key => !string.IsNullOrWhiteSpace(key.Namespace) && !string.IsNullOrWhiteSpace(key.Key))
            .GroupBy(key => CreateSharedKeyLookupKey(key.Namespace, key.Key), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

        var appliedWorkItemTranslations = 0;
        var appliedSharedKeyTranslations = 0;
        foreach (var item in items)
        {
            if (workItemsBySegmentId.TryGetValue(item.SegmentId, out var workItem))
            {
                var previous = state.WorkItemsBySegmentId.GetValueOrDefault(item.SegmentId);
                if (TryApplyServerTranslation(item, workItem.Translation, previous?.LastSubmittedTranslatedText, workItem.Status))
                {
                    appliedWorkItemTranslations++;
                }
            }

            if (item.IsReferenceBearingKey)
            {
                var lookupKey = CreateSharedKeyLookupKey(item.SymbolNamespace, item.OriginalSymbolKey);
                if (sharedKeysByLookupKey.TryGetValue(lookupKey, out var sharedKey))
                {
                    var previous = state.SharedKeysByLookupKey.GetValueOrDefault(lookupKey);
                    if (TryApplyServerTranslation(item, sharedKey.Translation, previous?.LastSubmittedTranslatedText, sharedKey.Status))
                    {
                        appliedSharedKeyTranslations++;
                    }
                }
            }
        }

        var updatedState = new TeamProjectState
        {
            LastSyncedScanRevisionId = sync.ScanRevisionId,
            LocalSourceScanRevisionId = state.LocalSourceScanRevisionId,
            TeamProjectDictionaryPath = string.IsNullOrWhiteSpace(state.TeamProjectDictionaryPath)
                ? context.TeamProjectDictionaryDirectory
                : state.TeamProjectDictionaryPath,
            SourceArchiveSha256 = string.IsNullOrWhiteSpace(sync.SourceArchiveSha256)
                ? state.SourceArchiveSha256
                : sync.SourceArchiveSha256,
            WorkItemsBySegmentId = workItemsBySegmentId.ToDictionary(
                pair => pair.Key,
                pair => new TeamWorkItemState
                {
                    ServerItemId = pair.Value.Id,
                    ServerRevision = pair.Value.ItemRevision,
                    LastSubmittedTranslatedText = pair.Value.Translation ?? state.WorkItemsBySegmentId.GetValueOrDefault(pair.Key)?.LastSubmittedTranslatedText ?? string.Empty,
                    ServerStatus = pair.Value.Status,
                },
                StringComparer.Ordinal),
            SharedKeysByLookupKey = sharedKeysByLookupKey.ToDictionary(
                pair => pair.Key,
                pair => new TeamSharedKeyState
                {
                    ServerSharedKeyId = pair.Value.Id,
                    ServerSharedRevision = pair.Value.SharedRevision,
                    Namespace = pair.Value.Namespace,
                    Key = pair.Value.Key,
                    LastSubmittedTranslatedText = pair.Value.Translation ?? state.SharedKeysByLookupKey.GetValueOrDefault(pair.Key)?.LastSubmittedTranslatedText ?? string.Empty,
                    ServerStatus = pair.Value.Status,
                },
                StringComparer.Ordinal),
            ConflictIdsByTargetId = state.ConflictIdsByTargetId,
            OfflineSubmissionQueue = state.OfflineSubmissionQueue,
        };
        _stateService.Save(context, updatedState);

        return new TeamSyncApplyResult(
            WorkItemMetadataCount: workItemsBySegmentId.Count,
            SharedKeyMetadataCount: sharedKeysByLookupKey.Count,
            AppliedWorkItemTranslations: appliedWorkItemTranslations,
            AppliedSharedKeyTranslations: appliedSharedKeyTranslations,
            SourceRevisionMismatch: !string.IsNullOrWhiteSpace(state.LocalSourceScanRevisionId)
                && !string.Equals(state.LocalSourceScanRevisionId, sync.ScanRevisionId, StringComparison.Ordinal));
    }

    public TeamSubmitBuildResult BuildSubmitRequest(
        TeamProjectContext context,
        string scanRevisionId,
        string clientId,
        IEnumerable<ExtractedTextItem> items,
        TeamProjectState state)
    {
        var workItemChanges = new List<TeamSubmitChange>();
        var sharedKeyChanges = new List<TeamSubmitChange>();

        foreach (var item in items)
        {
            if (!item.CanSave || item.IsExcluded || string.IsNullOrWhiteSpace(item.TranslatedText))
            {
                continue;
            }

            if (state.WorkItemsBySegmentId.TryGetValue(item.SegmentId, out var workItem)
                && !string.Equals(item.TranslatedText, workItem.LastSubmittedTranslatedText, StringComparison.Ordinal))
            {
                workItemChanges.Add(new TeamSubmitChange
                {
                    Id = workItem.ServerItemId,
                    BaseRevision = workItem.ServerRevision,
                    Translation = item.TranslatedText,
                });
            }

            if (!item.IsReferenceBearingKey)
            {
                continue;
            }

            var lookupKey = CreateSharedKeyLookupKey(item.SymbolNamespace, item.OriginalSymbolKey);
            if (state.SharedKeysByLookupKey.TryGetValue(lookupKey, out var sharedKey)
                && !string.Equals(item.TranslatedText, sharedKey.LastSubmittedTranslatedText, StringComparison.Ordinal))
            {
                sharedKeyChanges.Add(new TeamSubmitChange
                {
                    Id = sharedKey.ServerSharedKeyId,
                    BaseRevision = sharedKey.ServerSharedRevision,
                    Translation = item.TranslatedText,
                });
            }
        }

        var request = new TeamSubmitRequest
        {
            SubmissionId = Guid.NewGuid().ToString("N"),
            ScanRevisionId = scanRevisionId,
            ClientId = clientId,
            WorkItems = workItemChanges
                .GroupBy(change => change.Id, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToList(),
            SharedKeys = sharedKeyChanges
                .GroupBy(change => change.Id, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToList(),
        };

        return new TeamSubmitBuildResult(request, request.WorkItems.Count, request.SharedKeys.Count);
    }

    public TeamSubmitApplyResult ApplySubmitResponse(
        TeamProjectContext context,
        TeamSubmitRequest request,
        TeamSubmitResponse response,
        IEnumerable<ExtractedTextItem> items,
        TeamProjectState state)
    {
        var submittedWorkItems = request.WorkItems.ToDictionary(change => change.Id, StringComparer.Ordinal);
        var submittedSharedKeys = request.SharedKeys.ToDictionary(change => change.Id, StringComparer.Ordinal);
        var conflictIds = new Dictionary<string, string>(state.ConflictIdsByTargetId, StringComparer.Ordinal);

        foreach (var result in response.Results)
        {
            if (!string.IsNullOrWhiteSpace(result.ConflictId))
            {
                conflictIds[result.TargetId] = result.ConflictId;
            }
        }

        var workItemsBySegmentId = state.WorkItemsBySegmentId.ToDictionary(pair => pair.Key, pair =>
        {
            if (!submittedWorkItems.TryGetValue(pair.Value.ServerItemId, out var change)
                || !IsSubmitAccepted(response, pair.Value.ServerItemId))
            {
                return pair.Value;
            }

            return new TeamWorkItemState
            {
                ServerItemId = pair.Value.ServerItemId,
                ServerRevision = pair.Value.ServerRevision + 1,
                LastSubmittedTranslatedText = change.Translation ?? string.Empty,
                ServerStatus = "submitted",
            };
        }, StringComparer.Ordinal);
        var sharedKeysByLookupKey = state.SharedKeysByLookupKey.ToDictionary(pair => pair.Key, pair =>
        {
            if (!submittedSharedKeys.TryGetValue(pair.Value.ServerSharedKeyId, out var change)
                || !IsSubmitAccepted(response, pair.Value.ServerSharedKeyId))
            {
                return pair.Value;
            }

            return new TeamSharedKeyState
            {
                ServerSharedKeyId = pair.Value.ServerSharedKeyId,
                ServerSharedRevision = pair.Value.ServerSharedRevision + 1,
                Namespace = pair.Value.Namespace,
                Key = pair.Value.Key,
                LastSubmittedTranslatedText = change.Translation ?? string.Empty,
                ServerStatus = "submitted",
            };
        }, StringComparer.Ordinal);

        var targetIdsWithConflicts = response.Results
            .Where(result => !string.IsNullOrWhiteSpace(result.ConflictId))
            .Select(result => result.TargetId)
            .ToHashSet(StringComparer.Ordinal);
        MarkConflictItems(items, state, targetIdsWithConflicts);

        _stateService.Save(context, new TeamProjectState
        {
            LastSyncedScanRevisionId = state.LastSyncedScanRevisionId,
            LocalSourceScanRevisionId = state.LocalSourceScanRevisionId,
            TeamProjectDictionaryPath = string.IsNullOrWhiteSpace(state.TeamProjectDictionaryPath)
                ? context.TeamProjectDictionaryDirectory
                : state.TeamProjectDictionaryPath,
            SourceArchiveSha256 = state.SourceArchiveSha256,
            WorkItemsBySegmentId = workItemsBySegmentId,
            SharedKeysByLookupKey = sharedKeysByLookupKey,
            ConflictIdsByTargetId = conflictIds,
            OfflineSubmissionQueue = state.OfflineSubmissionQueue,
        });

        return new TeamSubmitApplyResult(response.AppliedCount, response.NoopCount, response.ConflictCount, response.RejectedCount);
    }

    public void EnqueueOfflineSubmission(TeamProjectContext context, TeamSubmitRequest request, IEnumerable<ExtractedTextItem> items, TeamProjectState state)
    {
        var itemByServerId = state.WorkItemsBySegmentId
            .ToDictionary(pair => pair.Value.ServerItemId, pair => pair.Key, StringComparer.Ordinal);
        var sharedByServerId = state.SharedKeysByLookupKey
            .ToDictionary(pair => pair.Value.ServerSharedKeyId, pair => pair.Key, StringComparer.Ordinal);
        var itemBySegmentId = items.ToDictionary(item => item.SegmentId, StringComparer.Ordinal);
        var offlineSubmission = new TeamOfflineSubmission
        {
            SubmissionId = request.SubmissionId,
            ScanRevisionId = request.ScanRevisionId,
            WorkItems = request.WorkItems.Select(change =>
            {
                itemByServerId.TryGetValue(change.Id, out var segmentId);
                var originalText = segmentId is not null && itemBySegmentId.TryGetValue(segmentId, out var item)
                    ? item.OriginalText
                    : string.Empty;
                return new TeamOfflineSubmissionChange
                {
                    Id = change.Id,
                    OriginalText = originalText,
                    TranslatedText = change.Translation ?? string.Empty,
                    BaseRevision = change.BaseRevision,
                };
            }).ToList(),
            SharedKeys = request.SharedKeys.Select(change =>
            {
                sharedByServerId.TryGetValue(change.Id, out var lookupKey);
                var originalText = lookupKey is not null && state.SharedKeysByLookupKey.TryGetValue(lookupKey, out var sharedKey)
                    ? sharedKey.Key
                    : string.Empty;
                return new TeamOfflineSubmissionChange
                {
                    Id = change.Id,
                    OriginalText = originalText,
                    TranslatedText = change.Translation ?? string.Empty,
                    BaseRevision = change.BaseRevision,
                };
            }).ToList(),
        };

        var queue = state.OfflineSubmissionQueue
            .Where(submission => !string.Equals(submission.SubmissionId, request.SubmissionId, StringComparison.Ordinal))
            .Append(offlineSubmission)
            .ToList();
        _stateService.Save(context, new TeamProjectState
        {
            LastSyncedScanRevisionId = state.LastSyncedScanRevisionId,
            LocalSourceScanRevisionId = state.LocalSourceScanRevisionId,
            TeamProjectDictionaryPath = string.IsNullOrWhiteSpace(state.TeamProjectDictionaryPath)
                ? context.TeamProjectDictionaryDirectory
                : state.TeamProjectDictionaryPath,
            SourceArchiveSha256 = state.SourceArchiveSha256,
            WorkItemsBySegmentId = state.WorkItemsBySegmentId,
            SharedKeysByLookupKey = state.SharedKeysByLookupKey,
            ConflictIdsByTargetId = state.ConflictIdsByTargetId,
            OfflineSubmissionQueue = queue,
        });
    }

    public static string CreateSharedKeyLookupKey(string symbolNamespace, string key)
    {
        return $"{SymbolNamespaceRegistry.CanonicalizeNamespace(symbolNamespace)}\u001f{key}";
    }

    private static bool TryApplyServerTranslation(
        ExtractedTextItem item,
        string? serverTranslation,
        string? lastSubmittedTranslation,
        string serverStatus)
    {
        if (string.IsNullOrWhiteSpace(serverTranslation))
        {
            return false;
        }

        var localDirty = !string.IsNullOrWhiteSpace(lastSubmittedTranslation)
            && !string.Equals(item.TranslatedText, lastSubmittedTranslation, StringComparison.Ordinal);
        if (localDirty)
        {
            return false;
        }

        if (string.Equals(item.TranslatedText, serverTranslation, StringComparison.Ordinal))
        {
            return false;
        }

        var status = string.Equals(serverStatus, "conflict", StringComparison.OrdinalIgnoreCase)
            ? "검수 필요"
            : "번역 완료";
        var validationStatus = string.Equals(serverStatus, "conflict", StringComparison.OrdinalIgnoreCase)
            ? "충돌"
            : "통과";
        var error = string.Equals(serverStatus, "conflict", StringComparison.OrdinalIgnoreCase)
            ? "서버 충돌 상태입니다. 팀 서버에서 해소가 필요합니다."
            : string.Empty;

        item.ApplyTranslationState(status, validationStatus, error, canSave: true, translatedText: serverTranslation);
        return true;
    }

    private static bool IsSubmitAccepted(TeamSubmitResponse response, string targetId)
    {
        var result = response.Results.LastOrDefault(result => string.Equals(result.TargetId, targetId, StringComparison.Ordinal));
        return result is not null
            && (string.Equals(result.Result, "Applied", StringComparison.OrdinalIgnoreCase)
                || string.Equals(result.Result, "NoOp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(result.Result, "applied", StringComparison.OrdinalIgnoreCase)
                || string.Equals(result.Result, "noop", StringComparison.OrdinalIgnoreCase));
    }

    private static void MarkConflictItems(
        IEnumerable<ExtractedTextItem> items,
        TeamProjectState state,
        HashSet<string> targetIdsWithConflicts)
    {
        if (targetIdsWithConflicts.Count == 0)
        {
            return;
        }

        foreach (var item in items)
        {
            if (state.WorkItemsBySegmentId.TryGetValue(item.SegmentId, out var workItem)
                && targetIdsWithConflicts.Contains(workItem.ServerItemId))
            {
                item.ApplyTranslationState(
                    "검수 필요",
                    "충돌",
                    "팀 서버 제출 중 충돌이 발생했습니다.",
                    canSave: false,
                    item.TranslatedText);
                continue;
            }

            var lookupKey = CreateSharedKeyLookupKey(item.SymbolNamespace, item.OriginalSymbolKey);
            if (state.SharedKeysByLookupKey.TryGetValue(lookupKey, out var sharedKey)
                && targetIdsWithConflicts.Contains(sharedKey.ServerSharedKeyId))
            {
                item.ApplyTranslationState(
                    "검수 필요",
                    "충돌",
                    "팀 서버 공통키 제출 중 충돌이 발생했습니다.",
                    canSave: false,
                    item.TranslatedText);
            }
        }
    }
}
