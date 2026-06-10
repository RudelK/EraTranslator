namespace EraTranslator.Models;

public sealed class ProviderSettings
{
    public TranslationProviderType ProviderType { get; init; }

    public string BaseUrl { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public PromptProfile PromptProfile { get; init; } = PromptProfile.Auto;

    public string ApiKey { get; init; } = string.Empty;

    public string SourceLanguage { get; init; } = "ja";

    public string TargetLanguage { get; init; } = "ko";

    public int BatchSize { get; init; } = 1;

    public int RetryCount { get; init; } = 1;

    public double Temperature { get; init; } = 0.3;

    public double? TopP { get; init; }

    public int? TopK { get; init; }

    public double? RepeatPenalty { get; init; }

    public double? PresencePenalty { get; init; }

    public int? Seed { get; init; }

    public int? MaxTokens { get; init; }

    public bool DisableThinking { get; init; }

    public bool EnableRequestResponseLogging { get; init; }

    public bool EnableDictionaryHitLogging { get; init; }

    public bool EnablePerformanceDebugLogging { get; init; }

    public bool ExcludeNonSourceText { get; init; }

    public bool EnableBundledDictionaryFirstPass { get; init; } = true;

    public bool EnableKanaTransliterationFallback { get; init; } = false;

    public bool EnableNaverJapaneseDictionaryLookup { get; init; } = false;

    public bool EnableKanjiReadingFallback { get; init; } = false;

    public int DictionaryFirstMaxTermLength { get; init; } = 6;

    public bool EnableGlossaryHints { get; init; } = true;

    public int GlossaryMaxHintsPerBatch { get; init; } = 8;

    public int GlossaryCharacterBudget { get; init; } = 360;

    public int GlossaryMinSourceLength { get; init; } = 2;

    public bool EnableBundledDictionaryGlossaryHints { get; init; } = true;

    public int BundledDictionaryGlossaryMaxHintsPerBatch { get; init; } = 4;

    public int BundledDictionaryGlossaryCharacterBudget { get; init; } = 160;

    public int BundledDictionaryGlossaryMinTermLength { get; init; } = 2;

    public int BundledDictionaryGlossaryMaxTermLength { get; init; } = 12;

    public string SystemPromptTemplate { get; init; } = string.Empty;

    public string RetryPromptTemplate { get; init; } = string.Empty;

    public string ProtectedFullWidthCharacters { get; init; } = "／【】＜＞「」（）『』％：";

    public string PapagoClientId { get; init; } = string.Empty;

    public string PapagoClientSecret { get; init; } = string.Empty;

    public string EzTransInstallationPath { get; init; } = string.Empty;

    public int EzTransProcessCount { get; init; } = 1;
}
