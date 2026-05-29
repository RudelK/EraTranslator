using System.Text.Encodings.Web;
using System.Text.Json;
using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class AppConfigService(string? baseDirectory = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private readonly IAppSecretStore _secretStore = new ProtectedAppSecretStore(baseDirectory);

    public string ConfigPath { get; } = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "EraTranslator.config.json");

    public string SecretPath => _secretStore.FilePath;

    public AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            return MergeSecrets(new AppConfig(), _secretStore.Load());
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
            var mergedSecrets = MergeLegacySecrets(loaded);
            var mergedConfig = new AppConfig
            {
                GameDirectory = loaded.GameDirectory,
                OutputDirectory = loaded.OutputDirectory,
                SaveMode = loaded.SaveMode,
                ProviderType = loaded.ProviderType,
                BaseUrl = loaded.BaseUrl,
                Model = loaded.Model,
                LmStudioPresetProfile = loaded.LmStudioPresetProfile,
                SourceLanguage = loaded.SourceLanguage,
                TargetLanguage = loaded.TargetLanguage,
                BatchSize = loaded.BatchSize,
                RetryCount = loaded.RetryCount,
                Temperature = loaded.Temperature,
                TopP = loaded.TopP,
                TopK = loaded.TopK,
                RepeatPenalty = loaded.RepeatPenalty,
                PresencePenalty = loaded.PresencePenalty,
                Seed = loaded.Seed,
                MaxTokens = loaded.MaxTokens,
                DisableThinking = loaded.DisableThinking,
                EnableRequestResponseLogging = loaded.EnableRequestResponseLogging,
                EnableResultStateLogging = loaded.EnableResultStateLogging,
                SystemPromptTemplate = NormalizePromptPlaceholders(loaded.SystemPromptTemplate),
                RetryPromptTemplate = NormalizePromptPlaceholders(loaded.RetryPromptTemplate),
                ExcludeNonSourceText = loaded.ExcludeNonSourceText,
                RefreshGridDuringTranslatedTextEdit = loaded.RefreshGridDuringTranslatedTextEdit,
                ProtectedFullWidthCharacters = loaded.ProtectedFullWidthCharacters ?? new AppConfig().ProtectedFullWidthCharacters,
                PapagoClientId = loaded.PapagoClientId,
                PapagoClientSecret = mergedSecrets.PapagoClientSecret,
                EzTransInstallationPath = loaded.EzTransInstallationPath,
                EzTransProcessCount = loaded.EzTransProcessCount,
                ProviderApiKeys = mergedSecrets.ProviderApiKeys,
            };

            if (HasLegacyPlaintextSecrets(loaded))
            {
                Save(mergedConfig);
            }

            return mergedConfig;
        }
        catch
        {
            return MergeSecrets(new AppConfig(), _secretStore.Load());
        }
    }

    public void Save(AppConfig config)
    {
        var directory = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _secretStore.Save(new AppSecrets
        {
            PapagoClientSecret = config.PapagoClientSecret,
            ProviderApiKeys = new Dictionary<TranslationProviderType, string>(config.ProviderApiKeys),
        });

        var sanitized = new AppConfigDocument
        {
            GameDirectory = config.GameDirectory,
            OutputDirectory = config.OutputDirectory,
            SaveMode = config.SaveMode,
            ProviderType = config.ProviderType,
            BaseUrl = config.BaseUrl,
            Model = config.Model,
            LmStudioPresetProfile = config.LmStudioPresetProfile,
            SourceLanguage = config.SourceLanguage,
            TargetLanguage = config.TargetLanguage,
            BatchSize = config.BatchSize,
            RetryCount = config.RetryCount,
            Temperature = config.Temperature,
            TopP = config.TopP,
            TopK = config.TopK,
            RepeatPenalty = config.RepeatPenalty,
            PresencePenalty = config.PresencePenalty,
            Seed = config.Seed,
            MaxTokens = config.MaxTokens,
            DisableThinking = config.DisableThinking,
            EnableRequestResponseLogging = config.EnableRequestResponseLogging,
            EnableResultStateLogging = config.EnableResultStateLogging,
            SystemPromptTemplate = config.SystemPromptTemplate,
            RetryPromptTemplate = config.RetryPromptTemplate,
            ExcludeNonSourceText = config.ExcludeNonSourceText,
            RefreshGridDuringTranslatedTextEdit = config.RefreshGridDuringTranslatedTextEdit,
            ProtectedFullWidthCharacters = config.ProtectedFullWidthCharacters,
            PapagoClientId = config.PapagoClientId,
            EzTransInstallationPath = config.EzTransInstallationPath,
            EzTransProcessCount = config.EzTransProcessCount,
        };

        var json = JsonSerializer.Serialize(sanitized, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }

    private AppSecrets MergeLegacySecrets(AppConfig loaded)
    {
        var storedSecrets = _secretStore.Load();
        var providerApiKeys = storedSecrets.ProviderApiKeys.Count > 0
            ? storedSecrets.ProviderApiKeys
            : loaded.ProviderApiKeys;
        var papagoClientSecret = !string.IsNullOrWhiteSpace(storedSecrets.PapagoClientSecret)
            ? storedSecrets.PapagoClientSecret
            : loaded.PapagoClientSecret;

        var mergedSecrets = new AppSecrets
        {
            PapagoClientSecret = papagoClientSecret,
            ProviderApiKeys = new Dictionary<TranslationProviderType, string>(providerApiKeys),
        };

        if (HasLegacyPlaintextSecrets(loaded) && !storedSecrets.HasAnySecrets)
        {
            _secretStore.Save(mergedSecrets);
        }

        return mergedSecrets;
    }

    private static AppConfig MergeSecrets(AppConfig config, AppSecrets secrets)
    {
        return new AppConfig
        {
            GameDirectory = config.GameDirectory,
            OutputDirectory = config.OutputDirectory,
            SaveMode = config.SaveMode,
            ProviderType = config.ProviderType,
            BaseUrl = config.BaseUrl,
            Model = config.Model,
            LmStudioPresetProfile = config.LmStudioPresetProfile,
            SourceLanguage = config.SourceLanguage,
            TargetLanguage = config.TargetLanguage,
            BatchSize = config.BatchSize,
            RetryCount = config.RetryCount,
            Temperature = config.Temperature,
            TopP = config.TopP,
            TopK = config.TopK,
            RepeatPenalty = config.RepeatPenalty,
            PresencePenalty = config.PresencePenalty,
            Seed = config.Seed,
            MaxTokens = config.MaxTokens,
            DisableThinking = config.DisableThinking,
            EnableRequestResponseLogging = config.EnableRequestResponseLogging,
            EnableResultStateLogging = config.EnableResultStateLogging,
            SystemPromptTemplate = config.SystemPromptTemplate,
            RetryPromptTemplate = config.RetryPromptTemplate,
            ExcludeNonSourceText = config.ExcludeNonSourceText,
            RefreshGridDuringTranslatedTextEdit = config.RefreshGridDuringTranslatedTextEdit,
            ProtectedFullWidthCharacters = config.ProtectedFullWidthCharacters,
            PapagoClientId = config.PapagoClientId,
            PapagoClientSecret = secrets.PapagoClientSecret,
            EzTransInstallationPath = config.EzTransInstallationPath,
            EzTransProcessCount = config.EzTransProcessCount,
            ProviderApiKeys = new Dictionary<TranslationProviderType, string>(secrets.ProviderApiKeys),
        };
    }

    private static bool HasLegacyPlaintextSecrets(AppConfig config)
    {
        return !string.IsNullOrWhiteSpace(config.PapagoClientSecret)
            || config.ProviderApiKeys.Any(pair => !string.IsNullOrWhiteSpace(pair.Value));
    }

    private static string NormalizePromptPlaceholders(string template)
    {
        return string.IsNullOrWhiteSpace(template)
            ? template
            : template.Replace("[[[ERA_PH_0]]]", "__PH0__", StringComparison.Ordinal);
    }

    private sealed class AppConfigDocument
    {
        public string GameDirectory { get; init; } = string.Empty;

        public string OutputDirectory { get; init; } = string.Empty;

        public SaveMode SaveMode { get; init; } = SaveMode.ExportCopy;

        public TranslationProviderType ProviderType { get; init; } = TranslationProviderType.OpenAi;

        public string BaseUrl { get; init; } = "https://api.openai.com/v1";

        public string Model { get; init; } = "gpt-4o-mini";

        public LmStudioPresetProfile LmStudioPresetProfile { get; init; } = LmStudioPresetProfile.Auto;

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

        public string SystemPromptTemplate { get; init; } = string.Empty;

        public string RetryPromptTemplate { get; init; } = string.Empty;

        public bool ExcludeNonSourceText { get; init; }

        public bool RefreshGridDuringTranslatedTextEdit { get; init; }

        public string ProtectedFullWidthCharacters { get; init; } = "／【】＜＞「」（）『』％";

        public string PapagoClientId { get; init; } = string.Empty;

        public string EzTransInstallationPath { get; init; } = string.Empty;

        public int EzTransProcessCount { get; init; } = 1;
    }
}
