namespace EraTranslator.Services;

public sealed record GlossaryCacheEntry(
    string SegmentId,
    string Source,
    string Target,
    string SourceFileType,
    string SourceStatus,
    string SourceSegmentType,
    string SourceNamespace,
    bool IsReferenceBearingKey,
    int RenderedLength,
    int StaticScore,
    string FirstChar,
    int PhaseMask,
    string EligibilityHash,
    int ScopeVersion,
    DateTimeOffset UpdatedAtUtc)
{
    public GlossaryHint ToHint()
    {
        return new GlossaryHint(Source, Target, SourceFileType)
        {
            SourceStatus = SourceStatus,
            SourceSegmentType = SourceSegmentType,
            SourceNamespace = SourceNamespace,
            IsReferenceBearingKey = IsReferenceBearingKey,
        };
    }
}

public sealed record GlossaryCacheRefreshResult(
    IReadOnlyList<GlossaryCacheEntry> Entries,
    IReadOnlyList<GlossaryCacheEntry> UpsertEntries,
    IReadOnlyList<string> DeleteSegmentIds,
    int HitCount,
    int MissCount,
    int UpdatedCount,
    int DeletedCount);
