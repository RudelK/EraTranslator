namespace EraTranslator.Models;

public sealed class AppConfig
{
    public string GameDirectory { get; init; } = string.Empty;

    public string OutputDirectory { get; init; } = string.Empty;

    public SaveMode SaveMode { get; init; } = SaveMode.ExportCopy;

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

    public string SystemPromptTemplate { get; init; } = string.Empty;

    public string RetryPromptTemplate { get; init; } = string.Empty;

    public bool ExcludeNonSourceText { get; init; } = true;

    public bool EnableBundledDictionaryFirstPass { get; init; } = true;

    public bool EnableKanaTransliterationFallback { get; init; } = true;

    public bool EnableNaverJapaneseDictionaryLookup { get; init; } = false;

    public bool EnableKanjiReadingFallback { get; init; } = true;

    public int DictionaryFirstMaxTermLength { get; init; } = 6;

    public bool RefreshGridDuringTranslatedTextEdit { get; init; }

    public string ProtectedFullWidthCharacters { get; init; } = "／【】＜＞「」（）『』％";

    public string PapagoClientId { get; init; } = string.Empty;

    public string PapagoClientSecret { get; init; } = string.Empty;

    public string EzTransInstallationPath { get; init; } = string.Empty;

    public int EzTransProcessCount { get; init; } = 1;

    public Dictionary<TranslationProviderType, string> ProviderApiKeys { get; init; } = [];
}
