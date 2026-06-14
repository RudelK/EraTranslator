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
        Assert.True(viewModel.ExcludeNonSourceText);
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
    public void DefaultExcludeNonSourceText_IsEnabled()
    {
        var viewModel = CreateViewModel();

        Assert.True(viewModel.ExcludeNonSourceText);
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
        Assert.Equal(LmStudioSamplingDefaults.GetRecommendedMaxTokens(viewModel.SelectedLmStudioPresetProfile, viewModel.Model), viewModel.MaxTokens);
    }

    [Fact]
    public void LmStudioModelChange_HyMt2ThirtyBAutoPresetAlsoUpdatesMaxTokens()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedProviderOption = viewModel.ProviderOptions.Single(option => option.ProviderType == TranslationProviderType.LmStudio);

        viewModel.Model = "tencent/Hy-MT2-30B-A3B";

        var preset = LmStudioSamplingDefaults.GetRecommendedPreset(viewModel.Model, viewModel.DisableThinking);
        Assert.Equal(preset.Temperature, viewModel.Temperature);
        Assert.Equal(preset.TopP, viewModel.TopP);
        Assert.Equal(preset.TopK, viewModel.TopK);
        Assert.Equal(preset.RepeatPenalty, viewModel.RepeatPenalty);
        Assert.Equal(preset.PresencePenalty, viewModel.PresencePenalty);
        Assert.Equal(4096, viewModel.MaxTokens);
    }

    [Fact]
    public void LmStudioModelChange_Gemma4E4BAutoPresetAppliesDedicatedSampling()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedProviderOption = viewModel.ProviderOptions.Single(option => option.ProviderType == TranslationProviderType.LmStudio);

        viewModel.Model = "google/gemma-4-e4b";

        var preset = LmStudioSamplingDefaults.GetRecommendedPreset(viewModel.Model, viewModel.DisableThinking);
        Assert.Equal(preset.Temperature, viewModel.Temperature);
        Assert.Equal(preset.TopP, viewModel.TopP);
        Assert.Equal(preset.TopK, viewModel.TopK);
        Assert.Equal(preset.RepeatPenalty, viewModel.RepeatPenalty);
        Assert.Equal(preset.PresencePenalty, viewModel.PresencePenalty);
    }

    [Fact]
    public void LmStudioModelChange_LeavingHyMt2ClearsAutoMaxTokens()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedProviderOption = viewModel.ProviderOptions.Single(option => option.ProviderType == TranslationProviderType.LmStudio);
        viewModel.Model = "tencent/Hy-MT2-30B-A3B";

        viewModel.Model = "google/gemma-4-27b";

        Assert.Null(viewModel.MaxTokens);
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
    public void LmStudioPresetOptions_IncludeGemma4E4BTranslateGemmaAndHyMt2()
    {
        var viewModel = CreateViewModel();

        Assert.Contains(viewModel.LmStudioPresetOptions, option => option.Profile == LmStudioPresetProfile.Gemma4E4B);
        Assert.Contains(viewModel.LmStudioPresetOptions, option => option.Profile == LmStudioPresetProfile.TranslateGemma);
        Assert.Contains(viewModel.LmStudioPresetOptions, option => option.Profile == LmStudioPresetProfile.HyMt2_7B);
        Assert.Contains(viewModel.LmStudioPresetOptions, option => option.Profile == LmStudioPresetProfile.HyMt2_30B_A3B);
    }

    [Fact]
    public void ApplySelectedLmStudioPreset_UsesExplicitGemma4E4BPreset()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedProviderOption = viewModel.ProviderOptions.Single(option => option.ProviderType == TranslationProviderType.LmStudio);
        viewModel.Model = "google/gemma-4-27b";
        viewModel.SelectedLmStudioPresetOption = viewModel.LmStudioPresetOptions.Single(option => option.Profile == LmStudioPresetProfile.Gemma4E4B);

        viewModel.ApplySelectedLmStudioPreset();

        var preset = LmStudioSamplingDefaults.GetRecommendedPreset(LmStudioPresetProfile.Gemma4E4B, viewModel.Model, viewModel.DisableThinking);
        Assert.Equal(preset.Temperature, viewModel.Temperature);
        Assert.Equal(preset.TopP, viewModel.TopP);
        Assert.Equal(preset.TopK, viewModel.TopK);
        Assert.Equal(preset.RepeatPenalty, viewModel.RepeatPenalty);
        Assert.Equal(preset.PresencePenalty, viewModel.PresencePenalty);
    }

    [Fact]
    public void ApplySelectedLmStudioPreset_UsesExplicitTranslateGemmaPreset()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedProviderOption = viewModel.ProviderOptions.Single(option => option.ProviderType == TranslationProviderType.LmStudio);
        viewModel.Model = "google/gemma-4-27b";
        viewModel.SelectedLmStudioPresetOption = viewModel.LmStudioPresetOptions.Single(option => option.Profile == LmStudioPresetProfile.TranslateGemma);

        viewModel.ApplySelectedLmStudioPreset();

        var preset = LmStudioSamplingDefaults.GetRecommendedPreset(LmStudioPresetProfile.TranslateGemma, viewModel.Model, viewModel.DisableThinking);
        Assert.Equal(preset.Temperature, viewModel.Temperature);
        Assert.Equal(preset.TopP, viewModel.TopP);
        Assert.Equal(preset.TopK, viewModel.TopK);
        Assert.Equal(preset.RepeatPenalty, viewModel.RepeatPenalty);
        Assert.Equal(preset.PresencePenalty, viewModel.PresencePenalty);
    }

    [Fact]
    public void ApplySelectedLmStudioPreset_UsesExplicitHyMt2SevenBPreset()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedProviderOption = viewModel.ProviderOptions.Single(option => option.ProviderType == TranslationProviderType.LmStudio);
        viewModel.Model = "qwen/qwen3.5-9b";
        viewModel.SelectedLmStudioPresetOption = viewModel.LmStudioPresetOptions.Single(option => option.Profile == LmStudioPresetProfile.HyMt2_7B);

        viewModel.ApplySelectedLmStudioPreset();

        var preset = LmStudioSamplingDefaults.GetRecommendedPreset(LmStudioPresetProfile.HyMt2_7B, viewModel.Model, viewModel.DisableThinking);
        Assert.Equal(preset.Temperature, viewModel.Temperature);
        Assert.Equal(preset.TopP, viewModel.TopP);
        Assert.Equal(preset.TopK, viewModel.TopK);
        Assert.Equal(preset.RepeatPenalty, viewModel.RepeatPenalty);
        Assert.Equal(preset.PresencePenalty, viewModel.PresencePenalty);
        Assert.Equal(4096, viewModel.MaxTokens);
    }

    [Fact]
    public void ApplySelectedLmStudioPreset_UsesExplicitHyMt2ThirtyBPresetAndAllowsNegativeTopK()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedProviderOption = viewModel.ProviderOptions.Single(option => option.ProviderType == TranslationProviderType.LmStudio);
        viewModel.Model = "tencent/Hy-MT2-30B-A3B";
        viewModel.SelectedLmStudioPresetOption = viewModel.LmStudioPresetOptions.Single(option => option.Profile == LmStudioPresetProfile.HyMt2_30B_A3B);

        viewModel.ApplySelectedLmStudioPreset();

        var preset = LmStudioSamplingDefaults.GetRecommendedPreset(LmStudioPresetProfile.HyMt2_30B_A3B, viewModel.Model, viewModel.DisableThinking);
        Assert.Equal(preset.Temperature, viewModel.Temperature);
        Assert.Equal(preset.TopP, viewModel.TopP);
        Assert.Equal(-1, viewModel.TopK);
        Assert.Equal(preset.RepeatPenalty, viewModel.RepeatPenalty);
        Assert.Equal(preset.PresencePenalty, viewModel.PresencePenalty);
        Assert.Equal(4096, viewModel.MaxTokens);
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

    [Fact]
    public void PromptProfile_AutoDetectsHyMt2AndResetUsesProfileDefaults()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedProviderOption = viewModel.ProviderOptions.Single(option => option.ProviderType == TranslationProviderType.LmStudio);
        viewModel.Model = "tencent/Hy-MT2-7B";
        viewModel.SelectedPromptProfile = PromptProfile.Auto;

        viewModel.ResetPromptTemplates();

        Assert.Contains("only output the translated result without any additional explanation", viewModel.SystemPromptTemplate, StringComparison.Ordinal);
        Assert.Contains("ONLY output the translated result", viewModel.RetryPromptTemplate, StringComparison.Ordinal);
    }

    [Fact]
    public void PromptProfile_AutoDetectsGemma4E4BAndResetUsesProfileDefaults()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedProviderOption = viewModel.ProviderOptions.Single(option => option.ProviderType == TranslationProviderType.LmStudio);
        viewModel.Model = "google/gemma-4-e4b";
        viewModel.SelectedPromptProfile = PromptProfile.Auto;

        viewModel.ResetPromptTemplates();

        Assert.Contains("Treat short labels, glossary entries, item names, skill names, stat names, trait names, and body-part terms as dictionary-style entries.", viewModel.SystemPromptTemplate, StringComparison.Ordinal);
        Assert.Contains("Prioritize exact short-label, glossary, and term-level accuracy.", viewModel.RetryPromptTemplate, StringComparison.Ordinal);
    }

    [Fact]
    public void PromptProfile_AutoUpdatesTemplatesWhenModelChangesToGemma4E4B()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedProviderOption = viewModel.ProviderOptions.Single(option => option.ProviderType == TranslationProviderType.LmStudio);
        viewModel.SelectedPromptProfile = PromptProfile.Auto;
        viewModel.Model = "google/gemma-4-27b";

        viewModel.Model = "google/gemma-4-e4b";

        Assert.Contains("Treat short labels, glossary entries, item names, skill names, stat names, trait names, and body-part terms as dictionary-style entries.", viewModel.SystemPromptTemplate, StringComparison.Ordinal);
        Assert.Contains("Prioritize exact short-label, glossary, and term-level accuracy.", viewModel.RetryPromptTemplate, StringComparison.Ordinal);
    }

    [Fact]
    public void PromptProfileStatusText_ShowsResolvedAutoProfileAfterModelChange()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedProviderOption = viewModel.ProviderOptions.Single(option => option.ProviderType == TranslationProviderType.LmStudio);
        viewModel.SelectedPromptProfile = PromptProfile.Auto;

        viewModel.Model = "google/gemma-4-e4b";

        Assert.Equal("자동 선택 결과: Gemma 4 E4B", viewModel.PromptProfileStatusText);
    }

    [Fact]
    public void PromptProfile_ChangeDoesNotOverwriteCustomEditedTemplates()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedProviderOption = viewModel.ProviderOptions.Single(option => option.ProviderType == TranslationProviderType.OpenAi);
        viewModel.SystemPromptTemplate = "CUSTOM SYSTEM";
        viewModel.RetryPromptTemplate = "CUSTOM RETRY";

        viewModel.SelectedPromptProfile = PromptProfile.HyMt2;

        Assert.Equal("CUSTOM SYSTEM", viewModel.SystemPromptTemplate);
        Assert.Equal("CUSTOM RETRY", viewModel.RetryPromptTemplate);
    }

    [Fact]
    public void TopKText_AcceptsNegativeOne()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedProviderOption = viewModel.ProviderOptions.Single(option => option.ProviderType == TranslationProviderType.LmStudio);

        viewModel.TopKText = "-1";

        Assert.Equal(-1, viewModel.TopK);
    }

    [Fact]
    public void BuildSettings_IncludesPromptProfile()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedProviderOption = viewModel.ProviderOptions.Single(option => option.ProviderType == TranslationProviderType.OpenAi);
        viewModel.SelectedPromptProfile = PromptProfile.HyMt2;

        var settings = viewModel.BuildSettings();

        Assert.Equal(PromptProfile.HyMt2, settings.PromptProfile);
    }

    [Fact]
    public void BuildSettings_IncludesIndependentDictionaryOptions()
    {
        var viewModel = CreateViewModel();
        viewModel.EnableBundledDictionaryFirstPass = false;
        viewModel.EnableKanaTransliterationFallback = true;
        viewModel.EnableNaverJapaneseDictionaryLookup = true;
        viewModel.EnableKanjiReadingFallback = false;
        viewModel.EnableDictionaryHitLogging = true;
        viewModel.DictionaryFirstMaxTermLength = 5;
        viewModel.EnableGlossaryHints = false;
        viewModel.GlossaryMaxHintsPerBatch = 4;
        viewModel.GlossaryCharacterBudget = 240;
        viewModel.GlossaryMinSourceLength = 3;
        viewModel.EnableBundledDictionaryGlossaryHints = false;
        viewModel.BundledDictionaryGlossaryMaxHintsPerBatch = 2;
        viewModel.BundledDictionaryGlossaryCharacterBudget = 80;
        viewModel.BundledDictionaryGlossaryMinTermLength = 3;
        viewModel.BundledDictionaryGlossaryMaxTermLength = 8;

        var settings = viewModel.BuildSettings();

        Assert.False(settings.EnableBundledDictionaryFirstPass);
        Assert.True(settings.EnableKanaTransliterationFallback);
        Assert.True(settings.EnableNaverJapaneseDictionaryLookup);
        Assert.False(settings.EnableKanjiReadingFallback);
        Assert.True(settings.EnableDictionaryHitLogging);
        Assert.Equal(5, settings.DictionaryFirstMaxTermLength);
        Assert.False(settings.EnableGlossaryHints);
        Assert.Equal(4, settings.GlossaryMaxHintsPerBatch);
        Assert.Equal(240, settings.GlossaryCharacterBudget);
        Assert.Equal(3, settings.GlossaryMinSourceLength);
        Assert.False(settings.EnableBundledDictionaryGlossaryHints);
        Assert.Equal(2, settings.BundledDictionaryGlossaryMaxHintsPerBatch);
        Assert.Equal(80, settings.BundledDictionaryGlossaryCharacterBudget);
        Assert.Equal(3, settings.BundledDictionaryGlossaryMinTermLength);
        Assert.Equal(8, settings.BundledDictionaryGlossaryMaxTermLength);
    }

    [Fact]
    public void XiaomiMiMoProvider_AppliesRecommendedDefaultsAndStaticModels()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedProviderOption = viewModel.ProviderOptions.Single(option => option.ProviderType == TranslationProviderType.XiaomiMiMo);

        Assert.True(viewModel.CanEditModel);
        Assert.False(viewModel.CanLoadModels);
        Assert.Equal("https://api.xiaomimimo.com/v1", viewModel.BaseUrl);
        Assert.Equal("mimo-v2.5-pro", viewModel.Model);
        Assert.Equal(1.0, viewModel.Temperature);
        Assert.Equal(0.95, viewModel.TopP);
        Assert.Null(viewModel.TopK);
        Assert.Null(viewModel.RepeatPenalty);
        Assert.Null(viewModel.MaxTokens);
        Assert.Contains("mimo-v2.5-pro", viewModel.AvailableModels);
        Assert.Contains("mimo-v2.5", viewModel.AvailableModels);
        Assert.Contains("mimo-v2-flash", viewModel.AvailableModels);
    }

    [Fact]
    public void OllamaProvider_AppliesLocalDefaultsAndSupportsModelCatalog()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedProviderOption = viewModel.ProviderOptions.Single(option => option.ProviderType == TranslationProviderType.Ollama);

        Assert.True(viewModel.CanEditModel);
        Assert.True(viewModel.CanLoadModels);
        Assert.True(viewModel.SupportsAdvancedSampling);
        Assert.Equal("http://127.0.0.1:11434/v1", viewModel.BaseUrl);
        Assert.Equal("llama3.1", viewModel.Model);
        Assert.Contains("JSON schema", viewModel.ProviderHelpText, StringComparison.Ordinal);
        Assert.Contains("tokenized fallback", viewModel.ProviderHelpText, StringComparison.Ordinal);
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
                ProviderType = TranslationProviderType.XiaomiMiMo,
                DisplayName = "Xiaomi MiMo",
                IsAvailable = true,
            },
            new ProviderOption
            {
                ProviderType = TranslationProviderType.Ollama,
                DisplayName = "Ollama",
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
