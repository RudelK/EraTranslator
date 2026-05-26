using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Data;
using EraTranslator.Services;

namespace EraTranslator.ViewModels;

public sealed class MainWindowViewModel : BindableBase, IDisposable
{
    private readonly FileScanner _fileScanner;
    private readonly TranslationCoordinator _translationCoordinator;
    private readonly OutputWriter _outputWriter;
    private readonly DebouncedAppConfigCoordinator _appConfigCoordinator;
    private readonly UserDictionaryService _userDictionaryService;
    private readonly ProjectStatePersistenceService _projectStatePersistenceService;
    private readonly TranslationProgressCarryoverService _translationProgressCarryoverService;
    private readonly TranslationTextExchangeService _translationTextExchangeService;
    private readonly SourceLanguageFilterService _sourceLanguageFilterService;
    private readonly EzTransXpInstallationService _ezTransXpInstallationService;
    private readonly Dictionary<TranslationProviderType, string> _providerApiKeys = [];
    private ScanSession? _session;
    private CancellationTokenSource? _cancellationTokenSource;
    private List<UserDictionaryEntry> _globalUserDictionary = [];
    private List<UserDictionaryEntry> _projectUserDictionary = [];
    private bool _isLoadingConfig;
    private string _gameDirectory = string.Empty;
    private string _outputDirectory = string.Empty;
    private string _statusText = "게임 디렉토리를 지정한 뒤 텍스트를 추출하세요.";
    private string _summaryText = "아직 스캔 전입니다.";
    private string _currentOperationDetail = "대기 중";
    private double _progressValue;
    private ExtractedTextItem? _selectedItem;
    private bool _warningsOnly;
    private bool _isBusy;
    private string _filterText = string.Empty;
    private bool _useRegexFilter;
    private string _selectedFileTypeFilter = "전체";
    private string _selectedStatusFilter = "전체";
    private SaveMode _selectedSaveMode = SaveMode.ExportCopy;
    private ProviderOption? _selectedProviderOption;
    private string _baseUrl = "https://api.openai.com/v1";
    private string _model = "gpt-4o-mini";
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
    private string _ezTransInstallationPath = string.Empty;
    private int _ezTransProcessCount = 1;
    private bool _suppressItemStatePersistence;
    private DateTimeOffset _lastProgressSaveAtUtc = DateTimeOffset.MinValue;
    private string _activeProjectDataDirectory = string.Empty;
    private readonly Stopwatch _translationProgressStopwatch = new();
    private bool _resumeTranslationTimingOnNextRun;

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
        TranslationProgressStateService? translationProgressStateService = null,
        EzTransXpInstallationService? ezTransXpInstallationService = null,
        bool detectSampleDirectory = true,
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
            translationProgressStateService ?? new TranslationProgressStateService());
        _translationProgressCarryoverService = translationProgressCarryoverService ?? new TranslationProgressCarryoverService();
        _translationTextExchangeService = translationTextExchangeService ?? new TranslationTextExchangeService();
        _sourceLanguageFilterService = sourceLanguageFilterService ?? new SourceLanguageFilterService();
        _ezTransXpInstallationService = ezTransXpInstallationService ?? new EzTransXpInstallationService();
        ProviderOptions = new ObservableCollection<ProviderOption>(BuildProviderOptions());
        _selectedProviderOption = ProviderOptions.FirstOrDefault(option => option.ProviderType == TranslationProviderType.OpenAi);
        FileTypeFilters = ["전체", "ERB", "CSV"];
        StatusFilters = ["전체", "대기", "제외됨", "중지됨", "수동 수정", "번역 완료", "검수 필요", "번역 실패"];
        SaveModeOptions = [SaveMode.ExportCopy, SaveMode.InPlaceWithBackup];
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = FilterItem;
        _globalUserDictionary = _userDictionaryService.LoadGlobal();

        var sampleDirectory = detectSampleDirectory ? TryFindSampleDirectory() : null;
        if (sampleDirectory is not null)
        {
            _gameDirectory = sampleDirectory;
            _outputDirectory = Path.Combine(Path.GetDirectoryName(sampleDirectory) ?? sampleDirectory, "translated-output");
        }

        RefreshProjectContext(restoreSession: false, clearSessionWhenMissing: false);
        LoadConfig();
        RefreshProjectContext(restoreLastSessionOnStartup, clearSessionWhenMissing: false);
    }

    public void FlushPendingConfigSave()
    {
        _appConfigCoordinator.FlushPendingSave();
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

    public IReadOnlyList<string> FileTypeFilters { get; }

    public IReadOnlyList<string> StatusFilters { get; }

    public IReadOnlyList<string> EditableStatusOptions { get; } = ["대기", "제외됨", "중지됨", "수동 수정", "번역 완료", "검수 필요", "번역 실패"];

    public IReadOnlyList<SaveMode> SaveModeOptions { get; }

    public TranslationProviderType SelectedProviderType => SelectedProviderOption?.ProviderType ?? TranslationProviderType.OpenAi;

    public string GameDirectory
    {
        get => _gameDirectory;
        set
        {
            if (SetProperty(ref _gameDirectory, value))
            {
                OnProjectPathInputsChanged();
            }
        }
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set
        {
            if (SetProperty(ref _outputDirectory, value))
            {
                OnProjectPathInputsChanged();
            }
        }
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
            if (SetProperty(ref _model, value))
            {
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
            if (SetProperty(ref _disableThinking, value))
            {
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
            return $"{providerName} / {modelName}";
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

            if (_selectedItem is not null)
            {
                _selectedItem.PropertyChanged -= SelectedItemOnPropertyChanged;
            }

            _selectedItem = value;
            if (_selectedItem is not null)
            {
                _selectedItem.PropertyChanged += SelectedItemOnPropertyChanged;
            }

            RaisePropertyChanged();
            RaisePropertyChanged(nameof(SelectedItemLogText));
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

            if (SelectedItem.IsReferenceBearingKey)
            {
                lines.Add($"참조 영향 수: {SelectedItem.ReferenceImpactCount}");
                lines.Add($"참조 해석 상태: {SelectedItem.ReferenceResolutionStatus}");
            }

            if (selectedDocument is not null
                && string.Equals(selectedDocument.FileType, "ERB", StringComparison.OrdinalIgnoreCase)
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
                ItemsView.Refresh();
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
            }
        }
    }

    public bool CanCancel => IsBusy;

    public bool CanStartActions => !IsBusy;

    public bool CanBrowseDirectories => !IsBusy;

    public bool IsExportSaveMode => SelectedSaveMode == SaveMode.ExportCopy;

    public bool IsInPlaceSaveMode => SelectedSaveMode == SaveMode.InPlaceWithBackup;

    public string SaveModeSummary => SelectedSaveMode switch
    {
        SaveMode.ExportCopy => "번역 파일을 별도 출력 폴더에 저장합니다. 원본 파일은 변경하지 않습니다.",
        SaveMode.InPlaceWithBackup => "원본 파일에 바로 저장하고, 저장 전에 .era-translator-backup 폴더에 백업을 만듭니다.",
        _ => string.Empty,
    };

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
            {
                ItemsView.Refresh();
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
                ItemsView.Refresh();
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
                ItemsView.Refresh();
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
                ItemsView.Refresh();
            }
        }
    }

    public async Task ScanAsync()
    {
        if (!Directory.Exists(GameDirectory))
        {
            StatusText = "유효한 게임 디렉토리를 선택하세요.";
            return;
        }

        await RunBusyAsync(
            "ERB/CSV 파일을 스캔 중입니다...",
            async cancellationToken =>
            {
                var projectDataDirectory = GetProjectDataDirectory();
                var previousSession = _projectStatePersistenceService.LoadScanSession(projectDataDirectory);
                var previousProgress = _projectStatePersistenceService.LoadTranslationProgress(projectDataDirectory);
                ProgressValue = 0.1;
                CurrentOperationDetail = "스캔 준비 중";
                var scanProgress = new Progress<(double value, string detail)>(tuple =>
                {
                    ProgressValue = tuple.value;
                    CurrentOperationDetail = tuple.detail;
                });
                var session = await Task.Run(() => _fileScanner.Scan(GameDirectory, scanProgress, cancellationToken), cancellationToken);
                var restoreResult = ApplySession(session, restoreProgress: false, previousSession, previousProgress);
                _projectStatePersistenceService.SaveScanSession(session, projectDataDirectory);
                SaveTranslationProgress();

                SummaryText =
                    $"문서 {session.Metrics.GetValueOrDefault("Documents")}개, " +
                    $"항목 {session.Metrics.GetValueOrDefault("Items")}개, " +
                    $"ERB {session.Metrics.GetValueOrDefault("ErbItems")}개, " +
                    $"CSV {session.Metrics.GetValueOrDefault("CsvItems")}개, " +
                    $"경고 {session.Metrics.GetValueOrDefault("Warnings")}건, " +
                    $"조사 패턴 {session.Metrics.GetValueOrDefault("JosaPatterns")}건";

                StatusText = restoreResult.RestoredCount > 0
                    ? $"스캔이 완료되었습니다. 이전 번역 상태 {restoreResult.ExactRestoredCount}개 정확 복원, {restoreResult.HeuristicRestoredCount}개 업데이트 승계, {restoreResult.UnmatchedCount}개 신규/변경 항목입니다."
                    : "스캔이 완료되었습니다.";
                CurrentOperationDetail = $"스캔 완료: {session.Metrics.GetValueOrDefault("Documents")}개 문서";
                ProgressValue = 1.0;
                ItemsView.Refresh();
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

    public async Task TranslatePendingAsync()
    {
        if (_session is null)
        {
            StatusText = "먼저 텍스트를 추출하세요.";
            return;
        }

        if (SelectedProviderOption is null)
        {
            StatusText = "번역 공급자를 선택하세요.";
            return;
        }

        if (!SelectedProviderOption.IsAvailable)
        {
            StatusText = $"{SelectedProviderOption.DisplayName}는 아직 준비 중입니다.";
            return;
        }

        var translationScope = GetCurrentTranslationScope();
        var pendingCount = translationScope.Count(item => item.NeedsTranslation);
        if (pendingCount == 0)
        {
            StatusText = "미번역 또는 번역 실패 항목이 없습니다.";
            CurrentOperationDetail = "자동 번역 대상 항목이 없습니다.";
            return;
        }

        await RunBusyAsync(
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
                    await _translationCoordinator.TranslateAsync(
                        translationScope,
                        BuildSettings(),
                        _userDictionaryService.BuildEffectiveDictionary(_globalUserDictionary, _projectUserDictionary),
                        progress,
                        () => SaveTranslationProgressIfDue(),
                        cancellationToken);
                }
                finally
                {
                    _suppressItemStatePersistence = false;
                }

                SaveTranslationProgress(force: true);
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
                ItemsView.Refresh();
            },
            cancelStatusText: "번역이 중지되었습니다. 다시 시작해도 미번역 또는 번역 실패 항목만 이어집니다.",
            cancelDetailFactory: () => $"현재 번역 상태를 저장했습니다. | 경과 시간: {FormatDuration(_translationProgressStopwatch.Elapsed)}",
            onCanceled: () => StopTranslationTiming(resumeOnNextRun: true),
            onFailed: _ => StopTranslationTiming(resumeOnNextRun: false));
    }

    public async Task SaveAsync()
    {
        if (_session is null)
        {
            StatusText = "먼저 스캔을 진행하세요.";
            return;
        }

        if (SelectedSaveMode == SaveMode.ExportCopy && string.IsNullOrWhiteSpace(OutputDirectory))
        {
            StatusText = "출력 디렉토리를 지정하세요.";
            return;
        }

        await RunBusyAsync(
            "번역 결과를 저장 중입니다...",
            async cancellationToken =>
            {
                CurrentOperationDetail = "저장 준비 중";
                var saveProgress = new Progress<(double value, string detail)>(tuple =>
                {
                    ProgressValue = tuple.value;
                    CurrentOperationDetail = tuple.detail;
                });
                var writeResult = await Task.Run(
                    () => _outputWriter.Save(_session, OutputDirectory, SelectedSaveMode, saveProgress, cancellationToken),
                    cancellationToken);

                StatusText = SelectedSaveMode == SaveMode.ExportCopy
                    ? $"{writeResult.WrittenFiles.Count}개 파일을 출력 폴더에 저장했습니다."
                    : $"{writeResult.WrittenFiles.Count}개 파일을 원본에 반영했고, {writeResult.BackupFiles.Count}개 백업을 생성했습니다.";
                CurrentOperationDetail = writeResult.WrittenFiles.Count == 0
                    ? "저장할 번역 항목 없음"
                    : $"저장 완료: {writeResult.WrittenFiles.Count}개 파일";
                ProgressValue = 1.0;
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
                item.ResetTranslationState();
            }
        }
        finally
        {
            _suppressItemStatePersistence = false;
        }

        ApplySourceLanguageFilter();
        SaveTranslationProgress();
        StatusText = "번역 상태를 리셋했습니다.";
        CurrentOperationDetail = "번역문, 실패 상태, 검증 상태를 초기화했습니다.";
        ItemsView.Refresh();
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
        ItemsView.Refresh();
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

        SaveTranslationProgress();
        ItemsView.Refresh();
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
            SearchText = FilterText,
            UseRegex = UseRegexFilter,
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

        SaveTranslationProgress();
        ItemsView.Refresh();
        FilterText = replaceViewModel.SearchText;
        UseRegexFilter = replaceViewModel.UseRegex;
        StatusText = updatedCount == 0
            ? "치환할 번역문을 찾지 못했습니다."
            : $"{updatedCount}개 번역문에 전역 치환을 적용했습니다.";
        CurrentOperationDetail = replaceViewModel.UseRegex ? "정규식 전역 치환 적용" : "일반 텍스트 전역 치환 적용";
    }

    public void HandleTranslatedTextEdited(ExtractedTextItem item)
    {
        var groupItems = Items
            .Where(candidate => string.Equals(candidate.OriginalText, item.OriginalText, StringComparison.Ordinal))
            .ToList();
        if (groupItems.Count == 0)
        {
            groupItems.Add(item);
        }

        _suppressItemStatePersistence = true;
        try
        {
            foreach (var groupItem in groupItems)
            {
                groupItem.TranslatedText = item.TranslatedText;
                groupItem.ApplyManualTranslationEdit();
            }
        }
        finally
        {
            _suppressItemStatePersistence = false;
        }

        ItemsView.Refresh();
        SaveTranslationProgress();
    }

    public TranslationSettingsViewModel CreateTranslationSettingsViewModel()
    {
        var viewModel = new TranslationSettingsViewModel(ProviderOptions, _ezTransXpInstallationService);
        viewModel.LoadFrom(this);
        return viewModel;
    }

    public UserDictionaryViewModel CreateUserDictionaryViewModel()
    {
        return new UserDictionaryViewModel(GetProjectDataDirectory(), _globalUserDictionary, _projectUserDictionary, _userDictionaryService);
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
        SourceLanguage = settingsViewModel.SourceLanguage;
        TargetLanguage = settingsViewModel.TargetLanguage;
        BatchSize = settingsViewModel.BatchSize;
        RetryCount = settingsViewModel.RetryCount;
        Temperature = settingsViewModel.Temperature;
        DisableThinking = settingsViewModel.DisableThinking;
        EnableRequestResponseLogging = settingsViewModel.EnableRequestResponseLogging;
        ExcludeNonSourceText = settingsViewModel.ExcludeNonSourceText;
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
        SummaryText =
            $"문서 {session.Metrics.GetValueOrDefault("Documents")}개, " +
            $"항목 {session.Metrics.GetValueOrDefault("Items")}개, " +
            $"ERB {session.Metrics.GetValueOrDefault("ErbItems")}개, " +
            $"CSV {session.Metrics.GetValueOrDefault("CsvItems")}개, " +
            $"경고 {session.Metrics.GetValueOrDefault("Warnings")}건, " +
            $"조사 패턴 {session.Metrics.GetValueOrDefault("JosaPatterns")}건";
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
        Items.ReplaceAll([]);
        SelectedItem = null;
        SummaryText = "아직 스캔 전입니다.";
        StatusText = "저장된 추출 상태가 없는 프로젝트입니다. 새로 추출을 실행하세요.";
        CurrentOperationDetail = "현재 경로에 복원할 추출 상태가 없습니다.";
        ProgressValue = 0;
        ItemsView.Refresh();
    }

    private void LoadConfig()
    {
        _isLoadingConfig = true;
        try
        {
            var config = _appConfigCoordinator.Load();
            if (!string.IsNullOrWhiteSpace(config.GameDirectory))
            {
                GameDirectory = config.GameDirectory;
            }

            if (!string.IsNullOrWhiteSpace(config.OutputDirectory))
            {
                OutputDirectory = config.OutputDirectory;
            }

            SelectedSaveMode = config.SaveMode;
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

            SourceLanguage = string.IsNullOrWhiteSpace(config.SourceLanguage) ? SourceLanguage : config.SourceLanguage;
            TargetLanguage = string.IsNullOrWhiteSpace(config.TargetLanguage) ? TargetLanguage : config.TargetLanguage;
            BatchSize = config.BatchSize;
            RetryCount = config.RetryCount;
            Temperature = config.Temperature;
            DisableThinking = config.DisableThinking;
            EnableRequestResponseLogging = config.EnableRequestResponseLogging;
            ExcludeNonSourceText = config.ExcludeNonSourceText;
            if (!string.IsNullOrWhiteSpace(config.SystemPromptTemplate))
            {
                SystemPromptTemplate = config.SystemPromptTemplate;
            }

            if (!string.IsNullOrWhiteSpace(config.RetryPromptTemplate))
            {
                RetryPromptTemplate = config.RetryPromptTemplate;
            }

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
            GameDirectory = GameDirectory,
            OutputDirectory = OutputDirectory,
            SaveMode = SelectedSaveMode,
            ProviderType = SelectedProviderType,
            BaseUrl = BaseUrl,
            Model = Model,
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
                break;
            case TranslationProviderType.LmStudio:
                BaseUrl = "http://127.0.0.1:1234/v1";
                Model = "local-model";
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

    private void SaveTranslationProgress()
    {
        if (_session is null)
        {
            return;
        }

        if (!Items.Any(item => item.HasPersistableState))
        {
            _projectStatePersistenceService.DeleteTranslationProgress(GetProjectDataDirectory(_session.GameRoot));
            return;
        }

        _projectStatePersistenceService.SaveTranslationProgress(GetProjectDataDirectory(_session.GameRoot), Items);
        _lastProgressSaveAtUtc = DateTimeOffset.UtcNow;
    }

    private void SaveTranslationProgress(bool force)
    {
        if (!force)
        {
            SaveTranslationProgressIfDue();
            return;
        }

        SaveTranslationProgress();
    }

    private void SaveTranslationProgressIfDue()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastProgressSaveAtUtc < TimeSpan.FromMilliseconds(750))
        {
            return;
        }

        SaveTranslationProgress();
    }

    private TranslationProgressCarryoverResult ApplySession(
        ScanSession session,
        bool restoreProgress,
        ScanSession? previousSession = null,
        TranslationProgressState? previousProgress = null)
    {
        _session = session;
        DetachItemStateHandlers(Items);
        var restoreResult = new TranslationProgressCarryoverResult(0, 0, session.Items.Count);
        _suppressItemStatePersistence = true;
        try
        {
            using (ItemsView.DeferRefresh())
            {
                Items.ReplaceAll(session.Items.OrderBy(item => item.RelativePath).ThenBy(item => item.LineNumber));
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

            _sourceLanguageFilterService.Apply(Items, SourceLanguage, ExcludeNonSourceText);
        }
        finally
        {
            _suppressItemStatePersistence = false;
        }

        AttachItemStateHandlers(Items);
        return restoreResult;
    }

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
        if (_suppressItemStatePersistence || sender is not ExtractedTextItem)
        {
            return;
        }

        if (MatchesPropertyChange(e, nameof(ExtractedTextItem.TranslatedText))
            || MatchesPropertyChange(e, nameof(ExtractedTextItem.Status))
            || MatchesPropertyChange(e, nameof(ExtractedTextItem.ValidationStatus))
            || MatchesPropertyChange(e, nameof(ExtractedTextItem.TranslationError))
            || MatchesPropertyChange(e, nameof(ExtractedTextItem.CanSave)))
        {
            SaveTranslationProgress();
        }
    }

    private async Task RunBusyAsync(
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
            return;
        }

        using var cancellationTokenSource = new CancellationTokenSource();
        _cancellationTokenSource = cancellationTokenSource;
        IsBusy = true;
        StatusText = startingMessage;
        CurrentOperationDetail = startingMessage;

        try
        {
            await action(cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            onCanceled?.Invoke();
            SaveTranslationProgress(force: true);
            StatusText = cancelStatusText;
            CurrentOperationDetail = cancelDetailFactory?.Invoke() ?? cancelDetailText;
        }
        catch (Exception ex)
        {
            onFailed?.Invoke(ex);
            StatusText = $"작업 실패: {ex.Message}";
            CurrentOperationDetail = "오류가 발생해 작업을 중단했습니다.";
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

        if (string.IsNullOrWhiteSpace(FilterText))
        {
            return true;
        }

        return IsFilterMatch(textItem.RelativePath)
            || IsFilterMatch(textItem.OriginalText)
            || IsFilterMatch(textItem.TranslatedText)
            || IsFilterMatch(textItem.SourceKey)
            || IsFilterMatch(textItem.SymbolNamespace)
            || IsFilterMatch(textItem.OriginalSymbolKey)
            || IsFilterMatch(textItem.TranslatedSymbolKey)
            || IsFilterMatch(textItem.ReferenceResolutionStatus)
            || IsFilterMatch(document?.JosaAnalysis.SyntaxType)
            || IsFilterMatch(document?.JosaAnalysis.ErhLinkStatus)
            || IsFilterMatch(document?.JosaAnalysis.PackageCompatibilityStatus);
    }

    private List<ExtractedTextItem> GetCurrentTranslationScope()
    {
        return ItemsView.Cast<ExtractedTextItem>().ToList();
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
            ProviderType = TranslationProviderType.LmStudio,
            DisplayName = "LM Studio",
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

    private void ApplySourceLanguageFilter()
    {
        if (_session is null)
        {
            return;
        }

        _suppressItemStatePersistence = true;
        try
        {
            _sourceLanguageFilterService.Apply(Items, SourceLanguage, ExcludeNonSourceText);
        }
        finally
        {
            _suppressItemStatePersistence = false;
        }

        ItemsView.Refresh();
        SaveTranslationProgress();
    }

    private string GetProjectDataDirectory(string? fallbackGameDirectory = null)
    {
        if (SelectedSaveMode == SaveMode.ExportCopy && !string.IsNullOrWhiteSpace(OutputDirectory))
        {
            return OutputDirectory;
        }

        return !string.IsNullOrWhiteSpace(GameDirectory)
            ? GameDirectory
            : fallbackGameDirectory ?? string.Empty;
    }

}
