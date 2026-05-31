using System.Collections.ObjectModel;
using System.Globalization;
using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.ViewModels;

public sealed class TranslationSettingsViewModel : BindableBase
{
    private readonly ModelCatalogService _modelCatalogService = new();
    private readonly EzTransXpInstallationService _ezTransXpInstallationService;
    private readonly Dictionary<TranslationProviderType, string> _providerApiKeys = [];
    private PromptProfile _selectedPromptProfile = PromptProfile.Auto;
    private string _lastAppliedSystemPromptTemplate = TranslationPromptTemplates.DefaultSystemPrompt;
    private string _lastAppliedRetryPromptTemplate = TranslationPromptTemplates.DefaultRetryPrompt;
    private LmStudioPresetOption? _selectedLmStudioPresetOption;
    private ProviderOption? _selectedProviderOption;
    private string _baseUrl = string.Empty;
    private string _model = string.Empty;
    private string _sourceLanguage = "ja";
    private string _targetLanguage = "ko";
    private int _batchSize = 1;
    private int _retryCount = 1;
    private double _temperature = 0.3;
    private string _temperatureText = "0.3";
    private double? _topP;
    private string _topPText = string.Empty;
    private int? _topK;
    private string _topKText = string.Empty;
    private double? _repeatPenalty;
    private string _repeatPenaltyText = string.Empty;
    private double? _presencePenalty;
    private string _presencePenaltyText = string.Empty;
    private int? _seed;
    private string _seedText = string.Empty;
    private int? _maxTokens;
    private string _maxTokensText = string.Empty;
    private bool _disableThinking = true;
    private bool _enableRequestResponseLogging;
    private bool _enableResultStateLogging = false;
    private bool _enableDictionaryHitLogging = false;
    private bool _excludeNonSourceText = true;
    private bool _enableBundledDictionaryFirstPass = true;
    private bool _enableKanaTransliterationFallback = true;
    private bool _enableNaverJapaneseDictionaryLookup = false;
    private bool _enableKanjiReadingFallback = true;
    private int _dictionaryFirstMaxTermLength = 6;
    private string _systemPromptTemplate = TranslationPromptTemplates.DefaultSystemPrompt;
    private string _retryPromptTemplate = TranslationPromptTemplates.DefaultRetryPrompt;
    private string _papagoClientId = string.Empty;
    private string _papagoClientSecret = string.Empty;
    private string _ezTransInstallationPath = string.Empty;
    private int _ezTransProcessCount = 1;
    private string _ezTransStatusText = "EzTransXP 설치 상태를 확인하세요.";
    private string _ezTransEngineText = string.Empty;
    private string _statusText = "공급자와 연결 정보를 확인하세요.";
    private bool _isLoadingModels;
    private readonly BundledJapaneseLexiconService _bundledJapaneseLexiconService = new();

    public TranslationSettingsViewModel(
        IEnumerable<ProviderOption> providerOptions,
        EzTransXpInstallationService? ezTransXpInstallationService = null)
    {
        _ezTransXpInstallationService = ezTransXpInstallationService ?? new EzTransXpInstallationService();
        ProviderOptions = new ObservableCollection<ProviderOption>(providerOptions.Select(option => new ProviderOption
        {
            ProviderType = option.ProviderType,
            DisplayName = option.DisplayName,
            IsAvailable = option.IsAvailable,
            AvailabilityText = option.AvailabilityText,
        }));
        SelectedLmStudioPresetOption = LmStudioPresetOptions[0];
        RefreshEzTransInstallationStatus();
    }

    public ObservableCollection<ProviderOption> ProviderOptions { get; }

    public IReadOnlyList<LmStudioPresetOption> LmStudioPresetOptions { get; } =
    [
        new() { Profile = LmStudioPresetProfile.Auto, DisplayName = LmStudioSamplingDefaults.GetPresetDisplayName(LmStudioPresetProfile.Auto) },
        new() { Profile = LmStudioPresetProfile.Gemma4, DisplayName = LmStudioSamplingDefaults.GetPresetDisplayName(LmStudioPresetProfile.Gemma4) },
        new() { Profile = LmStudioPresetProfile.Gemma4E4B, DisplayName = LmStudioSamplingDefaults.GetPresetDisplayName(LmStudioPresetProfile.Gemma4E4B) },
        new() { Profile = LmStudioPresetProfile.Qwen35_9B, DisplayName = LmStudioSamplingDefaults.GetPresetDisplayName(LmStudioPresetProfile.Qwen35_9B) },
        new() { Profile = LmStudioPresetProfile.TranslateGemma, DisplayName = LmStudioSamplingDefaults.GetPresetDisplayName(LmStudioPresetProfile.TranslateGemma) },
        new() { Profile = LmStudioPresetProfile.HyMt2_7B, DisplayName = LmStudioSamplingDefaults.GetPresetDisplayName(LmStudioPresetProfile.HyMt2_7B) },
        new() { Profile = LmStudioPresetProfile.HyMt2_30B_A3B, DisplayName = LmStudioSamplingDefaults.GetPresetDisplayName(LmStudioPresetProfile.HyMt2_30B_A3B) },
    ];

    public IReadOnlyList<PromptProfile> PromptProfiles { get; } =
    [
        PromptProfile.Auto,
        PromptProfile.Generic,
        PromptProfile.Gemma4E4B,
        PromptProfile.HyMt2,
    ];

    public BulkObservableCollection<string> AvailableModels { get; } = [];

    public IReadOnlyList<int> BatchSizeOptions { get; } = [1, 2, 5, 10, 15, 20, 30, 50];

    public IReadOnlyList<int> RetryCountOptions { get; } = [0, 1, 2, 3, 5, 10];

