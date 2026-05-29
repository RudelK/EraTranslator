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
    private bool _excludeNonSourceText = true;
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
        new() { Profile = LmStudioPresetProfile.Qwen35_9B, DisplayName = LmStudioSamplingDefaults.GetPresetDisplayName(LmStudioPresetProfile.Qwen35_9B) },
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
                ApplyLmStudioPresetIfEligible(previousModel, _disableThinking);
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
                ? Math.Clamp(value.Value, 1, 500)
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

    public bool ExcludeNonSourceText
    {
        get => _excludeNonSourceText;
        set => SetProperty(ref _excludeNonSourceText, value);
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

    public bool CanLoadModels => SupportsModelCatalog && !IsLoadingModels;

    public bool SupportsThinkingToggle => SelectedProviderOption?.ProviderType is
        TranslationProviderType.OpenAi or
        TranslationProviderType.LmStudio;

    public bool SupportsAdvancedSampling => SelectedProviderOption?.ProviderType == TranslationProviderType.LmStudio;

    public bool UsesApiKey => SelectedProviderOption?.ProviderType is
        TranslationProviderType.OpenAi or
        TranslationProviderType.DeepLFree or
        TranslationProviderType.DeepLPro;

    public bool UsesPapagoCredentials => SelectedProviderOption?.ProviderType == TranslationProviderType.Papago;

    public bool UsesEzTransXp => SelectedProviderOption?.ProviderType == TranslationProviderType.EzTransXp;

    public string ProviderHelpText => SelectedProviderOption?.ProviderType switch
    {
        TranslationProviderType.OpenAi => "OpenAI 호환 `/models` 엔드포인트에서 모델 목록을 불러옵니다.",
        TranslationProviderType.LmStudio => BuildLmStudioHelpText(),
        TranslationProviderType.DeepLFree => "DeepL Free 엔드포인트를 사용합니다. API Key는 Free 계정용 키를 입력하세요.",
        TranslationProviderType.DeepLPro => "DeepL Pro 엔드포인트를 사용합니다. API Key는 유료 계정용 키를 입력하세요.",
        TranslationProviderType.Papago => "Papago는 모델 목록 조회를 지원하지 않습니다.",
        TranslationProviderType.EzTransXp => "EzTransXP는 로컬 설치본을 사용합니다. 설치 경로와 워커 프로세스 수를 EzTransXP 탭에서 확인하세요.",
        _ => string.Empty,
    };

    public void LoadFrom(MainWindowViewModel source)
    {
        SelectedProviderOption = ProviderOptions.FirstOrDefault(option => option.ProviderType == source.SelectedProviderType);
        BaseUrl = source.BaseUrl;
        Model = source.Model;
        SelectedLmStudioPresetOption = FindLmStudioPresetOption(source.LmStudioPresetProfile);
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
        ExcludeNonSourceText = source.ExcludeNonSourceText;
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
            ExcludeNonSourceText = ExcludeNonSourceText,
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
        SystemPromptTemplate = TranslationPromptTemplates.DefaultSystemPrompt;
        RetryPromptTemplate = TranslationPromptTemplates.DefaultRetryPrompt;
    }

    public void ResetTranslationOptions()
    {
        SourceLanguage = "ja";
        TargetLanguage = "ko";
        BatchSize = 1;
        RetryCount = 1;
        DisableThinking = true;
        ExcludeNonSourceText = true;
        Seed = null;

        if (SelectedProviderOption?.ProviderType == TranslationProviderType.LmStudio)
        {
            ApplySelectedLmStudioPreset();
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
                break;
            case TranslationProviderType.LmStudio:
                BaseUrl = "http://127.0.0.1:1234/v1";
                if (string.IsNullOrWhiteSpace(Model))
                {
                    Model = "local-model";
                }
                ApplyLmStudioPresetIfEligible(Model, DisableThinking);
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
        var familyNote = family == LmStudioModelFamily.Qwen
            ? "Qwen 계열은 thinking 제어와 max_tokens 안전장치를 함께 사용합니다."
            : "LM Studio는 모델별 기본 preset이 다르며, 미지정 모델은 Gemma 기준 preset을 사용합니다.";
        return "LM Studio는 JSON schema 출력 우선, 실패 시 안전한 tokenized fallback으로 재시도합니다. "
            + $"현재 선택 프리셋: {presetLabel}. "
            + familyNote + " "
            + LmStudioSamplingDefaults.BuildPresetSummary(SelectedLmStudioPresetProfile, Model, DisableThinking);
    }

    private void ApplyLmStudioPresetIfEligible(string? previousModel, bool previousDisableThinking)
    {
        if (SelectedProviderOption?.ProviderType != TranslationProviderType.LmStudio)
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
    }

    public void ApplySelectedLmStudioPreset()
    {
        if (SelectedProviderOption?.ProviderType != TranslationProviderType.LmStudio)
        {
            return;
        }

        var preset = LmStudioSamplingDefaults.GetRecommendedPreset(SelectedLmStudioPresetProfile, Model, DisableThinking);
        Temperature = preset.Temperature;
        TopP = preset.TopP;
        TopK = preset.TopK;
        RepeatPenalty = preset.RepeatPenalty;
        PresencePenalty = preset.PresencePenalty;
        MaxTokens = null;
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
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
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
