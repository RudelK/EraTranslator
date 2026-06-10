namespace EraTranslator.Models;

public sealed class AppConfig
{
    public string GameDirectory { get; init; } = string.Empty;

    public string OutputDirectory { get; init; } = string.Empty;

    public SaveMode SaveMode { get; init; } = SaveMode.ExportCopy;

    public ProjectMode ProjectMode { get; init; } = ProjectMode.Local;

    public string TeamServerUrl { get; init; } = string.Empty;

    public string TeamProjectId { get; init; } = string.Empty;

    public string TeamDisplayName { get; init; } = string.Empty;

    public string ClientId { get; init; } = string.Empty;

    public string TeamWorkspaceRoot { get; init; } = string.Empty;

    public string TeamAuthToken { get; init; } = string.Empty;

    public TranslationProviderType ProviderType { get; init; } = TranslationProviderType.OpenAi;

    public string BaseUrl { get; init; } = "https://api.openai.com/v1";

    public string Model { get; init; } = "gpt-4o-mini";

    public LmStudioPresetProfile LmStudioPresetProfile { get; init; } = LmStudioPresetProfile.Auto;

    public PromptProfile PromptProfile { get; init; } = PromptProfile.Auto;

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

    public bool DisableThinking { get; init; } = true;

    public bool EnableRequestResponseLogging { get; init; }

    public bool EnableResultStateLogging { get; init; } = false;

    public bool EnableDictionaryHitLogging { get; init; } = false;

    public bool EnablePerformanceDebugLogging { get; init; } = false;

    public string SystemPromptTemplate { get; init; } = string.Empty;

    public string RetryPromptTemplate { get; init; } = string.Empty;

    public bool ExcludeNonSourceText { get; init; } = true;

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

    public bool RefreshGridDuringTranslatedTextEdit { get; init; }

    public string ProtectedFullWidthCharacters { get; init; } = "／【】＜＞「」（）『』％：";

    public string PapagoClientId { get; init; } = string.Empty;

    public string PapagoClientSecret { get; init; } = string.Empty;

    public string EzTransInstallationPath { get; init; } = string.Empty;

    public int EzTransProcessCount { get; init; } = 1;

    public Dictionary<TranslationProviderType, string> ProviderApiKeys { get; init; } = [];
}
