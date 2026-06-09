using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class TeamProjectStateServiceTests : IDisposable
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
    public void SaveAndLoad_RoundTripsTeamState()
    {
        var context = BuildContext();
        var service = new TeamProjectStateService();

        service.Save(context, new TeamProjectState
        {
            LastSyncedScanRevisionId = "scan-1",
            LocalSourceScanRevisionId = "scan-1",
            OfflineSubmissionQueue =
            [
                new TeamOfflineSubmission
                {
                    SubmissionId = "submission-1",
                    ScanRevisionId = "scan-1",
                    WorkItems =
                    [
                        new TeamOfflineSubmissionChange
                        {
                            Id = "work-1",
                            OriginalText = "原文",
                            TranslatedText = "번역",
                            BaseRevision = 3,
                        },
                    ],
                },
            ],
        });

        var actual = service.Load(context);

        Assert.Equal("scan-1", actual.LastSyncedScanRevisionId);
        Assert.Equal("scan-1", actual.LocalSourceScanRevisionId);
        Assert.Equal(context.TeamProjectDictionaryDirectory, actual.TeamProjectDictionaryPath);
        Assert.Single(actual.OfflineSubmissionQueue);
        Assert.Equal("submission-1", actual.OfflineSubmissionQueue[0].SubmissionId);
        Assert.Equal("work-1", actual.OfflineSubmissionQueue[0].WorkItems[0].Id);
    }

    private TeamProjectContext BuildContext()
    {
        return new TeamProjectContext(
            "http://localhost:8000",
            "project-1",
            "tester",
            "client-1",
            _rootPath,
            Path.Combine(_rootPath, "source"),
            Path.Combine(_rootPath, "output"),
            Path.Combine(_rootPath, ".era-translator"),
            Path.Combine(_rootPath, ".era-translator", "dictionaries"));
    }
}
