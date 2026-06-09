using System.Text.Json.Serialization;

namespace EraTranslator.Models;

public sealed class TeamScanManifestUploadRequest
{
    [JsonPropertyName("scan_revision_id")]
    public string ScanRevisionId { get; init; } = string.Empty;

    [JsonPropertyName("source_archive_sha256")]
    public string SourceArchiveSha256 { get; init; } = string.Empty;

    [JsonPropertyName("documents")]
    public List<TeamScanManifestDocument> Documents { get; init; } = [];

    [JsonPropertyName("items")]
    public List<TeamScanManifestItem> Items { get; init; } = [];

    [JsonPropertyName("symbol_references")]
    public List<TeamScanManifestSymbolReference> SymbolReferences { get; init; } = [];

    [JsonPropertyName("identifier_occurrences")]
    public List<TeamScanManifestIdentifierOccurrence> IdentifierOccurrences { get; init; } = [];
}

public sealed class TeamScanManifestDocument
{
    [JsonPropertyName("document_id")]
    public string DocumentId { get; init; } = string.Empty;

    [JsonPropertyName("relative_path")]
    public string RelativePath { get; init; } = string.Empty;

    [JsonPropertyName("file_type")]
    public string FileType { get; init; } = string.Empty;

    [JsonPropertyName("encoding")]
    public string EncodingName { get; init; } = string.Empty;
}

public sealed class TeamScanManifestValidationResponse
{
    [JsonPropertyName("scan_revision_id")]
    public string ScanRevisionId { get; init; } = string.Empty;

    [JsonPropertyName("validation_status")]
    public string ValidationStatus { get; init; } = string.Empty;

    [JsonPropertyName("validation_messages")]
    public List<Dictionary<string, object?>> ValidationMessages { get; init; } = [];

    [JsonPropertyName("document_count")]
    public int DocumentCount { get; init; }

    [JsonPropertyName("item_count")]
    public int ItemCount { get; init; }

    [JsonPropertyName("shared_key_count")]
    public int SharedKeyCount { get; init; }
}

public sealed class TeamScanManifestItem
{
    [JsonPropertyName("segment_id")]
    public string SegmentId { get; init; } = string.Empty;

    [JsonPropertyName("document_id")]
    public string DocumentId { get; init; } = string.Empty;

    [JsonPropertyName("relative_path")]
    public string RelativePath { get; init; } = string.Empty;

    [JsonPropertyName("file_type")]
    public string FileType { get; init; } = string.Empty;

    [JsonPropertyName("segment_type")]
    public string SegmentType { get; init; } = string.Empty;

    [JsonPropertyName("line_number")]
    public int LineNumber { get; init; }

    [JsonPropertyName("original_text")]
    public string OriginalText { get; init; } = string.Empty;

    [JsonPropertyName("source_key")]
    public string SourceKey { get; init; } = string.Empty;

    [JsonPropertyName("symbol_namespace")]
    public string SymbolNamespace { get; init; } = string.Empty;

    [JsonPropertyName("original_symbol_key")]
    public string OriginalSymbolKey { get; init; } = string.Empty;

    [JsonPropertyName("is_reference_bearing_key")]
    public bool IsReferenceBearingKey { get; init; }
}

public sealed class TeamScanManifestSymbolReference
{
    [JsonPropertyName("document_id")]
    public string DocumentId { get; init; } = string.Empty;

    [JsonPropertyName("namespace")]
    public string Namespace { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("resolution_kind")]
    public string ResolutionKind { get; init; } = string.Empty;

    [JsonPropertyName("original_key")]
    public string OriginalKey { get; init; } = string.Empty;

    [JsonPropertyName("variable_name")]
    public string VariableName { get; init; } = string.Empty;

    [JsonPropertyName("expression_text")]
    public string ExpressionText { get; init; } = string.Empty;

    [JsonPropertyName("absolute_start")]
    public int AbsoluteStart { get; init; }

    [JsonPropertyName("length")]
    public int Length { get; init; }

    [JsonPropertyName("line_number")]
    public int LineNumber { get; init; }

    [JsonPropertyName("candidate_keys")]
    public List<string> CandidateKeys { get; init; } = [];
}

public sealed class TeamScanManifestIdentifierOccurrence
{
    [JsonPropertyName("document_id")]
    public string DocumentId { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("original_name")]
    public string OriginalName { get; init; } = string.Empty;

    [JsonPropertyName("absolute_start")]
    public int AbsoluteStart { get; init; }

    [JsonPropertyName("length")]
    public int Length { get; init; }

    [JsonPropertyName("line_number")]
    public int LineNumber { get; init; }
}
