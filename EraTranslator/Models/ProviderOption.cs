namespace EraTranslator.Models;

public sealed class ProviderOption
{
    public required TranslationProviderType ProviderType { get; init; }

    public required string DisplayName { get; init; }

    public required bool IsAvailable { get; init; }

    public string AvailabilityText { get; init; } = string.Empty;
}
