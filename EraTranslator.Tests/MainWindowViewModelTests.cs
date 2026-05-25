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
}
