namespace EraTranslator.Models;

public sealed class TeamProjectState
{
    public string LastSyncedScanRevisionId { get; init; } = string.Empty;

    public string LocalSourceScanRevisionId { get; init; } = string.Empty;

    public string TeamProjectDictionaryPath { get; init; } = string.Empty;

    public string SourceArchiveSha256 { get; init; } = string.Empty;

    public Dictionary<string, TeamWorkItemState> WorkItemsBySegmentId { get; init; } = [];

    public Dictionary<string, TeamSharedKeyState> SharedKeysByLookupKey { get; init; } = [];

    public Dictionary<string, string> ConflictIdsByTargetId { get; init; } = [];

    public List<TeamOfflineSubmission> OfflineSubmissionQueue { get; init; } = [];
}

public sealed class TeamWorkItemState
{
    public string ServerItemId { get; init; } = string.Empty;

    public int ServerRevision { get; init; }

    public string LastSubmittedTranslatedText { get; init; } = string.Empty;

    public string ServerStatus { get; init; } = string.Empty;
}

public sealed class TeamSharedKeyState
{
    public string ServerSharedKeyId { get; init; } = string.Empty;

    public int ServerSharedRevision { get; init; }

    public string Namespace { get; init; } = string.Empty;

    public string Key { get; init; } = string.Empty;

    public string LastSubmittedTranslatedText { get; init; } = string.Empty;

    public string ServerStatus { get; init; } = string.Empty;
}

public sealed class TeamOfflineSubmission
{
    public string SubmissionId { get; init; } = string.Empty;

    public string ScanRevisionId { get; init; } = string.Empty;

    public List<TeamOfflineSubmissionChange> WorkItems { get; init; } = [];

    public List<TeamOfflineSubmissionChange> SharedKeys { get; init; } = [];

    public DateTimeOffset QueuedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class TeamOfflineSubmissionChange
{
    public string Id { get; init; } = string.Empty;

    public string OriginalText { get; init; } = string.Empty;

    public string TranslatedText { get; init; } = string.Empty;

    public int BaseRevision { get; init; }
}
