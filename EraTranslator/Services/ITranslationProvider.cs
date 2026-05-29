namespace EraTranslator.Services;

public interface ITranslationProvider
{
    Task<TranslationProviderResult> TranslateAsync(
        IReadOnlyList<ProtectedSegment> requests,
        ProviderSettings settings,
        CancellationToken cancellationToken,
        IReadOnlyList<GlossaryHint>? glossaryHints = null);
}
