namespace EraTranslator.Models;

public sealed class AppConfig
{
    public string GameDirectory { get; init; } = string.Empty;

    public string OutputDirectory { get; init; } = string.Empty;

    public SaveMode SaveMode { get; init; } = SaveMode.ExportCopy;

    public TranslationProviderType ProviderType { get; init; } = TranslationProviderType.OpenAi;

    public string BaseUrl { get; init; } = "https://api.openai.com/v1";

    public string Model { get; init; } = "gpt-4o-mini";

    public string SourceLanguage { get; init; } = "ja";

    public string TargetLanguage { get; init; } = "ko";

    public int BatchSize { get; init; } = 1;

    public int RetryCount { get; init; } = 1;

    public double Temperature { get; init; } = 0.3;

    public bool DisableThinking { get; init; } = true;

    public bool EnableRequestResponseLogging { get; init; }

    public string SystemPromptTemplate { get; init; } = string.Empty;

    public string RetryPromptTemplate { get; init; } = string.Empty;

    public bool ExcludeNonSourceText { get; init; }

    public string PapagoClientId { get; init; } = string.Empty;

    public string PapagoClientSecret { get; init; } = string.Empty;

    public string EzTransInstallationPath { get; init; } = string.Empty;

    public int EzTransProcessCount { get; init; } = 1;

    public Dictionary<TranslationProviderType, string> ProviderApiKeys { get; init; } = [];
}
