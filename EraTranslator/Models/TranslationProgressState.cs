namespace EraTranslator.Models;

public sealed class TranslationProgressState
{
    public DateTimeOffset SavedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public List<TranslationProgressItemState> Items { get; init; } = [];
}

public sealed class TranslationProgressItemState
{
    public string SegmentId { get; init; } = string.Empty;

    public string Status { get; init; } = "대기";

    public string ValidationStatus { get; init; } = "검증 전";

    public string TranslationError { get; init; } = string.Empty;

    public string TranslatedText { get; init; } = string.Empty;

    public bool CanSave { get; init; }

    public int ReferenceImpactCount { get; init; }

    public bool RequiresReferenceRewrite { get; init; }

    public string ReferenceResolutionStatus { get; init; } = string.Empty;
}
