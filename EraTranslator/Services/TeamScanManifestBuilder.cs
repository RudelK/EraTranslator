using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class TeamScanManifestBuilder
{
    public TeamScanManifestUploadRequest Build(
        ScanSession session,
        string scanRevisionId,
        string sourceArchiveSha256)
    {
        return new TeamScanManifestUploadRequest
        {
            ScanRevisionId = scanRevisionId,
            SourceArchiveSha256 = sourceArchiveSha256,
            Documents = session.Documents.Values
                .OrderBy(document => document.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(document => new TeamScanManifestDocument
                {
                    DocumentId = document.DocumentId,
                    RelativePath = document.RelativePath,
                    FileType = document.FileType,
                    EncodingName = document.EncodingInfo.Name,
                })
                .ToList(),
            Items = session.Items
                .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.LineNumber)
                .ThenBy(item => item.SegmentId, StringComparer.Ordinal)
                .Select(item => new TeamScanManifestItem
                {
                    SegmentId = item.SegmentId,
                    DocumentId = item.DocumentId,
                    RelativePath = item.RelativePath,
                    FileType = item.FileType,
                    SegmentType = item.SegmentType,
                    LineNumber = item.LineNumber,
                    OriginalText = item.OriginalText,
                    SourceKey = item.SourceKey ?? string.Empty,
                    SymbolNamespace = item.SymbolNamespace,
                    OriginalSymbolKey = item.OriginalSymbolKey,
                    IsReferenceBearingKey = item.IsReferenceBearingKey,
                })
                .ToList(),
            SymbolReferences = session.Documents.Values
                .SelectMany(document => document.SymbolReferences)
                .OrderBy(reference => reference.DocumentId, StringComparer.Ordinal)
                .ThenBy(reference => reference.AbsoluteStart)
                .Select(reference => new TeamScanManifestSymbolReference
                {
                    DocumentId = reference.DocumentId,
                    Namespace = reference.Namespace,
                    Kind = reference.Kind.ToString(),
                    ResolutionKind = reference.ResolutionKind.ToString(),
                    OriginalKey = reference.OriginalKey,
                    VariableName = reference.VariableName,
                    ExpressionText = reference.ExpressionText,
                    AbsoluteStart = reference.AbsoluteStart,
                    Length = reference.Length,
                    LineNumber = reference.LineNumber,
                    CandidateKeys = reference.CandidateKeys,
                })
                .ToList(),
            IdentifierOccurrences = session.Documents.Values
                .SelectMany(document => document.IdentifierOccurrences)
                .OrderBy(occurrence => occurrence.DocumentId, StringComparer.Ordinal)
                .ThenBy(occurrence => occurrence.AbsoluteStart)
                .Select(occurrence => new TeamScanManifestIdentifierOccurrence
                {
                    DocumentId = occurrence.DocumentId,
                    Kind = occurrence.Kind.ToString(),
                    Role = occurrence.Role.ToString(),
                    OriginalName = occurrence.OriginalName,
                    AbsoluteStart = occurrence.AbsoluteStart,
                    Length = occurrence.Length,
                    LineNumber = occurrence.LineNumber,
                })
                .ToList(),
        };
    }
}
