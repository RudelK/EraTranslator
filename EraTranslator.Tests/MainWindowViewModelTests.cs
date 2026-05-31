using System.Text;
using EraTranslator.Models;
using EraTranslator.Services;
using EraTranslator.ViewModels;

namespace EraTranslator.Tests;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    [Fact]
    public void Constructor_LeavesProjectDirectoriesEmptyByDefault()
    {
        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            restoreLastSessionOnStartup: false);

        Assert.Equal(string.Empty, viewModel.GameDirectory);
        Assert.Equal(string.Empty, viewModel.OutputDirectory);
    }

    [Fact]
    public void Constructor_LeavesResultStateLoggingDisabledByDefault()
    {
        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            restoreLastSessionOnStartup: false);

        Assert.False(viewModel.EnableResultStateLogging);
    }

    [Fact]
    public void Constructor_ExposesErhInFileTypeFilters()
    {
        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            restoreLastSessionOnStartup: false);

        Assert.Contains("ERH", viewModel.FileTypeFilters);
    }

    [Fact]
    public void ChangingProjectDataDirectory_ClearsStaleSessionWhenTargetHasNoSavedState()
    {
        var gameDirectory = Path.Combine(_rootPath, "Game");
        var firstOutputDirectory = Path.Combine(_rootPath, "Output1");
        var secondOutputDirectory = Path.Combine(_rootPath, "Output2");
        Directory.CreateDirectory(gameDirectory);
        Directory.CreateDirectory(firstOutputDirectory);
        Directory.CreateDirectory(secondOutputDirectory);

        var sessionStateService = new ScanSessionStateService();
        sessionStateService.Save(BuildSession(gameDirectory), firstOutputDirectory);

        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            scanSessionStateService: sessionStateService,
            translationProgressStateService: new TranslationProgressStateService(),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        viewModel.GameDirectory = gameDirectory;
        viewModel.OutputDirectory = firstOutputDirectory;

        Assert.Single(viewModel.Items);
        Assert.Equal("마지막 추출 상태를 불러왔습니다.", viewModel.StatusText);

        viewModel.OutputDirectory = secondOutputDirectory;

        Assert.Empty(viewModel.Items);
        Assert.Equal("아직 스캔 전입니다.", viewModel.SummaryText);
        Assert.Equal("저장된 추출 상태가 없는 프로젝트입니다. 새로 추출을 실행하세요.", viewModel.StatusText);
    }

    [Fact]
    public void AutomaticTranslationStateChange_RemainsCompleted()
    {
        var gameDirectory = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(gameDirectory);

        var sessionStateService = new ScanSessionStateService();
        sessionStateService.Save(BuildSession(gameDirectory), gameDirectory);

        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            scanSessionStateService: sessionStateService,
            translationProgressStateService: new TranslationProgressStateService(),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        viewModel.GameDirectory = gameDirectory;
        var item = Assert.Single(viewModel.Items);

        item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "안녕하세요");

        Assert.Equal("번역 완료", item.Status);
        Assert.Equal("통과", item.ValidationStatus);
        Assert.True(item.CanSave);
    }

    [Fact]
    public async Task TranslatePendingAsync_PropagatesFilteredTranslationToSameOriginalOutsideFilter()
    {
        var gameDirectory = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(gameDirectory);

        var sessionStateService = new ScanSessionStateService();
        sessionStateService.Save(BuildSessionWithSameOriginalInDifferentFiles(gameDirectory), gameDirectory);
        var provider = new RecordingProvider(requests =>
        {
            var result = new TranslationProviderResult();
            result.Translations["ERB/Visible.ERB:0"] = "안녕하세요";
            return result;
        });

        var viewModel = new MainWindowViewModel(
            translationCoordinator: new TranslationCoordinator(new FakeTranslationProviderFactory(provider)),
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            scanSessionStateService: sessionStateService,
            translationProgressStateService: new TranslationProgressStateService(),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        viewModel.GameDirectory = gameDirectory;
        viewModel.FilterText = "Visible.ERB";
        viewModel.EnableBundledDictionaryFirstPass = false;

        var completed = await viewModel.TranslatePendingAsync();

        var visible = viewModel.Items.Single(item => item.SegmentId == "ERB/Visible.ERB:0");
        var hidden = viewModel.Items.Single(item => item.SegmentId == "ERB/Hidden.ERB:0");
        Assert.True(completed);
        Assert.Single(provider.RequestHistory);
        Assert.Equal(["ERB/Visible.ERB:0"], provider.RequestHistory[0]);
        Assert.Equal("안녕하세요", visible.TranslatedText);
        Assert.Equal("안녕하세요", hidden.TranslatedText);
        Assert.Equal("번역 완료", hidden.Status);
    }

    [Fact]
    public async Task TranslatePendingAsync_ExecutesCsvReferenceGeneralErhErbPhasesAndBuildsGlossaryBetweenStages()
    {
        var gameDirectory = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(gameDirectory);

        var sessionStateService = new ScanSessionStateService();
        sessionStateService.Save(BuildPhasedSession(gameDirectory), gameDirectory);
        var recordingStore = new RecordingSqliteProjectStateStore();
        var provider = new RecordingProvider(requests =>
        {
            var result = new TranslationProviderResult();
            foreach (var request in requests)
            {
                result.Translations[request.Id] = request.Id switch
                {
                    "CSV/Talent.csv:0" => "쾌락",
                    "CSV/Terms.csv:0" => "쾌락치",
                    "ERH/Terms.ERH:0" => "쾌락치 게이지",
                    "ERB/Test.ERB:0" => "쾌락치 게이지가 상승했다",
                    _ => $"번역:{request.OriginalText}",
                };
            }

            return result;
        });

        var viewModel = new MainWindowViewModel(
            translationCoordinator: new TranslationCoordinator(new FakeTranslationProviderFactory(provider)),
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            scanSessionStateService: sessionStateService,
            translationProgressStateService: new TranslationProgressStateService(),
            sqliteProjectStateStore: recordingStore,
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        viewModel.GameDirectory = gameDirectory;
        viewModel.EnableBundledDictionaryFirstPass = false;
        viewModel.EnableKanaTransliterationFallback = false;
        viewModel.EnableKanjiReadingFallback = false;
        recordingStore.ResetCounts();

        var completed = await viewModel.TranslatePendingAsync();

        Assert.True(completed);
        Assert.Equal(4, provider.RequestHistory.Count);
        Assert.Equal(["CSV/Talent.csv:0"], provider.RequestHistory[0]);
        Assert.Equal(["CSV/Terms.csv:0"], provider.RequestHistory[1]);
        Assert.Equal(["ERH/Terms.ERH:0"], provider.RequestHistory[2]);
        Assert.Equal(["ERB/Test.ERB:0"], provider.RequestHistory[3]);
        Assert.Empty(provider.GlossaryHistory[0]);
        Assert.Equal(["快楽"], provider.GlossaryHistory[1].Select(static hint => hint.Source).ToList());
        Assert.Equal(["快楽値", "快楽"], provider.GlossaryHistory[2].Select(static hint => hint.Source).ToList());
        Assert.Equal(["快楽値ゲージ", "快楽値", "快楽"], provider.GlossaryHistory[3].Select(static hint => hint.Source).ToList());
        Assert.True(recordingStore.SnapshotSaveCount >= 4);
    }

    [Fact]
    public void ItemsView_OrdersReferenceBearingCsvBeforeGeneralCsvAndKeepsFilteredOrder()
    {
        var gameDirectory = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(gameDirectory);

        var sessionStateService = new ScanSessionStateService();
        sessionStateService.Save(BuildPhasedSession(gameDirectory), gameDirectory);

        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            scanSessionStateService: sessionStateService,
            translationProgressStateService: new TranslationProgressStateService(),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        viewModel.GameDirectory = gameDirectory;

        Assert.Equal(
            ["CSV/Talent.csv:0", "CSV/Terms.csv:0", "ERH/Terms.ERH:0", "ERB/Test.ERB:0"],
            viewModel.ItemsView.Cast<ExtractedTextItem>().Select(item => item.SegmentId).ToList());

        viewModel.SelectedFileTypeFilter = "CSV";

        Assert.Equal(
            ["CSV/Talent.csv:0", "CSV/Terms.csv:0"],
            viewModel.ItemsView.Cast<ExtractedTextItem>().Select(item => item.SegmentId).ToList());
    }

    [Fact]
    public void ManualExcludedStatus_IsRestoredAfterReopen()
    {
        var gameDirectory = Path.Combine(_rootPath, "Game");
        var configPath = Path.Combine(_rootPath, "Config");
        var appDataPath = Path.Combine(_rootPath, "AppData");
        Directory.CreateDirectory(gameDirectory);

        var sessionStateService = new ScanSessionStateService();
        sessionStateService.Save(BuildSession(gameDirectory), gameDirectory);

        var first = new MainWindowViewModel(
            appConfigService: new AppConfigService(configPath),
            userDictionaryService: new UserDictionaryService(appDataPath),
            scanSessionStateService: sessionStateService,
            translationProgressStateService: new TranslationProgressStateService(),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        first.GameDirectory = gameDirectory;
        var firstItem = Assert.Single(first.Items);
        firstItem.ApplyManualStatusOverride("제외됨");

        var second = new MainWindowViewModel(
            appConfigService: new AppConfigService(configPath),
            userDictionaryService: new UserDictionaryService(appDataPath),
            scanSessionStateService: sessionStateService,
            translationProgressStateService: new TranslationProgressStateService(),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        second.GameDirectory = gameDirectory;
        var restoredItem = Assert.Single(second.Items);

        Assert.Equal("제외됨", restoredItem.Status);
        Assert.Equal("수동 제외", restoredItem.ValidationStatus);
        Assert.True(restoredItem.CanSave);
        Assert.False(restoredItem.NeedsTranslation);
    }

    [Fact]
    public void LegacyManualExcludedStatus_IsRestoredAfterReopen()
    {
        var gameDirectory = Path.Combine(_rootPath, "Game");
        var configPath = Path.Combine(_rootPath, "Config");
        var appDataPath = Path.Combine(_rootPath, "AppData");
        Directory.CreateDirectory(gameDirectory);

        var sessionStateService = new ScanSessionStateService();
        sessionStateService.Save(BuildSession(gameDirectory), gameDirectory);

        var sqliteStore = new SqliteProjectStateStore();
        sqliteStore.SaveScanSession(BuildSession(gameDirectory), gameDirectory);
        sqliteStore.SaveTranslationProgressSnapshot(
            gameDirectory,
            new TranslationProgressState
            {
                Items =
                [
                    new TranslationProgressItemState
                    {
                        SegmentId = "ERB/Test.ERB:0",
                        Status = "제외됨",
                        ValidationStatus = "언어 제외",
                        TranslationError = "수동으로 제외 상태로 표시했습니다.",
                        TranslatedText = string.Empty,
                        CanSave = true,
                    },
                ],
            });

        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(configPath),
            userDictionaryService: new UserDictionaryService(appDataPath),
            scanSessionStateService: sessionStateService,
            translationProgressStateService: new TranslationProgressStateService(),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        viewModel.GameDirectory = gameDirectory;
        var restoredItem = Assert.Single(viewModel.Items);

        Assert.Equal("제외됨", restoredItem.Status);
        Assert.Equal("언어 제외", restoredItem.ValidationStatus);
        Assert.True(restoredItem.CanSave);
        Assert.False(restoredItem.NeedsTranslation);
    }

    [Fact]
    public void RestoreLastSession_RecomputesReferenceAnalysisAfterApplyingPersistedProgress()
    {
        var gameDirectory = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(gameDirectory);
        var sqliteStore = new SqliteProjectStateStore();
        sqliteStore.SaveScanSession(BuildReferenceSession(gameDirectory), gameDirectory);
        sqliteStore.SaveTranslationProgressSnapshot(
            gameDirectory,
            new TranslationProgressState
            {
                Items =
                [
                    new TranslationProgressItemState
                    {
                        SegmentId = "ERB/Test.ERB:0",
                        Status = "번역 완료",
                        ValidationStatus = "통과",
                        TranslationError = string.Empty,
                        TranslatedText = "見た目年齢",
                        CanSave = true,
                        ReferenceOriginalSymbolKey = "外見年齢",
                        ReferenceImpactCount = 99,
                        RequiresReferenceRewrite = false,
                        ReferenceResolutionStatus = "이전 참조 상태",
                    },
                ],
            });

        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            sqliteProjectStateStore: sqliteStore,
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        viewModel.GameDirectory = gameDirectory;
        var restoredItem = Assert.Single(viewModel.Items);

        Assert.Equal(1, restoredItem.ReferenceImpactCount);
        Assert.True(restoredItem.RequiresReferenceRewrite);
        Assert.Equal("직접 참조만", restoredItem.ReferenceResolutionStatus);
    }

    [Fact]
    public void HandleTranslatedTextEdited_MarksItemAsManualEdit()
    {
        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);
        var item = new ExtractedTextItem
        {
            SegmentId = "doc:1",
            DocumentId = "doc",
            FileType = "ERB",
            RelativePath = "ERB\\Test.ERB",
            EncodingName = "UTF-8",
            SegmentType = "quoted-string",
            LineNumber = 1,
            OriginalText = "こんにちは",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            TranslatedText = "안녕하세요",
            Status = "번역 완료",
            ValidationStatus = "통과",
            WarningText = string.Empty,
        };

        viewModel.HandleTranslatedTextEdited(item);

        Assert.Equal("수동 수정", item.Status);
        Assert.Equal("통과", item.ValidationStatus);
        Assert.True(item.CanSave);
    }

    [Fact]
    public void HandleTranslatedTextEdited_AppliesManualEditToItemsWithSameOriginalText()
    {
        var gameDirectory = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(gameDirectory);

        var sessionStateService = new ScanSessionStateService();
        sessionStateService.Save(BuildSessionWithDuplicateOriginals(gameDirectory), gameDirectory);

        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            scanSessionStateService: sessionStateService,
            translationProgressStateService: new TranslationProgressStateService(),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        viewModel.GameDirectory = gameDirectory;

        var first = viewModel.Items.Single(item => item.SegmentId == "ERB/Test.ERB:0");
        var second = viewModel.Items.Single(item => item.SegmentId == "ERB/Test.ERB:1");
        first.TranslatedText = "안녕히 가세요";

        viewModel.HandleTranslatedTextEdited(first);

        Assert.Equal("안녕히 가세요", first.TranslatedText);
        Assert.Equal("안녕히 가세요", second.TranslatedText);
        Assert.Equal("수동 수정", first.Status);
        Assert.Equal("수동 수정", second.Status);
        Assert.Equal("통과", first.ValidationStatus);
        Assert.Equal("통과", second.ValidationStatus);
    }

    [Fact]
    public void SelectedItemPreview_ShowsOriginalAndTranslatedText()
    {
        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);
        var item = new ExtractedTextItem
        {
            SegmentId = "doc:1",
            DocumentId = "doc",
            FileType = "ERB",
            RelativePath = "ERB\\Test.ERB",
            EncodingName = "UTF-8",
            SegmentType = "quoted-string",
            LineNumber = 1,
            OriginalText = "こんにちは",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            TranslatedText = "안녕하세요",
            Status = "번역 완료",
            ValidationStatus = "통과",
            WarningText = string.Empty,
        };

        viewModel.SelectedItem = item;

        Assert.Equal("こんにちは", viewModel.SelectedItemOriginalPreviewText);
        Assert.Equal("안녕하세요", viewModel.SelectedItemTranslatedTextEditor);

        viewModel.SelectedItem = null;

        Assert.Equal(string.Empty, viewModel.SelectedItemOriginalPreviewText);
        Assert.Equal(string.Empty, viewModel.SelectedItemTranslatedTextEditor);
    }

    [Fact]
    public void SelectedItemPreview_EditCommitsAndPropagatesToSameOriginalText()
    {
        var gameDirectory = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(gameDirectory);

        var sessionStateService = new ScanSessionStateService();
        sessionStateService.Save(BuildSessionWithDuplicateOriginals(gameDirectory), gameDirectory);

        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            scanSessionStateService: sessionStateService,
            translationProgressStateService: new TranslationProgressStateService(),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        viewModel.GameDirectory = gameDirectory;

        var first = viewModel.Items.Single(item => item.SegmentId == "ERB/Test.ERB:0");
        var second = viewModel.Items.Single(item => item.SegmentId == "ERB/Test.ERB:1");
        viewModel.SelectedItem = first;
        viewModel.SelectedItemTranslatedTextEditor = "안녕히 가세요";

        viewModel.CommitSelectedItemTranslatedTextEdit();

        Assert.Equal("안녕히 가세요", first.TranslatedText);
        Assert.Equal("안녕히 가세요", second.TranslatedText);
        Assert.Equal("안녕히 가세요", viewModel.SelectedItemTranslatedTextEditor);
        Assert.Equal("수동 수정", first.Status);
        Assert.Equal("수동 수정", second.Status);
    }

    [Fact]
    public void SelectedItemPreview_UsesEventTextAndPropagatesBeforeBindingSourceUpdates()
    {
        var gameDirectory = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(gameDirectory);

        var sessionStateService = new ScanSessionStateService();
        sessionStateService.Save(BuildSessionWithDuplicateOriginals(gameDirectory), gameDirectory);

        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            scanSessionStateService: sessionStateService,
            translationProgressStateService: new TranslationProgressStateService(),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        viewModel.GameDirectory = gameDirectory;

        var first = viewModel.Items.Single(item => item.SegmentId == "ERB/Test.ERB:0");
        var second = viewModel.Items.Single(item => item.SegmentId == "ERB/Test.ERB:1");
        viewModel.SelectedItem = first;

        viewModel.PreviewSelectedItemTranslatedTextEdit("안녕하네요");

        Assert.Equal("안녕하네요", first.TranslatedText);
        Assert.Equal("안녕하네요", second.TranslatedText);
        Assert.Equal("안녕하네요", viewModel.SelectedItemTranslatedTextEditor);
        Assert.Equal("수동 수정", first.Status);
        Assert.Equal("수동 수정", second.Status);
    }

    [Fact]
    public void SelectedItemPreview_CommitUsesEventTextAndSavesAllSameOriginalItems()
    {
        var gameDirectory = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(gameDirectory);

        var sessionStateService = new ScanSessionStateService();
        sessionStateService.Save(BuildSessionWithDuplicateOriginals(gameDirectory), gameDirectory);
        var recordingStore = new RecordingSqliteProjectStateStore();

        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            scanSessionStateService: sessionStateService,
            translationProgressStateService: new TranslationProgressStateService(),
            sqliteProjectStateStore: recordingStore,
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        viewModel.GameDirectory = gameDirectory;
        recordingStore.ResetCounts();

        var first = viewModel.Items.Single(item => item.SegmentId == "ERB/Test.ERB:0");
        var second = viewModel.Items.Single(item => item.SegmentId == "ERB/Test.ERB:1");
        viewModel.SelectedItem = first;

        viewModel.CommitSelectedItemTranslatedTextEdit("안녕하네요");

        Assert.Equal("안녕하네요", first.TranslatedText);
        Assert.Equal("안녕하네요", second.TranslatedText);
        Assert.Equal("안녕하네요", viewModel.SelectedItemTranslatedTextEditor);
        Assert.Equal(1, recordingStore.UpsertItemsCallCount);
        Assert.Equal(2, Assert.Single(recordingStore.UpsertBatchSizes));
    }

    [Fact]
    public void SelectedItemPreview_EditCommitMarksManualEvenIfTextWasAlreadyApplied()
    {
        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);
        var item = new ExtractedTextItem
        {
            SegmentId = "doc:1",
            DocumentId = "doc",
            FileType = "ERB",
            RelativePath = "ERB\\Test.ERB",
            EncodingName = "UTF-8",
            SegmentType = "quoted-string",
            LineNumber = 1,
            OriginalText = "こんにちは",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            TranslatedText = "안녕하세요",
            Status = "번역 완료",
            ValidationStatus = "통과",
            WarningText = string.Empty,
        };
        viewModel.Items.Add(item);
        viewModel.SelectedItem = item;
        viewModel.SelectedItemTranslatedTextEditor = "안녕하네요";

        item.TranslatedText = "안녕하네요";
        viewModel.CommitSelectedItemTranslatedTextEdit();

        Assert.Equal("안녕하네요", item.TranslatedText);
        Assert.Equal("수동 수정", item.Status);
        Assert.Equal("통과", item.ValidationStatus);
        Assert.Equal("안녕하네요", viewModel.SelectedItemTranslatedTextEditor);
    }

    [Fact]
    public void SelectedItemPreview_TextChangeImmediatelyMarksManualEditWithoutSavingProgress()
    {
        var gameDirectory = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(gameDirectory);

        var sessionStateService = new ScanSessionStateService();
        sessionStateService.Save(BuildSession(gameDirectory), gameDirectory);
        var recordingStore = new RecordingSqliteProjectStateStore();

        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            scanSessionStateService: sessionStateService,
            translationProgressStateService: new TranslationProgressStateService(),
            sqliteProjectStateStore: recordingStore,
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        viewModel.GameDirectory = gameDirectory;
        var item = Assert.Single(viewModel.Items);
        item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "안녕하세요");
        viewModel.SelectedItem = item;
        recordingStore.ResetCounts();

        viewModel.SelectedItemTranslatedTextEditor = "안녕하네요";
        viewModel.PreviewSelectedItemTranslatedTextEdit();

        Assert.Equal("안녕하네요", item.TranslatedText);
        Assert.Equal("수동 수정", item.Status);
        Assert.Equal("통과", item.ValidationStatus);
        Assert.Equal(0, recordingStore.SnapshotSaveCount);
        Assert.Equal(0, recordingStore.UpsertItemsCallCount);
        Assert.Equal(0, recordingStore.DeleteItemsCallCount);
    }

    [Fact]
    public void SelectedItemPreview_LostFocusWithoutEdit_DoesNotChangeAutomaticStatus()
    {
        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);
        var item = new ExtractedTextItem
        {
            SegmentId = "doc:2",
            DocumentId = "doc",
            FileType = "ERB",
            RelativePath = "ERB\\Test.ERB",
            EncodingName = "UTF-8",
            SegmentType = "quoted-string",
            LineNumber = 2,
            OriginalText = "さようなら",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            TranslatedText = "안녕",
            Status = "번역 완료",
            ValidationStatus = "통과",
            WarningText = string.Empty,
        };
        viewModel.Items.Add(item);
        viewModel.SelectedItem = item;

        viewModel.CommitSelectedItemTranslatedTextEdit();

        Assert.Equal("번역 완료", item.Status);
        Assert.Equal("통과", item.ValidationStatus);
        Assert.Equal("안녕", viewModel.SelectedItemTranslatedTextEditor);
    }

    [Fact]
    public void HandleTranslatedTextEdited_UsesSingleIncrementalProgressSave()
    {
        var gameDirectory = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(gameDirectory);

        var sessionStateService = new ScanSessionStateService();
        sessionStateService.Save(BuildSessionWithDuplicateOriginals(gameDirectory), gameDirectory);
        var recordingStore = new RecordingSqliteProjectStateStore();

        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            scanSessionStateService: sessionStateService,
            translationProgressStateService: new TranslationProgressStateService(),
            sqliteProjectStateStore: recordingStore,
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        viewModel.GameDirectory = gameDirectory;
        recordingStore.ResetCounts();

        var first = viewModel.Items.Single(item => item.SegmentId == "ERB/Test.ERB:0");
        viewModel.HandleTranslatedTextEdited(first, "안녕하네요");

        Assert.Equal(0, recordingStore.SnapshotSaveCount);
        Assert.Equal(1, recordingStore.UpsertItemsCallCount);
        Assert.Equal(0, recordingStore.DeleteItemsCallCount);
        Assert.Equal(2, Assert.Single(recordingStore.UpsertBatchSizes));
    }

    [Fact]
    public void EditableStatusChange_UsesSingleIncrementalProgressSave()
    {
        var gameDirectory = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(gameDirectory);

        var sessionStateService = new ScanSessionStateService();
        sessionStateService.Save(BuildSession(gameDirectory), gameDirectory);
        var recordingStore = new RecordingSqliteProjectStateStore();

        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            scanSessionStateService: sessionStateService,
            translationProgressStateService: new TranslationProgressStateService(),
            sqliteProjectStateStore: recordingStore,
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        viewModel.GameDirectory = gameDirectory;
        recordingStore.ResetCounts();

        var item = Assert.Single(viewModel.Items);
        item.ApplyManualStatusOverride("제외됨");

        Assert.Equal(0, recordingStore.SnapshotSaveCount);
        Assert.Equal(1, recordingStore.UpsertItemsCallCount);
        Assert.Equal(0, recordingStore.DeleteItemsCallCount);
        Assert.Equal(1, Assert.Single(recordingStore.UpsertBatchSizes));
    }

    [Fact]
    public void ResetTranslations_UsesSnapshotProgressSave()
    {
        var gameDirectory = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(gameDirectory);

        var sessionStateService = new ScanSessionStateService();
        sessionStateService.Save(BuildSession(gameDirectory), gameDirectory);
        var recordingStore = new RecordingSqliteProjectStateStore();

        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            scanSessionStateService: sessionStateService,
            translationProgressStateService: new TranslationProgressStateService(),
            sqliteProjectStateStore: recordingStore,
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        viewModel.GameDirectory = gameDirectory;
        var item = Assert.Single(viewModel.Items);
        item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "안녕하네요");
        recordingStore.ResetCounts();

        viewModel.ResetTranslations();

        Assert.Equal(1, recordingStore.SnapshotSaveCount);
        Assert.Equal(0, recordingStore.UpsertItemsCallCount);
        Assert.Equal(0, recordingStore.DeleteItemsCallCount);
    }

    [Fact]
    public void ResetTranslations_PreservesManualExcludedItems()
    {
        var gameDirectory = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(gameDirectory);

        var sessionStateService = new ScanSessionStateService();
        sessionStateService.Save(BuildSession(gameDirectory), gameDirectory);

        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            scanSessionStateService: sessionStateService,
            translationProgressStateService: new TranslationProgressStateService(),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        viewModel.GameDirectory = gameDirectory;
        var item = Assert.Single(viewModel.Items);
        item.ApplyManualStatusOverride("제외됨");

        viewModel.ResetTranslations();

        Assert.Equal("제외됨", item.Status);
        Assert.Equal("수동 제외", item.ValidationStatus);
        Assert.True(item.CanSave);
        Assert.Equal(string.Empty, item.TranslatedText);
    }

    [Fact]
    public void ResetTranslations_RefreshesSummaryWarningCount()
    {
        var gameDirectory = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(gameDirectory);

        var sessionStateService = new ScanSessionStateService();
        sessionStateService.Save(BuildSession(gameDirectory), gameDirectory);

        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            scanSessionStateService: sessionStateService,
            translationProgressStateService: new TranslationProgressStateService(),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        viewModel.GameDirectory = gameDirectory;
        var item = Assert.Single(viewModel.Items);
        item.ApplyTranslationState("번역 실패", "HTTP 500", "server error", false);

        Assert.Contains("경고 1건", viewModel.SummaryText, StringComparison.Ordinal);

        viewModel.ResetTranslations();

        Assert.Contains("경고 0건", viewModel.SummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyJosaRewriteToCurrentScope_UpdatesFilteredItemsAndUsesSnapshotSave()
    {
        var gameDirectory = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(gameDirectory);

        var sessionStateService = new ScanSessionStateService();
        sessionStateService.Save(BuildSessionWithDuplicateOriginals(gameDirectory), gameDirectory);
        var recordingStore = new RecordingSqliteProjectStateStore();

        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            scanSessionStateService: sessionStateService,
            translationProgressStateService: new TranslationProgressStateService(),
            sqliteProjectStateStore: recordingStore,
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        viewModel.GameDirectory = gameDirectory;
        var first = viewModel.Items.Single(item => item.SegmentId == "ERB/Test.ERB:0");
        var second = viewModel.Items.Single(item => item.SegmentId == "ERB/Test.ERB:1");
        first.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "%CALLNAME:娼婦キャラ番号%은 시선을 피했다.");
        second.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "그냥 문장");
        viewModel.FilterText = "娼婦キャラ番号";
        recordingStore.ResetCounts();

        viewModel.ApplyJosaRewriteToCurrentScope();

        Assert.Equal("%조사처리(CALLNAME:娼婦キャラ番号,\"는\")% 시선을 피했다.", first.TranslatedText);
        Assert.Equal("그냥 문장", second.TranslatedText);
        Assert.Equal(1, recordingStore.SnapshotSaveCount);
        Assert.Equal(0, recordingStore.UpsertItemsCallCount);
        Assert.Equal(0, recordingStore.DeleteItemsCallCount);
    }

    [Fact]
    public void ApplyJosaRewriteToCurrentScope_RewritesGeneralKoreanSentences()
    {
        var gameDirectory = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(gameDirectory);

        var sessionStateService = new ScanSessionStateService();
        sessionStateService.Save(BuildSessionWithDuplicateOriginals(gameDirectory), gameDirectory);
        var recordingStore = new RecordingSqliteProjectStateStore();

        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            scanSessionStateService: sessionStateService,
            translationProgressStateService: new TranslationProgressStateService(),
            sqliteProjectStateStore: recordingStore,
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        viewModel.GameDirectory = gameDirectory;
        var first = viewModel.Items.Single(item => item.SegmentId == "ERB/Test.ERB:0");
        var second = viewModel.Items.Single(item => item.SegmentId == "ERB/Test.ERB:1");
        first.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "사과은 길으로 간다.");
        second.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "영문ABC은 유지");
        viewModel.FilterText = "사과은";
        recordingStore.ResetCounts();

        viewModel.ApplyJosaRewriteToCurrentScope();

        Assert.Equal("사과는 길로 간다.", first.TranslatedText);
        Assert.Equal("영문ABC은 유지", second.TranslatedText);
        Assert.Equal(1, recordingStore.SnapshotSaveCount);
        Assert.Equal(0, recordingStore.UpsertItemsCallCount);
        Assert.Equal(0, recordingStore.DeleteItemsCallCount);
    }

    [Fact]
    public void ApplyErbFunctionCorrectionToCurrentScope_UpdatesFilteredItemsAndUsesSnapshotSave()
    {
        var gameDirectory = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(gameDirectory);

        var sessionStateService = new ScanSessionStateService();
        sessionStateService.Save(BuildSessionWithDuplicateOriginals(gameDirectory), gameDirectory);
        var recordingStore = new RecordingSqliteProjectStateStore();

        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            scanSessionStateService: sessionStateService,
            translationProgressStateService: new TranslationProgressStateService(),
            sqliteProjectStateStore: recordingStore,
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        viewModel.GameDirectory = gameDirectory;
        var first = viewModel.Items.Single(item => item.SegmentId == "ERB/Test.ERB:0");
        var second = viewModel.Items.Single(item => item.SegmentId == "ERB/Test.ERB:1");
        first.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "GET_SP_TRAIN_MEETING_CHARA_NAME(SP_TRAIN_MEETING_CHARA、3)");
        second.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "그냥 문장");
        viewModel.FilterText = "GET_SP_TRAIN_MEETING_CHARA_NAME";
        recordingStore.ResetCounts();

        viewModel.ApplyErbFunctionCorrectionToCurrentScope();

        Assert.Equal("GET_SP_TRAIN_MEETING_CHARA_NAME(SP_TRAIN_MEETING_CHARA,3)", first.TranslatedText);
        Assert.Equal("그냥 문장", second.TranslatedText);
        Assert.Equal(1, recordingStore.SnapshotSaveCount);
        Assert.Equal(0, recordingStore.UpsertItemsCallCount);
        Assert.Equal(0, recordingStore.DeleteItemsCallCount);
    }

    [Fact]
    public void ApplyErbFunctionCorrectionToCurrentScope_RewritesBraceExpressions()
    {
        var gameDirectory = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(gameDirectory);

        var sessionStateService = new ScanSessionStateService();
        sessionStateService.Save(BuildSessionWithDuplicateOriginals(gameDirectory), gameDirectory);
        var recordingStore = new RecordingSqliteProjectStateStore();

        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            scanSessionStateService: sessionStateService,
            translationProgressStateService: new TranslationProgressStateService(),
            sqliteProjectStateStore: recordingStore,
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        viewModel.GameDirectory = gameDirectory;
        var first = viewModel.Items.Single(item => item.SegmentId == "ERB/Test.ERB:0");
        first.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "{needPoint、5、RIGHT}");
        viewModel.FilterText = "needPoint";
        recordingStore.ResetCounts();

        viewModel.ApplyErbFunctionCorrectionToCurrentScope();

        Assert.Equal("{needPoint,5,RIGHT}", first.TranslatedText);
        Assert.Equal(1, recordingStore.SnapshotSaveCount);
        Assert.Equal(0, recordingStore.UpsertItemsCallCount);
        Assert.Equal(0, recordingStore.DeleteItemsCallCount);
    }

    [Fact]
    public async Task SaveAsync_CommitsSelectedEditorTextBeforeWritingOutput()
    {
        var gameDirectory = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(Path.Combine(gameDirectory, "ERB"));
        var session = BuildSession(gameDirectory);
        File.WriteAllText(session.Documents["ERB/Test.ERB"].FullPath, session.Documents["ERB/Test.ERB"].OriginalText, new UTF8Encoding(true));

        var sessionStateService = new ScanSessionStateService();
        sessionStateService.Save(session, gameDirectory);

        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            scanSessionStateService: sessionStateService,
            translationProgressStateService: new TranslationProgressStateService(),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        viewModel.GameDirectory = gameDirectory;
        viewModel.SelectedSaveMode = SaveMode.InPlaceWithBackup;
        var item = Assert.Single(viewModel.Items);
        viewModel.SelectedItem = item;
        viewModel.SelectedItemTranslatedTextEditor = "안녕하네요";

        await viewModel.SaveAsync();

        Assert.Equal("수동 수정", item.Status);
        Assert.Contains("안녕하네요", File.ReadAllText(Path.Combine(gameDirectory, "ERB", "Test.ERB"), Encoding.UTF8), StringComparison.Ordinal);
    }

    [Fact]
    public void HandleTranslatedTextEdited_DoesNotRefreshFilteredGridByDefault()
    {
        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);
        var item = new ExtractedTextItem
        {
            SegmentId = "doc:1",
            DocumentId = "doc",
            FileType = "ERB",
            RelativePath = "ERB\\Test.ERB",
            EncodingName = "UTF-8",
            SegmentType = "quoted-string",
            LineNumber = 1,
            OriginalText = "こんにちは",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            TranslatedText = "안녕하세요",
            Status = "번역 완료",
            ValidationStatus = "통과",
            WarningText = string.Empty,
        };
        viewModel.Items.Add(item);
        viewModel.FilterText = "안녕하세요";

        Assert.Single(viewModel.ItemsView.Cast<ExtractedTextItem>());

        item.TranslatedText = "잘 가";
        viewModel.HandleTranslatedTextEdited(item);
        viewModel.ItemsView.Refresh();

        Assert.False(viewModel.RefreshGridDuringTranslatedTextEdit);
        Assert.Single(viewModel.ItemsView.Cast<ExtractedTextItem>());

        viewModel.UseRegexFilter = true;

        Assert.Empty(viewModel.ItemsView.Cast<ExtractedTextItem>());
    }

    [Fact]
    public void HandleTranslatedTextEdited_RefreshesFilteredGridWhenOptionEnabled()
    {
        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);
        var item = new ExtractedTextItem
        {
            SegmentId = "doc:1",
            DocumentId = "doc",
            FileType = "ERB",
            RelativePath = "ERB\\Test.ERB",
            EncodingName = "UTF-8",
            SegmentType = "quoted-string",
            LineNumber = 1,
            OriginalText = "こんにちは",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            TranslatedText = "안녕하세요",
            Status = "번역 완료",
            ValidationStatus = "통과",
            WarningText = string.Empty,
        };
        viewModel.Items.Add(item);
        viewModel.FilterText = "안녕하세요";
        viewModel.RefreshGridDuringTranslatedTextEdit = true;

        Assert.Single(viewModel.ItemsView.Cast<ExtractedTextItem>());

        item.TranslatedText = "잘 가";
        viewModel.HandleTranslatedTextEdited(item);

        Assert.Empty(viewModel.ItemsView.Cast<ExtractedTextItem>());
    }

    [Fact]
    public void HandleTranslatedTextEdited_DoesNotRefreshStatusFilteredGridUntilManualRefresh()
    {
        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);
        var item = new ExtractedTextItem
        {
            SegmentId = "doc:1",
            DocumentId = "doc",
            FileType = "ERB",
            RelativePath = "ERB\\Test.ERB",
            EncodingName = "UTF-8",
            SegmentType = "quoted-string",
            LineNumber = 1,
            OriginalText = "こんにちは",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            TranslatedText = "안녕하세요",
            Status = "번역 완료",
            ValidationStatus = "통과",
            WarningText = string.Empty,
        };
        viewModel.Items.Add(item);
        viewModel.SelectedStatusFilter = "번역 완료";

        Assert.Single(viewModel.ItemsView.Cast<ExtractedTextItem>());

        item.TranslatedText = "잘 가";
        viewModel.HandleTranslatedTextEdited(item);
        viewModel.ItemsView.Refresh();

        Assert.False(viewModel.RefreshGridDuringTranslatedTextEdit);
        Assert.Single(viewModel.ItemsView.Cast<ExtractedTextItem>());

        viewModel.RefreshItemsView();

        Assert.Empty(viewModel.ItemsView.Cast<ExtractedTextItem>());
    }

    [Fact]
    public void GlobalReplace_DoesNotReadOrOverwriteFilterState()
    {
        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);
        var item = new ExtractedTextItem
        {
            SegmentId = "doc:1",
            DocumentId = "doc",
            FileType = "ERB",
            RelativePath = "ERB\\Test.ERB",
            EncodingName = "UTF-8",
            SegmentType = "quoted-string",
            LineNumber = 1,
            OriginalText = "こんにちは",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            TranslatedText = "안녕하세요",
            Status = "번역 완료",
            ValidationStatus = "통과",
            WarningText = string.Empty,
        };
        viewModel.Items.Add(item);
        viewModel.FilterText = "필터값";
        viewModel.UseRegexFilter = true;

        var replaceViewModel = viewModel.CreateGlobalReplaceViewModel();

        Assert.Equal(string.Empty, replaceViewModel.SearchText);
        Assert.False(replaceViewModel.UseRegex);

        replaceViewModel.SearchText = "안녕";
        replaceViewModel.ReplaceText = "반가워";
        replaceViewModel.UseRegex = false;

        viewModel.ApplyGlobalReplace(replaceViewModel);

        Assert.Equal("필터값", viewModel.FilterText);
        Assert.True(viewModel.UseRegexFilter);
        Assert.Equal("반가워하세요", item.TranslatedText);
    }

    [Fact]
    public void Filter_CanTargetSpecificSearchField()
    {
        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);
        var item = new ExtractedTextItem
        {
            SegmentId = "doc:1",
            DocumentId = "doc",
            FileType = "ERB",
            RelativePath = "ERB\\Heroine.ERB",
            EncodingName = "UTF-8",
            SegmentType = "quoted-string",
            LineNumber = 1,
            OriginalText = "こんにちは",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            TranslatedText = "안녕하세요",
            ReferenceResolutionStatus = "참조 확인됨",
            Status = "번역 완료",
            ValidationStatus = "통과",
            WarningText = string.Empty,
        };
        viewModel.Items.Add(item);

        viewModel.FilterText = "안녕하세요";
        Assert.Single(viewModel.ItemsView.Cast<ExtractedTextItem>());

        viewModel.SelectedSearchFieldFilter = "원문";
        Assert.Empty(viewModel.ItemsView.Cast<ExtractedTextItem>());

        viewModel.SelectedSearchFieldFilter = "번역문";
        Assert.Single(viewModel.ItemsView.Cast<ExtractedTextItem>());

        viewModel.FilterText = "Heroine";
        viewModel.SelectedSearchFieldFilter = "파일";
        Assert.Single(viewModel.ItemsView.Cast<ExtractedTextItem>());

        viewModel.SelectedSearchFieldFilter = "참조 상태";
        Assert.Empty(viewModel.ItemsView.Cast<ExtractedTextItem>());
    }

    [Fact]
    public void Filter_CanShowTranslationsInsideFunctionsOrExpressions()
    {
        var gameDirectory = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(gameDirectory);

        var sessionStateService = new ScanSessionStateService();
        sessionStateService.Save(BuildSessionWithFunctionExpressionSegments(gameDirectory), gameDirectory);

        var viewModel = new MainWindowViewModel(
            appConfigService: new AppConfigService(Path.Combine(_rootPath, "Config")),
            userDictionaryService: new UserDictionaryService(Path.Combine(_rootPath, "AppData")),
            scanSessionStateService: sessionStateService,
            translationProgressStateService: new TranslationProgressStateService(),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        viewModel.GameDirectory = gameDirectory;
        viewModel.SelectedSearchFieldFilter = "함수/표현식";

        var visibleItems = viewModel.ItemsView.Cast<ExtractedTextItem>().ToList();

        Assert.Equal(2, visibleItems.Count);
        Assert.Contains(visibleItems, item => item.OriginalText == "噴乳経験");
        Assert.Contains(visibleItems, item => item.OriginalText == "はい");
        Assert.DoesNotContain(visibleItems, item => item.OriginalText == "普通の文");

        viewModel.FilterText = "噴乳";

        var filteredItem = Assert.Single(viewModel.ItemsView.Cast<ExtractedTextItem>());
        Assert.Equal("噴乳経験", filteredItem.OriginalText);
    }

    [Fact]
    public void EzTransSettings_ArePersistedThroughConfig()
    {
        var configPath = Path.Combine(_rootPath, "Config");
        var appDataPath = Path.Combine(_rootPath, "AppData");
        var first = new MainWindowViewModel(
            appConfigService: new AppConfigService(configPath),
            userDictionaryService: new UserDictionaryService(appDataPath),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        first.EzTransInstallationPath = @"C:\Utils\ezTransXPggudor";
        first.EzTransProcessCount = 6;
        first.FlushPendingConfigSave();

        var second = new MainWindowViewModel(
            appConfigService: new AppConfigService(configPath),
            userDictionaryService: new UserDictionaryService(appDataPath),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        Assert.Equal(@"C:\Utils\ezTransXPggudor", second.EzTransInstallationPath);
        Assert.Equal(6, second.EzTransProcessCount);
    }

    [Fact]
    public void RefreshGridDuringTranslatedTextEdit_IsPersistedThroughConfig()
    {
        var configPath = Path.Combine(_rootPath, "Config");
        var appDataPath = Path.Combine(_rootPath, "AppData");
        var first = new MainWindowViewModel(
            appConfigService: new AppConfigService(configPath),
            userDictionaryService: new UserDictionaryService(appDataPath),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        first.RefreshGridDuringTranslatedTextEdit = true;
        first.FlushPendingConfigSave();

        var second = new MainWindowViewModel(
            appConfigService: new AppConfigService(configPath),
            userDictionaryService: new UserDictionaryService(appDataPath),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        Assert.True(second.RefreshGridDuringTranslatedTextEdit);
    }

    [Fact]
    public void LoggingOptions_ArePersistedThroughConfig()
    {
        var configPath = Path.Combine(_rootPath, "Config");
        var appDataPath = Path.Combine(_rootPath, "AppData");
        var first = new MainWindowViewModel(
            appConfigService: new AppConfigService(configPath),
            userDictionaryService: new UserDictionaryService(appDataPath),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        first.EnableResultStateLogging = true;
        first.EnableDictionaryHitLogging = true;
        first.FlushPendingConfigSave();

        var second = new MainWindowViewModel(
            appConfigService: new AppConfigService(configPath),
            userDictionaryService: new UserDictionaryService(appDataPath),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        Assert.True(second.EnableResultStateLogging);
        Assert.True(second.EnableDictionaryHitLogging);
    }

    [Fact]
    public void TryFormatRemainingTime_ReturnsMinuteAndSecondEstimate()
    {
        var formatted = MainWindowViewModel.TryFormatRemainingTime(0.25, TimeSpan.FromMinutes(1));

        Assert.Equal("3분", formatted);
    }

    [Fact]
    public void TryFormatRemainingTime_ReturnsNullWithoutProgress()
    {
        var formatted = MainWindowViewModel.TryFormatRemainingTime(0, TimeSpan.FromSeconds(30));

        Assert.Null(formatted);
    }

    [Fact]
    public void FormatDuration_ReturnsMinuteAndSecondText()
    {
        var formatted = MainWindowViewModel.FormatDuration(TimeSpan.FromSeconds(65));

        Assert.Equal("1분 5초", formatted);
    }

    private static ScanSession BuildSession(string gameDirectory)
    {
        var session = new ScanSession
        {
            GameRoot = gameDirectory,
        };

        var document = new SourceFileDocument
        {
            DocumentId = "ERB/Test.ERB",
            FullPath = Path.Combine(gameDirectory, "ERB", "Test.ERB"),
            RelativePath = Path.Combine("ERB", "Test.ERB"),
            FileType = "ERB",
            OriginalText = "PRINTFORMW \"こんにちは\"",
            EncodingInfo = new DetectedEncodingInfo
            {
                Encoding = new UTF8Encoding(true),
                Name = "UTF-8 BOM",
                Kind = DetectedEncodingKind.Utf8Bom,
                HasBom = true,
            },
            NewLineSequence = Environment.NewLine,
            CsvKind = CsvDocumentKind.None,
        };

        document.Segments.Add(new TextSegment
        {
            SegmentId = "ERB/Test.ERB:0",
            DocumentId = document.DocumentId,
            SegmentType = "quoted-string",
            AbsoluteStart = 12,
            Length = 5,
            LineNumber = 1,
            OriginalText = "こんにちは",
        });

        session.Documents[document.DocumentId] = document;
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = "ERB/Test.ERB:0",
            DocumentId = document.DocumentId,
            FileType = "ERB",
            RelativePath = document.RelativePath,
            EncodingName = "UTF-8 BOM",
            SegmentType = "quoted-string",
            LineNumber = 1,
            OriginalText = "こんにちは",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            WarningText = string.Empty,
        });
        session.Metrics["Documents"] = 1;
        session.Metrics["Items"] = 1;
        session.Metrics["ErbItems"] = 1;
        session.Metrics["CsvItems"] = 0;
        session.Metrics["Warnings"] = 0;
        session.Metrics["JosaPatterns"] = 0;
        return session;
    }

    private static ScanSession BuildSessionWithDuplicateOriginals(string gameDirectory)
    {
        var session = new ScanSession
        {
            GameRoot = gameDirectory,
        };

        var document = new SourceFileDocument
        {
            DocumentId = "ERB/Test.ERB",
            FullPath = Path.Combine(gameDirectory, "ERB", "Test.ERB"),
            RelativePath = Path.Combine("ERB", "Test.ERB"),
            FileType = "ERB",
            OriginalText = "PRINTFORMW \"こんにちは\"\nPRINTFORMW \"こんにちは\"",
            EncodingInfo = new DetectedEncodingInfo
            {
                Encoding = new UTF8Encoding(true),
                Name = "UTF-8 BOM",
                Kind = DetectedEncodingKind.Utf8Bom,
                HasBom = true,
            },
            NewLineSequence = Environment.NewLine,
            CsvKind = CsvDocumentKind.None,
        };

        document.Segments.Add(new TextSegment
        {
            SegmentId = "ERB/Test.ERB:0",
            DocumentId = document.DocumentId,
            SegmentType = "quoted-string",
            AbsoluteStart = 12,
            Length = 5,
            LineNumber = 1,
            OriginalText = "こんにちは",
        });
        document.Segments.Add(new TextSegment
        {
            SegmentId = "ERB/Test.ERB:1",
            DocumentId = document.DocumentId,
            SegmentType = "quoted-string",
            AbsoluteStart = 31,
            Length = 5,
            LineNumber = 2,
            OriginalText = "こんにちは",
        });

        session.Documents[document.DocumentId] = document;
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = "ERB/Test.ERB:0",
            DocumentId = document.DocumentId,
            FileType = "ERB",
            RelativePath = document.RelativePath,
            EncodingName = "UTF-8 BOM",
            SegmentType = "quoted-string",
            LineNumber = 1,
            OriginalText = "こんにちは",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            WarningText = string.Empty,
        });
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = "ERB/Test.ERB:1",
            DocumentId = document.DocumentId,
            FileType = "ERB",
            RelativePath = document.RelativePath,
            EncodingName = "UTF-8 BOM",
            SegmentType = "quoted-string",
            LineNumber = 2,
            OriginalText = "こんにちは",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            WarningText = string.Empty,
        });
        session.Metrics["Documents"] = 1;
        session.Metrics["Items"] = 2;
        session.Metrics["ErbItems"] = 2;
        session.Metrics["CsvItems"] = 0;
        session.Metrics["Warnings"] = 0;
        session.Metrics["JosaPatterns"] = 0;
        return session;
    }

    private static ScanSession BuildSessionWithSameOriginalInDifferentFiles(string gameDirectory)
    {
        var session = new ScanSession
        {
            GameRoot = gameDirectory,
        };

        AddDocument("ERB/Visible.ERB", Path.Combine("ERB", "Visible.ERB"));
        AddDocument("ERB/Hidden.ERB", Path.Combine("ERB", "Hidden.ERB"));
        session.Metrics["Documents"] = 2;
        session.Metrics["Items"] = 2;
        session.Metrics["ErbItems"] = 2;
        session.Metrics["CsvItems"] = 0;
        session.Metrics["Warnings"] = 0;
        session.Metrics["JosaPatterns"] = 0;
        return session;

        void AddDocument(string documentId, string relativePath)
        {
            var document = new SourceFileDocument
            {
                DocumentId = documentId,
                FullPath = Path.Combine(gameDirectory, relativePath),
                RelativePath = relativePath,
                FileType = "ERB",
                OriginalText = "PRINTFORMW \"こんにちは\"",
                EncodingInfo = new DetectedEncodingInfo
                {
                    Encoding = new UTF8Encoding(true),
                    Name = "UTF-8 BOM",
                    Kind = DetectedEncodingKind.Utf8Bom,
                    HasBom = true,
                },
                NewLineSequence = Environment.NewLine,
                CsvKind = CsvDocumentKind.None,
            };
            document.Segments.Add(new TextSegment
            {
                SegmentId = $"{documentId}:0",
                DocumentId = document.DocumentId,
                SegmentType = "quoted-string",
                AbsoluteStart = 12,
                Length = 5,
                LineNumber = 1,
                OriginalText = "こんにちは",
            });
            session.Documents[document.DocumentId] = document;
            session.Items.Add(new ExtractedTextItem
            {
                SegmentId = $"{documentId}:0",
                DocumentId = document.DocumentId,
                FileType = "ERB",
                RelativePath = document.RelativePath,
                EncodingName = "UTF-8 BOM",
                SegmentType = "quoted-string",
                LineNumber = 1,
                OriginalText = "こんにちは",
                CsvFieldRole = CsvFieldRole.TranslatableValue,
                WarningText = string.Empty,
            });
        }
    }

    private static ScanSession BuildReferenceSession(string gameDirectory)
    {
        var session = new ScanSession
        {
            GameRoot = gameDirectory,
        };
        var relativePath = Path.Combine("ERB", "Test.ERB");
        var document = new SourceFileDocument
        {
            DocumentId = "ERB/Test.ERB",
            FullPath = Path.Combine(gameDirectory, "ERB", "Test.ERB"),
            RelativePath = relativePath,
            FileType = "ERB",
            OriginalText = "PRINTFORMW GETNUM(CFLAG,\"外見年齢\")",
            EncodingInfo = new DetectedEncodingInfo
            {
                Encoding = new UTF8Encoding(true),
                Name = "UTF-8 BOM",
                Kind = DetectedEncodingKind.Utf8Bom,
                HasBom = true,
            },
            NewLineSequence = Environment.NewLine,
            CsvKind = CsvDocumentKind.None,
        };

        document.Segments.Add(new TextSegment
        {
            SegmentId = "ERB/Test.ERB:0",
            DocumentId = document.DocumentId,
            SegmentType = "quoted-string",
            AbsoluteStart = 22,
            Length = 4,
            LineNumber = 1,
            OriginalText = "外見年齢",
            SymbolNamespace = "CFLAG",
            OriginalSymbolKey = "外見年齢",
            IsReferenceBearingKey = true,
        });
        document.SymbolReferences.Add(new ErbSymbolReference
        {
            DocumentId = document.DocumentId,
            Namespace = "CFLAG",
            Kind = ErbSymbolReferenceKind.DirectLiteral,
            ResolutionKind = SymbolReferenceResolutionKind.Direct,
            OriginalKey = "外見年齢",
            AbsoluteStart = 22,
            Length = 4,
            LineNumber = 1,
            CandidateKeys = ["外見年齢"],
        });

        session.Documents[document.DocumentId] = document;
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = "ERB/Test.ERB:0",
            DocumentId = document.DocumentId,
            FileType = "ERB",
            RelativePath = relativePath,
            EncodingName = "UTF-8 BOM",
            SegmentType = "quoted-string",
            LineNumber = 1,
            OriginalText = "外見年齢",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            SymbolNamespace = "CFLAG",
            OriginalSymbolKey = "外見年齢",
            IsReferenceBearingKey = true,
            ReferenceOriginalSymbolKey = "外見年齢",
            ReferenceImpactCount = 1,
            RequiresReferenceRewrite = true,
            ReferenceResolutionStatus = "직접 참조만",
            WarningText = string.Empty,
        });
        session.Metrics["Documents"] = 1;
        session.Metrics["Items"] = 1;
        session.Metrics["ErbItems"] = 1;
        session.Metrics["CsvItems"] = 0;
        session.Metrics["Warnings"] = 0;
        session.Metrics["JosaPatterns"] = 0;
        return session;
    }

    private static ScanSession BuildSessionWithFunctionExpressionSegments(string gameDirectory)
    {
        var session = new ScanSession
        {
            GameRoot = gameDirectory,
        };

        const string content = "PRINTFORMW GETNUM(EXP,\"噴乳経験\")\nPRINTFORMW \"普通の文\"\nPRINTFORML \\@ FLAG ? はい # いいえ \\@";
        var document = new SourceFileDocument
        {
            DocumentId = "ERB/Test.ERB",
            FullPath = Path.Combine(gameDirectory, "ERB", "Test.ERB"),
            RelativePath = Path.Combine("ERB", "Test.ERB"),
            FileType = "ERB",
            OriginalText = content,
            EncodingInfo = new DetectedEncodingInfo
            {
                Encoding = new UTF8Encoding(true),
                Name = "UTF-8 BOM",
                Kind = DetectedEncodingKind.Utf8Bom,
                HasBom = true,
            },
            NewLineSequence = "\n",
            CsvKind = CsvDocumentKind.None,
        };

        AddSegment("ERB/Test.ERB:0", "quoted-string", "噴乳経験");
        AddSegment("ERB/Test.ERB:1", "quoted-string", "普通の文");
        AddSegment("ERB/Test.ERB:2", "inline-conditional-left", "はい");
        session.Documents[document.DocumentId] = document;
        session.Items.AddRange(document.Segments.Select(segment => new ExtractedTextItem
        {
            SegmentId = segment.SegmentId,
            DocumentId = document.DocumentId,
            FileType = "ERB",
            RelativePath = document.RelativePath,
            EncodingName = "UTF-8 BOM",
            SegmentType = segment.SegmentType,
            LineNumber = segment.LineNumber,
            OriginalText = segment.OriginalText,
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            WarningText = string.Empty,
        }));
        session.Metrics["Documents"] = 1;
        session.Metrics["Items"] = session.Items.Count;
        session.Metrics["ErbItems"] = session.Items.Count;
        session.Metrics["CsvItems"] = 0;
        session.Metrics["Warnings"] = 0;
        session.Metrics["JosaPatterns"] = 0;
        return session;

        void AddSegment(string segmentId, string segmentType, string originalText)
        {
            var absoluteStart = content.IndexOf(originalText, StringComparison.Ordinal);
            document.Segments.Add(new TextSegment
            {
                SegmentId = segmentId,
                DocumentId = document.DocumentId,
                SegmentType = segmentType,
                AbsoluteStart = absoluteStart,
                Length = originalText.Length,
                LineNumber = content[..absoluteStart].Count(ch => ch == '\n') + 1,
                OriginalText = originalText,
            });
        }
    }

    private static ScanSession BuildPhasedSession(string gameDirectory)
    {
        var session = new ScanSession
        {
            GameRoot = gameDirectory,
        };

        var csvReferenceDocument = new SourceFileDocument
        {
            DocumentId = "CSV/Talent.csv",
            FullPath = Path.Combine(gameDirectory, "CSV", "Talent.csv"),
            RelativePath = Path.Combine("CSV", "Talent.csv"),
            FileType = "CSV",
            OriginalText = "178,快楽,;説明",
            EncodingInfo = new DetectedEncodingInfo
            {
                Encoding = new UTF8Encoding(true),
                Name = "UTF-8 BOM",
                Kind = DetectedEncodingKind.Utf8Bom,
                HasBom = true,
            },
            NewLineSequence = "\n",
            CsvKind = CsvDocumentKind.IdFirstTable,
        };
        csvReferenceDocument.Segments.Add(new TextSegment
        {
            SegmentId = "CSV/Talent.csv:0",
            DocumentId = csvReferenceDocument.DocumentId,
            SegmentType = "csv-idfirst-field-1",
            AbsoluteStart = 4,
            Length = 2,
            LineNumber = 1,
            OriginalText = "快楽",
            FieldIndex = 1,
            SourceKey = "178",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            SymbolNamespace = "TALENT",
            OriginalSymbolKey = "快楽",
            IsReferenceBearingKey = true,
        });
        session.Documents[csvReferenceDocument.DocumentId] = csvReferenceDocument;

        var csvDocument = new SourceFileDocument
        {
            DocumentId = "CSV/Terms.csv",
            FullPath = Path.Combine(gameDirectory, "CSV", "Terms.csv"),
            RelativePath = Path.Combine("CSV", "Terms.csv"),
            FileType = "CSV",
            OriginalText = "快楽値",
            EncodingInfo = new DetectedEncodingInfo
            {
                Encoding = new UTF8Encoding(true),
                Name = "UTF-8 BOM",
                Kind = DetectedEncodingKind.Utf8Bom,
                HasBom = true,
            },
            NewLineSequence = "\n",
            CsvKind = CsvDocumentKind.GenericTable,
        };
        csvDocument.Segments.Add(new TextSegment
        {
            SegmentId = "CSV/Terms.csv:0",
            DocumentId = csvDocument.DocumentId,
            SegmentType = "csv-generic-field-1",
            AbsoluteStart = 0,
            Length = 3,
            LineNumber = 1,
            OriginalText = "快楽値",
            FieldIndex = 1,
            SourceKey = "Terms:快楽値",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
        });
        session.Documents[csvDocument.DocumentId] = csvDocument;

        var erhDocument = new SourceFileDocument
        {
            DocumentId = "ERH/Terms.ERH",
            FullPath = Path.Combine(gameDirectory, "ERH", "Terms.ERH"),
            RelativePath = Path.Combine("ERH", "Terms.ERH"),
            FileType = "ERH",
            OriginalText = "\"快楽値ゲージ\"",
            EncodingInfo = new DetectedEncodingInfo
            {
                Encoding = new UTF8Encoding(true),
                Name = "UTF-8 BOM",
                Kind = DetectedEncodingKind.Utf8Bom,
                HasBom = true,
            },
            NewLineSequence = "\n",
            CsvKind = CsvDocumentKind.None,
        };
        erhDocument.Segments.Add(new TextSegment
        {
            SegmentId = "ERH/Terms.ERH:0",
            DocumentId = erhDocument.DocumentId,
            SegmentType = "quoted-string",
            AbsoluteStart = 1,
            Length = 6,
            LineNumber = 1,
            OriginalText = "快楽値ゲージ",
        });
        session.Documents[erhDocument.DocumentId] = erhDocument;

        var erbDocument = new SourceFileDocument
        {
            DocumentId = "ERB/Test.ERB",
            FullPath = Path.Combine(gameDirectory, "ERB", "Test.ERB"),
            RelativePath = Path.Combine("ERB", "Test.ERB"),
            FileType = "ERB",
            OriginalText = "\"快楽値ゲージが上がった\"",
            EncodingInfo = new DetectedEncodingInfo
            {
                Encoding = new UTF8Encoding(true),
                Name = "UTF-8 BOM",
                Kind = DetectedEncodingKind.Utf8Bom,
                HasBom = true,
            },
            NewLineSequence = "\n",
            CsvKind = CsvDocumentKind.None,
        };
        erbDocument.Segments.Add(new TextSegment
        {
            SegmentId = "ERB/Test.ERB:0",
            DocumentId = erbDocument.DocumentId,
            SegmentType = "quoted-string",
            AbsoluteStart = 1,
            Length = 10,
            LineNumber = 1,
            OriginalText = "快楽値ゲージが上がった",
        });
        session.Documents[erbDocument.DocumentId] = erbDocument;

        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = "ERB/Test.ERB:0",
            DocumentId = "ERB/Test.ERB",
            FileType = "ERB",
            RelativePath = Path.Combine("ERB", "Test.ERB"),
            EncodingName = "utf-8",
            SegmentType = "quoted-string",
            LineNumber = 1,
            OriginalText = "快楽値ゲージが上がった",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            WarningText = string.Empty,
        });
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = "CSV/Terms.csv:0",
            DocumentId = "CSV/Terms.csv",
            FileType = "CSV",
            RelativePath = Path.Combine("CSV", "Terms.csv"),
            EncodingName = "utf-8",
            SegmentType = "csv-generic-field-1",
            LineNumber = 1,
            OriginalText = "快楽値",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            SourceKey = "Terms:快楽値",
            WarningText = string.Empty,
        });
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = "ERH/Terms.ERH:0",
            DocumentId = "ERH/Terms.ERH",
            FileType = "ERH",
            RelativePath = Path.Combine("ERH", "Terms.ERH"),
            EncodingName = "utf-8",
            SegmentType = "quoted-string",
            LineNumber = 1,
            OriginalText = "快楽値ゲージ",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            WarningText = string.Empty,
        });
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = "CSV/Talent.csv:0",
            DocumentId = "CSV/Talent.csv",
            FileType = "CSV",
            RelativePath = Path.Combine("CSV", "Talent.csv"),
            EncodingName = "utf-8",
            SegmentType = "csv-idfirst-field-1",
            LineNumber = 1,
            OriginalText = "快楽",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            SourceKey = "178",
            SymbolNamespace = "TALENT",
            OriginalSymbolKey = "快楽",
            IsReferenceBearingKey = true,
            WarningText = string.Empty,
        });

        session.Metrics["Documents"] = 4;
        session.Metrics["Items"] = session.Items.Count;
        session.Metrics["ErbItems"] = 2;
        session.Metrics["CsvItems"] = 2;
        session.Metrics["Warnings"] = 0;
        session.Metrics["JosaPatterns"] = 0;
        return session;
    }

    private sealed class RecordingSqliteProjectStateStore : SqliteProjectStateStore
    {
        public int SnapshotSaveCount { get; private set; }

        public int UpsertItemsCallCount { get; private set; }

        public int DeleteItemsCallCount { get; private set; }

        public List<int> UpsertBatchSizes { get; } = [];

        public override void SaveTranslationProgressSnapshot(string projectDataDirectory, IEnumerable<ExtractedTextItem> items)
        {
            SnapshotSaveCount++;
            base.SaveTranslationProgressSnapshot(projectDataDirectory, items);
        }

        public override void UpsertTranslationProgressItems(string projectDataDirectory, IEnumerable<ExtractedTextItem> items)
        {
            var itemList = items.ToList();
            UpsertItemsCallCount++;
            UpsertBatchSizes.Add(itemList.Count);
            base.UpsertTranslationProgressItems(projectDataDirectory, itemList);
        }

        public override void DeleteTranslationProgressItems(string projectDataDirectory, IEnumerable<string> segmentIds)
        {
            DeleteItemsCallCount++;
            base.DeleteTranslationProgressItems(projectDataDirectory, segmentIds);
        }

        public void ResetCounts()
        {
            SnapshotSaveCount = 0;
            UpsertItemsCallCount = 0;
            DeleteItemsCallCount = 0;
            UpsertBatchSizes.Clear();
        }
    }

    private sealed class FakeTranslationProviderFactory(ITranslationProvider provider) : ITranslationProviderFactory
    {
        public ITranslationProvider Create(ProviderSettings settings) => provider;
    }

    private sealed class RecordingProvider(Func<IReadOnlyList<ProtectedSegment>, TranslationProviderResult> handler) : ITranslationProvider
    {
        public List<IReadOnlyList<string>> RequestHistory { get; } = [];
        public List<IReadOnlyList<GlossaryHint>> GlossaryHistory { get; } = [];

        public Task<TranslationProviderResult> TranslateAsync(
            IReadOnlyList<ProtectedSegment> requests,
            ProviderSettings settings,
            CancellationToken cancellationToken,
            IReadOnlyList<GlossaryHint>? glossaryHints = null)
        {
            RequestHistory.Add(requests.Select(request => request.Id).ToList());
            GlossaryHistory.Add((glossaryHints ?? []).ToList());
            return Task.FromResult(handler(requests));
        }
    }
}
