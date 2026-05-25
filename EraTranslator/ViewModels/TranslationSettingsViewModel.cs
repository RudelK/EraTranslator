using System.Collections.ObjectModel;
using EraTranslator.Services;

namespace EraTranslator.ViewModels;

public sealed class TranslationSettingsViewModel : BindableBase
{
    private readonly ModelCatalogService _modelCatalogService = new();
    private readonly Dictionary<TranslationProviderType, string> _providerApiKeys = [];
    private ProviderOption? _selectedProviderOption;
    private string _baseUrl = string.Empty;
    private string _model = string.Empty;
    private string _sourceLanguage = "ja";
    private string _targetLanguage = "ko";
    private int _batchSize = 1;
    private int _retryCount = 1;
    private double _temperature = 0.3;
    private bool _disableThinking = true;
    private bool _enableRequestResponseLogging;
    private bool _excludeNonSourceText;
    private string _systemPromptTemplate = TranslationPromptTemplates.DefaultSystemPrompt;
    private string _retryPromptTemplate = TranslationPromptTemplates.DefaultRetryPrompt;
    private string _papagoClientId = string.Empty;
    private string _papagoClientSecret = string.Empty;
    private string _statusText = "공급자와 연결 정보를 확인하세요.";
    private bool _isLoadingModels;

    public TranslationSettingsViewModel(IEnumerable<ProviderOption> providerOptions)
    {
        ProviderOptions = new ObservableCollection<ProviderOption>(providerOptions.Select(option => new ProviderOption
        {
            ProviderType = option.ProviderType,
            DisplayName = option.DisplayName,
            IsAvailable = option.IsAvailable,
        }));
    }

    public ObservableCollection<ProviderOption> ProviderOptions { get; }

    public BulkObservableCollection<string> AvailableModels { get; } = [];

    public IReadOnlyList<int> BatchSizeOptions { get; } = [1, 2, 5, 10, 15, 20, 30, 50];

    public IReadOnlyList<int> RetryCountOptions { get; } = [0, 1, 2, 3, 5, 10];

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
                RaisePropertyChanged(nameof(ProviderHelpText));
            }
        }
    }

    public string BaseUrl
    {
        get => _baseUrl;
        set => SetProperty(ref _baseUrl, value);
    }

    public string Model
    {
        get => _model;
        set => SetProperty(ref _model, value);
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
        set => SetProperty(ref _temperature, Math.Clamp(value, 0, 2));
    }

    public bool DisableThinking
    {
        get => _disableThinking;
        set => SetProperty(ref _disableThinking, value);
    }

    public bool EnableRequestResponseLogging
    {
        get => _enableRequestResponseLogging;
        set => SetProperty(ref _enableRequestResponseLogging, value);
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

    public bool UsesApiKey => SelectedProviderOption?.ProviderType is
        TranslationProviderType.OpenAi or
        TranslationProviderType.DeepLFree or
        TranslationProviderType.DeepLPro;

    public bool UsesPapagoCredentials => SelectedProviderOption?.ProviderType == TranslationProviderType.Papago;

    public string ProviderHelpText => SelectedProviderOption?.ProviderType switch
    {
        TranslationProviderType.OpenAi => "OpenAI 호환 `/models` 엔드포인트에서 모델 목록을 불러옵니다.",
        TranslationProviderType.LmStudio => "LM Studio 로컬 서버의 `/models` 엔드포인트에서 모델 목록을 불러옵니다.",
        TranslationProviderType.DeepLFree => "DeepL Free 엔드포인트를 사용합니다. API Key는 Free 계정용 키를 입력하세요.",
        TranslationProviderType.DeepLPro => "DeepL Pro 엔드포인트를 사용합니다. API Key는 유료 계정용 키를 입력하세요.",
        TranslationProviderType.Papago => "Papago는 모델 목록 조회를 지원하지 않습니다.",
        TranslationProviderType.EzTransXp => "EzTransXP는 후속 phase에서 연동 예정입니다.",
        _ => string.Empty,
    };

    public void LoadFrom(MainWindowViewModel source)
    {
        SelectedProviderOption = ProviderOptions.FirstOrDefault(option => option.ProviderType == source.SelectedProviderType);
        BaseUrl = source.BaseUrl;
        Model = source.Model;
        foreach (var pair in source.ProviderApiKeys)
        {
            _providerApiKeys[pair.Key] = pair.Value;
        }
        SourceLanguage = source.SourceLanguage;
        TargetLanguage = source.TargetLanguage;
        BatchSize = source.BatchSize;
        RetryCount = source.RetryCount;
        Temperature = source.Temperature;
        DisableThinking = source.DisableThinking;
        EnableRequestResponseLogging = source.EnableRequestResponseLogging;
        ExcludeNonSourceText = source.ExcludeNonSourceText;
        SystemPromptTemplate = source.SystemPromptTemplate;
        RetryPromptTemplate = source.RetryPromptTemplate;
        PapagoClientId = source.PapagoClientId;
        PapagoClientSecret = source.PapagoClientSecret;
        StatusText = "현재 설정을 불러왔습니다.";
        RaisePropertyChanged(nameof(ApiKey));
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
            DisableThinking = DisableThinking,
            EnableRequestResponseLogging = EnableRequestResponseLogging,
            ExcludeNonSourceText = ExcludeNonSourceText,
            SystemPromptTemplate = SystemPromptTemplate,
            RetryPromptTemplate = RetryPromptTemplate,
            PapagoClientId = PapagoClientId,
            PapagoClientSecret = PapagoClientSecret,
        };
    }

    public void ResetPromptTemplates()
    {
        SystemPromptTemplate = TranslationPromptTemplates.DefaultSystemPrompt;
        RetryPromptTemplate = TranslationPromptTemplates.DefaultRetryPrompt;
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
}