    public IReadOnlyList<int> EzTransProcessCountOptions { get; } = [1, 2, 3, 4, 6, 8, 12, 16];

    public ProviderOption? SelectedProviderOption
    {
        get => _selectedProviderOption;
        set
        {
            if (SetProperty(ref _selectedProviderOption, value) && value is not null)
            {
                ApplyProviderDefaults(value.ProviderType);
                RaisePropertyChanged(nameof(ApiKey));
                RaisePropertyChanged(nameof(UsesApiKey));
                RaisePropertyChanged(nameof(UsesPapagoCredentials));
                RaisePropertyChanged(nameof(SupportsModelCatalog));
                RaisePropertyChanged(nameof(CanEditModel));
                RaisePropertyChanged(nameof(CanLoadModels));
                RaisePropertyChanged(nameof(SupportsThinkingToggle));
                RaisePropertyChanged(nameof(SupportsAdvancedSampling));
                RaisePropertyChanged(nameof(ProviderHelpText));
                RaisePropertyChanged(nameof(UsesEzTransXp));
            }
        }
    }

    public string BaseUrl
    {
        get => _baseUrl;
        set => SetProperty(ref _baseUrl, value);
    }

    public PromptProfile SelectedPromptProfile
    {
        get => _selectedPromptProfile;
        set
        {
            var previousProfile = _selectedPromptProfile;
            if (SetProperty(ref _selectedPromptProfile, value))
            {
                ApplyPromptProfileIfEligible(previousProfile, _model);
                RaisePropertyChanged(nameof(ProviderHelpText));
                RaisePropertyChanged(nameof(PromptProfileStatusText));
            }
        }
    }

    public LmStudioPresetOption? SelectedLmStudioPresetOption
    {
        get => _selectedLmStudioPresetOption;
        set
        {
            if (SetProperty(ref _selectedLmStudioPresetOption, value))
            {
                RaisePropertyChanged(nameof(ProviderHelpText));
            }
        }
    }

    public string Model
    {
        get => _model;
        set
        {
            var previousModel = _model;
            if (SetProperty(ref _model, value))
            {
                RaisePropertyChanged(nameof(ProviderHelpText));
                RaisePropertyChanged(nameof(PromptProfileStatusText));
                ApplyLmStudioPresetIfEligible(previousModel, _disableThinking);
                ApplyPromptProfileIfEligible(_selectedPromptProfile, previousModel);
            }
        }
    }

    public string ApiKey
    {
        get => SelectedProviderOption is null
            ? string.Empty
            : _providerApiKeys.GetValueOrDefault(SelectedProviderOption.ProviderType, string.Empty);
        set
        {
            if (SelectedProviderOption is null)
            {
                return;
            }

            var providerType = SelectedProviderOption.ProviderType;
            var currentValue = _providerApiKeys.GetValueOrDefault(providerType, string.Empty);
            if (string.Equals(currentValue, value, StringComparison.Ordinal))
            {
                return;
            }

            _providerApiKeys[providerType] = value;
            RaisePropertyChanged();
        }
    }

    public string SourceLanguage
    {
        get => _sourceLanguage;
        set => SetProperty(ref _sourceLanguage, value);
    }

    public string TargetLanguage
    {
        get => _targetLanguage;
        set => SetProperty(ref _targetLanguage, value);
    }

    public int BatchSize
    {
        get => _batchSize;
        set => SetProperty(ref _batchSize, Math.Clamp(value, 1, 100));
    }

    public int RetryCount
    {
        get => _retryCount;
        set => SetProperty(ref _retryCount, Math.Clamp(value, 0, 10));
    }

    public double Temperature
    {
        get => _temperature;
        set
        {
            if (SetProperty(ref _temperature, Math.Clamp(Math.Round(value, 2), 0, 2)))
            {
                SetProperty(ref _temperatureText, FormatDouble(_temperature), nameof(TemperatureText));
            }
        }
    }

    public string TemperatureText
    {
        get => _temperatureText;
        set
        {
            if (SetProperty(ref _temperatureText, value))
            {
                if (TryParseFlexibleDouble(value, out var parsed))
                {
                    Temperature = parsed;
                }
            }
        }
    }

    public double? TopP
    {
        get => _topP;
        set
        {
            var normalized = value.HasValue
                ? Math.Clamp(Math.Round(value.Value, 2), 0, 1)
                : (double?)null;
            if (SetProperty(ref _topP, normalized))
            {
                SetProperty(ref _topPText, FormatNullableDouble(_topP), nameof(TopPText));
            }
        }
    }

