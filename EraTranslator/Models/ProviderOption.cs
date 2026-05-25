namespace EraTranslator.Models;

public sealed class ProviderOption
{
    public required TranslationProviderType ProviderType { get; init; }

    public required string DisplayName { get; init; }

    public required bool IsAvailable { get; init; }

    public string AvailabilityText => IsAvailable ? string.Empty : "준비 중";
}
