using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Data;
using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.ViewModels;

public sealed class MainWindowViewModel : BindableBase, IDisposable
{
    private readonly FileScanner _fileScanner;
    private readonly TranslationCoordinator _translationCoordinator;
    private readonly OutputWriter _outputWriter;
    private readonly JosaPatternAnalyzer _josaPatternAnalyzer = new();
    private readonly DebouncedAppConfigCoordinator _appConfigCoordinator;
    private readonly UserDictionaryService _userDictionaryService;
    private readonly ProjectStatePersistenceService _projectStatePersistenceService;
    private readonly TranslationProgressCarryoverService _translationProgressCarryoverService;
    private readonly TranslationTextExchangeService _translationTextExchangeService;
    private readonly SourceLanguageFilterService _sourceLanguageFilterService;
    private readonly ProjectContextFactory _projectContextFactory;
    private readonly TeamSourceSyncService _teamSourceSyncService;
    private readonly TeamCollaborationService _teamCollaborationService;
    private readonly TeamProjectStateService _teamProjectStateService;
    private readonly TeamScanManifestBuilder _teamScanManifestBuilder;
    private readonly PhaseScopedGlossaryBuilder _phaseScopedGlossaryBuilder = new();
    private readonly EzTransXpInstallationService _ezTransXpInstallationService;
    private readonly FileResultStateLogger _resultStateLogger = new();
    private readonly Dictionary<TranslationProviderType, string> _providerApiKeys = [];
    private ScanSession? _session;
    private CancellationTokenSource? _cancellationTokenSource;
    private List<UserDictionaryEntry> _globalUserDictionary = [];
    private List<UserDictionaryEntry> _projectUserDictionary = [];
    private bool _isLoadingConfig;
    private string _gameDirectory = string.Empty;
    private string _outputDirectory = string.Empty;
    private string _localGameDirectory = string.Empty;
    private string _localOutputDirectory = string.Empty;
    private ProjectMode _projectMode = ProjectMode.Local;
    private string _teamServerUrl = string.Empty;
    private string _teamProjectId = string.Empty;
    private string _teamDisplayName = string.Empty;
    private string _clientId = string.Empty;
    private string _teamWorkspaceRoot = string.Empty;
    private string _teamAuthToken = string.Empty;
    private string _teamStatusSummary = "로컬 작업 모드";
    private TeamProjectSummary? _selectedTeamProject;
    private bool _isManualTeamProjectId = true;
    private string _statusText = "게임 디렉토리를 지정한 뒤 텍스트를 추출하세요.";
    private string _summaryText = "아직 스캔 전입니다.";
    private string _currentOperationDetail = "대기 중";
    private double _progressValue;
    private ExtractedTextItem? _selectedItem;
    private string _selectedItemTranslatedTextEditor = string.Empty;
    private bool _selectedItemTranslatedTextEditorDirty;
    private bool _syncingSelectedItemTranslatedTextEditor;
    private bool _warningsOnly;
    private bool _refreshGridDuringTranslatedTextEdit;
    private bool _isBusy;
    private string _filterText = string.Empty;
    private bool _useRegexFilter;
    private string _selectedSearchFieldFilter = "전체";
    private string _selectedFileTypeFilter = "전체";
    private string _selectedStatusFilter = "전체";
    private SaveMode _selectedSaveMode = SaveMode.ExportCopy;
    private ProviderOption? _selectedProviderOption;
    private string _baseUrl = "https://api.openai.com/v1";
    private string _model = "gpt-4o-mini";
    private LmStudioPresetProfile _lmStudioPresetProfile = LmStudioPresetProfile.Auto;
    private PromptProfile _promptProfile = PromptProfile.Auto;
    private string _sourceLanguage = "ja";
    private string _targetLanguage = "ko";
    private int _batchSize = 1;
    private int _retryCount = 1;
    private double _temperature = 0.3;
    private double? _topP;
    private int? _topK;
    private double? _repeatPenalty;
    private double? _presencePenalty;
    private int? _seed;
    private int? _maxTokens;
    private bool _disableThinking = true;
    private bool _enableRequestResponseLogging;
    private bool _enableResultStateLogging = false;
    private bool _enableDictionaryHitLogging = false;
    private bool _excludeNonSourceText = true;
    private bool _enableBundledDictionaryFirstPass = true;
    private bool _enableKanaTransliterationFallback = false;
    private bool _enableNaverJapaneseDictionaryLookup = false;
    private bool _enableKanjiReadingFallback = false;
    private int _dictionaryFirstMaxTermLength = 6;
    private string _systemPromptTemplate = TranslationPromptTemplates.DefaultSystemPrompt;
    private string _retryPromptTemplate = TranslationPromptTemplates.DefaultRetryPrompt;
    private string _protectedFullWidthCharacters = PlaceholderProtector.DefaultFullWidthSpecialCharacters;
    private string _papagoClientId = string.Empty;
    private string _papagoClientSecret = string.Empty;
    private string _ezTransInstallationPath = string.Empty;
    private int _ezTransProcessCount = 1;
    private bool _suppressItemStatePersistence;
    private DateTimeOffset _lastProgressSaveAtUtc = DateTimeOffset.MinValue;
    private string _activeProjectDataDirectory = string.Empty;
    private readonly Stopwatch _translationProgressStopwatch = new();
    private bool _resumeTranslationTimingOnNextRun;
    private bool _isSavingResults;
    private HashSet<string>? _visibleItemSnapshot;
    private bool _buildingVisibleItemSnapshot;
    private bool _itemsViewRefreshQueued;
    private bool _handlingManualStatusOverride;
    private bool _startupProjectContextRestored;
    private bool _isStartupLoading;
    private static readonly SaveMode EffectiveSaveMode = SaveMode.ExportCopy;

    public MainWindowViewModel(
        FileScanner? fileScanner = null,
        TranslationCoordinator? translationCoordinator = null,
        OutputWriter? outputWriter = null,
        AppConfigService? appConfigService = null,
        DebouncedAppConfigCoordinator? appConfigCoordinator = null,
        UserDictionaryService? userDictionaryService = null,
        ScanSessionStateService? scanSessionStateService = null,
        TranslationProgressCarryoverService? translationProgressCarryoverService = null,
        TranslationTextExchangeService? translationTextExchangeService = null,
        SourceLanguageFilterService? sourceLanguageFilterService = null,
        ProjectContextFactory? projectContextFactory = null,
        TeamSourceSyncService? teamSourceSyncService = null,
        TeamCollaborationService? teamCollaborationService = null,
        TeamProjectStateService? teamProjectStateService = null,
        TeamScanManifestBuilder? teamScanManifestBuilder = null,
        TranslationProgressStateService? translationProgressStateService = null,
        EzTransXpInstallationService? ezTransXpInstallationService = null,
        SqliteProjectStateStore? sqliteProjectStateStore = null,
        bool detectSampleDirectory = false,
        bool restoreLastSessionOnStartup = true)
    {
        _fileScanner = fileScanner ?? new FileScanner();
        _translationCoordinator = translationCoordinator ?? new TranslationCoordinator();
        _outputWriter = outputWriter ?? new OutputWriter();
        var resolvedAppConfigService = appConfigService ?? new AppConfigService();
        _appConfigCoordinator = appConfigCoordinator ?? new DebouncedAppConfigCoordinator(resolvedAppConfigService);
        _userDictionaryService = userDictionaryService ?? new UserDictionaryService();
        _projectStatePersistenceService = new ProjectStatePersistenceService(
            scanSessionStateService ?? new ScanSessionStateService(),
            translationProgressStateService ?? new TranslationProgressStateService(),
            sqliteProjectStateStore);
        _translationProgressCarryoverService = translationProgressCarryoverService ?? new TranslationProgressCarryoverService();
        _translationTextExchangeService = translationTextExchangeService ?? new TranslationTextExchangeService();
        _sourceLanguageFilterService = sourceLanguageFilterService ?? new SourceLanguageFilterService();
        _projectContextFactory = projectContextFactory ?? new ProjectContextFactory();
        _teamSourceSyncService = teamSourceSyncService ?? new TeamSourceSyncService();
        _teamCollaborationService = teamCollaborationService ?? new TeamCollaborationService();
        _teamProjectStateService = teamProjectStateService ?? new TeamProjectStateService();
        _teamScanManifestBuilder = teamScanManifestBuilder ?? new TeamScanManifestBuilder();
        _ezTransXpInstallationService = ezTransXpInstallationService ?? new EzTransXpInstallationService();
        ProviderOptions = new ObservableCollection<ProviderOption>(BuildProviderOptions());
        _selectedProviderOption = ProviderOptions.FirstOrDefault(option => option.ProviderType == TranslationProviderType.OpenAi);
        SearchFieldFilters = ["전체", "파일", "원문", "번역문", "참조 상태", "함수/표현식"];
        FileTypeFilters = ["전체", "ERB", "ERH", "CSV", "ERD"];
        StatusFilters = ["전체", "번역 대기", "제외됨", "중지됨", "수동 수정", "번역 완료", "검수 필요", "번역 실패"];
        SaveModeOptions = [SaveMode.ExportCopy, SaveMode.InPlaceWithBackup];
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = FilterItem;
        if (ItemsView is ICollectionViewLiveShaping liveShaping)
        {
            if (liveShaping.CanChangeLiveFiltering)
            {
                liveShaping.IsLiveFiltering = false;
            }

            if (liveShaping.CanChangeLiveSorting)
            {
                liveShaping.IsLiveSorting = false;
            }

            if (liveShaping.CanChangeLiveGrouping)
            {
                liveShaping.IsLiveGrouping = false;
            }
        }
        if (ItemsView is ListCollectionView listCollectionView)
        {
            listCollectionView.CustomSort = ExtractedTextItemPriorityComparer.Instance;
        }
        _globalUserDictionary = _userDictionaryService.LoadGlobal();

        var sampleDirectory = detectSampleDirectory ? TryFindSampleDirectory() : null;
        if (sampleDirectory is not null)
        {
            _gameDirectory = sampleDirectory;
            _outputDirectory = Path.Combine(Path.GetDirectoryName(sampleDirectory) ?? sampleDirectory, "translated-output");
        }

        RefreshProjectContext(restoreSession: false, clearSessionWhenMissing: false);
        LoadConfig();
        if (restoreLastSessionOnStartup)
        {
            _startupProjectContextRestored = true;
            RefreshProjectContext(restoreSession: true, clearSessionWhenMissing: false);
        }
    }

    public void FlushPendingConfigSave()
    {
        _appConfigCoordinator.FlushPendingSave();
    }

    public bool HasStartupProjectContextCandidate()
    {
        var projectDataDirectory = GetProjectDataDirectory();
        return _projectStatePersistenceService.HasPersistedState(projectDataDirectory);
    }

    public async Task RestoreStartupProjectContextIfAvailableAsync()
    {
        if (_startupProjectContextRestored)
        {
            return;
        }

        _startupProjectContextRestored = true;
        var projectDataDirectory = GetProjectDataDirectory();
        _activeProjectDataDirectory = projectDataDirectory;
        ReloadProjectDictionary(projectDataDirectory);
        RaisePropertyChanged(nameof(UserDictionarySummary));

        if (string.IsNullOrWhiteSpace(projectDataDirectory) || !Directory.Exists(projectDataDirectory))
        {
            return;
        }

        var previousStatusText = StatusText;
        var previousOperationDetail = CurrentOperationDetail;
        var previousProgressValue = ProgressValue;

        IsStartupLoading = true;
        StatusText = "DB를 불러오는 중입니다.";
        CurrentOperationDetail = "저장된 추출 상태를 확인하는 중입니다.";
        ProgressValue = 0;

        try
        {
            var session = System.Windows.Application.Current is null
                ? _projectStatePersistenceService.LoadScanSession(projectDataDirectory)
                : await Task.Run(() => _projectStatePersistenceService.LoadScanSession(projectDataDirectory));
            if (session is null)
            {
                StatusText = previousStatusText;
                CurrentOperationDetail = previousOperationDetail;
                ProgressValue = previousProgressValue;
                return;
            }

            CurrentOperationDetail = "저장된 진행 상태를 적용하는 중입니다.";
            ApplySession(session, restoreProgress: true);
            RefreshSummaryText();
            StatusText = "마지막 추출 상태를 불러왔습니다.";
            CurrentOperationDetail = $"복원 완료: {session.Documents.Count}개 문서";
            ProgressValue = 1.0;
        }
        catch (Exception ex)
        {
            StatusText = $"DB 로드 실패: {ex.Message}";
            CurrentOperationDetail = "저장된 추출 상태를 불러오지 못했습니다.";
            ProgressValue = 0;
        }
        finally
        {
            IsStartupLoading = false;
        }
    }

    public void Dispose()
    {
        StopCurrentOperation();
        FlushPendingConfigSave();
        _translationCoordinator.Dispose();
    }

    public BulkObservableCollection<ExtractedTextItem> Items { get; } = [];

    public ICollectionView ItemsView { get; }

    public ObservableCollection<ProviderOption> ProviderOptions { get; }

    public ObservableCollection<TeamProjectSummary> TeamProjects { get; } = [];

    public IReadOnlyList<string> FileTypeFilters { get; }

    public IReadOnlyList<string> SearchFieldFilters { get; }

    public IReadOnlyList<string> StatusFilters { get; }

    public IReadOnlyList<string> EditableStatusOptions { get; } = ["번역 대기", "제외됨", "중지됨", "수동 수정", "번역 완료", "검수 필요", "번역 실패"];

    public IReadOnlyList<SaveMode> SaveModeOptions { get; }

    public TranslationProviderType SelectedProviderType => SelectedProviderOption?.ProviderType ?? TranslationProviderType.OpenAi;

    public string GameDirectory
    {
        get => _gameDirectory;
        set
        {
            if (SetProperty(ref _gameDirectory, value))
            {
                if (!IsTeamMode)
                {
                    _localGameDirectory = value;
                }

                EnsureOutputDirectoryDistinctFromGameDirectory();
                OnProjectPathInputsChanged();
            }
        }
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set => TryApplyOutputDirectory(value, out _);
    }

