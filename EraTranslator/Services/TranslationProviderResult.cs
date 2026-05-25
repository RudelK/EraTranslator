namespace EraTranslator.Services;

public sealed class TranslationProviderResult
{
    public Dictionary<string, string> Translations { get; } = [];

    public Dictionary<string, TranslationErrorDetail> Errors { get; } = [];
}
