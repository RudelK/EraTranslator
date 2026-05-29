using EraTranslator.Models;
using EraTranslator.Services;
using EraTranslator.ViewModels;

namespace EraTranslator.Tests;

public sealed class TranslationSettingsViewModelTests
{
    [Fact]
    public void DecimalTextInputs_AcceptDotSeparatedValues()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedProviderOption = viewModel.ProviderOptions.Single(option => option.ProviderType == TranslationProviderType.LmStudio);

        viewModel.TemperatureText = "0.15";
        viewModel.TopPText = "0.92";
        viewModel.RepeatPenaltyText = "1.10";
        viewModel.PresencePenaltyText = "1.50";

        Assert.Equal(0.15, viewModel.Temperature);
        Assert.Equal(0.92, viewModel.TopP);
        Assert.Equal(1.10, viewModel.RepeatPenalty);
        Assert.Equal(1.50, viewModel.PresencePenalty);
    }

    [Fact]
    public void ResetTranslationOptions_LmStudioRestoresRecommendedDefaults()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedProviderOption = viewModel.ProviderOptions.Single(option => option.ProviderType == TranslationProviderType.LmStudio);
        viewModel.SourceLanguage = "en";
        viewModel.TargetLanguage = "ja";
        viewModel.BatchSize = 5;
        viewModel.RetryCount = 3;
        viewModel.DisableThinking = false;
        viewModel.ExcludeNonSourceText = true;
        viewModel.TemperatureText = "0.25";
        viewModel.TopPText = "0.8";
        viewModel.TopKText = "99";
        viewModel.RepeatPenaltyText = "1.3";
        viewModel.SeedText = "77";

        viewModel.ResetTranslationOptions();

        Assert.Equal("ja", viewModel.SourceLanguage);
        Assert.Equal("ko", viewModel.TargetLanguage);
        Assert.Equal(1, viewModel.BatchSize);
        Assert.Equal(1, viewModel.RetryCount);
        Assert.True(viewModel.DisableThinking);
        Assert.False(viewModel.ExcludeNonSourceText);
        var preset = LmStudioSamplingDefaults.GetRecommendedPreset(viewModel.Model, viewModel.DisableThinking);
        Assert.Equal(preset.Temperature, viewModel.Temperature);
        Assert.Equal(preset.TopP, viewModel.TopP);
        Assert.Equal(preset.TopK, viewModel.TopK);
        Assert.Equal(preset.RepeatPenalty, viewModel.RepeatPenalty);
        Assert.Equal(preset.PresencePenalty, viewModel.PresencePenalty);
        Assert.Null(viewModel.Seed);
        Assert.Null(viewModel.MaxTokens);
    }

    [Fact]
    public void LmStudioModelChange_QwenPresetAppliesWhenUsingDefaults()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedProviderOption = viewModel.ProviderOptions.Single(option => option.ProviderType == TranslationProviderType.LmStudio);

        viewModel.Model = "qwen/qwen3.5-9b";

        var preset = LmStudioSamplingDefaults.GetRecommendedPreset(viewModel.Model, viewModel.DisableThinking);
        Assert.Equal(preset.Temperature, viewModel.Temperature);
        Assert.Equal(preset.TopP, viewModel.TopP);
        Assert.Equal(preset.TopK, viewModel.TopK);
        Assert.Equal(preset.RepeatPenalty, viewModel.RepeatPenalty);
        Assert.Equal(preset.PresencePenalty, viewModel.PresencePenalty);
    }

    [Fact]
    public void LmStudioModelChange_DoesNotOverwriteCustomSamplingValues()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedProviderOption = viewModel.ProviderOptions.Single(option => option.ProviderType == TranslationProviderType.LmStudio);
        viewModel.TemperatureText = "0.22";
        viewModel.TopPText = "0.77";
        viewModel.TopKText = "77";
        viewModel.RepeatPenaltyText = "1.22";
        viewModel.PresencePenaltyText = "0.25";

        viewModel.Model = "qwen/qwen3.5-9b";

        Assert.Equal(0.22, viewModel.Temperature);
        Assert.Equal(0.77, viewModel.TopP);
        Assert.Equal(77, viewModel.TopK);
        Assert.Equal(1.22, viewModel.RepeatPenalty);
        Assert.Equal(0.25, viewModel.PresencePenalty);
    }

    [Fact]
    public void ApplySelectedLmStudioPreset_UsesExplicitQwenPresetEvenOnGemmaModel()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedProviderOption = viewModel.ProviderOptions.Single(option => option.ProviderType == TranslationProviderType.LmStudio);
        viewModel.Model = "google/gemma-4-27b";
        viewModel.SelectedLmStudioPresetOption = viewModel.LmStudioPresetOptions.Single(option => option.Profile == LmStudioPresetProfile.Qwen35_9B);

        viewModel.ApplySelectedLmStudioPreset();

        var preset = LmStudioSamplingDefaults.GetRecommendedPreset(LmStudioPresetProfile.Qwen35_9B, viewModel.Model, viewModel.DisableThinking);
        Assert.Equal(LmStudioPresetProfile.Qwen35_9B, viewModel.SelectedLmStudioPresetProfile);
        Assert.Equal(preset.Temperature, viewModel.Temperature);
        Assert.Equal(preset.TopP, viewModel.TopP);
        Assert.Equal(preset.TopK, viewModel.TopK);
        Assert.Equal(preset.RepeatPenalty, viewModel.RepeatPenalty);
        Assert.Equal(preset.PresencePenalty, viewModel.PresencePenalty);
    }

    [Fact]
    public void ResetTranslationOptions_UsesSelectedExplicitPreset()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedProviderOption = viewModel.ProviderOptions.Single(option => option.ProviderType == TranslationProviderType.LmStudio);
        viewModel.SelectedLmStudioPresetOption = viewModel.LmStudioPresetOptions.Single(option => option.Profile == LmStudioPresetProfile.Gemma4);
        viewModel.DisableThinking = false;

        viewModel.ResetTranslationOptions();

        var preset = LmStudioSamplingDefaults.GetRecommendedPreset(LmStudioPresetProfile.Gemma4, viewModel.Model, viewModel.DisableThinking);
        Assert.Equal(preset.Temperature, viewModel.Temperature);
        Assert.Equal(preset.TopP, viewModel.TopP);
        Assert.Equal(preset.TopK, viewModel.TopK);
        Assert.Equal(preset.RepeatPenalty, viewModel.RepeatPenalty);
        Assert.Equal(preset.PresencePenalty, viewModel.PresencePenalty);
    }

    private static TranslationSettingsViewModel CreateViewModel()
    {
        return new TranslationSettingsViewModel(
        [
            new ProviderOption
            {
                ProviderType = TranslationProviderType.OpenAi,
                DisplayName = "OpenAI",
                IsAvailable = true,
            },
            new ProviderOption
            {
                ProviderType = TranslationProviderType.LmStudio,
                DisplayName = "LM Studio",
                IsAvailable = true,
            },
        ]);
    }
}