    public string TopPText
    {
        get => _topPText;
        set
        {
            if (SetProperty(ref _topPText, value))
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    TopP = null;
                }
                else if (TryParseFlexibleDouble(value, out var parsed))
                {
                    TopP = parsed;
                }
            }
        }
    }

    public int? TopK
    {
        get => _topK;
        set
        {
            var normalized = value.HasValue
                ? value.Value == 0 ? 1 : Math.Clamp(value.Value, -1, 500)
                : (int?)null;
            if (SetProperty(ref _topK, normalized))
            {
                SetProperty(ref _topKText, FormatNullableInt(_topK), nameof(TopKText));
            }
        }
    }

    public string TopKText
    {
        get => _topKText;
        set
        {
            if (SetProperty(ref _topKText, value))
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    TopK = null;
                }
                else if (TryParseFlexibleInt(value, out var parsed))
                {
                    TopK = parsed;
                }
            }
        }
    }

    public double? RepeatPenalty
    {
        get => _repeatPenalty;
        set
        {
            var normalized = value.HasValue
                ? Math.Clamp(Math.Round(value.Value, 2), 0, 2)
                : (double?)null;
            if (SetProperty(ref _repeatPenalty, normalized))
            {
                SetProperty(ref _repeatPenaltyText, FormatNullableDouble(_repeatPenalty), nameof(RepeatPenaltyText));
            }
        }
    }

    public string RepeatPenaltyText
    {
        get => _repeatPenaltyText;
        set
        {
            if (SetProperty(ref _repeatPenaltyText, value))
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    RepeatPenalty = null;
                }
                else if (TryParseFlexibleDouble(value, out var parsed))
                {
                    RepeatPenalty = parsed;
                }
            }
        }
    }

    public double? PresencePenalty
    {
        get => _presencePenalty;
        set
        {
            var normalized = value.HasValue
                ? Math.Clamp(Math.Round(value.Value, 2), -2, 2)
                : (double?)null;
            if (SetProperty(ref _presencePenalty, normalized))
            {
                SetProperty(ref _presencePenaltyText, FormatNullableDouble(_presencePenalty), nameof(PresencePenaltyText));
            }
        }
    }

    public string PresencePenaltyText
    {
        get => _presencePenaltyText;
        set
        {
            if (SetProperty(ref _presencePenaltyText, value))
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    PresencePenalty = null;
                }
                else if (TryParseFlexibleDouble(value, out var parsed))
                {
                    PresencePenalty = parsed;
                }
            }
        }
    }

    public int? Seed
    {
        get => _seed;
        set
        {
            var normalized = value.HasValue
                ? Math.Max(0, value.Value)
                : (int?)null;
            if (SetProperty(ref _seed, normalized))
            {
                SetProperty(ref _seedText, FormatNullableInt(_seed), nameof(SeedText));
            }
        }
    }

    public string SeedText
    {
        get => _seedText;
        set
        {
            if (SetProperty(ref _seedText, value))
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    Seed = null;
                }
                else if (TryParseFlexibleInt(value, out var parsed))
                {
                    Seed = parsed;
                }
            }
        }
    }

    public int? MaxTokens
    {
        get => _maxTokens;
        set
        {
            var normalized = value.HasValue
                ? Math.Clamp(value.Value, 1, 8192)
                : (int?)null;
            if (SetProperty(ref _maxTokens, normalized))
            {
                SetProperty(ref _maxTokensText, FormatNullableInt(_maxTokens), nameof(MaxTokensText));
            }
        }
    }

    public string MaxTokensText
    {
        get => _maxTokensText;
        set
        {
            if (SetProperty(ref _maxTokensText, value))
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    MaxTokens = null;
                }
                else if (TryParseFlexibleInt(value, out var parsed))
                {
                    MaxTokens = parsed;
                }
            }
        }
    }

    public bool DisableThinking
    {
        get => _disableThinking;
        set
        {
            var previousDisableThinking = _disableThinking;
            if (SetProperty(ref _disableThinking, value))
            {
                RaisePropertyChanged(nameof(ProviderHelpText));
                ApplyLmStudioPresetIfEligible(_model, previousDisableThinking);
            }
        }
    }

    public bool EnableRequestResponseLogging
    {
        get => _enableRequestResponseLogging;
        set => SetProperty(ref _enableRequestResponseLogging, value);
    }

    public bool EnableResultStateLogging
    {
        get => _enableResultStateLogging;
        set => SetProperty(ref _enableResultStateLogging, value);
    }

    public bool EnableDictionaryHitLogging
    {
        get => _enableDictionaryHitLogging;
        set => SetProperty(ref _enableDictionaryHitLogging, value);
    }

    public bool ExcludeNonSourceText
    {
        get => _excludeNonSourceText;
        set => SetProperty(ref _excludeNonSourceText, value);
    }

    public bool EnableBundledDictionaryFirstPass
    {
        get => _enableBundledDictionaryFirstPass;
        set => SetProperty(ref _enableBundledDictionaryFirstPass, value);
    }

    public bool EnableKanaTransliterationFallback
    {
        get => _enableKanaTransliterationFallback;
        set => SetProperty(ref _enableKanaTransliterationFallback, value);
    }

    public bool EnableNaverJapaneseDictionaryLookup
    {
        get => _enableNaverJapaneseDictionaryLookup;
        set => SetProperty(ref _enableNaverJapaneseDictionaryLookup, value);
    }

    public bool EnableKanjiReadingFallback
    {
        get => _enableKanjiReadingFallback;
        set => SetProperty(ref _enableKanjiReadingFallback, value);
    }

    public int DictionaryFirstMaxTermLength
    {
        get => _dictionaryFirstMaxTermLength;
        set => SetProperty(ref _dictionaryFirstMaxTermLength, Math.Clamp(value, 1, 12));
    }

    public string SystemPromptTemplate
    {
        get => _systemPromptTemplate;
        set => SetProperty(ref _systemPromptTemplate, value);
    }

    public string RetryPromptTemplate
    {
        get => _retryPromptTemplate;
        set => SetProperty(ref _retryPromptTemplate, value);
    }

    public string RequestResponseLogPath => new FileRequestResponseLogger().LogFilePath;

    public string ResultStateLogPath => new FileResultStateLogger().LogFilePath;

    public string DictionaryHitLogPath => new FileDictionaryHitLogger().LogFilePath;

    public string BundledDictionarySnapshotText => _bundledJapaneseLexiconService.GetSnapshotSummary();

    public string BundledDictionaryAttributionText => _bundledJapaneseLexiconService.GetAttributionText();

    public string BundledDictionaryNoticePath => _bundledJapaneseLexiconService.NoticeFilePath;

    public string PapagoClientId
    {
        get => _papagoClientId;
        set => SetProperty(ref _papagoClientId, value);
    }

    public string PapagoClientSecret
    {
        get => _papagoClientSecret;
        set => SetProperty(ref _papagoClientSecret, value);
    }

    public string EzTransInstallationPath
    {
        get => _ezTransInstallationPath;
        set
        {
            if (SetProperty(ref _ezTransInstallationPath, value))
            {
                RefreshEzTransInstallationStatus();
            }
        }
    }

    public int EzTransProcessCount
    {
        get => _ezTransProcessCount;
        set => SetProperty(ref _ezTransProcessCount, Math.Clamp(value, 1, 16));
    }

    public string EzTransStatusText
    {
        get => _ezTransStatusText;
        set => SetProperty(ref _ezTransStatusText, value);
    }

    public string EzTransEngineText
    {
        get => _ezTransEngineText;
        set => SetProperty(ref _ezTransEngineText, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool IsLoadingModels
    {
        get => _isLoadingModels;
        set
        {
            if (SetProperty(ref _isLoadingModels, value))
            {
                RaisePropertyChanged(nameof(CanLoadModels));
            }
        }
    }

    public bool SupportsModelCatalog => SelectedProviderOption is not null
        && _modelCatalogService.SupportsModelCatalog(SelectedProviderOption.ProviderType);

    public bool CanEditModel => SelectedProviderOption?.ProviderType is
        TranslationProviderType.OpenAi or
        TranslationProviderType.XiaomiMiMo or
        TranslationProviderType.LmStudio or
        TranslationProviderType.Lemonade;

    public bool CanLoadModels => SupportsModelCatalog && !IsLoadingModels;

    public bool SupportsThinkingToggle => SelectedProviderOption?.ProviderType is
        TranslationProviderType.OpenAi or
        TranslationProviderType.XiaomiMiMo or
        TranslationProviderType.LmStudio or
        TranslationProviderType.Lemonade;

    public bool SupportsAdvancedSampling => SelectedProviderOption?.ProviderType is TranslationProviderType.LmStudio or TranslationProviderType.Lemonade or TranslationProviderType.XiaomiMiMo;

    public bool UsesApiKey => SelectedProviderOption?.ProviderType is
        TranslationProviderType.OpenAi or
        TranslationProviderType.XiaomiMiMo or
        TranslationProviderType.DeepLFree or
        TranslationProviderType.DeepLPro;

    public bool UsesPapagoCredentials => SelectedProviderOption?.ProviderType == TranslationProviderType.Papago;

    public bool UsesEzTransXp => SelectedProviderOption?.ProviderType == TranslationProviderType.EzTransXp;

    public string ProviderHelpText => SelectedProviderOption?.ProviderType switch
    {
        TranslationProviderType.OpenAi => "OpenAI 호환 `/models` 엔드포인트에서 모델 목록을 불러옵니다.",
        TranslationProviderType.XiaomiMiMo => BuildXiaomiMiMoHelpText(),
        TranslationProviderType.LmStudio => BuildLmStudioHelpText(),
        TranslationProviderType.Lemonade => BuildLemonadeHelpText(),
        TranslationProviderType.DeepLFree => "DeepL Free 엔드포인트를 사용합니다. API Key는 Free 계정용 키를 입력하세요.",
        TranslationProviderType.DeepLPro => "DeepL Pro 엔드포인트를 사용합니다. API Key는 유료 계정용 키를 입력하세요.",
        TranslationProviderType.Papago => "Papago는 모델 목록 조회를 지원하지 않습니다.",
        TranslationProviderType.EzTransXp => "EzTransXP는 로컬 설치본을 사용합니다. 설치 경로와 워커 프로세스 수를 EzTransXP 탭에서 확인하세요.",
        _ => string.Empty,
    };

    public string PromptProfileStatusText
    {
        get
        {
            var resolvedProfile = ResolvePromptProfile(Model, SelectedPromptProfile);
            var resolvedLabel = GetPromptProfileDisplayName(resolvedProfile);
            return SelectedPromptProfile == PromptProfile.Auto
                ? $"자동 선택 결과: {resolvedLabel}"
                : $"현재 적용 프롬프트: {resolvedLabel}";
        }
    }

    public void LoadFrom(MainWindowViewModel source)
    {
        SelectedProviderOption = ProviderOptions.FirstOrDefault(option => option.ProviderType == source.SelectedProviderType)
            ?? ProviderOptions.FirstOrDefault(option => option.ProviderType == TranslationProviderType.OpenAi);
        BaseUrl = source.BaseUrl;
        Model = source.Model;
        SelectedLmStudioPresetOption = FindLmStudioPresetOption(source.LmStudioPresetProfile);
        SelectedPromptProfile = source.PromptProfile;
        foreach (var pair in source.ProviderApiKeys)
        {
            _providerApiKeys[pair.Key] = pair.Value;
        }
        SourceLanguage = source.SourceLanguage;
        TargetLanguage = source.TargetLanguage;
        BatchSize = source.BatchSize;
        RetryCount = source.RetryCount;
        Temperature = source.Temperature;
        TopP = source.TopP;
        TopK = source.TopK;
        RepeatPenalty = source.RepeatPenalty;
        PresencePenalty = source.PresencePenalty;
        Seed = source.Seed;
        MaxTokens = source.MaxTokens;
        DisableThinking = source.DisableThinking;
        EnableRequestResponseLogging = source.EnableRequestResponseLogging;
        EnableResultStateLogging = source.EnableResultStateLogging;
        EnableDictionaryHitLogging = source.EnableDictionaryHitLogging;
        ExcludeNonSourceText = source.ExcludeNonSourceText;
        EnableBundledDictionaryFirstPass = source.EnableBundledDictionaryFirstPass;
        EnableKanaTransliterationFallback = source.EnableKanaTransliterationFallback;
        EnableNaverJapaneseDictionaryLookup = source.EnableNaverJapaneseDictionaryLookup;
        EnableKanjiReadingFallback = source.EnableKanjiReadingFallback;
        DictionaryFirstMaxTermLength = source.DictionaryFirstMaxTermLength;
        SystemPromptTemplate = source.SystemPromptTemplate;
        RetryPromptTemplate = source.RetryPromptTemplate;
        PapagoClientId = source.PapagoClientId;
        PapagoClientSecret = source.PapagoClientSecret;
        EzTransInstallationPath = source.EzTransInstallationPath;
        EzTransProcessCount = source.EzTransProcessCount;
        StatusText = "현재 설정을 불러왔습니다.";
        RaisePropertyChanged(nameof(ApiKey));
        RefreshEzTransInstallationStatus();
    }

    public async Task LoadModelsAsync(CancellationToken cancellationToken)
    {
        if (SelectedProviderOption is null)
        {
            StatusText = "번역 공급자를 선택하세요.";
            return;
        }

        if (!SupportsModelCatalog)
        {
            StatusText = "현재 공급자는 모델 목록을 지원하지 않습니다.";
            return;
        }

        IsLoadingModels = true;
        StatusText = "모델 목록을 불러오는 중입니다...";

        try
        {
            var models = await _modelCatalogService.LoadModelsAsync(BuildSettings(), cancellationToken);
            AvailableModels.ReplaceAll(models);
            if (string.IsNullOrWhiteSpace(Model) || !models.Contains(Model, StringComparer.Ordinal))
            {
                Model = models[0];
            }

            StatusText = $"{models.Count}개 모델을 불러왔습니다.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            IsLoadingModels = false;
        }
    }

    public ProviderSettings BuildSettings()
    {
        return new ProviderSettings
        {
            ProviderType = SelectedProviderOption?.ProviderType ?? TranslationProviderType.OpenAi,
            BaseUrl = BaseUrl,
            Model = Model,
            ApiKey = ApiKey,
            PromptProfile = SelectedPromptProfile,
            SourceLanguage = SourceLanguage,
            TargetLanguage = TargetLanguage,
            BatchSize = BatchSize,
            RetryCount = RetryCount,
            Temperature = Temperature,
            TopP = TopP,
            TopK = TopK,
            RepeatPenalty = RepeatPenalty,
            PresencePenalty = PresencePenalty,
            Seed = Seed,
            MaxTokens = MaxTokens,
            DisableThinking = DisableThinking,
            EnableRequestResponseLogging = EnableRequestResponseLogging,
            EnableDictionaryHitLogging = EnableDictionaryHitLogging,
            ExcludeNonSourceText = ExcludeNonSourceText,
            EnableBundledDictionaryFirstPass = EnableBundledDictionaryFirstPass,
            EnableKanaTransliterationFallback = EnableKanaTransliterationFallback,
            EnableNaverJapaneseDictionaryLookup = EnableNaverJapaneseDictionaryLookup,
            EnableKanjiReadingFallback = EnableKanjiReadingFallback,
            DictionaryFirstMaxTermLength = DictionaryFirstMaxTermLength,
            SystemPromptTemplate = SystemPromptTemplate,
            RetryPromptTemplate = RetryPromptTemplate,
            PapagoClientId = PapagoClientId,
            PapagoClientSecret = PapagoClientSecret,
            EzTransInstallationPath = EzTransInstallationPath,
            EzTransProcessCount = EzTransProcessCount,
        };
    }

    public LmStudioPresetProfile SelectedLmStudioPresetProfile => SelectedLmStudioPresetOption?.Profile ?? LmStudioPresetProfile.Auto;

    public bool ValidateBeforeSave()
    {
        if (!TryCommitNumericInputs())
        {
            return false;
        }

        if (!UsesEzTransXp)
        {
            return true;
        }

        var info = _ezTransXpInstallationService.Detect(EzTransInstallationPath);
        if (info.IsAvailable)
        {
            return true;
        }

        StatusText = info.StatusText;
        return false;
    }

    public void RefreshEzTransInstallationStatus()
    {
        var info = _ezTransXpInstallationService.Detect(EzTransInstallationPath);
        EzTransStatusText = info.StatusText;
        EzTransEngineText = info.IsAvailable
            ? $"{Path.GetFileName(info.EnginePath)} / Dat={info.DatPath} / {(info.UsesEnhancedEngine ? "Ehnd 사용" : "기본 엔진 사용")}"
            : "엔진 DLL 또는 Dat 폴더를 확인하세요.";
    }

    public void ResetPromptTemplates()
    {
        ApplyPromptProfileDefaults(ResolvePromptProfile(Model, SelectedPromptProfile));
    }

    public void ResetTranslationOptions()
    {
        SourceLanguage = "ja";
        TargetLanguage = "ko";
        BatchSize = 1;
        RetryCount = 1;
        DisableThinking = true;
        ExcludeNonSourceText = true;
        EnableBundledDictionaryFirstPass = true;
        EnableKanaTransliterationFallback = true;
        EnableNaverJapaneseDictionaryLookup = false;
        EnableKanjiReadingFallback = true;
        DictionaryFirstMaxTermLength = 6;
        Seed = null;

        if (SelectedProviderOption?.ProviderType is TranslationProviderType.LmStudio or TranslationProviderType.Lemonade)
        {
            ApplySelectedLmStudioPreset();
        }
        else if (SelectedProviderOption?.ProviderType == TranslationProviderType.XiaomiMiMo)
        {
            ApplyXiaomiMiMoDefaults();
        }
        else
        {
            Temperature = 0.3;
            TopP = null;
            TopK = null;
            RepeatPenalty = null;
            PresencePenalty = null;
            MaxTokens = null;
        }

        StatusText = "번역 옵션을 기본값으로 되돌렸습니다.";
    }

    private void ApplyProviderDefaults(TranslationProviderType providerType)
    {
        switch (providerType)
        {
            case TranslationProviderType.OpenAi:
                BaseUrl = "https://api.openai.com/v1";
                if (string.IsNullOrWhiteSpace(Model) || Model == "local-model")
                {
                    Model = "gpt-4o-mini";
                }
                ApplyPromptProfileIfEligible(_selectedPromptProfile, Model);
                break;
            case TranslationProviderType.XiaomiMiMo:
                BaseUrl = "https://api.xiaomimimo.com/v1";
                AvailableModels.ReplaceAll(["mimo-v2.5-pro", "mimo-v2.5", "mimo-v2-flash"]);
                if (string.IsNullOrWhiteSpace(Model) || Model == "local-model")
                {
                    Model = "mimo-v2.5-pro";
                }
                ApplyXiaomiMiMoDefaults();
                ApplyPromptProfileIfEligible(_selectedPromptProfile, Model);
                break;
            case TranslationProviderType.LmStudio:
                BaseUrl = "http://127.0.0.1:1234/v1";
                if (string.IsNullOrWhiteSpace(Model))
                {
                    Model = "local-model";
                }
                ApplyLmStudioPresetIfEligible(Model, DisableThinking);
                ApplyPromptProfileIfEligible(_selectedPromptProfile, Model);
                break;
            case TranslationProviderType.Lemonade:
                BaseUrl = "http://127.0.0.1:13305/v1";
                if (string.IsNullOrWhiteSpace(Model))
                {
                    Model = "local-model";
                }
                ApplyLmStudioPresetIfEligible(Model, DisableThinking);
                ApplyPromptProfileIfEligible(_selectedPromptProfile, Model);
                break;
            case TranslationProviderType.DeepLFree:
                BaseUrl = "https://api-free.deepl.com/v2/translate";
                AvailableModels.ReplaceAll([]);
                break;
            case TranslationProviderType.DeepLPro:
                BaseUrl = "https://api.deepl.com/v2/translate";
                AvailableModels.ReplaceAll([]);
                break;
            case TranslationProviderType.Papago:
                BaseUrl = "https://openapi.naver.com/v1/papago/n2mt";
                AvailableModels.ReplaceAll([]);
                break;
            case TranslationProviderType.EzTransXp:
                BaseUrl = string.Empty;
                AvailableModels.ReplaceAll([]);
                break;
        }
    }

    public IReadOnlyDictionary<TranslationProviderType, string> GetProviderApiKeys()
    {
        return new Dictionary<TranslationProviderType, string>(_providerApiKeys);
    }

    private string BuildLmStudioHelpText()
    {
        var family = LmStudioSamplingDefaults.DetectModelFamily(Model);
        var presetLabel = LmStudioSamplingDefaults.GetPresetDisplayName(SelectedLmStudioPresetProfile);
        var promptProfile = ResolvePromptProfile(Model, SelectedPromptProfile);
        var promptProfileLabel = GetPromptProfileDisplayName(promptProfile);
        var familyNote = family switch
        {
            LmStudioModelFamily.Qwen => "Qwen 계열은 thinking 제어와 max_tokens 안전장치를 함께 사용합니다.",
            LmStudioModelFamily.Gemma4E4B => "Gemma 4 E4B는 짧은 용어/라벨 정확도를 높이기 위해 전용 프롬프트 프로필과 더 보수적인 샘플링 권장값을 사용할 수 있습니다.",
            LmStudioModelFamily.TranslateGemma => "TranslateGemma는 전용 LM Studio 형식을 사용하며 glossary 힌트와 사용자 프롬프트 템플릿을 사용하지 않고, 배치 크기는 실질적으로 1로 동작합니다.",
            LmStudioModelFamily.HyMt2 when LmStudioSamplingDefaults.DetectHyMt2PresetProfile(Model) == LmStudioPresetProfile.HyMt2_30B_A3B => "Hy-MT2 30B-A3B는 top_k = -1, max_tokens = 4096 권장값을 사용할 수 있습니다.",
            LmStudioModelFamily.HyMt2 => "Hy-MT2 7B는 기존 LM Studio structured output 경로를 유지하며 HF 권장 inference 값을 사용할 수 있습니다.",
            _ => "LM Studio는 모델별 기본 preset이 다르며, 미지정 모델은 Gemma 기준 preset을 사용합니다.",
        };
        return "LM Studio는 JSON schema 출력 우선, 실패 시 안전한 tokenized fallback으로 재시도합니다. "
            + $"현재 선택 프리셋: {presetLabel}. "
            + $"현재 프롬프트 프로필: {promptProfileLabel}. "
            + familyNote + " "
            + LmStudioSamplingDefaults.BuildPresetSummary(SelectedLmStudioPresetProfile, Model, DisableThinking);
    }

    private string BuildLemonadeHelpText()
    {
        var family = LmStudioSamplingDefaults.DetectModelFamily(Model);
        var presetLabel = LmStudioSamplingDefaults.GetPresetDisplayName(SelectedLmStudioPresetProfile);
        var promptProfile = ResolvePromptProfile(Model, SelectedPromptProfile);
        var promptProfileLabel = GetPromptProfileDisplayName(promptProfile);
        var familyNote = family switch
        {
            LmStudioModelFamily.Gemma4E4B => "Gemma 4 E4B는 짧은 용어/라벨 정확도를 높이기 위한 전용 프롬프트 프로필과 보수적인 샘플링 권장값을 사용할 수 있습니다.",
            LmStudioModelFamily.TranslateGemma => "TranslateGemma는 전용 요청 형식을 사용하므로 glossary 힌트와 사용자 프롬프트 템플릿을 실제 요청에는 적용하지 않습니다.",
            LmStudioModelFamily.HyMt2 when LmStudioSamplingDefaults.DetectHyMt2PresetProfile(Model) == LmStudioPresetProfile.HyMt2_30B_A3B => "Hy-MT2 30B-A3B는 top_k = -1, max_tokens = 4096 권장값을 사용할 수 있습니다.",
            LmStudioModelFamily.HyMt2 => "Hy-MT2 7B는 HF 권장 inference 값과 Hy-MT2 프롬프트 프로필을 함께 사용할 수 있습니다.",
            _ => "Lemonade는 OpenAI 호환 `/v1/models`와 `/v1/chat/completions`를 사용하며, 모델명 기준 로컬 프리셋을 적용합니다.",
        };
        return "Lemonade는 OpenAI 호환 서버입니다. "
            + $"현재 선택 프리셋: {presetLabel}. "
            + $"현재 프롬프트 프로필: {promptProfileLabel}. "
            + familyNote + " "
            + "문서 기준 지원 샘플링은 temperature, top_p, top_k, repeat_penalty, max_tokens 또는 max_completion_tokens입니다. presence_penalty, seed, json_schema response_format, enable_thinking는 공식 chat/completions 문서에 없습니다. "
            + LmStudioSamplingDefaults.BuildPresetSummary(SelectedLmStudioPresetProfile, Model, DisableThinking);
    }

    private string BuildXiaomiMiMoHelpText()
    {
        var modelNote = Model.Contains("mimo-v2-flash", StringComparison.OrdinalIgnoreCase)
            ? "mimo-v2-flash는 기본 Temperature 0.3 / Top P 0.95를 사용합니다."
            : "mimo-v2.5-pro와 mimo-v2.5는 thinking 모드에서 Temperature 1.0이 권장됩니다.";
        return "Xiaomi MiMo는 OpenAI 호환 클라우드 서비스입니다. "
            + "기본 URL은 https://api.xiaomimimo.com/v1 이고, API Key는 Bearer 인증으로 보냅니다. "
            + "라이브 /models 조회는 사용하지 않고, mimo-v2.5-pro / mimo-v2.5 / mimo-v2-flash 추천 목록을 제공합니다. "
            + "thinking.type과 max_completion_tokens를 사용하며, MiMo 권장 시스템 프롬프트는 자동으로 주입하지 않습니다. "
            + modelNote;
    }

    private void ApplyLmStudioPresetIfEligible(string? previousModel, bool previousDisableThinking)
    {
        if (SelectedProviderOption?.ProviderType is not (TranslationProviderType.LmStudio or TranslationProviderType.Lemonade))
        {
            return;
        }

        var previousPreset = LmStudioSamplingDefaults.GetRecommendedPreset(SelectedLmStudioPresetProfile, previousModel, previousDisableThinking);
        var currentPreset = LmStudioSamplingDefaults.GetRecommendedPreset(SelectedLmStudioPresetProfile, Model, DisableThinking);

        if (Math.Abs(Temperature - 0.3) < 0.0001 || Math.Abs(Temperature - previousPreset.Temperature) < 0.0001)
        {
            Temperature = currentPreset.Temperature;
        }

        ApplyPresetValue(previousPreset.TopP, currentPreset.TopP, TopP, value => TopP = value);
        ApplyPresetValue(previousPreset.TopK, currentPreset.TopK, TopK, value => TopK = value);
        ApplyPresetValue(previousPreset.RepeatPenalty, currentPreset.RepeatPenalty, RepeatPenalty, value => RepeatPenalty = value);
        ApplyPresetValue(previousPreset.PresencePenalty, currentPreset.PresencePenalty, PresencePenalty, value => PresencePenalty = value);
        ApplyPresetValue(
            LmStudioSamplingDefaults.GetRecommendedMaxTokens(SelectedLmStudioPresetProfile, previousModel),
            LmStudioSamplingDefaults.GetRecommendedMaxTokens(SelectedLmStudioPresetProfile, Model),
            MaxTokens,
            value => MaxTokens = value);
    }

    public void ApplySelectedLmStudioPreset()
    {
        if (SelectedProviderOption?.ProviderType is not (TranslationProviderType.LmStudio or TranslationProviderType.Lemonade))
        {
            return;
        }

        var preset = LmStudioSamplingDefaults.GetRecommendedPreset(SelectedLmStudioPresetProfile, Model, DisableThinking);
        Temperature = preset.Temperature;
        TopP = preset.TopP;
        TopK = preset.TopK;
        RepeatPenalty = preset.RepeatPenalty;
        PresencePenalty = preset.PresencePenalty;
        MaxTokens = LmStudioSamplingDefaults.GetRecommendedMaxTokens(SelectedLmStudioPresetProfile, Model);
        StatusText = $"{LmStudioSamplingDefaults.GetPresetDisplayName(SelectedLmStudioPresetProfile)} 프리셋을 적용했습니다.";
    }

    private LmStudioPresetOption FindLmStudioPresetOption(LmStudioPresetProfile profile)
    {
        return LmStudioPresetOptions.First(option => option.Profile == profile);
    }

    private static void ApplyPresetValue<T>(T? previousPresetValue, T? currentPresetValue, T? currentValue, Action<T?> assign)
        where T : struct
    {
        if (!currentValue.HasValue || EqualityComparer<T?>.Default.Equals(currentValue, previousPresetValue))
        {
            assign(currentPresetValue);
        }
    }

    private bool TryCommitNumericInputs()
    {
        if (!TryParseFlexibleDouble(TemperatureText, out var temperature))
        {
            StatusText = "Temperature 값이 올바르지 않습니다.";
            return false;
        }

        Temperature = temperature;

        if (!TryCommitNullableDouble(TopPText, value => TopP = value, "Top P"))
        {
            return false;
        }

        if (!TryCommitNullableInt(TopKText, value => TopK = value, "Top K"))
        {
            return false;
        }

        if (!TryCommitNullableDouble(RepeatPenaltyText, value => RepeatPenalty = value, "Repeat Penalty"))
        {
            return false;
        }

        if (!TryCommitNullableDouble(PresencePenaltyText, value => PresencePenalty = value, "Presence Penalty"))
        {
            return false;
        }

        if (!TryCommitNullableInt(SeedText, value => Seed = value, "Seed"))
        {
            return false;
        }

        if (!TryCommitNullableInt(MaxTokensText, value => MaxTokens = value, "Max Tokens"))
        {
            return false;
        }

        return true;
    }

    private bool TryCommitNullableDouble(string text, Action<double?> apply, string label)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            apply(null);
            return true;
        }

        if (!TryParseFlexibleDouble(text, out var parsed))
        {
            StatusText = $"{label} 값이 올바르지 않습니다.";
            return false;
        }

        apply(parsed);
        return true;
    }

    private bool TryCommitNullableInt(string text, Action<int?> apply, string label)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            apply(null);
            return true;
        }

        if (!TryParseFlexibleInt(text, out var parsed))
        {
            StatusText = $"{label} 값이 올바르지 않습니다.";
            return false;
        }

        apply(parsed);
        return true;
    }

    private static bool TryParseFlexibleDouble(string text, out double value)
    {
        var normalized = text.Trim();
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            || double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseFlexibleInt(string text, out int value)
    {
        var normalized = text.Trim();
        if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.CurrentCulture, out value)
            || int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        if (TryParseFlexibleDouble(normalized, out var doubleValue))
        {
            var rounded = Math.Round(doubleValue, 6);
            if (Math.Abs(rounded % 1) < 0.000001)
            {
                value = (int)Math.Round(rounded, MidpointRounding.AwayFromZero);
                return value is >= -1 and <= 500 && value != 0;
            }
        }

        value = default;
        return false;
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private void ApplyPromptProfileIfEligible(PromptProfile previousProfile, string? previousModel)
    {
        if (SelectedProviderOption?.ProviderType is not (TranslationProviderType.OpenAi or TranslationProviderType.XiaomiMiMo or TranslationProviderType.LmStudio or TranslationProviderType.Lemonade))
        {
            return;
        }

        var previousResolved = ResolvePromptProfile(previousModel, previousProfile);
        var currentResolved = ResolvePromptProfile(Model, SelectedPromptProfile);
        var previousSystem = TranslationPromptTemplates.GetDefaultTemplate(previousResolved, isRetryPrompt: false);
        var previousRetry = TranslationPromptTemplates.GetDefaultTemplate(previousResolved, isRetryPrompt: true);
        var currentSystem = TranslationPromptTemplates.GetDefaultTemplate(currentResolved, isRetryPrompt: false);
        var currentRetry = TranslationPromptTemplates.GetDefaultTemplate(currentResolved, isRetryPrompt: true);

        if (ShouldReplacePromptTemplate(SystemPromptTemplate, previousSystem, _lastAppliedSystemPromptTemplate))
        {
            SystemPromptTemplate = currentSystem;
        }

        if (ShouldReplacePromptTemplate(RetryPromptTemplate, previousRetry, _lastAppliedRetryPromptTemplate))
        {
            RetryPromptTemplate = currentRetry;
        }

        _lastAppliedSystemPromptTemplate = currentSystem;
        _lastAppliedRetryPromptTemplate = currentRetry;
    }

    private void ApplyPromptProfileDefaults(PromptProfile resolvedProfile)
    {
        SystemPromptTemplate = TranslationPromptTemplates.GetDefaultTemplate(resolvedProfile, isRetryPrompt: false);
        RetryPromptTemplate = TranslationPromptTemplates.GetDefaultTemplate(resolvedProfile, isRetryPrompt: true);
        _lastAppliedSystemPromptTemplate = SystemPromptTemplate;
        _lastAppliedRetryPromptTemplate = RetryPromptTemplate;
    }

    private PromptProfile ResolvePromptProfile(string? model, PromptProfile selectedProfile)
    {
        if (selectedProfile != PromptProfile.Auto)
        {
            return selectedProfile;
        }

        if (model?.Contains("gemma-4-e4b", StringComparison.OrdinalIgnoreCase) == true)
        {
            return PromptProfile.Gemma4E4B;
        }

        return model?.Contains("hy-mt2", StringComparison.OrdinalIgnoreCase) == true
            ? PromptProfile.HyMt2
            : PromptProfile.Generic;
    }

    private static bool ShouldReplacePromptTemplate(string currentTemplate, string previousDefault, string lastAppliedDefault)
    {
        return string.IsNullOrWhiteSpace(currentTemplate)
            || string.Equals(currentTemplate, previousDefault, StringComparison.Ordinal)
            || string.Equals(currentTemplate, lastAppliedDefault, StringComparison.Ordinal);
    }

    private static string GetPromptProfileDisplayName(PromptProfile profile)
    {
        return profile switch
        {
            PromptProfile.Auto => "자동",
            PromptProfile.Generic => "기본",
            PromptProfile.Gemma4E4B => "Gemma 4 E4B",
            PromptProfile.HyMt2 => "Hy-MT2",
            _ => profile.ToString(),
        };
    }

    private void ApplyXiaomiMiMoDefaults()
    {
        if (Model.Contains("mimo-v2-flash", StringComparison.OrdinalIgnoreCase))
        {
            Temperature = 0.3;
            TopP = 0.95;
        }
        else
        {
            Temperature = 1.0;
            TopP = 0.95;
        }

        TopK = null;
        RepeatPenalty = null;
        PresencePenalty = null;
        MaxTokens = null;
    }

    private static string FormatNullableDouble(double? value)
    {
        return value?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string FormatNullableInt(int? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
