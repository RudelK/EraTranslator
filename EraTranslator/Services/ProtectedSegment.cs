namespace EraTranslator.Services;

public sealed record ProtectedSegment(
    string Id,
    string Text,
    string OriginalText,
    IReadOnlyList<string> Placeholders);