    public bool IsTeamMode
    {
        get => _projectMode == ProjectMode.Team;
        set
        {
            var nextMode = value ? ProjectMode.Team : ProjectMode.Local;
            if (_projectMode == nextMode)
            {
                return;
            }

            _projectMode = nextMode;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(IsLocalMode));
            RaisePropertyChanged(nameof(CanBrowseDirectories));
            RaisePropertyChanged(nameof(CanUseTeamActions));
            RaisePropertyChanged(nameof(CanEditTeamProjectId));
            TeamStatusSummary = IsTeamMode ? "팀 작업 모드: 서버와 프로젝트를 설정하세요." : "로컬 작업 모드";
            if (IsTeamMode)
            {
                ApplyTeamWorkspacePaths(restoreSession: true);
            }
            else
            {
                SetProperty(ref _gameDirectory, _localGameDirectory, nameof(GameDirectory));
                SetProperty(ref _outputDirectory, _localOutputDirectory, nameof(OutputDirectory));
                OnProjectPathInputsChanged();
            }

            PersistConfig();
        }
    }

    public bool IsLocalMode
    {
        get => !IsTeamMode;
        set
        {
            if (value)
            {
                IsTeamMode = false;
            }
        }
    }

    public string TeamServerUrl
    {
        get => _teamServerUrl;
        set
        {
            if (SetProperty(ref _teamServerUrl, value?.Trim() ?? string.Empty))
            {
                PersistConfig();
                RaisePropertyChanged(nameof(TeamStatusSummary));
            }
        }
    }

    public string TeamProjectId
    {
        get => _teamProjectId;
        set
        {
            if (SetProperty(ref _teamProjectId, value?.Trim() ?? string.Empty))
            {
                ApplyTeamWorkspacePaths(restoreSession: true);
                PersistConfig();
            }
        }
    }

    public bool IsManualTeamProjectId
    {
        get => _isManualTeamProjectId;
        set
        {
            if (SetProperty(ref _isManualTeamProjectId, value))
            {
                RaisePropertyChanged(nameof(CanEditTeamProjectId));
            }
        }
    }

    public bool CanEditTeamProjectId => IsTeamMode && IsManualTeamProjectId;

    public TeamProjectSummary? SelectedTeamProject
    {
        get => _selectedTeamProject;
        set
        {
            if (SetProperty(ref _selectedTeamProject, value) && value is not null)
            {
                IsManualTeamProjectId = false;
                TeamProjectId = value.Id;
            }
        }
    }

    public string TeamDisplayName
    {
        get => _teamDisplayName;
        set
        {
            if (SetProperty(ref _teamDisplayName, value?.Trim() ?? string.Empty))
            {
                PersistConfig();
            }
        }
    }

    public string ClientId
    {
        get => _clientId;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim();
            if (SetProperty(ref _clientId, normalized))
            {
                PersistConfig();
            }
        }
    }

    public string TeamWorkspaceRoot
    {
        get => _teamWorkspaceRoot;
        set
        {
            if (SetProperty(ref _teamWorkspaceRoot, value?.Trim() ?? string.Empty))
            {
                ApplyTeamWorkspacePaths(restoreSession: true);
                PersistConfig();
            }
        }
    }

    public string TeamAuthToken
    {
        get => _teamAuthToken;
        set
        {
            if (SetProperty(ref _teamAuthToken, value?.Trim() ?? string.Empty))
            {
                PersistConfig();
            }
        }
    }

    public string TeamStatusSummary
    {
        get => _teamStatusSummary;
        private set => SetProperty(ref _teamStatusSummary, value);
    }

    public bool CanUseTeamActions => CanStartActions && IsTeamMode;

    public bool TryApplyOutputDirectory(string? value, out string errorMessage)
    {
        var normalizedValue = value?.Trim() ?? string.Empty;
        if (IsOutputDirectorySameAsGameDirectory(normalizedValue))
        {
            errorMessage = "출력 폴더는 게임 폴더와 같은 경로로 지정할 수 없습니다.";
            StatusText = errorMessage;
            CurrentOperationDetail = "출력 폴더 선택이 취소되었습니다.";
            return false;
        }

        errorMessage = string.Empty;
        if (SetProperty(ref _outputDirectory, normalizedValue))
        {
            if (!IsTeamMode)
            {
                _localOutputDirectory = normalizedValue;
            }

            OnProjectPathInputsChanged();
        }

        return true;
    }

    public ProviderOption? SelectedProviderOption
    {
        get => _selectedProviderOption;
        set
        {
            if (SetProperty(ref _selectedProviderOption, value) && value is not null)
            {
                ApplyProviderDefaults(value.ProviderType);
                PersistConfig();
            }
        }
    }

    public string BaseUrl
    {
        get => _baseUrl;
        set
        {
            if (SetProperty(ref _baseUrl, value))
            {
                PersistConfig();
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
                ApplyLmStudioPresetIfEligible(previousModel, _disableThinking);
                RaisePropertyChanged(nameof(TranslationSettingsSummary));
                PersistConfig();
            }
        }
    }

    public string ApiKey => _providerApiKeys.GetValueOrDefault(SelectedProviderType, string.Empty);

    public string SourceLanguage
    {
        get => _sourceLanguage;
        set
        {
            if (SetProperty(ref _sourceLanguage, value))
            {
                ApplySourceLanguageFilter();
                PersistConfig();
            }
        }
    }

    public string TargetLanguage
    {
        get => _targetLanguage;
        set
        {
            if (SetProperty(ref _targetLanguage, value))
            {
                PersistConfig();
            }
        }
    }

    public int BatchSize
    {
        get => _batchSize;
        set
        {
            if (SetProperty(ref _batchSize, Math.Clamp(value, 1, 100)))
            {
                PersistConfig();
            }
        }
    }

    public int RetryCount
    {
        get => _retryCount;
        set
        {
            if (SetProperty(ref _retryCount, Math.Clamp(value, 0, 10)))
            {
                PersistConfig();
            }
        }
    }

    public double Temperature
    {
        get => _temperature;
        set
        {
            var normalized = Math.Clamp(Math.Round(value, 2), 0, 2);
            if (SetProperty(ref _temperature, normalized))
            {
                PersistConfig();
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
                ApplyLmStudioPresetIfEligible(_model, previousDisableThinking);
                PersistConfig();
            }
        }
    }

    public LmStudioPresetProfile LmStudioPresetProfile
    {
        get => _lmStudioPresetProfile;
        set
        {
            if (SetProperty(ref _lmStudioPresetProfile, value))
            {
                PersistConfig();
            }
        }
    }

    public PromptProfile PromptProfile
    {
        get => _promptProfile;
        set
        {
            if (SetProperty(ref _promptProfile, value))
            {
                RaisePropertyChanged(nameof(TranslationSettingsSummary));
                PersistConfig();
            }
        }
    }

    public bool EnableRequestResponseLogging
    {
        get => _enableRequestResponseLogging;
        set
        {
            if (SetProperty(ref _enableRequestResponseLogging, value))
            {
                PersistConfig();
            }
        }
    }

    public bool ExcludeNonSourceText
    {
        get => _excludeNonSourceText;
        set
        {
            if (SetProperty(ref _excludeNonSourceText, value))
            {
                ApplySourceLanguageFilter();
                PersistConfig();
            }
        }
    }

    public bool EnableDictionaryHitLogging
    {
        get => _enableDictionaryHitLogging;
        set
        {
            if (SetProperty(ref _enableDictionaryHitLogging, value))
            {
                PersistConfig();
            }
        }
    }

    public bool EnableBundledDictionaryFirstPass
    {
        get => _enableBundledDictionaryFirstPass;
        set
        {
            if (SetProperty(ref _enableBundledDictionaryFirstPass, value))
            {
                RaisePropertyChanged(nameof(TranslationSettingsSummary));
                PersistConfig();
            }
        }
    }

    public bool EnableKanaTransliterationFallback
    {
        get => _enableKanaTransliterationFallback;
        set
        {
            if (SetProperty(ref _enableKanaTransliterationFallback, value))
            {
                RaisePropertyChanged(nameof(TranslationSettingsSummary));
                PersistConfig();
            }
        }
    }

    public bool EnableNaverJapaneseDictionaryLookup
    {
        get => _enableNaverJapaneseDictionaryLookup;
        set
        {
            if (SetProperty(ref _enableNaverJapaneseDictionaryLookup, value))
            {
                RaisePropertyChanged(nameof(TranslationSettingsSummary));
                PersistConfig();
            }
        }
    }

    public bool EnableKanjiReadingFallback
    {
        get => _enableKanjiReadingFallback;
        set
        {
            if (SetProperty(ref _enableKanjiReadingFallback, value))
            {
                RaisePropertyChanged(nameof(TranslationSettingsSummary));
                PersistConfig();
            }
        }
    }

    public int DictionaryFirstMaxTermLength
    {
        get => _dictionaryFirstMaxTermLength;
        set
        {
            if (SetProperty(ref _dictionaryFirstMaxTermLength, Math.Clamp(value, 1, 12)))
            {
                RaisePropertyChanged(nameof(TranslationSettingsSummary));
                PersistConfig();
            }
        }
    }

    public string SystemPromptTemplate
    {
        get => _systemPromptTemplate;
        set
        {
            if (SetProperty(ref _systemPromptTemplate, value))
            {
                PersistConfig();
            }
        }
    }

    public string RetryPromptTemplate
    {
        get => _retryPromptTemplate;
        set
        {
            if (SetProperty(ref _retryPromptTemplate, value))
            {
                PersistConfig();
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
                PersistConfig();
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
                PersistConfig();
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
                PersistConfig();
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
                PersistConfig();
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
                PersistConfig();
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
                PersistConfig();
            }
        }
    }

    public string ProtectedFullWidthCharacters
    {
        get => _protectedFullWidthCharacters;
        set
        {
            if (SetProperty(ref _protectedFullWidthCharacters, value))
            {
                PersistConfig();
            }
        }
    }

    public string PapagoClientId
    {
        get => _papagoClientId;
        set
        {
            if (SetProperty(ref _papagoClientId, value))
            {
                PersistConfig();
            }
        }
    }

    public string PapagoClientSecret
    {
        get => _papagoClientSecret;
        set
        {
            if (SetProperty(ref _papagoClientSecret, value))
            {
                PersistConfig();
            }
        }
    }

    public string EzTransInstallationPath
    {
        get => _ezTransInstallationPath;
        set
        {
            if (SetProperty(ref _ezTransInstallationPath, value))
            {
                PersistConfig();
            }
        }
    }

    public int EzTransProcessCount
    {
        get => _ezTransProcessCount;
        set
        {
            if (SetProperty(ref _ezTransProcessCount, Math.Clamp(value, 1, 16)))
            {
                PersistConfig();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string SummaryText
    {
        get => _summaryText;
        set => SetProperty(ref _summaryText, value);
    }

    public string TranslationSettingsSummary
    {
        get
        {
            var providerName = SelectedProviderOption?.DisplayName ?? "공급자 미설정";
            var modelName = string.IsNullOrWhiteSpace(Model) ? "모델 미지정" : Model;
            var enabledModes = new List<string>();
            if (EnableBundledDictionaryFirstPass)
            {
                enabledModes.Add("exact");
            }

            if (EnableKanaTransliterationFallback)
            {
                enabledModes.Add("카타카나");
            }

            if (EnableNaverJapaneseDictionaryLookup)
            {
                enabledModes.Add("네이버");
            }

            if (EnableKanjiReadingFallback)
            {
                enabledModes.Add("한자");
            }

            var dictionaryLabel = enabledModes.Count == 0
                ? "사전 선행 OFF"
                : $"사전 선행 ({string.Join(", ", enabledModes)}) / {DictionaryFirstMaxTermLength}자";
            return $"{providerName} / {modelName} / {dictionaryLabel}";
        }
    }

    public string UserDictionarySummary
    {
        get
        {
            var effectiveCount = _userDictionaryService.BuildEffectiveDictionary(_globalUserDictionary, _projectUserDictionary).Count;
            return $"사용자 사전 {effectiveCount}개 적용 가능";
        }
    }

    public IReadOnlyDictionary<TranslationProviderType, string> ProviderApiKeys => _providerApiKeys;

    public ExtractedTextItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (ReferenceEquals(_selectedItem, value))
            {
                return;
            }

            CommitSelectedItemTranslatedTextEdit();

            if (_selectedItem is not null)
            {
                _selectedItem.PropertyChanged -= SelectedItemOnPropertyChanged;
            }

            _selectedItem = value;
            if (_selectedItem is not null)
            {
                _selectedItem.PropertyChanged += SelectedItemOnPropertyChanged;
            }

            SyncSelectedItemPreview();
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(SelectedItemLogText));
        }
    }

    public string SelectedItemOriginalPreviewText => SelectedItem?.OriginalText ?? string.Empty;

    public string SelectedItemTranslatedTextEditor
    {
        get => _selectedItemTranslatedTextEditor;
        set
        {
            if (SetProperty(ref _selectedItemTranslatedTextEditor, value) && !_syncingSelectedItemTranslatedTextEditor)
            {
                _selectedItemTranslatedTextEditorDirty = true;
            }
        }
    }

    public string SelectedItemLogText
    {
        get
        {
            if (SelectedItem is null)
            {
                return "항목을 선택하면 검증, 오류, 경고 상세를 여기에서 확인할 수 있습니다.";
            }

            var lines = new List<string>
            {
                $"파일: {SelectedItem.RelativePath}",
                $"줄: {SelectedItem.LineNumber}",
                $"상태: {SelectedItem.Status}",
                $"검증: {SelectedItem.ValidationStatus}",
            };
            var selectedDocument = _session?.Documents.GetValueOrDefault(SelectedItem.DocumentId);

            if (!string.IsNullOrWhiteSpace(SelectedItem.SourceKey))
            {
                lines.Add($"CSV 키: {SelectedItem.SourceKey}");
            }

            if (!string.IsNullOrWhiteSpace(SelectedItem.SymbolNamespace))
            {
                lines.Add($"심볼 네임스페이스: {SelectedItem.SymbolNamespace}");
            }

            if (!string.IsNullOrWhiteSpace(SelectedItem.OriginalSymbolKey))
            {
                lines.Add($"원본 심볼 키: {SelectedItem.OriginalSymbolKey}");
            }

            if (!string.IsNullOrWhiteSpace(SelectedItem.TranslatedSymbolKey))
            {
                lines.Add($"새 심볼 키: {SelectedItem.TranslatedSymbolKey}");
            }

            if (!string.IsNullOrWhiteSpace(SelectedItem.TranslationSource))
            {
                lines.Add($"번역 출처: {SelectedItem.TranslationSource}");
            }

            if (SelectedItem.IsReferenceBearingKey)
            {
                lines.Add($"참조 영향 수: {SelectedItem.ReferenceImpactCount}");
                lines.Add($"참조 해석 상태: {SelectedItem.ReferenceResolutionStatus}");
            }

            if (selectedDocument is not null
                && DocumentFileTypes.SupportsJosaRewrite(selectedDocument.FileType)
                && selectedDocument.JosaAnalysis.PatternCount > 0)
            {
                lines.Add($"조사 문법 유형: {selectedDocument.JosaAnalysis.SyntaxType}");
                lines.Add($"조사 패턴 수: {selectedDocument.JosaAnalysis.PatternCount}");
                lines.Add($"자동 변환 가능 수: {selectedDocument.JosaAnalysis.AutoConvertibleCount}");
                lines.Add($"범용 함수 유지 수: {selectedDocument.JosaAnalysis.GenericFunctionCount}");
                lines.Add($"ERH 필요 여부: {(selectedDocument.JosaAnalysis.RequiresErh ? "필요" : "불필요")}");
                lines.Add($"ERH 연결 상태: {selectedDocument.JosaAnalysis.ErhLinkStatus}");
                lines.Add($"최신 ZNAME 패키지 호환 여부: {selectedDocument.JosaAnalysis.PackageCompatibilityStatus}");
            }

            if (!string.IsNullOrWhiteSpace(SelectedItem.TranslationError))
            {
                lines.Add($"오류: {SelectedItem.TranslationError}");
            }
            else if (SelectedItem.Status == "제외됨")
            {
                lines.Add("오류: 원문 언어 판정으로 자동 제외되었습니다.");
            }

            if (!string.IsNullOrWhiteSpace(SelectedItem.WarningText))
            {
                lines.Add($"경고: {SelectedItem.WarningText}");
            }

            if (string.IsNullOrWhiteSpace(SelectedItem.TranslationError)
                && string.IsNullOrWhiteSpace(SelectedItem.WarningText)
                && string.Equals(SelectedItem.ValidationStatus, "통과", StringComparison.Ordinal))
            {
                lines.Add("상세 로그: 특이사항 없음");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    public string CurrentOperationDetail
    {
        get => _currentOperationDetail;
        set => SetProperty(ref _currentOperationDetail, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        set => SetProperty(ref _progressValue, value);
    }

    public SaveMode SelectedSaveMode
    {
        get => _selectedSaveMode;
        set
        {
            if (SetProperty(ref _selectedSaveMode, value))
            {
                RaisePropertyChanged(nameof(IsExportSaveMode));
                RaisePropertyChanged(nameof(IsInPlaceSaveMode));
                RaisePropertyChanged(nameof(SaveModeSummary));
                OnProjectPathInputsChanged();
            }
        }
    }

    public bool WarningsOnly
    {
        get => _warningsOnly;
        set
        {
            if (SetProperty(ref _warningsOnly, value))
            {
                RefreshItemsView();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaisePropertyChanged(nameof(CanCancel));
                RaisePropertyChanged(nameof(CanStartActions));
                RaisePropertyChanged(nameof(CanBrowseDirectories));
                RaisePropertyChanged(nameof(CanUseTeamActions));
            }
        }
    }

    public bool IsStartupLoading
    {
        get => _isStartupLoading;
        private set
        {
            if (SetProperty(ref _isStartupLoading, value))
            {
                RaisePropertyChanged(nameof(CanCancel));
                RaisePropertyChanged(nameof(CanStartActions));
                RaisePropertyChanged(nameof(CanBrowseDirectories));
                RaisePropertyChanged(nameof(CanUseTeamActions));
            }
        }
    }

    public bool CanCancel => IsBusy && !IsStartupLoading;

    public bool CanStartActions => !IsBusy && !IsStartupLoading;

    public bool CanBrowseDirectories => !IsBusy && !IsStartupLoading && !IsTeamMode;

    public bool RefreshGridDuringTranslatedTextEdit
    {
        get => _refreshGridDuringTranslatedTextEdit;
        set
        {
            if (SetProperty(ref _refreshGridDuringTranslatedTextEdit, value))
            {
                RefreshItemsView();
                PersistConfig();
            }
        }
    }

    public bool EnableResultStateLogging
    {
        get => _enableResultStateLogging;
        set
        {
            if (SetProperty(ref _enableResultStateLogging, value))
            {
                PersistConfig();
            }
        }
    }

    public bool IsExportSaveMode => true;

    public bool IsInPlaceSaveMode => false;

    public string SaveModeSummary => "번역 파일을 별도 출력 폴더에 저장합니다. 원본 파일은 변경하지 않습니다.";

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
            {
                RefreshItemsView();
            }
        }
    }

    public bool UseRegexFilter
    {
        get => _useRegexFilter;
        set
        {
            if (SetProperty(ref _useRegexFilter, value))
            {
                RefreshItemsView();
            }
        }
    }

    public string SelectedSearchFieldFilter
    {
        get => _selectedSearchFieldFilter;
        set
        {
            if (SetProperty(ref _selectedSearchFieldFilter, value))
            {
                RefreshItemsView();
            }
        }
    }

    public string SelectedFileTypeFilter
    {
        get => _selectedFileTypeFilter;
        set
        {
            if (SetProperty(ref _selectedFileTypeFilter, value))
            {
                RefreshItemsView();
            }
        }
    }

    public string SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            if (SetProperty(ref _selectedStatusFilter, value))
            {
                RefreshItemsView();
            }
        }
    }

    public async Task<bool> ScanAsync()
    {
        if (!Directory.Exists(GameDirectory))
        {
            StatusText = "유효한 게임 디렉토리를 선택하세요.";
            return false;
        }

        return await RunBusyAsync(
            "ERB/CSV 파일을 스캔 중입니다...",
            async cancellationToken =>
            {
                var projectDataDirectory = GetProjectDataDirectory();
                ProgressValue = 0.1;
                CurrentOperationDetail = "스캔 준비 중";
                var scanProgress = new Progress<(double value, string detail)>(tuple =>
                {
                    ProgressValue = tuple.value;
                    CurrentOperationDetail = tuple.detail;
                });

                var scanResult = await Task.Run(() =>
                {
                    var previousSession = _projectStatePersistenceService.LoadScanSession(projectDataDirectory);
                    var previousProgress = _projectStatePersistenceService.LoadTranslationProgress(projectDataDirectory);
                    var session = _fileScanner.Scan(GameDirectory, scanProgress, cancellationToken);
                    return new ScanExecutionResult(session, previousSession, previousProgress);
                }, cancellationToken);

                ProgressValue = Math.Max(ProgressValue, 0.94);
                CurrentOperationDetail = "결과 적용 중";
                var restoreResult = ApplySession(scanResult.Session, restoreProgress: false, scanResult.PreviousSession, scanResult.PreviousProgress);

                ProgressValue = Math.Max(ProgressValue, 0.97);
                CurrentOperationDetail = "캐시 저장 중";
                await Task.Run(() => _projectStatePersistenceService.SaveScanSession(scanResult.Session, projectDataDirectory), cancellationToken);
                SaveTranslationProgressSnapshot("ScanAsync completed");

                RefreshSummaryText();

                StatusText = restoreResult.RestoredCount > 0
                    ? $"스캔이 완료되었습니다. 이전 번역 상태 {restoreResult.ExactRestoredCount}개 정확 복원, {restoreResult.HeuristicRestoredCount}개 업데이트 승계, {restoreResult.UnmatchedCount}개 신규/변경 항목입니다."
                    : "스캔이 완료되었습니다.";
                CurrentOperationDetail = $"스캔 완료: {scanResult.Session.Metrics.GetValueOrDefault("Documents")}개 문서";
                ProgressValue = 1.0;
                RefreshItemsView();
            });
    }

    public async Task<bool> TeamSyncAsync()
    {
        if (!TryValidateTeamSettings(out var errorMessage))
        {
            StatusText = errorMessage;
            return false;
        }

        return await RunBusyAsync(
            "팀 서버와 동기화 중입니다...",
            async cancellationToken =>
            {
                var context = CreateTeamProjectContext();
                _projectContextFactory.EnsureWorkspace(context);
                ApplyTeamWorkspacePaths(restoreSession: false);

                ProgressValue = 0.05;
                CurrentOperationDetail = "source snapshot 확인 중";
                var sourceResult = await _teamSourceSyncService.EnsureSourceAsync(context, TeamAuthToken, cancellationToken);

                var projectDataDirectory = context.TeamProjectDataDirectory;
                var shouldScan = sourceResult.Downloaded
                    || _session is null
                    || !string.Equals(_session.GameRoot, context.SourceDirectory, StringComparison.OrdinalIgnoreCase);
                if (shouldScan)
                {
                    ProgressValue = 0.25;
                    CurrentOperationDetail = sourceResult.Downloaded
                        ? "새 source snapshot 추출 중"
                        : "팀 source 작업본 추출 중";
                    var scanProgress = new Progress<(double value, string detail)>(tuple =>
                    {
                        ProgressValue = 0.25 + tuple.value * 0.45;
                        CurrentOperationDetail = tuple.detail;
                    });
                    var scanResult = await Task.Run(() =>
                    {
                        var previousSession = _projectStatePersistenceService.LoadScanSession(projectDataDirectory);
                        var previousProgress = _projectStatePersistenceService.LoadTranslationProgress(projectDataDirectory);
                        var session = _fileScanner.Scan(context.SourceDirectory, scanProgress, cancellationToken);
                        return new ScanExecutionResult(session, previousSession, previousProgress);
                    }, cancellationToken);

                    var restoreResult = ApplySession(scanResult.Session, restoreProgress: false, scanResult.PreviousSession, scanResult.PreviousProgress);
                    await Task.Run(() => _projectStatePersistenceService.SaveScanSession(scanResult.Session, projectDataDirectory), cancellationToken);
                    CurrentOperationDetail = $"팀 source 추출 완료: 복원 {restoreResult.RestoredCount}개";
                }

                ProgressValue = 0.75;
                CurrentOperationDetail = "서버 work item/shared key 적용 중";
                var sync = await _teamCollaborationService.SyncAsync(context, TeamAuthToken, cancellationToken);
                var state = _teamProjectStateService.Load(context);
                _suppressItemStatePersistence = true;
                TeamSyncApplyResult applyResult;
                try
                {
                    applyResult = _teamCollaborationService.ApplySyncResponse(context, sync, Items, state);
                }
                finally
                {
                    _suppressItemStatePersistence = false;
                }

                SaveTranslationProgressSnapshot(force: true, reason: "TeamSyncAsync completed");
                RefreshSummaryText();
                RefreshItemsView();
                TeamStatusSummary = $"팀 동기화 완료: {context.ProjectId} / source {sync.ScanRevisionId} / work {applyResult.WorkItemMetadataCount} / shared {applyResult.SharedKeyMetadataCount}";
                StatusText = applyResult.SourceRevisionMismatch
                    ? "서버 source revision이 로컬과 달라 재동기화가 필요합니다."
                    : "팀 동기화가 완료되었습니다.";
                CurrentOperationDetail = $"서버 번역 적용: 일반 {applyResult.AppliedWorkItemTranslations}개, 공통키 {applyResult.AppliedSharedKeyTranslations}개";
                ProgressValue = 1.0;
            });
    }

    public async Task<bool> RefreshTeamProjectsAsync()
    {
        if (!IsTeamMode)
        {
            StatusText = "팀 작업 모드가 아닙니다.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(TeamServerUrl) || string.IsNullOrWhiteSpace(TeamAuthToken))
        {
            StatusText = "팀 서버 URL과 인증 토큰을 먼저 입력하세요.";
            return false;
        }

        return await RunBusyAsync(
            "팀 프로젝트 목록을 불러오는 중입니다...",
            async cancellationToken =>
            {
                if (!string.IsNullOrWhiteSpace(TeamDisplayName))
                {
                    await _teamCollaborationService.RegisterClientAsync(
                        TeamServerUrl,
                        TeamAuthToken,
                        ClientId,
                        TeamDisplayName,
                        cancellationToken);
                }

                var projects = await _teamCollaborationService.GetProjectsAsync(TeamServerUrl, TeamAuthToken, cancellationToken);
                TeamProjects.Clear();
                foreach (var project in projects.OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase))
                {
                    TeamProjects.Add(project);
                }

                SelectedTeamProject = TeamProjects.FirstOrDefault(project => string.Equals(project.Id, TeamProjectId, StringComparison.Ordinal))
                    ?? TeamProjects.FirstOrDefault();
                if (TeamProjects.Count == 0)
                {
                    IsManualTeamProjectId = true;
                }

                StatusText = TeamProjects.Count == 0
                    ? "팀 서버에서 선택 가능한 프로젝트를 찾지 못했습니다."
                    : $"{TeamProjects.Count}개 팀 프로젝트를 불러왔습니다.";
                CurrentOperationDetail = SelectedTeamProject is null
                    ? "프로젝트 미선택"
                    : $"선택: {SelectedTeamProject.Name} ({SelectedTeamProject.Id})";
                TeamStatusSummary = CurrentOperationDetail;
                ProgressValue = 1.0;
            });
    }

    public async Task<bool> UploadTeamScanManifestAsync()
    {
        if (!TryValidateTeamSettings(out var errorMessage))
        {
            StatusText = errorMessage;
            return false;
        }

        if (_session is null)
        {
            StatusText = "먼저 팀 동기화 또는 텍스트 추출을 실행하세요.";
            return false;
        }

        return await RunBusyAsync(
            "팀 scan manifest를 업로드 중입니다...",
            async cancellationToken =>
            {
                var context = CreateTeamProjectContext();
                var state = _teamProjectStateService.Load(context);
                var scanRevisionId = string.IsNullOrWhiteSpace(state.LocalSourceScanRevisionId)
                    ? state.LastSyncedScanRevisionId
                    : state.LocalSourceScanRevisionId;
                if (string.IsNullOrWhiteSpace(scanRevisionId) || string.IsNullOrWhiteSpace(state.SourceArchiveSha256))
                {
                    throw new InvalidOperationException("source snapshot revision/hash 정보가 없습니다. 먼저 팀 동기화를 실행하세요.");
                }

                ProgressValue = 0.25;
                CurrentOperationDetail = "manifest 생성 중";
                var manifest = _teamScanManifestBuilder.Build(_session, scanRevisionId, state.SourceArchiveSha256);
                ProgressValue = 0.55;
                CurrentOperationDetail = "manifest 업로드 중";
                var validation = await _teamCollaborationService.UploadScanManifestAsync(context, TeamAuthToken, manifest, cancellationToken);
                TeamStatusSummary = $"Manifest: {validation.ValidationStatus} / 문서 {validation.DocumentCount} / 항목 {validation.ItemCount} / 공통키 {validation.SharedKeyCount}";
                StatusText = "팀 scan manifest 업로드가 완료되었습니다.";
                CurrentOperationDetail = validation.ValidationMessages.Count == 0
                    ? "manifest validation: valid"
                    : $"manifest validation: {validation.ValidationMessages.Count}개 메시지";
                ProgressValue = 1.0;
            });
    }

    public async Task<bool> SubmitTeamChangesAsync()
    {
        CommitSelectedItemTranslatedTextEdit();

        if (!TryValidateTeamSettings(out var errorMessage))
        {
            StatusText = errorMessage;
            return false;
        }

        if (_session is null)
        {
            StatusText = "먼저 팀 동기화 또는 텍스트 추출을 실행하세요.";
            return false;
        }

        return await RunBusyAsync(
            "팀 서버에 변경분을 제출 중입니다...",
            async cancellationToken =>
            {
                var context = CreateTeamProjectContext();
                var state = _teamProjectStateService.Load(context);
                if (!string.Equals(state.LocalSourceScanRevisionId, state.LastSyncedScanRevisionId, StringComparison.Ordinal))
                {
                    StatusText = "source revision이 바뀐 상태라 제출하지 않았습니다. 먼저 팀 동기화를 다시 실행하세요.";
                    CurrentOperationDetail = $"local={state.LocalSourceScanRevisionId}, server={state.LastSyncedScanRevisionId}";
                    return;
                }

                var submitBuild = _teamCollaborationService.BuildSubmitRequest(
                    context,
                    state.LastSyncedScanRevisionId,
                    ClientId,
                    Items,
                    state);
                if (submitBuild.WorkItemChangeCount == 0 && submitBuild.SharedKeyChangeCount == 0)
                {
                    StatusText = "팀 서버에 제출할 변경분이 없습니다.";
                    CurrentOperationDetail = "dirty change 0개";
                    ProgressValue = 1.0;
                    return;
                }

                try
                {
                    ProgressValue = 0.45;
                    CurrentOperationDetail = $"제출 중: 일반 {submitBuild.WorkItemChangeCount}개, 공통키 {submitBuild.SharedKeyChangeCount}개";
                    var response = await _teamCollaborationService.SubmitAsync(context, TeamAuthToken, submitBuild.Request, cancellationToken);
                    _suppressItemStatePersistence = true;
                    TeamSubmitApplyResult applyResult;
                    try
                    {
                        applyResult = _teamCollaborationService.ApplySubmitResponse(context, submitBuild.Request, response, Items, state);
                    }
                    finally
                    {
                        _suppressItemStatePersistence = false;
                    }

                    SaveTranslationProgressSnapshot(force: true, reason: "SubmitTeamChangesAsync completed");
                    RefreshItemsView();
                    StatusText = "팀 서버 제출이 완료되었습니다.";
                    CurrentOperationDetail = $"Applied {applyResult.AppliedCount}, NoOp {applyResult.NoopCount}, Conflict {applyResult.ConflictCount}, Rejected {applyResult.RejectedCount}";
                    ProgressValue = 1.0;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
                {
                    _teamCollaborationService.EnqueueOfflineSubmission(context, submitBuild.Request, Items, state);
                    StatusText = "팀 서버 제출 실패로 변경분을 offline queue에 보관했습니다.";
                    CurrentOperationDetail = ex.Message;
                    ProgressValue = 1.0;
                }
            });
    }

    public async Task<bool> RetryTeamOfflineQueueAsync()
    {
        if (!TryValidateTeamSettings(out var errorMessage))
        {
            StatusText = errorMessage;
            return false;
        }

        if (_session is null)
        {
            StatusText = "먼저 팀 동기화 또는 텍스트 추출을 실행하세요.";
            return false;
        }

        return await RunBusyAsync(
            "offline queue를 재전송 중입니다...",
            async cancellationToken =>
            {
                var context = CreateTeamProjectContext();
                var state = _teamProjectStateService.Load(context);
                var retryableQueue = state.OfflineSubmissionQueue
                    .Where(submission => string.Equals(submission.ScanRevisionId, state.LocalSourceScanRevisionId, StringComparison.Ordinal))
                    .ToList();
                if (retryableQueue.Count == 0)
                {
                    StatusText = state.OfflineSubmissionQueue.Count == 0
                        ? "재전송할 offline queue가 없습니다."
                        : "source revision이 달라 offline queue를 자동 제출하지 않았습니다. 먼저 팀 동기화를 실행하세요.";
                    CurrentOperationDetail = $"queue {state.OfflineSubmissionQueue.Count}개";
                    return;
                }

                var succeededSubmissionIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var submission in retryableQueue)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var request = new TeamSubmitRequest
                    {
                        SubmissionId = submission.SubmissionId,
                        ScanRevisionId = submission.ScanRevisionId,
                        ClientId = ClientId,
                        WorkItems = submission.WorkItems.Select(change => new TeamSubmitChange
                        {
                            Id = change.Id,
                            BaseRevision = change.BaseRevision,
                            Translation = change.TranslatedText,
                        }).ToList(),
                        SharedKeys = submission.SharedKeys.Select(change => new TeamSubmitChange
                        {
                            Id = change.Id,
                            BaseRevision = change.BaseRevision,
                            Translation = change.TranslatedText,
                        }).ToList(),
                    };
                    var response = await _teamCollaborationService.SubmitAsync(context, TeamAuthToken, request, cancellationToken);
                    _teamCollaborationService.ApplySubmitResponse(context, request, response, Items, state);
                    succeededSubmissionIds.Add(submission.SubmissionId);
                    ProgressValue = Math.Min(0.95, ProgressValue + 1.0 / retryableQueue.Count);
                }

                var updatedState = _teamProjectStateService.Load(context);
                _teamProjectStateService.Save(context, new TeamProjectState
                {
                    LastSyncedScanRevisionId = updatedState.LastSyncedScanRevisionId,
                    LocalSourceScanRevisionId = updatedState.LocalSourceScanRevisionId,
                    TeamProjectDictionaryPath = updatedState.TeamProjectDictionaryPath,
                    SourceArchiveSha256 = updatedState.SourceArchiveSha256,
                    WorkItemsBySegmentId = updatedState.WorkItemsBySegmentId,
                    SharedKeysByLookupKey = updatedState.SharedKeysByLookupKey,
                    ConflictIdsByTargetId = updatedState.ConflictIdsByTargetId,
                    OfflineSubmissionQueue = updatedState.OfflineSubmissionQueue
                        .Where(submission => !succeededSubmissionIds.Contains(submission.SubmissionId))
                        .ToList(),
                });

                SaveTranslationProgressSnapshot(force: true, reason: "RetryTeamOfflineQueueAsync completed");
                RefreshItemsView();
                StatusText = $"{succeededSubmissionIds.Count}개 offline submission을 재전송했습니다.";
                CurrentOperationDetail = $"남은 queue: {updatedState.OfflineSubmissionQueue.Count - succeededSubmissionIds.Count}개";
                ProgressValue = 1.0;
            });
    }

    public async Task ConvertEncodingsAsync()
    {
        if (!Directory.Exists(GameDirectory))
        {
            StatusText = "유효한 게임 디렉토리를 선택하세요.";
            return;
        }

        await RunBusyAsync(
            "인코딩을 변환 중입니다...",
            async cancellationToken =>
            {
                CurrentOperationDetail = "변환 준비 중";
                var convertProgress = new Progress<(double value, string detail)>(tuple =>
                {
                    ProgressValue = tuple.value;
                    CurrentOperationDetail = tuple.detail;
                });
                var converted = await Task.Run(() => _fileScanner.ConvertEncodingsToUtf8Bom(GameDirectory, convertProgress, cancellationToken), cancellationToken);
                StatusText = converted.Count == 0
                    ? "변환할 Shift-JIS/EUC-JP 파일이 없습니다."
                    : $"{converted.Count}개 파일을 UTF-8 BOM으로 변환했습니다.";
                CurrentOperationDetail = converted.Count == 0 ? "변환 대상 없음" : $"변환 완료: {converted.Count}개 파일";
                ProgressValue = 1.0;
            });
    }

    public async Task<bool> TranslatePendingAsync()
    {
        if (_session is null)
        {
            StatusText = "먼저 텍스트를 추출하세요.";
            return false;
        }

        if (SelectedProviderOption is null)
        {
            StatusText = "번역 공급자를 선택하세요.";
            return false;
        }

        if (!SelectedProviderOption.IsAvailable)
        {
            StatusText = $"{SelectedProviderOption.DisplayName}는 아직 준비 중입니다.";
            return false;
        }

        var translationScope = GetCurrentTranslationScope();
        var phasePlans = BuildTranslationPhasePlans(translationScope);
        var pendingCount = phasePlans.Sum(plan => plan.PendingCount);
        if (pendingCount == 0)
        {
            StatusText = "미번역 또는 번역 실패 항목이 없습니다.";
            CurrentOperationDetail = "자동 번역 대상 항목이 없습니다.";
            return false;
        }

        return await RunBusyAsync(
            "번역을 준비 중입니다...",
            async cancellationToken =>
            {
                ProgressValue = 0;
                StartTranslationTiming();
                var progress = new Progress<(double value, string status, string detail)>(tuple =>
                {
                    ProgressValue = tuple.value;
                    StatusText = tuple.status;
                    CurrentOperationDetail = BuildTranslationProgressDetail(tuple.detail, tuple.value);
                });

                CurrentOperationDetail = "번역 준비 중";
                _suppressItemStatePersistence = true;
                _lastProgressSaveAtUtc = DateTimeOffset.MinValue;
                try
                {
                    await TranslatePendingPhasesAsync(
                        phasePlans,
                        pendingCount,
                        BuildSettings(),
                        _userDictionaryService.BuildEffectiveDictionary(_globalUserDictionary, _projectUserDictionary),
                        progress,
                        cancellationToken);
                }
                finally
                {
                    _suppressItemStatePersistence = false;
                }

                SaveTranslationProgressSnapshot(force: true, reason: "TranslatePendingAsync completed");
                var remainingCount = translationScope.Count(item => item.NeedsTranslation);
                var completedCount = translationScope.Count(item => item.IsTranslatedSuccessfully);
                StopTranslationTiming(resumeOnNextRun: false);
                StatusText = remainingCount == 0
                    ? $"번역이 완료되었습니다. 완료 {completedCount}개"
                    : $"번역이 끝났지만 자동 재번역 대상 항목 {remainingCount}개가 남았습니다.";
                var firstRemaining = translationScope.FirstOrDefault(item => item.NeedsTranslation);
                var elapsedText = FormatDuration(_translationProgressStopwatch.Elapsed);
                CurrentOperationDetail = firstRemaining is null
                    ? $"번역 작업 완료 | 경과 시간: {elapsedText}"
                    : $"다음 재개 지점: {firstRemaining.RelativePath} | 경과 시간: {elapsedText}";
                ProgressValue = 1.0;
                RefreshItemsView();
            },
            cancelStatusText: "번역이 중지되었습니다. 다시 시작해도 미번역 또는 번역 실패 항목만 이어집니다.",
            cancelDetailFactory: () => $"현재 번역 상태를 저장했습니다. | 경과 시간: {FormatDuration(_translationProgressStopwatch.Elapsed)}",
            onCanceled: () => StopTranslationTiming(resumeOnNextRun: true),
            onFailed: _ => StopTranslationTiming(resumeOnNextRun: false));
    }

    public async Task SaveAsync()
    {
        CommitSelectedItemTranslatedTextEdit();

        if (_session is null)
        {
            StatusText = "먼저 스캔을 진행하세요.";
            return;
        }

        if (EffectiveSaveMode == SaveMode.ExportCopy && string.IsNullOrWhiteSpace(OutputDirectory))
        {
            StatusText = "출력 디렉토리를 지정하세요.";
            return;
        }

        await RunBusyAsync(
            "번역 결과를 저장 중입니다...",
            async cancellationToken =>
            {
                CurrentOperationDetail = "저장 준비 중";
                _isSavingResults = true;
                LogResultState(
                    "RESULT_SAVE_START",
                    "결과 저장을 시작합니다.",
                    new Dictionary<string, string>
                    {
                        ["ConfiguredSaveMode"] = SelectedSaveMode.ToString(),
                        ["EffectiveSaveMode"] = EffectiveSaveMode.ToString(),
                        ["GameDirectory"] = GameDirectory,
                        ["OutputDirectory"] = OutputDirectory,
                        ["ProjectDataDirectory"] = GetProjectDataDirectory(_session.GameRoot),
                        ["ItemCount"] = Items.Count.ToString(),
                    });
                var saveProgress = new Progress<(double value, string detail)>(tuple =>
                {
                    ProgressValue = tuple.value;
                    CurrentOperationDetail = tuple.detail;
                });
                try
                {
                    var writeResult = await Task.Run(
                        () => _outputWriter.Save(_session, OutputDirectory, EffectiveSaveMode, saveProgress, cancellationToken),
                        cancellationToken);

                    StatusText = EffectiveSaveMode == SaveMode.ExportCopy
                        ? $"{writeResult.WrittenFiles.Count}개 파일을 출력 폴더에 저장했습니다."
                        : $"{writeResult.WrittenFiles.Count}개 파일을 원본에 반영했고, {writeResult.BackupFiles.Count}개 백업을 생성했습니다.";
                    CurrentOperationDetail = writeResult.WrittenFiles.Count == 0
                        ? "저장할 번역 항목 없음"
                        : $"저장 완료: {writeResult.WrittenFiles.Count}개 파일";
                    ProgressValue = 1.0;
                    LogResultState(
                        "RESULT_SAVE_END",
                        "결과 저장이 완료되었습니다.",
                        new Dictionary<string, string>
                        {
                            ["WrittenFiles"] = writeResult.WrittenFiles.Count.ToString(),
                            ["BackupFiles"] = writeResult.BackupFiles.Count.ToString(),
                            ["SkippedFiles"] = writeResult.SkippedFiles.Count.ToString(),
                            ["StartedAt"] = writeResult.StartedAt.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"),
                            ["CompletedAt"] = writeResult.CompletedAt.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"),
                            ["Elapsed"] = FormatDuration(writeResult.TotalElapsed),
                            ["RefreshElapsedMs"] = Math.Round(writeResult.RefreshElapsed.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
                            ["RewritePlanElapsedMs"] = Math.Round(writeResult.RewritePlanElapsed.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
                            ["CopyElapsedMs"] = Math.Round(writeResult.CopyElapsed.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
                            ["BackupElapsedMs"] = Math.Round(writeResult.BackupElapsed.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
                            ["DocumentWriteElapsedMs"] = Math.Round(writeResult.DocumentWriteElapsed.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
                            ["PackageWriteElapsedMs"] = Math.Round(writeResult.PackageWriteElapsed.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
                        });
                }
                finally
                {
                    _isSavingResults = false;
                }
            });
    }

    public void StopCurrentOperation()
    {
        _cancellationTokenSource?.Cancel();
    }

    public void ResetTranslations()
    {
        if (_session is null)
        {
            StatusText = "리셋할 번역 상태가 없습니다.";
            return;
        }

        _suppressItemStatePersistence = true;
        try
        {
            foreach (var item in Items)
            {
                if (item.IsExcluded)
                {
                    continue;
                }

                item.ResetTranslationState();
            }
        }
        finally
        {
            _suppressItemStatePersistence = false;
        }

        ApplySourceLanguageFilter(persistProgress: false);
        SaveTranslationProgressSnapshot("ResetTranslations");
        RefreshSummaryText();
        StatusText = "번역 상태를 리셋했습니다.";
        CurrentOperationDetail = "번역문, 실패 상태, 검증 상태를 초기화했습니다.";
        RefreshItemsView();
    }

    public void ResetExtraction()
    {
        var targetDirectory = GetProjectDataDirectory();
        DetachItemStateHandlers(Items);
        _session = null;
        Items.ReplaceAll([]);
        SelectedItem = null;
        _projectStatePersistenceService.DeleteAll(targetDirectory);
        SummaryText = "아직 스캔 전입니다.";
        StatusText = "추출 상태를 리셋했습니다.";
        CurrentOperationDetail = "저장된 추출 결과와 번역 진행 상태를 삭제했습니다.";
        ProgressValue = 0;
        RefreshItemsView();
    }

    public void ExportTranslationsToText(string path)
    {
        if (_session is null)
        {
            StatusText = "먼저 텍스트를 추출하세요.";
            return;
        }

        _translationTextExchangeService.Export(path, Items);
        StatusText = "번역 내용을 텍스트 파일로 내보냈습니다.";
        CurrentOperationDetail = path;
    }

    public void ImportTranslationsFromText(string path)
    {
        if (_session is null)
        {
            StatusText = "먼저 텍스트를 추출하세요.";
            return;
        }

        var importedEntries = _translationTextExchangeService.Import(path);
        var itemMap = Items.ToDictionary(item => item.SegmentId, StringComparer.Ordinal);
        var updatedCount = 0;

        _suppressItemStatePersistence = true;
        try
        {
            foreach (var entry in importedEntries)
            {
                if (!itemMap.TryGetValue(entry.SegmentId, out var item))
                {
                    continue;
                }

                if (!string.Equals(item.OriginalText.Replace("\r\n", "\n", StringComparison.Ordinal), entry.OriginalText, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.TranslatedText))
                {
                    continue;
                }

                item.TranslatedText = entry.TranslatedText;
                item.ApplyManualTranslationEdit();
                updatedCount++;
            }
        }
        finally
        {
            _suppressItemStatePersistence = false;
        }

        SaveTranslationProgressSnapshot("ImportTranslationsFromText");
        RefreshItemsView();
        StatusText = updatedCount == 0
            ? "적용할 번역문을 찾지 못했습니다."
            : $"{updatedCount}개 번역문을 텍스트 파일에서 가져왔습니다.";
        CurrentOperationDetail = path;
    }

    public GlobalReplaceViewModel CreateGlobalReplaceViewModel()
    {
        var translatedCount = Items.Count(item => !string.IsNullOrWhiteSpace(item.TranslatedText));
        return new GlobalReplaceViewModel
        {
            ScopeDescription = $"현재 프로젝트의 번역문 {translatedCount}개를 대상으로 전역 검색/치환을 적용합니다.",
        };
    }

    public void ApplyGlobalReplace(GlobalReplaceViewModel replaceViewModel)
    {
        if (string.IsNullOrWhiteSpace(replaceViewModel.SearchText))
        {
            StatusText = "검색어를 입력한 뒤 전역 치환을 실행하세요.";
            return;
        }

        if (replaceViewModel.UseRegex)
        {
            try
            {
                _ = new Regex(replaceViewModel.SearchText, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch (ArgumentException ex)
            {
                StatusText = $"정규식 오류: {ex.Message}";
                return;
            }
        }

        var updatedCount = 0;
        _suppressItemStatePersistence = true;
        try
        {
            foreach (var item in Items)
            {
                if (string.IsNullOrWhiteSpace(item.TranslatedText))
                {
                    continue;
                }

                if (!TryReplaceText(item.TranslatedText, replaceViewModel.SearchText, replaceViewModel.ReplaceText, replaceViewModel.UseRegex, out var replacedText))
                {
                    continue;
                }

                if (string.Equals(item.TranslatedText, replacedText, StringComparison.Ordinal))
                {
                    continue;
                }

                item.TranslatedText = replacedText;
                item.ApplyManualTranslationEdit();
                updatedCount++;
            }
        }
        catch (ArgumentException ex)
        {
            StatusText = $"정규식 치환 오류: {ex.Message}";
            return;
        }
        finally
        {
            _suppressItemStatePersistence = false;
        }

        SaveTranslationProgressSnapshot("ApplyGlobalReplace");
        RefreshItemsView();
        StatusText = updatedCount == 0
            ? "치환할 번역문을 찾지 못했습니다."
            : $"{updatedCount}개 번역문에 전역 치환을 적용했습니다.";
        CurrentOperationDetail = replaceViewModel.UseRegex ? "정규식 전역 치환 적용" : "일반 텍스트 전역 치환 적용";
    }

    public void HandleTranslatedTextEdited(ExtractedTextItem item, string? editedText = null)
    {
        var groupItems = GetItemsWithSameOriginalText(item);

        _suppressItemStatePersistence = true;
        try
        {
            var editedValue = editedText ?? item.TranslatedText;
            foreach (var groupItem in groupItems)
            {
                groupItem.TranslatedText = editedValue;
                groupItem.ApplyManualTranslationEdit();
            }
        }
        finally
        {
            _suppressItemStatePersistence = false;
        }

        if (RefreshGridDuringTranslatedTextEdit)
        {
            RequestItemsViewRefresh();
        }

        SaveTranslationProgressItems(groupItems, "HandleTranslatedTextEdited");
    }

    public void CommitSelectedItemTranslatedTextEdit(string? editedText = null)
    {
        if (SelectedItem is null)
        {
            if (!string.IsNullOrEmpty(SelectedItemTranslatedTextEditor))
            {
                SelectedItemTranslatedTextEditor = string.Empty;
            }

            _selectedItemTranslatedTextEditorDirty = false;
            return;
        }

        var effectiveText = editedText ?? SelectedItemTranslatedTextEditor;
        var textChanged = !string.Equals(SelectedItem.TranslatedText, effectiveText, StringComparison.Ordinal);
        if (!textChanged && !_selectedItemTranslatedTextEditorDirty)
        {
            return;
        }

        HandleTranslatedTextEdited(SelectedItem, effectiveText);
        SyncSelectedItemTranslatedTextEditor();
    }

    public void PreviewSelectedItemTranslatedTextEdit(string? editedText = null)
    {
        if (SelectedItem is null)
        {
            return;
        }

        var effectiveText = editedText ?? SelectedItemTranslatedTextEditor;
        if (editedText is not null && !string.Equals(SelectedItemTranslatedTextEditor, editedText, StringComparison.Ordinal))
        {
            _selectedItemTranslatedTextEditor = editedText;
            RaisePropertyChanged(nameof(SelectedItemTranslatedTextEditor));
            _selectedItemTranslatedTextEditorDirty = true;
        }

        if (!_selectedItemTranslatedTextEditorDirty)
        {
            return;
        }

        var groupItems = GetItemsWithSameOriginalText(SelectedItem);

        _suppressItemStatePersistence = true;
        try
        {
            foreach (var groupItem in groupItems)
            {
                groupItem.TranslatedText = effectiveText;
                groupItem.ApplyManualTranslationEdit();
            }
        }
        finally
        {
            _suppressItemStatePersistence = false;
        }
    }

    private List<ExtractedTextItem> GetItemsWithSameOriginalText(ExtractedTextItem item)
    {
        var originalKey = NormalizeOriginalTextForPropagation(item.OriginalText);
        var groupItems = Items
            .Where(candidate => string.Equals(
                NormalizeOriginalTextForPropagation(candidate.OriginalText),
                originalKey,
                StringComparison.Ordinal))
            .ToList();
        if (groupItems.Count == 0)
        {
            groupItems.Add(item);
        }

        return groupItems;
    }

    private static string NormalizeOriginalTextForPropagation(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    private static bool IsSameOriginalCorrectionSource(ExtractedTextItem item)
    {
        return item.IsTranslatedSuccessfully
            && !item.IsExcluded
            && !string.IsNullOrWhiteSpace(item.TranslatedText);
    }

    private static int GetSameOriginalCorrectionSourcePriority(ExtractedTextItem item)
    {
        if (DocumentFileTypes.IsCsvLike(item.FileType))
        {
            return 0;
        }

        if (string.Equals(item.FileType, DocumentFileTypes.Erh, StringComparison.OrdinalIgnoreCase)
            && !IdentifierSegmentTypes.IsIdentifier(item.SegmentType))
        {
            return 1;
        }

        if (IdentifierSegmentTypes.IsIdentifier(item.SegmentType))
        {
            return 2;
        }

        return int.MaxValue;
    }

    private static int GetSameOriginalCorrectionStatusPriority(ExtractedTextItem item)
    {
        return item.Status switch
        {
            "수동 수정" => 0,
            "번역 완료" => 1,
            "검수 필요" => 2,
            _ => 3,
        };
    }

    public void RefreshItemsView()
    {
        RebuildVisibleItemSnapshot();
        if (TryRefreshItemsViewNow())
        {
            return;
        }

        QueueItemsViewRefresh();
    }

    public void ApplySameOriginalCorrection()
    {
        if (_session is null)
        {
            StatusText = "먼저 텍스트를 추출하세요.";
            return;
        }

        var groups = Items
            .Where(item => !string.IsNullOrWhiteSpace(item.OriginalText))
            .GroupBy(item => NormalizeOriginalTextForPropagation(item.OriginalText), StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToList();
        if (groups.Count == 0)
        {
            StatusText = "동일 원문을 가진 항목을 찾지 못했습니다.";
            CurrentOperationDetail = "동일원문교정 대상 0개";
            return;
        }

        var changedItems = new List<ExtractedTextItem>();
        var correctedGroupCount = 0;
        _suppressItemStatePersistence = true;
        try
        {
            foreach (var group in groups)
            {
                var candidates = group
                    .Where(IsSameOriginalCorrectionSource)
                    .Select(item => new
                    {
                        Item = item,
                        SourcePriority = GetSameOriginalCorrectionSourcePriority(item),
                        StatusPriority = GetSameOriginalCorrectionStatusPriority(item),
                    })
                    .Where(candidate => candidate.SourcePriority < int.MaxValue)
                    .OrderBy(candidate => candidate.SourcePriority)
                    .ThenBy(candidate => candidate.StatusPriority)
                    .ThenBy(candidate => candidate.Item.RelativePath, StringComparer.Ordinal)
                    .ThenBy(candidate => candidate.Item.LineNumber)
                    .ThenBy(candidate => candidate.Item.SegmentId, StringComparer.Ordinal)
                    .ToList();
                if (candidates.Count == 0)
                {
                    continue;
                }

                var canonicalText = candidates[0].Item.TranslatedText;
                var groupUpdatedCount = 0;
                foreach (var item in group.Where(static item => !item.IsExcluded))
                {
                    if (string.Equals(item.TranslatedText, canonicalText, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    item.TranslatedText = canonicalText;
                    item.ApplyManualTranslationEdit();
                    changedItems.Add(item);
                    groupUpdatedCount++;
                }

                if (groupUpdatedCount > 0)
                {
                    correctedGroupCount++;
                }
            }
        }
        finally
        {
            _suppressItemStatePersistence = false;
        }

        if (changedItems.Count > 0)
        {
            SaveTranslationProgressItems(changedItems, "ApplySameOriginalCorrection");
        }

        RefreshItemsView();
        StatusText = changedItems.Count == 0
            ? "동일 원문 항목 중 교정할 번역문 차이를 찾지 못했습니다."
            : $"{changedItems.Count}개 번역문을 동일원문 기준값으로 교정했습니다.";
        CurrentOperationDetail = $"동일원문교정 그룹 {correctedGroupCount}개 / 변경 {changedItems.Count}개";
    }

    public void ApplyJosaRewriteToCurrentScope()
    {
        if (_session is null)
        {
            StatusText = "먼저 텍스트를 추출하세요.";
            return;
        }

        var scope = GetCurrentTranslationScope()
            .Where(item => DocumentFileTypes.SupportsJosaRewrite(item.FileType))
            .Where(item => !string.IsNullOrWhiteSpace(item.TranslatedText))
            .ToList();
        if (scope.Count == 0)
        {
            StatusText = "현재 필터 범위에 조사처리할 ERB 번역문이 없습니다.";
            CurrentOperationDetail = "조사처리 대상 0개";
            return;
        }

        var updatedCount = 0;
        _suppressItemStatePersistence = true;
        try
        {
            foreach (var item in scope)
            {
                var rewrittenText = _josaPatternAnalyzer.RewriteText(
                    item.TranslatedText,
                    new Dictionary<(string Namespace, string OriginalKey), string>(),
                    _session.JosaPackageInfo).Text;
                if (!TryApplyBulkTranslatedTextChange(item, rewrittenText))
                {
                    continue;
                }

                updatedCount++;
            }
        }
        finally
        {
            _suppressItemStatePersistence = false;
        }

        if (updatedCount > 0)
        {
            SaveTranslationProgressSnapshot("ApplyJosaRewriteToCurrentScope");
        }

        RefreshItemsView();
        StatusText = updatedCount == 0
            ? "현재 필터 범위에서 적용할 조사 패턴을 찾지 못했습니다."
            : $"{updatedCount}개 번역문에 조사처리를 적용했습니다.";
        CurrentOperationDetail = $"현재 필터 범위 {scope.Count}개 중 {updatedCount}개 조사처리";
    }

    public void ApplyErbFunctionCorrectionToCurrentScope()
    {
        if (_session is null)
        {
            StatusText = "먼저 텍스트를 추출하세요.";
            return;
        }

        var scope = GetCurrentTranslationScope()
            .Where(item => DocumentFileTypes.IsErbLike(item.FileType))
            .Where(item => !string.IsNullOrWhiteSpace(item.TranslatedText))
            .ToList();
        if (scope.Count == 0)
        {
            StatusText = "현재 필터 범위에 함수 교정할 ERB/ERH 번역문이 없습니다.";
            CurrentOperationDetail = "함수 교정 대상 0개";
            return;
        }

        var updatedCount = 0;
        _suppressItemStatePersistence = true;
        try
        {
            foreach (var item in scope)
            {
                var correctedText = TranslationQualityRules.NormalizeErbFunctionArgumentSeparators(item.TranslatedText);
                if (!TryApplyBulkTranslatedTextChange(item, correctedText))
                {
                    continue;
                }

                updatedCount++;
            }
        }
        finally
        {
            _suppressItemStatePersistence = false;
        }

        if (updatedCount > 0)
        {
            SaveTranslationProgressSnapshot("ApplyErbFunctionCorrectionToCurrentScope");
        }

        RefreshItemsView();
        StatusText = updatedCount == 0
            ? "현재 필터 범위에서 교정할 ERB 함수/표현식 표기를 찾지 못했습니다."
            : $"{updatedCount}개 번역문에 ERB 함수 교정을 적용했습니다.";
        CurrentOperationDetail = $"현재 필터 범위 {scope.Count}개 중 {updatedCount}개 함수 교정";
    }

    public TranslationSettingsViewModel CreateTranslationSettingsViewModel()
    {
        var viewModel = new TranslationSettingsViewModel(ProviderOptions, _ezTransXpInstallationService);
        viewModel.LoadFrom(this);
        return viewModel;
    }

    public UserDictionaryViewModel CreateUserDictionaryViewModel()
    {
        return new UserDictionaryViewModel(
            GetProjectDataDirectory(),
            _globalUserDictionary,
            _projectUserDictionary,
            _userDictionaryService,
            ProtectedFullWidthCharacters);
    }

    public void ApplyTranslationSettings(TranslationSettingsViewModel settingsViewModel)
    {
        SelectedProviderOption = ProviderOptions.FirstOrDefault(option =>
            option.ProviderType == settingsViewModel.SelectedProviderOption?.ProviderType)
            ?? SelectedProviderOption;
        _providerApiKeys.Clear();
        foreach (var pair in settingsViewModel.GetProviderApiKeys())
        {
            _providerApiKeys[pair.Key] = pair.Value;
        }
        BaseUrl = settingsViewModel.BaseUrl;
        Model = settingsViewModel.Model;
        LmStudioPresetProfile = settingsViewModel.SelectedLmStudioPresetProfile;
        PromptProfile = settingsViewModel.SelectedPromptProfile;
        SourceLanguage = settingsViewModel.SourceLanguage;
        TargetLanguage = settingsViewModel.TargetLanguage;
        BatchSize = settingsViewModel.BatchSize;
        RetryCount = settingsViewModel.RetryCount;
        Temperature = settingsViewModel.Temperature;
        TopP = settingsViewModel.TopP;
        TopK = settingsViewModel.TopK;
        RepeatPenalty = settingsViewModel.RepeatPenalty;
        PresencePenalty = settingsViewModel.PresencePenalty;
        Seed = settingsViewModel.Seed;
        MaxTokens = settingsViewModel.MaxTokens;
        DisableThinking = settingsViewModel.DisableThinking;
        EnableRequestResponseLogging = settingsViewModel.EnableRequestResponseLogging;
        EnableResultStateLogging = settingsViewModel.EnableResultStateLogging;
        EnableDictionaryHitLogging = settingsViewModel.EnableDictionaryHitLogging;
        ExcludeNonSourceText = settingsViewModel.ExcludeNonSourceText;
        EnableBundledDictionaryFirstPass = settingsViewModel.EnableBundledDictionaryFirstPass;
        EnableKanaTransliterationFallback = settingsViewModel.EnableKanaTransliterationFallback;
        EnableNaverJapaneseDictionaryLookup = settingsViewModel.EnableNaverJapaneseDictionaryLookup;
        EnableKanjiReadingFallback = settingsViewModel.EnableKanjiReadingFallback;
        DictionaryFirstMaxTermLength = settingsViewModel.DictionaryFirstMaxTermLength;
        SystemPromptTemplate = settingsViewModel.SystemPromptTemplate;
        RetryPromptTemplate = settingsViewModel.RetryPromptTemplate;
        PapagoClientId = settingsViewModel.PapagoClientId;
        PapagoClientSecret = settingsViewModel.PapagoClientSecret;
        EzTransInstallationPath = settingsViewModel.EzTransInstallationPath;
        EzTransProcessCount = settingsViewModel.EzTransProcessCount;
        RaisePropertyChanged(nameof(TranslationSettingsSummary));
        PersistConfig();
    }

    public void ApplyUserDictionary(UserDictionaryViewModel dictionaryViewModel)
    {
        _globalUserDictionary = dictionaryViewModel.GetGlobalEntries().ToList();
        _projectUserDictionary = dictionaryViewModel.GetProjectEntries().ToList();
        ProtectedFullWidthCharacters = dictionaryViewModel.ProtectedFullWidthCharacters;
        _userDictionaryService.SaveGlobal(_globalUserDictionary);
        _userDictionaryService.SaveProject(GetProjectDataDirectory(), _projectUserDictionary);
        RaisePropertyChanged(nameof(UserDictionarySummary));
    }

    private ProviderSettings BuildSettings()
    {
        return new ProviderSettings
        {
            ProviderType = SelectedProviderOption?.ProviderType ?? TranslationProviderType.OpenAi,
            BaseUrl = BaseUrl,
            Model = Model,
            ApiKey = _providerApiKeys.GetValueOrDefault(SelectedProviderType, string.Empty),
            PromptProfile = PromptProfile,
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
            ProtectedFullWidthCharacters = ProtectedFullWidthCharacters,
            PapagoClientId = PapagoClientId,
            PapagoClientSecret = PapagoClientSecret,
            EzTransInstallationPath = EzTransInstallationPath,
            EzTransProcessCount = EzTransProcessCount,
        };
    }

    private void ReloadProjectDictionary(string? projectDataDirectory = null)
    {
        _projectUserDictionary = _userDictionaryService.LoadProject(projectDataDirectory ?? GetProjectDataDirectory());
    }

    private bool RestoreLastSessionIfAvailable(string? projectDataDirectory = null)
    {
        projectDataDirectory ??= GetProjectDataDirectory();
        if (string.IsNullOrWhiteSpace(projectDataDirectory) || !Directory.Exists(projectDataDirectory))
        {
            return false;
        }

        var session = _projectStatePersistenceService.LoadScanSession(projectDataDirectory);
        if (session is null)
        {
            return false;
        }

        ApplySession(session, restoreProgress: true);
        RefreshSummaryText();
        StatusText = "마지막 추출 상태를 불러왔습니다.";
        CurrentOperationDetail = $"복원 완료: {session.Documents.Count}개 문서";
        ProgressValue = 1.0;
        return true;
    }

    private void OnProjectPathInputsChanged()
    {
        var projectDataDirectory = GetProjectDataDirectory();
        var projectDataDirectoryChanged = !string.Equals(_activeProjectDataDirectory, projectDataDirectory, StringComparison.OrdinalIgnoreCase);
        if (!_isLoadingConfig && projectDataDirectoryChanged)
        {
            RefreshProjectContext(restoreSession: true, clearSessionWhenMissing: true);
        }

        RaisePropertyChanged(nameof(UserDictionarySummary));
        PersistConfig();
    }

    private void ApplyTeamWorkspacePaths(bool restoreSession)
    {
        if (!IsTeamMode || string.IsNullOrWhiteSpace(TeamProjectId))
        {
            return;
        }

        var context = CreateTeamProjectContext();
        var gameChanged = SetProperty(ref _gameDirectory, context.SourceDirectory, nameof(GameDirectory));
        var outputChanged = SetProperty(ref _outputDirectory, context.TeamOutputDirectory, nameof(OutputDirectory));
        TeamStatusSummary = $"팀 작업 모드: {context.ProjectId} / {context.SourceDirectory}";

        if ((gameChanged || outputChanged) && !_isLoadingConfig)
        {
            RefreshProjectContext(restoreSession, clearSessionWhenMissing: true);
        }

        RaisePropertyChanged(nameof(UserDictionarySummary));
    }

    private TeamProjectContext CreateTeamProjectContext()
    {
        var context = _projectContextFactory.Create(new AppConfig
        {
            ProjectMode = ProjectMode.Team,
            TeamServerUrl = TeamServerUrl,
            TeamProjectId = TeamProjectId,
            TeamDisplayName = TeamDisplayName,
            ClientId = ClientId,
            TeamWorkspaceRoot = TeamWorkspaceRoot,
        });

        return context is TeamProjectContext teamContext
            ? teamContext
            : throw new InvalidOperationException("팀 프로젝트 컨텍스트를 만들 수 없습니다.");
    }

    private bool TryValidateTeamSettings(out string errorMessage)
    {
        if (!IsTeamMode)
        {
            errorMessage = "팀 작업 모드가 아닙니다.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(TeamServerUrl))
        {
            errorMessage = "팀 서버 URL을 입력하세요.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(TeamProjectId))
        {
            errorMessage = "팀 프로젝트 ID를 입력하세요.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            errorMessage = "클라이언트 ID가 비어 있습니다.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(TeamAuthToken))
        {
            errorMessage = "팀 서버 인증 토큰을 입력하세요.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private void EnsureOutputDirectoryDistinctFromGameDirectory()
    {
        if (!IsOutputDirectorySameAsGameDirectory(_outputDirectory))
        {
            return;
        }

        if (SetProperty(ref _outputDirectory, string.Empty))
        {
            StatusText = "출력 폴더는 게임 폴더와 같은 경로로 둘 수 없어 비웠습니다.";
            CurrentOperationDetail = "출력 폴더 충돌을 정리했습니다.";
        }
    }

    private void RefreshProjectContext(bool restoreSession, bool clearSessionWhenMissing)
    {
        var projectDataDirectory = GetProjectDataDirectory();
        _activeProjectDataDirectory = projectDataDirectory;
        ReloadProjectDictionary(projectDataDirectory);
        RaisePropertyChanged(nameof(UserDictionarySummary));

        if (!restoreSession)
        {
            return;
        }

        if (RestoreLastSessionIfAvailable(projectDataDirectory))
        {
            return;
        }

        if (clearSessionWhenMissing)
        {
            ClearCurrentSessionView();
        }
    }

    private void ClearCurrentSessionView()
    {
        if (_session is null && Items.Count == 0)
        {
            return;
        }

        DetachItemStateHandlers(Items);
        _session = null;
        ClearVisibleItemSnapshot();
        Items.ReplaceAll([]);
        SelectedItem = null;
        SummaryText = "아직 스캔 전입니다.";
        StatusText = "저장된 추출 상태가 없는 프로젝트입니다. 새로 추출을 실행하세요.";
        CurrentOperationDetail = "현재 경로에 복원할 추출 상태가 없습니다.";
        ProgressValue = 0;
        RefreshItemsView();
    }

    private void LoadConfig()
    {
        _isLoadingConfig = true;
        try
        {
            var config = _appConfigCoordinator.Load();
            _projectMode = config.ProjectMode;
            _teamServerUrl = config.TeamServerUrl;
            _teamProjectId = config.TeamProjectId;
            _teamDisplayName = config.TeamDisplayName;
            _clientId = string.IsNullOrWhiteSpace(config.ClientId) ? Guid.NewGuid().ToString("N") : config.ClientId;
            _teamWorkspaceRoot = config.TeamWorkspaceRoot;
            _teamAuthToken = config.TeamAuthToken;
            _localGameDirectory = config.GameDirectory;
            _localOutputDirectory = config.OutputDirectory;
            _isManualTeamProjectId = true;
            RaisePropertyChanged(nameof(IsTeamMode));
            RaisePropertyChanged(nameof(IsLocalMode));
            RaisePropertyChanged(nameof(IsManualTeamProjectId));
            RaisePropertyChanged(nameof(CanEditTeamProjectId));
            RaisePropertyChanged(nameof(TeamServerUrl));
            RaisePropertyChanged(nameof(TeamProjectId));
            RaisePropertyChanged(nameof(TeamDisplayName));
            RaisePropertyChanged(nameof(ClientId));
            RaisePropertyChanged(nameof(TeamWorkspaceRoot));
            RaisePropertyChanged(nameof(TeamAuthToken));

            if (IsTeamMode && !string.IsNullOrWhiteSpace(TeamProjectId))
            {
                ApplyTeamWorkspacePaths(restoreSession: false);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(config.GameDirectory))
                {
                    GameDirectory = config.GameDirectory;
                }

                if (!string.IsNullOrWhiteSpace(config.OutputDirectory))
                {
                    OutputDirectory = config.OutputDirectory;
                }
            }

            SelectedSaveMode = EffectiveSaveMode;
            SelectedProviderOption = ProviderOptions.FirstOrDefault(option => option.ProviderType == config.ProviderType)
                ?? SelectedProviderOption;

            if (!string.IsNullOrWhiteSpace(config.BaseUrl))
            {
                BaseUrl = config.BaseUrl;
            }

            if (!string.IsNullOrWhiteSpace(config.Model))
            {
                Model = config.Model;
            }

            LmStudioPresetProfile = config.LmStudioPresetProfile;
            PromptProfile = config.PromptProfile;

            SourceLanguage = string.IsNullOrWhiteSpace(config.SourceLanguage) ? SourceLanguage : config.SourceLanguage;
            TargetLanguage = string.IsNullOrWhiteSpace(config.TargetLanguage) ? TargetLanguage : config.TargetLanguage;
            BatchSize = config.BatchSize;
            RetryCount = config.RetryCount;
            Temperature = config.Temperature;
            TopP = config.TopP;
            TopK = config.TopK;
            RepeatPenalty = config.RepeatPenalty;
            PresencePenalty = config.PresencePenalty;
            Seed = config.Seed;
            MaxTokens = config.MaxTokens;
            DisableThinking = config.DisableThinking;
            EnableRequestResponseLogging = config.EnableRequestResponseLogging;
            EnableResultStateLogging = config.EnableResultStateLogging;
            EnableDictionaryHitLogging = config.EnableDictionaryHitLogging;
            ExcludeNonSourceText = config.ExcludeNonSourceText;
            EnableBundledDictionaryFirstPass = config.EnableBundledDictionaryFirstPass;
            EnableKanaTransliterationFallback = config.EnableKanaTransliterationFallback;
            EnableNaverJapaneseDictionaryLookup = config.EnableNaverJapaneseDictionaryLookup;
            EnableKanjiReadingFallback = config.EnableKanjiReadingFallback;
            DictionaryFirstMaxTermLength = config.DictionaryFirstMaxTermLength;
            RefreshGridDuringTranslatedTextEdit = config.RefreshGridDuringTranslatedTextEdit;
            if (!string.IsNullOrWhiteSpace(config.SystemPromptTemplate))
            {
                SystemPromptTemplate = config.SystemPromptTemplate;
            }

            if (!string.IsNullOrWhiteSpace(config.RetryPromptTemplate))
            {
                RetryPromptTemplate = config.RetryPromptTemplate;
            }

            ProtectedFullWidthCharacters = config.ProtectedFullWidthCharacters;
            PapagoClientId = config.PapagoClientId;
            PapagoClientSecret = config.PapagoClientSecret;
            EzTransInstallationPath = config.EzTransInstallationPath;
            EzTransProcessCount = config.EzTransProcessCount;

            _providerApiKeys.Clear();
            foreach (var pair in config.ProviderApiKeys)
            {
                _providerApiKeys[pair.Key] = pair.Value;
            }
        }
        finally
        {
            _isLoadingConfig = false;
        }
    }

    private void PersistConfig()
    {
        if (_isLoadingConfig)
        {
            return;
        }

        _appConfigCoordinator.ScheduleSave(new AppConfig
        {
            GameDirectory = IsTeamMode ? _localGameDirectory : GameDirectory,
            OutputDirectory = IsTeamMode ? _localOutputDirectory : OutputDirectory,
            SaveMode = EffectiveSaveMode,
            ProjectMode = _projectMode,
            TeamServerUrl = TeamServerUrl,
            TeamProjectId = TeamProjectId,
            TeamDisplayName = TeamDisplayName,
            ClientId = ClientId,
            TeamWorkspaceRoot = TeamWorkspaceRoot,
            TeamAuthToken = TeamAuthToken,
            ProviderType = SelectedProviderType,
            BaseUrl = BaseUrl,
            Model = Model,
            LmStudioPresetProfile = LmStudioPresetProfile,
            PromptProfile = PromptProfile,
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
            EnableResultStateLogging = EnableResultStateLogging,
            EnableDictionaryHitLogging = EnableDictionaryHitLogging,
            ExcludeNonSourceText = ExcludeNonSourceText,
            EnableBundledDictionaryFirstPass = EnableBundledDictionaryFirstPass,
            EnableKanaTransliterationFallback = EnableKanaTransliterationFallback,
            EnableNaverJapaneseDictionaryLookup = EnableNaverJapaneseDictionaryLookup,
            EnableKanjiReadingFallback = EnableKanjiReadingFallback,
            DictionaryFirstMaxTermLength = DictionaryFirstMaxTermLength,
            RefreshGridDuringTranslatedTextEdit = RefreshGridDuringTranslatedTextEdit,
            SystemPromptTemplate = SystemPromptTemplate,
            RetryPromptTemplate = RetryPromptTemplate,
            ProtectedFullWidthCharacters = ProtectedFullWidthCharacters,
            PapagoClientId = PapagoClientId,
            PapagoClientSecret = PapagoClientSecret,
            EzTransInstallationPath = EzTransInstallationPath,
            EzTransProcessCount = EzTransProcessCount,
            ProviderApiKeys = new Dictionary<TranslationProviderType, string>(_providerApiKeys),
        });
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
                RaisePropertyChanged(nameof(TranslationSettingsSummary));
                break;
            case TranslationProviderType.XiaomiMiMo:
                BaseUrl = "https://api.xiaomimimo.com/v1";
                Model = "mimo-v2.5-pro";
                RaisePropertyChanged(nameof(TranslationSettingsSummary));
                break;
            case TranslationProviderType.LmStudio:
                BaseUrl = "http://127.0.0.1:1234/v1";
                Model = "local-model";
                ApplyLmStudioPresetIfEligible(Model, DisableThinking);
                RaisePropertyChanged(nameof(TranslationSettingsSummary));
                break;
            case TranslationProviderType.Lemonade:
                BaseUrl = "http://127.0.0.1:13305/v1";
                Model = "local-model";
                ApplyLmStudioPresetIfEligible(Model, DisableThinking);
                RaisePropertyChanged(nameof(TranslationSettingsSummary));
                break;
            case TranslationProviderType.DeepLFree:
                BaseUrl = "https://api-free.deepl.com/v2/translate";
                break;
            case TranslationProviderType.DeepLPro:
                BaseUrl = "https://api.deepl.com/v2/translate";
                break;
            case TranslationProviderType.Papago:
                BaseUrl = "https://openapi.naver.com/v1/papago/n2mt";
                break;
            case TranslationProviderType.EzTransXp:
                BaseUrl = string.Empty;
                Model = string.Empty;
                break;
        }

        RaisePropertyChanged(nameof(TranslationSettingsSummary));
    }

    private void ApplyLmStudioPresetIfEligible(string? previousModel, bool previousDisableThinking)
    {
        if (SelectedProviderType is not (TranslationProviderType.LmStudio or TranslationProviderType.Lemonade))
        {
            return;
        }

        var previousPreset = LmStudioSamplingDefaults.GetRecommendedPreset(LmStudioPresetProfile, previousModel, previousDisableThinking);
        var currentPreset = LmStudioSamplingDefaults.GetRecommendedPreset(LmStudioPresetProfile, Model, DisableThinking);

        if (Math.Abs(Temperature - 0.3) < 0.0001 || Math.Abs(Temperature - previousPreset.Temperature) < 0.0001)
        {
            Temperature = currentPreset.Temperature;
        }

        ApplyPresetValue(previousPreset.TopP, currentPreset.TopP, TopP, value => TopP = value);
        ApplyPresetValue(previousPreset.TopK, currentPreset.TopK, TopK, value => TopK = value);
        ApplyPresetValue(previousPreset.RepeatPenalty, currentPreset.RepeatPenalty, RepeatPenalty, value => RepeatPenalty = value);
        ApplyPresetValue(previousPreset.PresencePenalty, currentPreset.PresencePenalty, PresencePenalty, value => PresencePenalty = value);
        ApplyPresetValue(
            LmStudioSamplingDefaults.GetRecommendedMaxTokens(LmStudioPresetProfile, previousModel),
            LmStudioSamplingDefaults.GetRecommendedMaxTokens(LmStudioPresetProfile, Model),
            MaxTokens,
            value => MaxTokens = value);
    }

    private static void ApplyPresetValue<T>(T? previousPresetValue, T? currentPresetValue, T? currentValue, Action<T?> assign)
        where T : struct
    {
        if (!currentValue.HasValue || EqualityComparer<T?>.Default.Equals(currentValue, previousPresetValue))
        {
            assign(currentPresetValue);
        }
    }

    private void SaveTranslationProgressSnapshot(string? reason = null, [System.Runtime.CompilerServices.CallerMemberName] string callerName = "")
    {
        if (_session is null)
        {
            LogResultState("PROGRESS_SAVE_SKIPPED", "번역 진행 상태 저장을 건너뜁니다.", new Dictionary<string, string>
            {
                ["Reason"] = ResolveProgressSaveReason(reason, callerName),
                ["Cause"] = "SessionMissing",
            });
            return;
        }

        var projectDataDirectory = GetProjectDataDirectory(_session.GameRoot);
        var persistableCount = Items.Count(item => item.HasPersistableState);
        LogResultState("PROGRESS_SAVE", "번역 진행 상태 스냅샷을 저장합니다.", new Dictionary<string, string>
        {
            ["Reason"] = ResolveProgressSaveReason(reason, callerName),
            ["ProjectDataDirectory"] = projectDataDirectory,
            ["PersistableItemCount"] = persistableCount.ToString(),
            ["IsSavingResults"] = _isSavingResults.ToString(),
        });
        _projectStatePersistenceService.SaveTranslationProgressSnapshot(GetProjectDataDirectory(_session.GameRoot), Items);
        _lastProgressSaveAtUtc = DateTimeOffset.UtcNow;
    }

    private void SaveTranslationProgressSnapshot(bool force, string? reason = null, [System.Runtime.CompilerServices.CallerMemberName] string callerName = "")
    {
        if (!force)
        {
            SaveTranslationProgressSnapshotIfDue(reason, callerName);
            return;
        }

        SaveTranslationProgressSnapshot(reason, callerName);
    }

    private void SaveTranslationProgressSnapshotIfDue(string? reason = null, [System.Runtime.CompilerServices.CallerMemberName] string callerName = "")
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastProgressSaveAtUtc < TimeSpan.FromMilliseconds(750))
        {
            LogResultState("PROGRESS_SAVE_DEBOUNCED", "진행 상태 저장을 debounce로 건너뜁니다.", new Dictionary<string, string>
            {
                ["Reason"] = ResolveProgressSaveReason(reason, callerName),
                ["IsSavingResults"] = _isSavingResults.ToString(),
            });
            return;
        }

        SaveTranslationProgressSnapshot(reason, callerName);
    }

    private void SaveTranslationProgressItems(
        IEnumerable<ExtractedTextItem> changedItems,
        string? reason = null,
        [System.Runtime.CompilerServices.CallerMemberName] string callerName = "")
    {
        if (_session is null)
        {
            LogResultState("PROGRESS_SAVE_SKIPPED", "번역 진행 상태 저장을 건너뜁니다.", new Dictionary<string, string>
            {
                ["Reason"] = ResolveProgressSaveReason(reason, callerName),
                ["Cause"] = "SessionMissing",
            });
            return;
        }

        var distinctChangedItems = changedItems
            .Where(item => item is not null)
            .GroupBy(item => item.SegmentId, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToList();
        if (distinctChangedItems.Count == 0)
        {
            return;
        }

        var projectDataDirectory = GetProjectDataDirectory(_session.GameRoot);
        var upsertItems = distinctChangedItems
            .Where(item => item.HasPersistableState)
            .ToList();
        var deleteIds = distinctChangedItems
            .Where(item => !item.HasPersistableState)
            .Select(item => item.SegmentId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        LogResultState("PROGRESS_SAVE_ITEMS", "변경된 진행 상태 row만 저장합니다.", new Dictionary<string, string>
        {
            ["Reason"] = ResolveProgressSaveReason(reason, callerName),
            ["ProjectDataDirectory"] = projectDataDirectory,
            ["UpsertItemCount"] = upsertItems.Count.ToString(),
            ["DeleteItemCount"] = deleteIds.Count.ToString(),
            ["IsSavingResults"] = _isSavingResults.ToString(),
        });

        if (upsertItems.Count > 0)
        {
            _projectStatePersistenceService.UpsertTranslationProgressItems(projectDataDirectory, upsertItems);
        }

        if (deleteIds.Count > 0)
        {
            _projectStatePersistenceService.DeleteTranslationProgressItems(projectDataDirectory, deleteIds);
        }

        _lastProgressSaveAtUtc = DateTimeOffset.UtcNow;
    }

    private TranslationProgressCarryoverResult ApplySession(
        ScanSession session,
        bool restoreProgress,
        ScanSession? previousSession = null,
        TranslationProgressState? previousProgress = null)
    {
        _session = session;
        DetachItemStateHandlers(Items);
        ClearVisibleItemSnapshot();
        var restoreResult = new TranslationProgressCarryoverResult(0, 0, session.Items.Count);
        _suppressItemStatePersistence = true;
        try
        {
            using (ItemsView.DeferRefresh())
            {
                Items.ReplaceAll(OrderItemsForDisplay(session.Items));
            }

            if (restoreProgress)
            {
                var exactRestoredCount = _projectStatePersistenceService.ApplyTranslationProgress(GetProjectDataDirectory(session.GameRoot), Items);
                restoreResult = new TranslationProgressCarryoverResult(
                    exactRestoredCount,
                    0,
                    Math.Max(0, Items.Count - exactRestoredCount));
            }
            else if (previousSession is not null)
            {
                restoreResult = _translationProgressCarryoverService.Apply(previousSession, previousProgress, Items);
            }

            if (restoreProgress)
            {
                new SymbolReferenceAnalyzer().Analyze(session);
            }

            _sourceLanguageFilterService.Apply(Items, SourceLanguage, TargetLanguage, ExcludeNonSourceText);
        }
        finally
        {
            _suppressItemStatePersistence = false;
        }

        AttachItemStateHandlers(Items);
        RefreshSummaryText();
        RefreshItemsView();
        return restoreResult;
    }

    private sealed record ScanExecutionResult(
        ScanSession Session,
        ScanSession? PreviousSession,
        TranslationProgressState PreviousProgress);

    private void AttachItemStateHandlers(IEnumerable<ExtractedTextItem> items)
    {
        foreach (var item in items)
        {
            item.PropertyChanged += ItemOnPropertyChanged;
        }
    }

    private void DetachItemStateHandlers(IEnumerable<ExtractedTextItem> items)
    {
        foreach (var item in items)
        {
            item.PropertyChanged -= ItemOnPropertyChanged;
        }
    }

    private void ItemOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressItemStatePersistence || sender is not ExtractedTextItem item)
        {
            return;
        }

        if (MatchesPropertyChange(e, nameof(ExtractedTextItem.Status))
            || MatchesPropertyChange(e, nameof(ExtractedTextItem.ValidationStatus))
            || MatchesPropertyChange(e, nameof(ExtractedTextItem.WarningText))
            || MatchesPropertyChange(e, nameof(ExtractedTextItem.TranslationError)))
        {
            RefreshSummaryText();
        }

        if (MatchesPropertyChange(e, nameof(ExtractedTextItem.ManualStatusOverrideVersion)))
        {
            HandleManualStatusEdited(item);
            return;
        }
    }

    private void HandleManualStatusEdited(ExtractedTextItem item)
    {
        if (_handlingManualStatusOverride)
        {
            return;
        }

        var groupItems = GetItemsWithSameOriginalText(item);
        _handlingManualStatusOverride = true;
        _suppressItemStatePersistence = true;
        try
        {
            foreach (var groupItem in groupItems)
            {
                if (ReferenceEquals(groupItem, item))
                {
                    continue;
                }

                groupItem.ApplyManualStatusOverride(item.Status);
            }
        }
        finally
        {
            _suppressItemStatePersistence = false;
            _handlingManualStatusOverride = false;
        }

        LogResultState(
            "ITEM_MANUAL_STATUS_CHANGED",
            "수동 상태 변경을 동일 원문 항목에 전파하고 저장합니다.",
            new Dictionary<string, string>
            {
                ["SegmentId"] = item.SegmentId,
                ["Status"] = item.Status,
                ["ValidationStatus"] = item.ValidationStatus,
                ["GroupItemCount"] = groupItems.Count.ToString(),
                ["IsSavingResults"] = _isSavingResults.ToString(),
            });
        SaveTranslationProgressItems(groupItems, $"HandleManualStatusEdited:{item.SegmentId}:{item.Status}");
    }

    private void RefreshSummaryText()
    {
        if (_session is null)
        {
            SummaryText = "아직 스캔 전입니다.";
            return;
        }

        SummaryText =
            $"문서 {_session.Metrics.GetValueOrDefault("Documents")}개, " +
            $"항목 {_session.Metrics.GetValueOrDefault("Items")}개, " +
            $"ERB {_session.Metrics.GetValueOrDefault("ErbItems")}개, " +
            $"CSV {_session.Metrics.GetValueOrDefault("CsvItems")}개, " +
            $"경고 {GetCurrentWarningCount()}건, " +
            $"조사 패턴 {_session.Metrics.GetValueOrDefault("JosaPatterns")}건";
    }

    private int GetCurrentWarningCount()
    {
        return Items.Count(static item =>
            !string.IsNullOrWhiteSpace(item.WarningText)
            || item.Status is "검수 필요" or "번역 실패");
    }

    private async Task<bool> RunBusyAsync(
        string startingMessage,
        Func<CancellationToken, Task> action,
        string cancelStatusText = "작업이 취소되었습니다.",
        string cancelDetailText = "사용자 요청으로 작업이 중단되었습니다.",
        Func<string>? cancelDetailFactory = null,
        Action? onCanceled = null,
        Action<Exception>? onFailed = null)
    {
        if (IsBusy)
        {
            return false;
        }

        using var cancellationTokenSource = new CancellationTokenSource();
        _cancellationTokenSource = cancellationTokenSource;
        IsBusy = true;
        StatusText = startingMessage;
        CurrentOperationDetail = startingMessage;

        try
        {
            await action(cancellationTokenSource.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            onCanceled?.Invoke();
            SaveTranslationProgressSnapshot(force: true, reason: "RunBusyAsync canceled");
            StatusText = cancelStatusText;
            CurrentOperationDetail = cancelDetailFactory?.Invoke() ?? cancelDetailText;
            return false;
        }
        catch (Exception ex)
        {
            onFailed?.Invoke(ex);
            StatusText = $"작업 실패: {ex.Message}";
            CurrentOperationDetail = "오류가 발생해 작업을 중단했습니다.";
            return false;
        }
        finally
        {
            IsBusy = false;
            _cancellationTokenSource = null;
        }
    }

    private bool FilterItem(object item)
    {
        if (item is not ExtractedTextItem textItem)
        {
            return false;
        }

        if (!_buildingVisibleItemSnapshot && _visibleItemSnapshot is not null)
        {
            return _visibleItemSnapshot.Contains(textItem.SegmentId);
        }

        return EvaluateFilterItem(textItem);
    }

    private bool EvaluateFilterItem(ExtractedTextItem textItem)
    {
        var document = _session?.Documents.GetValueOrDefault(textItem.DocumentId);

        if (SelectedFileTypeFilter != "전체" && !textItem.FileType.Equals(SelectedFileTypeFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (SelectedStatusFilter != "전체"
            && !textItem.StateText.Contains(SelectedStatusFilter, StringComparison.Ordinal))
        {
            return false;
        }

        if (WarningsOnly && string.IsNullOrWhiteSpace(textItem.WarningText))
        {
            return false;
        }

        if (string.Equals(SelectedSearchFieldFilter, "함수/표현식", StringComparison.Ordinal))
        {
            if (!IsFunctionOrExpressionTranslation(textItem, document))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(FilterText)
                || IsFilterMatch(textItem.OriginalText)
                || IsFilterMatch(textItem.TranslatedText)
                || IsFilterMatch(textItem.RelativePath)
                || IsFilterMatch(textItem.SegmentType);
        }

        if (string.IsNullOrWhiteSpace(FilterText))
        {
            return true;
        }

        return SelectedSearchFieldFilter switch
        {
            "파일" => IsFilterMatch(textItem.RelativePath),
            "원문" => IsFilterMatch(textItem.OriginalText),
            "번역문" => IsFilterMatch(textItem.TranslatedText),
            "참조 상태" => IsFilterMatch(textItem.ReferenceResolutionStatus),
            _ => IsFilterMatch(textItem.RelativePath)
                || IsFilterMatch(textItem.OriginalText)
                || IsFilterMatch(textItem.TranslatedText)
                || IsFilterMatch(textItem.SourceKey)
                || IsFilterMatch(textItem.SymbolNamespace)
                || IsFilterMatch(textItem.OriginalSymbolKey)
                || IsFilterMatch(textItem.TranslatedSymbolKey)
                || IsFilterMatch(textItem.ReferenceResolutionStatus)
                || IsFilterMatch(document?.JosaAnalysis.SyntaxType)
                || IsFilterMatch(document?.JosaAnalysis.ErhLinkStatus)
                || IsFilterMatch(document?.JosaAnalysis.PackageCompatibilityStatus),
        };
    }

    private static bool IsFunctionOrExpressionTranslation(ExtractedTextItem textItem, SourceFileDocument? document)
    {
        if (!DocumentFileTypes.IsErbLike(textItem.FileType))
        {
            return false;
        }

        if (textItem.SegmentType is "assignment-fragment" or "inline-conditional-left" or "inline-conditional-right")
        {
            return true;
        }

        if (document is null)
        {
            return false;
        }

        var segment = document.Segments.FirstOrDefault(segment => string.Equals(segment.SegmentId, textItem.SegmentId, StringComparison.Ordinal));
        if (segment is null)
        {
            return false;
        }

        return IsSegmentInsideErbExpression(document.OriginalText, segment.AbsoluteStart);
    }

    private static bool IsSegmentInsideErbExpression(string content, int absoluteStart)
    {
        if (absoluteStart < 0 || absoluteStart > content.Length)
        {
            return false;
        }

        var lineStart = content.LastIndexOf('\n', Math.Max(0, absoluteStart - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var lineEnd = content.IndexOf('\n', absoluteStart);
        lineEnd = lineEnd < 0 ? content.Length : lineEnd;
        var line = content[lineStart..lineEnd].TrimEnd('\r');
        var relativeStart = Math.Clamp(absoluteStart - lineStart, 0, line.Length);
        var before = line[..relativeStart];

        if (IsInsideDelimitedExpression(before, '%', '%') || IsInsideDelimitedExpression(before, '{', '}'))
        {
            return true;
        }

        return IsInsideFunctionCall(before);
    }

    private static bool IsInsideDelimitedExpression(string before, char open, char close)
    {
        var quote = false;
        var balance = 0;
        foreach (var ch in before)
        {
            if (ch == '"')
            {
                quote = !quote;
                continue;
            }

            if (quote)
            {
                continue;
            }

            if (ch == open)
            {
                balance++;
            }
            else if (ch == close && balance > 0)
            {
                balance--;
            }
        }

        return balance > 0;
    }

    private static bool IsInsideFunctionCall(string before)
    {
        var quote = false;
        var parenDepth = 0;
        var lastOpenParen = -1;
        for (var index = 0; index < before.Length; index++)
        {
            var ch = before[index];
            if (ch == '"')
            {
                quote = !quote;
                continue;
            }

            if (quote)
            {
                continue;
            }

            if (ch == '(')
            {
                parenDepth++;
                lastOpenParen = index;
            }
            else if (ch == ')' && parenDepth > 0)
            {
                parenDepth--;
            }
        }

        if (parenDepth <= 0 || lastOpenParen <= 0)
        {
            return false;
        }

        var functionNameEnd = lastOpenParen - 1;
        while (functionNameEnd >= 0 && char.IsWhiteSpace(before[functionNameEnd]))
        {
            functionNameEnd--;
        }

        var functionNameStart = functionNameEnd;
        while (functionNameStart >= 0 && (char.IsLetterOrDigit(before[functionNameStart]) || before[functionNameStart] == '_'))
        {
            functionNameStart--;
        }

        return functionNameEnd > functionNameStart;
    }

    private void RebuildVisibleItemSnapshot()
    {
        _buildingVisibleItemSnapshot = true;
        try
        {
            _visibleItemSnapshot = Items
                .Where(EvaluateFilterItem)
                .Select(item => item.SegmentId)
                .ToHashSet(StringComparer.Ordinal);
        }
        finally
        {
            _buildingVisibleItemSnapshot = false;
        }
    }

    private void ClearVisibleItemSnapshot()
    {
        _visibleItemSnapshot = null;
    }

    private List<ExtractedTextItem> GetCurrentTranslationScope()
    {
        return ItemsView.Cast<ExtractedTextItem>().ToList();
    }

    private async Task TranslatePendingPhasesAsync(
        IReadOnlyList<TranslationPhasePlan> phasePlans,
        int totalPendingCount,
        ProviderSettings settings,
        IReadOnlyList<UserDictionaryEntry> dictionaryEntries,
        IProgress<(double value, string status, string detail)> progress,
        CancellationToken cancellationToken)
    {
        if (phasePlans.Count == 0)
        {
            return;
        }

        var overallProcessedCount = 0;
        foreach (var phasePlan in phasePlans)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (phasePlan.PendingCount == 0)
            {
                continue;
            }

            var glossaryHints = _phaseScopedGlossaryBuilder.BuildForPhase(Items, phasePlan.Kind);
            var phaseLabel = GetTranslationPhaseLabel(phasePlan.Kind);
            CurrentOperationDetail = glossaryHints.Count > 0
                ? $"{phaseLabel} 단계 번역 준비 중 | glossary {glossaryHints.Count}개"
                : $"{phaseLabel} 단계 번역 준비 중";

            var phaseProgress = new Progress<(double value, string status, string detail)>(tuple =>
            {
                var overallValue = totalPendingCount == 0
                    ? 1.0
                    : (overallProcessedCount + (tuple.value * phasePlan.PendingCount)) / totalPendingCount;
                ProgressValue = overallValue;
                StatusText = tuple.status;
                var detailPrefix = glossaryHints.Count > 0
                    ? $"{phaseLabel} | glossary {glossaryHints.Count}개"
                    : phaseLabel;
                var combinedDetail = string.IsNullOrWhiteSpace(tuple.detail)
                    ? detailPrefix
                    : $"{detailPrefix} | {tuple.detail}";
                CurrentOperationDetail = BuildTranslationProgressDetail(combinedDetail, overallValue);
            });

                await _translationCoordinator.TranslateAsync(
                    phasePlan.Items,
                    Items.ToList(),
                    settings,
                    dictionaryEntries,
                    glossaryHints,
                    phaseProgress,
                    changedItems => SaveTranslationProgressItems(changedItems, $"TranslatePendingAsync {phaseLabel} progress callback"),
                    cancellationToken);

            overallProcessedCount += phasePlan.PendingCount;
            SaveTranslationProgressSnapshot(force: true, reason: $"TranslatePendingAsync {phaseLabel} completed");
        }
    }

    private static List<TranslationPhasePlan> BuildTranslationPhasePlans(IReadOnlyList<ExtractedTextItem> translationScope)
    {
        return new[]
        {
            CreatePhasePlan(translationScope, TranslationPhaseKind.CsvReferenceKeys),
            CreatePhasePlan(translationScope, TranslationPhaseKind.CsvGeneral),
            CreatePhasePlan(translationScope, TranslationPhaseKind.ErbIdentifiers),
            CreatePhasePlan(translationScope, TranslationPhaseKind.Erh),
            CreatePhasePlan(translationScope, TranslationPhaseKind.Erb),
        }
        .Where(static plan => plan.Items.Count > 0)
        .ToList();
    }

    private static TranslationPhasePlan CreatePhasePlan(
        IReadOnlyList<ExtractedTextItem> translationScope,
        TranslationPhaseKind kind)
    {
        var items = translationScope
            .Where(item => GetTranslationPhaseForItem(item) == kind)
            .OrderBy(GetTranslationPhaseSortOrder)
            .ThenBy(GetCsvNamespaceSortOrder)
            .ThenBy(item => item.RelativePath, StringComparer.Ordinal)
            .ThenBy(item => item.LineNumber)
            .ThenBy(item => item.SegmentId, StringComparer.Ordinal)
            .ToList();
        return new TranslationPhasePlan(kind, items, items.Count(item => item.NeedsTranslation));
    }

    private static string GetTranslationPhaseLabel(TranslationPhaseKind kind)
    {
        return kind switch
        {
            TranslationPhaseKind.CsvReferenceKeys => "CSV-참조키",
            TranslationPhaseKind.CsvGeneral => "CSV-일반",
            TranslationPhaseKind.ErbIdentifiers => "ERB-식별자",
            TranslationPhaseKind.Erh => "ERH",
            TranslationPhaseKind.Erb => "ERB",
            _ => "번역",
        };
    }

    private static IOrderedEnumerable<ExtractedTextItem> OrderItemsForDisplay(IEnumerable<ExtractedTextItem> items)
    {
        return items
            .OrderBy(GetTranslationPhaseSortOrder)
            .ThenBy(GetCsvNamespaceSortOrder)
            .ThenBy(item => item.RelativePath, StringComparer.Ordinal)
            .ThenBy(item => item.LineNumber)
            .ThenBy(item => item.SegmentId, StringComparer.Ordinal);
    }

    private static TranslationPhaseKind GetTranslationPhaseForItem(ExtractedTextItem item)
    {
        if (DocumentFileTypes.IsCsvLike(item.FileType))
        {
            return item.IsReferenceBearingKey
                ? TranslationPhaseKind.CsvReferenceKeys
                : TranslationPhaseKind.CsvGeneral;
        }

        if (IdentifierSegmentTypes.IsIdentifier(item.SegmentType))
        {
            return TranslationPhaseKind.ErbIdentifiers;
        }

        if (string.Equals(item.FileType, "ERH", StringComparison.OrdinalIgnoreCase))
        {
            return TranslationPhaseKind.Erh;
        }

        return TranslationPhaseKind.Erb;
    }

    private static int GetTranslationPhaseSortOrder(ExtractedTextItem item)
    {
        return GetTranslationPhaseForItem(item) switch
        {
            TranslationPhaseKind.CsvReferenceKeys => 0,
            TranslationPhaseKind.CsvGeneral => 1,
            TranslationPhaseKind.ErbIdentifiers => 2,
            TranslationPhaseKind.Erh => 3,
            TranslationPhaseKind.Erb => 4,
            _ => int.MaxValue,
        };
    }

    private static int GetCsvNamespaceSortOrder(ExtractedTextItem item)
    {
        if (!DocumentFileTypes.IsCsvLike(item.FileType))
        {
            return 0;
        }

        var fileStem = Path.GetFileNameWithoutExtension(item.RelativePath);
        if (TryGetBuiltInCsvNamespaceOrder(item.SymbolNamespace, out var namespaceOrder))
        {
            return namespaceOrder + GetCsvNamespaceSourceOffset(item, fileStem, item.SymbolNamespace);
        }

        if (TryGetBuiltInCsvNamespaceOrder(fileStem, out namespaceOrder))
        {
            return namespaceOrder;
        }

        if (IsCharacterSheetItem(item, fileStem))
        {
            return 900;
        }

        return item.IsReferenceBearingKey ? 300 : 500;
    }

    private static int GetCsvNamespaceSourceOffset(ExtractedTextItem item, string? fileStem, string symbolNamespace)
    {
        if (IsBuiltInNamespaceFile(fileStem, symbolNamespace))
        {
            return 0;
        }

        return IsCharacterSheetItem(item, fileStem) ? 220 : 5;
    }

    private static bool TryGetBuiltInCsvNamespaceOrder(string? value, out int order)
    {
        switch (SymbolNamespaceRegistry.CanonicalizeNamespace(value ?? string.Empty))
        {
            case "BASE":
            case "MAXBASE":
            case "DOWNBASE":
                order = 0;
                return true;
            case "CFLAG":
                order = 10;
                return true;
            case "TFLAG":
                order = 20;
                return true;
            case "FLAG":
                order = 30;
                return true;
            case "TALENT":
                order = 40;
                return true;
            case "ABL":
                order = 50;
                return true;
            case "EXP":
                order = 60;
                return true;
            case "MARK":
                order = 70;
                return true;
            case "PALAM":
            case "JUEL":
                order = 80;
                return true;
            case "CUP":
            case "CDOWN":
                order = 90;
                return true;
            case "SOURCE":
                order = 100;
                return true;
            case "TEQUIP":
                order = 110;
                return true;
            case "NOWEX":
            case "EX":
                order = 120;
                return true;
            case "ITEM":
            case "ITEMPRICE":
                order = 130;
                return true;
            case "CSTR":
            case "STR":
            case "SAVESTR":
            case "CALLNAME":
            case "TCVAR":
                order = 140;
                return true;
            default:
                order = 0;
                return false;
        }
    }

    private static bool IsCharacterSheetItem(ExtractedTextItem item, string? fileStem)
    {
        return item.SegmentType.StartsWith("csv-CharacterSheet", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(fileStem)
                && fileStem.StartsWith("Chara", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsBuiltInNamespaceFile(string? fileStem, string symbolNamespace)
    {
        if (string.IsNullOrWhiteSpace(fileStem))
        {
            return false;
        }

        var canonicalStem = SymbolNamespaceRegistry.CanonicalizeNamespace(fileStem);
        var canonicalNamespace = SymbolNamespaceRegistry.CanonicalizeNamespace(symbolNamespace);
        return canonicalNamespace switch
        {
            "MAXBASE" or "DOWNBASE" => canonicalStem == "BASE",
            "ITEMPRICE" => canonicalStem == "ITEM",
            "CDOWN" => canonicalStem == "PALAM",
            _ => canonicalStem == canonicalNamespace,
        };
    }

    private bool IsFilterMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(FilterText))
        {
            return true;
        }

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (!UseRegexFilter)
        {
            return value.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
        }

        try
        {
            return Regex.IsMatch(value, FilterText, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryReplaceText(string source, string searchPattern, string replaceText, bool useRegex, out string replaced)
    {
        replaced = source;
        if (string.IsNullOrEmpty(searchPattern))
        {
            return false;
        }

        if (useRegex)
        {
            replaced = Regex.Replace(source, searchPattern, replaceText, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return true;
        }

        if (!source.Contains(searchPattern, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        replaced = source.Replace(searchPattern, replaceText, StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private static bool TryApplyBulkTranslatedTextChange(ExtractedTextItem item, string transformedText)
    {
        if (string.Equals(item.TranslatedText, transformedText, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(transformedText))
        {
            item.ResetTranslationState();
            return true;
        }

        var reviewReason = TranslationQualityRules.GetReviewReason(item.OriginalText, transformedText);
        item.ApplyTranslationState(
            reviewReason is null ? "수동 수정" : "검수 필요",
            "통과",
            reviewReason ?? string.Empty,
            canSave: true,
            transformedText);
        return true;
    }

    private IEnumerable<ProviderOption> BuildProviderOptions()
    {
        var ezTransInfo = _ezTransXpInstallationService.Detect();
        yield return new ProviderOption
        {
            ProviderType = TranslationProviderType.OpenAi,
            DisplayName = "OpenAI API",
            IsAvailable = true,
        };
        yield return new ProviderOption
        {
            ProviderType = TranslationProviderType.XiaomiMiMo,
            DisplayName = "Xiaomi MiMo API",
            IsAvailable = true,
        };
        yield return new ProviderOption
        {
            ProviderType = TranslationProviderType.LmStudio,
            DisplayName = "LM Studio",
            IsAvailable = true,
        };
        yield return new ProviderOption
        {
            ProviderType = TranslationProviderType.Lemonade,
            DisplayName = "Lemonade",
            IsAvailable = true,
        };
        yield return new ProviderOption
        {
            ProviderType = TranslationProviderType.DeepLFree,
            DisplayName = "DeepL API Free",
            IsAvailable = true,
        };
        yield return new ProviderOption
        {
            ProviderType = TranslationProviderType.DeepLPro,
            DisplayName = "DeepL API Pro",
            IsAvailable = true,
        };
        yield return new ProviderOption
        {
            ProviderType = TranslationProviderType.Papago,
            DisplayName = "Papago API",
            IsAvailable = true,
        };
        yield return new ProviderOption
        {
            ProviderType = TranslationProviderType.EzTransXp,
            DisplayName = "EzTransXP",
            IsAvailable = true,
            AvailabilityText = ezTransInfo.IsAvailable ? "설치됨" : "설치 미감지",
        };
    }

    private static string? TryFindSampleDirectory()
    {
        var current = AppContext.BaseDirectory;
        for (var depth = 0; depth < 6 && current is not null; depth++)
        {
            var candidate = Path.Combine(current, "sample", "era魔界牧場1.050");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        var workspaceCandidate = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "sample", "era魔界牧場1.050"));
        return Directory.Exists(workspaceCandidate) ? workspaceCandidate : null;
    }

    private void SelectedItemOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (MatchesPropertyChange(e, nameof(ExtractedTextItem.OriginalText)))
        {
            RaisePropertyChanged(nameof(SelectedItemOriginalPreviewText));
        }

        if (MatchesPropertyChange(e, nameof(ExtractedTextItem.TranslatedText)))
        {
            SyncSelectedItemTranslatedTextEditor(preserveDirtyIfTextMatches: true);
        }

        if (MatchesPropertyChange(e, nameof(ExtractedTextItem.Status))
            || MatchesPropertyChange(e, nameof(ExtractedTextItem.ValidationStatus))
            || MatchesPropertyChange(e, nameof(ExtractedTextItem.TranslationError))
            || MatchesPropertyChange(e, nameof(ExtractedTextItem.WarningText))
            || MatchesPropertyChange(e, nameof(ExtractedTextItem.StateText))
            || MatchesPropertyChange(e, nameof(ExtractedTextItem.TranslatedText)))
        {
            RaisePropertyChanged(nameof(SelectedItemLogText));
        }
    }

    private static bool MatchesPropertyChange(PropertyChangedEventArgs e, string propertyName)
    {
        return string.IsNullOrEmpty(e.PropertyName)
            || string.Equals(e.PropertyName, propertyName, StringComparison.Ordinal);
    }

    private void SyncSelectedItemPreview()
    {
        RaisePropertyChanged(nameof(SelectedItemOriginalPreviewText));
        SyncSelectedItemTranslatedTextEditor();
    }

    private void SyncSelectedItemTranslatedTextEditor(bool preserveDirtyIfTextMatches = false)
    {
        var previousText = _selectedItemTranslatedTextEditor;
        var wasDirty = _selectedItemTranslatedTextEditorDirty;
        _syncingSelectedItemTranslatedTextEditor = true;
        try
        {
            SelectedItemTranslatedTextEditor = SelectedItem?.TranslatedText ?? string.Empty;
            _selectedItemTranslatedTextEditorDirty =
                preserveDirtyIfTextMatches
                && wasDirty
                && string.Equals(previousText, SelectedItemTranslatedTextEditor, StringComparison.Ordinal);
        }
        finally
        {
            _syncingSelectedItemTranslatedTextEditor = false;
        }
    }

    private void RequestItemsViewRefresh()
    {
        RebuildVisibleItemSnapshot();
        if (TryRefreshItemsViewNow())
        {
            return;
        }

        QueueItemsViewRefresh();
    }

    private bool TryRefreshItemsViewNow()
    {
        if (ItemsView is IEditableCollectionView editableCollectionView
            && (editableCollectionView.IsAddingNew || editableCollectionView.IsEditingItem))
        {
            return false;
        }

        ItemsView.Refresh();
        _itemsViewRefreshQueued = false;
        return true;
    }

    private void QueueItemsViewRefresh()
    {
        if (_itemsViewRefreshQueued)
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            try
            {
                ItemsView.Refresh();
                _itemsViewRefreshQueued = false;
            }
            catch (InvalidOperationException)
            {
                // Unit tests can run without an Application dispatcher while a collection edit is still open.
            }
            return;
        }

        _itemsViewRefreshQueued = true;
        Action? refreshAction = null;
        refreshAction = () =>
        {
            RebuildVisibleItemSnapshot();
            if (TryRefreshItemsViewNow())
            {
                return;
            }

            dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, refreshAction!);
        };

        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, refreshAction);
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, refreshAction);
    }

    internal string BuildTranslationProgressDetail(string? detail, double progressValue)
    {
        var baseDetail = string.IsNullOrWhiteSpace(detail) ? "번역 진행 중" : $"현재 파일: {detail}";
        var elapsedTimeText = FormatDuration(_translationProgressStopwatch.Elapsed);
        var remainingTimeText = TryFormatRemainingTime(progressValue, _translationProgressStopwatch.Elapsed);
        return string.IsNullOrWhiteSpace(remainingTimeText)
            ? $"{baseDetail} | 경과 시간: {elapsedTimeText}"
            : $"{baseDetail} | 경과 시간: {elapsedTimeText} | 예상 남은 시간: {remainingTimeText}";
    }

    internal static string? TryFormatRemainingTime(double progressValue, TimeSpan elapsed)
    {
        if (progressValue <= 0
            || progressValue >= 1
            || elapsed <= TimeSpan.Zero)
        {
            return null;
        }

        var estimatedTotalTicks = elapsed.Ticks / progressValue;
        var remainingTicks = estimatedTotalTicks - elapsed.Ticks;
        if (remainingTicks <= 0)
        {
            return null;
        }

        return FormatDuration(TimeSpan.FromTicks((long)Math.Ceiling(remainingTicks)));
    }

    internal static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            var hours = (int)duration.TotalHours;
            var minutes = duration.Minutes;
            return minutes > 0 ? $"{hours}시간 {minutes}분" : $"{hours}시간";
        }

        if (duration.TotalMinutes >= 1)
        {
            var minutes = (int)duration.TotalMinutes;
            var seconds = duration.Seconds;
            return seconds > 0 ? $"{minutes}분 {seconds}초" : $"{minutes}분";
        }

        return $"{Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds))}초";
    }

    private void StartTranslationTiming()
    {
        if (_resumeTranslationTimingOnNextRun && _translationProgressStopwatch.Elapsed > TimeSpan.Zero)
        {
            _translationProgressStopwatch.Start();
        }
        else
        {
            _translationProgressStopwatch.Restart();
        }

        _resumeTranslationTimingOnNextRun = false;
    }

    private void StopTranslationTiming(bool resumeOnNextRun)
    {
        _translationProgressStopwatch.Stop();
        _resumeTranslationTimingOnNextRun = resumeOnNextRun;
    }

    private void ApplySourceLanguageFilter(bool persistProgress = true)
    {
        if (_session is null)
        {
            return;
        }

        _suppressItemStatePersistence = true;
        try
        {
            _sourceLanguageFilterService.Apply(Items, SourceLanguage, TargetLanguage, ExcludeNonSourceText);
        }
        finally
        {
            _suppressItemStatePersistence = false;
        }

        RefreshItemsView();
        if (persistProgress)
        {
            SaveTranslationProgressSnapshot("ApplySourceLanguageFilter");
        }
    }

    private string GetProjectDataDirectory(string? fallbackGameDirectory = null)
    {
        if (IsTeamMode && !string.IsNullOrWhiteSpace(TeamProjectId))
        {
            return CreateTeamProjectContext().TeamProjectDataDirectory;
        }

        if (EffectiveSaveMode == SaveMode.ExportCopy && !string.IsNullOrWhiteSpace(OutputDirectory))
        {
            return OutputDirectory;
        }

        return !string.IsNullOrWhiteSpace(GameDirectory)
            ? GameDirectory
            : fallbackGameDirectory ?? string.Empty;
    }

    private void LogResultState(string category, string message, IReadOnlyDictionary<string, string>? fields = null)
    {
        if (!EnableResultStateLogging)
        {
            return;
        }

        _resultStateLogger.Log(category, message, fields);
    }

    private static string ResolveProgressSaveReason(string? reason, string callerName)
    {
        return string.IsNullOrWhiteSpace(reason) ? callerName : reason;
    }

    private bool IsOutputDirectorySameAsGameDirectory(string? outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory)
            || string.IsNullOrWhiteSpace(GameDirectory))
        {
            return false;
        }

        return string.Equals(
            NormalizeDirectoryPath(outputDirectory),
            NormalizeDirectoryPath(GameDirectory),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectoryPath(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private sealed record TranslationPhasePlan(
        TranslationPhaseKind Kind,
        IReadOnlyList<ExtractedTextItem> Items,
        int PendingCount);

    private sealed class ExtractedTextItemPriorityComparer : IComparer
    {
        public static ExtractedTextItemPriorityComparer Instance { get; } = new();

        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is not ExtractedTextItem left)
            {
                return -1;
            }

            if (y is not ExtractedTextItem right)
            {
                return 1;
            }

            var phaseComparison = GetTranslationPhaseSortOrder(left).CompareTo(GetTranslationPhaseSortOrder(right));
            if (phaseComparison != 0)
            {
                return phaseComparison;
            }

            var csvNamespaceComparison = GetCsvNamespaceSortOrder(left).CompareTo(GetCsvNamespaceSortOrder(right));
            if (csvNamespaceComparison != 0)
            {
                return csvNamespaceComparison;
            }

            var pathComparison = StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath);
            if (pathComparison != 0)
            {
                return pathComparison;
            }

            var lineComparison = left.LineNumber.CompareTo(right.LineNumber);
            if (lineComparison != 0)
            {
                return lineComparison;
            }

            return StringComparer.Ordinal.Compare(left.SegmentId, right.SegmentId);
        }
    }
}
