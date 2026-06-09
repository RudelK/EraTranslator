using System.Text.Json.Serialization;

namespace EraTranslator.Models;

public sealed class TeamProjectSummary
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("current_scan_revision_id")]
    public string? CurrentScanRevisionId { get; init; }
}

public sealed class TeamProjectListResponse
{
    [JsonPropertyName("projects")]
    public List<TeamProjectSummary> Projects { get; init; } = [];
}

public sealed class TeamSyncResponse
{
    [JsonPropertyName("project_id")]
    public string ProjectId { get; init; } = string.Empty;

    [JsonPropertyName("scan_revision_id")]
    public string ScanRevisionId { get; init; } = string.Empty;

    [JsonPropertyName("source_archive_sha256")]
    public string SourceArchiveSha256 { get; init; } = string.Empty;

    [JsonPropertyName("work_items")]
    public List<TeamWorkItemDto> WorkItems { get; init; } = [];

    [JsonPropertyName("shared_keys")]
    public List<TeamSharedKeyDto> SharedKeys { get; init; } = [];
}

public sealed class TeamWorkItemDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("scan_revision_id")]
    public string ScanRevisionId { get; init; } = string.Empty;

    [JsonPropertyName("segment_id")]
    public string SegmentId { get; init; } = string.Empty;

    [JsonPropertyName("relative_path")]
    public string RelativePath { get; init; } = string.Empty;

    [JsonPropertyName("line_number")]
    public int? LineNumber { get; init; }

    [JsonPropertyName("file_type")]
    public string FileType { get; init; } = string.Empty;

    [JsonPropertyName("segment_type")]
    public string SegmentType { get; init; } = string.Empty;

    [JsonPropertyName("original_text")]
    public string OriginalText { get; init; } = string.Empty;

    [JsonPropertyName("source_key")]
    public string? SourceKey { get; init; }

    [JsonPropertyName("symbol_namespace")]
    public string? SymbolNamespace { get; init; }

    [JsonPropertyName("original_symbol_key")]
    public string? OriginalSymbolKey { get; init; }

    [JsonPropertyName("is_reference_bearing_key")]
    public bool IsReferenceBearingKey { get; init; }

    [JsonPropertyName("translation")]
    public string? Translation { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("item_revision")]
    public int ItemRevision { get; init; }

    [JsonPropertyName("carryover_state")]
    public string CarryoverState { get; init; } = string.Empty;
}

public sealed class TeamSharedKeyDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("namespace")]
    public string Namespace { get; init; } = string.Empty;

    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("original_text")]
    public string OriginalText { get; init; } = string.Empty;

    [JsonPropertyName("translation")]
    public string? Translation { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("shared_revision")]
    public int SharedRevision { get; init; }
}

public sealed class TeamClientRegisterRequest
{
    [JsonPropertyName("client_id")]
    public string ClientId { get; init; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = string.Empty;
}

public sealed class TeamSubmitRequest
{
    [JsonPropertyName("submission_id")]
    public string SubmissionId { get; init; } = string.Empty;

    [JsonPropertyName("scan_revision_id")]
    public string ScanRevisionId { get; init; } = string.Empty;

    [JsonPropertyName("client_id")]
    public string ClientId { get; init; } = string.Empty;

    [JsonPropertyName("work_items")]
    public List<TeamSubmitChange> WorkItems { get; init; } = [];

    [JsonPropertyName("shared_keys")]
    public List<TeamSubmitChange> SharedKeys { get; init; } = [];
}

public sealed class TeamSubmitChange
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("base_revision")]
    public int BaseRevision { get; init; }

    [JsonPropertyName("translation")]
    public string? Translation { get; init; }
}

public sealed class TeamSubmitResponse
{
    [JsonPropertyName("submission_id")]
    public string SubmissionId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("applied_count")]
    public int AppliedCount { get; init; }

    [JsonPropertyName("noop_count")]
    public int NoopCount { get; init; }

    [JsonPropertyName("conflict_count")]
    public int ConflictCount { get; init; }

    [JsonPropertyName("rejected_count")]
    public int RejectedCount { get; init; }

    [JsonPropertyName("results")]
    public List<TeamSubmitChangeResult> Results { get; init; } = [];
}

public sealed class TeamSubmitChangeResult
{
    [JsonPropertyName("target_kind")]
    public string TargetKind { get; init; } = string.Empty;

    [JsonPropertyName("target_id")]
    public string TargetId { get; init; } = string.Empty;

    [JsonPropertyName("result")]
    public string Result { get; init; } = string.Empty;

    [JsonPropertyName("conflict_id")]
    public string? ConflictId { get; init; }
}
