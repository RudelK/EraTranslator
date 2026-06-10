namespace EraTranslator.Services;

public sealed record GlossaryHint(
    string Source,
    string Target,
    string SourceFileType)
{
    public string SourceStatus { get; init; } = string.Empty;

    public string SourceSegmentType { get; init; } = string.Empty;

    public string SourceNamespace { get; init; } = string.Empty;

    public bool IsReferenceBearingKey { get; init; }

    public bool IsUserPromptingDictionary { get; init; }

    public bool IsBundledDictionary { get; init; }

    public int BundledDictionaryPriority { get; init; }

    public bool IsBundledDictionaryName { get; init; }
}
