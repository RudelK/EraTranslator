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
    public void EnableResultStateLogging_IsPersistedThroughConfig()
    {
        var configPath = Path.Combine(_rootPath, "Config");
        var appDataPath = Path.Combine(_rootPath, "AppData");
        var first = new MainWindowViewModel(
            appConfigService: new AppConfigService(configPath),
            userDictionaryService: new UserDictionaryService(appDataPath),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        first.EnableResultStateLogging = true;
        first.FlushPendingConfigSave();

        var second = new MainWindowViewModel(
            appConfigService: new AppConfigService(configPath),
            userDictionaryService: new UserDictionaryService(appDataPath),
            detectSampleDirectory: false,
            restoreLastSessionOnStartup: false);

        Assert.True(second.EnableResultStateLogging);
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
}
