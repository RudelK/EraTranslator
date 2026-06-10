using System.Text.Encodings.Web;
using System.Text.Json;
using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class AppConfigService
{
    private const string LegacyDefaultProtectedFullWidthCharacters = "／【】＜＞「」（）『』％";
    private const string DefaultSettingsFolderName = "UserSettings";
    private const string DefaultConfigFileName = "EraTranslator.config.json";
    private const string DefaultSecretFileName = "EraTranslator.secrets.dat";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _baseDirectory;
    private readonly string _appDataRoot;
    private readonly string _configFileName;
    private readonly string _secretFileName;
    private readonly IAppSecretStore _secretStore;

    public AppConfigService(
        string? baseDirectory = null,
        string? appDataRoot = null,
        string settingsFolderName = DefaultSettingsFolderName,
        string configFileName = DefaultConfigFileName,
        string secretFileName = DefaultSecretFileName)
    {
        _baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : baseDirectory;
        _appDataRoot = string.IsNullOrWhiteSpace(appDataRoot)
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : appDataRoot;
        _configFileName = configFileName;
        _secretFileName = secretFileName;
        ConfigPath = Path.Combine(_baseDirectory, settingsFolderName, _configFileName);
        _secretStore = new ProtectedAppSecretStore(Path.GetDirectoryName(ConfigPath), _secretFileName);
        var settingsDirectory = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrWhiteSpace(settingsDirectory))
        {
            Directory.CreateDirectory(settingsDirectory);
        }
    }

    public string ConfigPath { get; }

    public string SecretPath => _secretStore.FilePath;

    public AppConfig Load()
    {
        MigrateLegacyFilesIfNeeded();

        if (!File.Exists(ConfigPath))
        {
            var config = MergeSecrets(new AppConfig(), _secretStore.Load());
            Save(config);
            return config;
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
                ProjectMode = loaded.ProjectMode,
                TeamServerUrl = loaded.TeamServerUrl,
                TeamProjectId = loaded.TeamProjectId,
                TeamDisplayName = loaded.TeamDisplayName,
                ClientId = string.IsNullOrWhiteSpace(loaded.ClientId) ? Guid.NewGuid().ToString("N") : loaded.ClientId,
                TeamWorkspaceRoot = loaded.TeamWorkspaceRoot,
                TeamAuthToken = mergedSecrets.TeamAuthToken,
                ProviderType = loaded.ProviderType,
                BaseUrl = loaded.BaseUrl,
                Model = loaded.Model,
                LmStudioPresetProfile = loaded.LmStudioPresetProfile,
                PromptProfile = loaded.PromptProfile,
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
                EnableDictionaryHitLogging = loaded.EnableDictionaryHitLogging,
                EnablePerformanceDebugLogging = loaded.EnablePerformanceDebugLogging,
                SystemPromptTemplate = NormalizePromptPlaceholders(loaded.SystemPromptTemplate),
                RetryPromptTemplate = NormalizePromptPlaceholders(loaded.RetryPromptTemplate),
                ExcludeNonSourceText = loaded.ExcludeNonSourceText,
                EnableBundledDictionaryFirstPass = loaded.EnableBundledDictionaryFirstPass,
                EnableKanaTransliterationFallback = loaded.EnableKanaTransliterationFallback,
                EnableNaverJapaneseDictionaryLookup = loaded.EnableNaverJapaneseDictionaryLookup,
                EnableKanjiReadingFallback = loaded.EnableKanjiReadingFallback,
                DictionaryFirstMaxTermLength = loaded.DictionaryFirstMaxTermLength <= 0 ? new AppConfig().DictionaryFirstMaxTermLength : loaded.DictionaryFirstMaxTermLength,
                EnableGlossaryHints = loaded.EnableGlossaryHints,
                GlossaryMaxHintsPerBatch = loaded.GlossaryMaxHintsPerBatch <= 0 ? new AppConfig().GlossaryMaxHintsPerBatch : loaded.GlossaryMaxHintsPerBatch,
                GlossaryCharacterBudget = loaded.GlossaryCharacterBudget <= 0 ? new AppConfig().GlossaryCharacterBudget : loaded.GlossaryCharacterBudget,
                GlossaryMinSourceLength = loaded.GlossaryMinSourceLength <= 0 ? new AppConfig().GlossaryMinSourceLength : loaded.GlossaryMinSourceLength,
                EnableBundledDictionaryGlossaryHints = loaded.EnableBundledDictionaryGlossaryHints,
                BundledDictionaryGlossaryMaxHintsPerBatch = loaded.BundledDictionaryGlossaryMaxHintsPerBatch <= 0 ? new AppConfig().BundledDictionaryGlossaryMaxHintsPerBatch : loaded.BundledDictionaryGlossaryMaxHintsPerBatch,
                BundledDictionaryGlossaryCharacterBudget = loaded.BundledDictionaryGlossaryCharacterBudget <= 0 ? new AppConfig().BundledDictionaryGlossaryCharacterBudget : loaded.BundledDictionaryGlossaryCharacterBudget,
                BundledDictionaryGlossaryMinTermLength = loaded.BundledDictionaryGlossaryMinTermLength <= 0 ? new AppConfig().BundledDictionaryGlossaryMinTermLength : loaded.BundledDictionaryGlossaryMinTermLength,
                BundledDictionaryGlossaryMaxTermLength = loaded.BundledDictionaryGlossaryMaxTermLength <= 0 ? new AppConfig().BundledDictionaryGlossaryMaxTermLength : loaded.BundledDictionaryGlossaryMaxTermLength,
                RefreshGridDuringTranslatedTextEdit = loaded.RefreshGridDuringTranslatedTextEdit,
                ProtectedFullWidthCharacters = NormalizeProtectedFullWidthCharacters(loaded.ProtectedFullWidthCharacters),
                PapagoClientId = loaded.PapagoClientId,
                PapagoClientSecret = mergedSecrets.PapagoClientSecret,
                EzTransInstallationPath = loaded.EzTransInstallationPath,
                EzTransProcessCount = loaded.EzTransProcessCount,
                ProviderApiKeys = mergedSecrets.ProviderApiKeys,
            };

            if (HasLegacyPlaintextSecrets(loaded)
                || string.IsNullOrWhiteSpace(loaded.ClientId))
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

    public string GetLegacyBaseConfigPath()
    {
        return Path.Combine(_baseDirectory, _configFileName);
    }

    public string GetLegacyBaseSecretPath()
    {
        return Path.Combine(_baseDirectory, _secretFileName);
    }

    public string GetLegacyAppDataConfigPath()
    {
        return Path.Combine(_appDataRoot, "EraTranslator", _configFileName);
    }

    public string GetLegacyAppDataSecretPath()
    {
        return Path.Combine(_appDataRoot, "EraTranslator", _secretFileName);
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
            TeamAuthToken = config.TeamAuthToken,
            ProviderApiKeys = new Dictionary<TranslationProviderType, string>(config.ProviderApiKeys),
        });

        var sanitized = new AppConfigDocument
        {
            GameDirectory = config.GameDirectory,
            OutputDirectory = config.OutputDirectory,
            SaveMode = config.SaveMode,
            ProjectMode = config.ProjectMode,
            TeamServerUrl = config.TeamServerUrl,
            TeamProjectId = config.TeamProjectId,
            TeamDisplayName = config.TeamDisplayName,
            ClientId = string.IsNullOrWhiteSpace(config.ClientId) ? Guid.NewGuid().ToString("N") : config.ClientId,
            TeamWorkspaceRoot = config.TeamWorkspaceRoot,
            ProviderType = config.ProviderType,
            BaseUrl = config.BaseUrl,
            Model = config.Model,
            LmStudioPresetProfile = config.LmStudioPresetProfile,
            PromptProfile = config.PromptProfile,
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
            EnableDictionaryHitLogging = config.EnableDictionaryHitLogging,
            EnablePerformanceDebugLogging = config.EnablePerformanceDebugLogging,
            SystemPromptTemplate = config.SystemPromptTemplate,
            RetryPromptTemplate = config.RetryPromptTemplate,
            ExcludeNonSourceText = config.ExcludeNonSourceText,
            EnableBundledDictionaryFirstPass = config.EnableBundledDictionaryFirstPass,
            EnableKanaTransliterationFallback = config.EnableKanaTransliterationFallback,
            EnableNaverJapaneseDictionaryLookup = config.EnableNaverJapaneseDictionaryLookup,
            EnableKanjiReadingFallback = config.EnableKanjiReadingFallback,
            DictionaryFirstMaxTermLength = config.DictionaryFirstMaxTermLength,
            EnableGlossaryHints = config.EnableGlossaryHints,
            GlossaryMaxHintsPerBatch = config.GlossaryMaxHintsPerBatch,
            GlossaryCharacterBudget = config.GlossaryCharacterBudget,
            GlossaryMinSourceLength = config.GlossaryMinSourceLength,
            EnableBundledDictionaryGlossaryHints = config.EnableBundledDictionaryGlossaryHints,
            BundledDictionaryGlossaryMaxHintsPerBatch = config.BundledDictionaryGlossaryMaxHintsPerBatch,
            BundledDictionaryGlossaryCharacterBudget = config.BundledDictionaryGlossaryCharacterBudget,
            BundledDictionaryGlossaryMinTermLength = config.BundledDictionaryGlossaryMinTermLength,
            BundledDictionaryGlossaryMaxTermLength = config.BundledDictionaryGlossaryMaxTermLength,
            RefreshGridDuringTranslatedTextEdit = config.RefreshGridDuringTranslatedTextEdit,
            ProtectedFullWidthCharacters = config.ProtectedFullWidthCharacters,
            PapagoClientId = config.PapagoClientId,
            EzTransInstallationPath = config.EzTransInstallationPath,
            EzTransProcessCount = config.EzTransProcessCount,
        };

        var json = JsonSerializer.Serialize(sanitized, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }

    private void MigrateLegacyFilesIfNeeded()
    {
        MoveFirstExistingFileIfMissing(
            ConfigPath,
            GetLegacyBaseConfigPath(),
            GetLegacyAppDataConfigPath());
        MoveFirstExistingFileIfMissing(
            SecretPath,
            GetLegacyBaseSecretPath(),
            GetLegacyAppDataSecretPath());
    }

    private static void MoveFirstExistingFileIfMissing(string targetPath, params string[] sourcePaths)
    {
        if (File.Exists(targetPath))
        {
            return;
        }

        var targetFullPath = Path.GetFullPath(targetPath);
        foreach (var sourcePath in sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)
                || !File.Exists(sourcePath))
            {
                continue;
            }

            var sourceFullPath = Path.GetFullPath(sourcePath);
            if (string.Equals(sourceFullPath, targetFullPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            try
            {
                File.Move(sourcePath, targetPath);
            }
            catch
            {
                try
                {
                    File.Copy(sourcePath, targetPath, overwrite: false);
                    File.Delete(sourcePath);
                }
                catch
                {
                    // If migration fails, leave the legacy file in place and continue with defaults.
                }
            }

            return;
        }
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
        var teamAuthToken = !string.IsNullOrWhiteSpace(storedSecrets.TeamAuthToken)
            ? storedSecrets.TeamAuthToken
            : loaded.TeamAuthToken;

        var mergedSecrets = new AppSecrets
        {
            PapagoClientSecret = papagoClientSecret,
            TeamAuthToken = teamAuthToken,
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
            ProjectMode = config.ProjectMode,
            TeamServerUrl = config.TeamServerUrl,
            TeamProjectId = config.TeamProjectId,
            TeamDisplayName = config.TeamDisplayName,
            ClientId = string.IsNullOrWhiteSpace(config.ClientId) ? Guid.NewGuid().ToString("N") : config.ClientId,
            TeamWorkspaceRoot = config.TeamWorkspaceRoot,
            TeamAuthToken = secrets.TeamAuthToken,
            ProviderType = config.ProviderType,
            BaseUrl = config.BaseUrl,
            Model = config.Model,
            LmStudioPresetProfile = config.LmStudioPresetProfile,
            PromptProfile = config.PromptProfile,
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
            EnableDictionaryHitLogging = config.EnableDictionaryHitLogging,
            EnablePerformanceDebugLogging = config.EnablePerformanceDebugLogging,
            SystemPromptTemplate = config.SystemPromptTemplate,
            RetryPromptTemplate = config.RetryPromptTemplate,
            ExcludeNonSourceText = config.ExcludeNonSourceText,
            EnableBundledDictionaryFirstPass = config.EnableBundledDictionaryFirstPass,
            EnableKanaTransliterationFallback = config.EnableKanaTransliterationFallback,
            EnableNaverJapaneseDictionaryLookup = config.EnableNaverJapaneseDictionaryLookup,
            EnableKanjiReadingFallback = config.EnableKanjiReadingFallback,
            DictionaryFirstMaxTermLength = config.DictionaryFirstMaxTermLength,
            EnableGlossaryHints = config.EnableGlossaryHints,
            GlossaryMaxHintsPerBatch = config.GlossaryMaxHintsPerBatch,
            GlossaryCharacterBudget = config.GlossaryCharacterBudget,
            GlossaryMinSourceLength = config.GlossaryMinSourceLength,
            EnableBundledDictionaryGlossaryHints = config.EnableBundledDictionaryGlossaryHints,
            BundledDictionaryGlossaryMaxHintsPerBatch = config.BundledDictionaryGlossaryMaxHintsPerBatch,
            BundledDictionaryGlossaryCharacterBudget = config.BundledDictionaryGlossaryCharacterBudget,
            BundledDictionaryGlossaryMinTermLength = config.BundledDictionaryGlossaryMinTermLength,
            BundledDictionaryGlossaryMaxTermLength = config.BundledDictionaryGlossaryMaxTermLength,
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
            || !string.IsNullOrWhiteSpace(config.TeamAuthToken)
            || config.ProviderApiKeys.Any(pair => !string.IsNullOrWhiteSpace(pair.Value));
    }

    private static string NormalizePromptPlaceholders(string template)
    {
        return string.IsNullOrWhiteSpace(template)
            ? template
            : template.Replace("[[[ERA_PH_0]]]", "__PH0__", StringComparison.Ordinal);
    }

    private static string NormalizeProtectedFullWidthCharacters(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || string.Equals(value, LegacyDefaultProtectedFullWidthCharacters, StringComparison.Ordinal))
        {
            return new AppConfig().ProtectedFullWidthCharacters;
        }

        return value;
    }

    private sealed class AppConfigDocument
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

        public string EzTransInstallationPath { get; init; } = string.Empty;

        public int EzTransProcessCount { get; init; } = 1;
    }
}
