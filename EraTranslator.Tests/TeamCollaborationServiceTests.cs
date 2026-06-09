using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class TeamCollaborationServiceTests : IDisposable
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
    public void ApplySyncResponse_AppliesSharedKeyAndStoresServerMetadata()
    {
        var context = BuildContext();
        var item = BuildReferenceItem();
        var state = new TeamProjectState
        {
            LocalSourceScanRevisionId = "scan-1",
        };
        var sync = new TeamSyncResponse
        {
            ProjectId = "project-1",
            ScanRevisionId = "scan-1",
            SourceArchiveSha256 = new string('a', 64),
            WorkItems =
            [
                new TeamWorkItemDto
                {
                    Id = "work-1",
                    SegmentId = item.SegmentId,
                    ItemRevision = 3,
                    Translation = "서버 일반 번역",
                    Status = "approved",
                },
            ],
            SharedKeys =
            [
                new TeamSharedKeyDto
                {
                    Id = "shared-1",
                    Namespace = "TALENT",
                    Key = "気丈",
                    Translation = "기개",
                    SharedRevision = 5,
                    Status = "approved",
                },
            ],
        };
        var service = new TeamCollaborationService(stateService: new TeamProjectStateService());

        var result = service.ApplySyncResponse(context, sync, [item], state);

        Assert.Equal(1, result.WorkItemMetadataCount);
        Assert.Equal(1, result.SharedKeyMetadataCount);
        Assert.Equal("기개", item.TranslatedText);
        Assert.Equal("번역 완료", item.Status);
        var restoredState = new TeamProjectStateService().Load(context);
        Assert.Equal("work-1", restoredState.WorkItemsBySegmentId[item.SegmentId].ServerItemId);
        var sharedLookupKey = TeamCollaborationService.CreateSharedKeyLookupKey("TALENT", "気丈");
        Assert.Equal("shared-1", restoredState.SharedKeysByLookupKey[sharedLookupKey].ServerSharedKeyId);
    }

    [Fact]
    public void BuildSubmitRequest_UsesDirtyWorkItemsAndSharedKeys()
    {
        var context = BuildContext();
        var item = BuildReferenceItem();
        item.ApplyTranslationState("번역 완료", "통과", string.Empty, canSave: true, translatedText: "기개 수정");
        var sharedLookupKey = TeamCollaborationService.CreateSharedKeyLookupKey("TALENT", "気丈");
        var state = new TeamProjectState
        {
            LastSyncedScanRevisionId = "scan-1",
            LocalSourceScanRevisionId = "scan-1",
            WorkItemsBySegmentId = new Dictionary<string, TeamWorkItemState>
            {
                [item.SegmentId] = new()
                {
                    ServerItemId = "work-1",
                    ServerRevision = 3,
                    LastSubmittedTranslatedText = "기개",
                },
            },
            SharedKeysByLookupKey = new Dictionary<string, TeamSharedKeyState>
            {
                [sharedLookupKey] = new()
                {
                    ServerSharedKeyId = "shared-1",
                    ServerSharedRevision = 5,
                    Namespace = "TALENT",
                    Key = "気丈",
                    LastSubmittedTranslatedText = "기개",
                },
            },
        };

        var result = new TeamCollaborationService().BuildSubmitRequest(context, "scan-1", "client-1", [item], state);

        Assert.Equal(1, result.WorkItemChangeCount);
        Assert.Equal(1, result.SharedKeyChangeCount);
        Assert.Equal("work-1", result.Request.WorkItems[0].Id);
        Assert.Equal(3, result.Request.WorkItems[0].BaseRevision);
        Assert.Equal("shared-1", result.Request.SharedKeys[0].Id);
        Assert.Equal(5, result.Request.SharedKeys[0].BaseRevision);
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

    private static ExtractedTextItem BuildReferenceItem()
    {
        return new ExtractedTextItem
        {
            SegmentId = "seg-1",
            DocumentId = "doc-1",
            FileType = "CSV",
            RelativePath = "CSV/Talent.csv",
            EncodingName = "UTF-8",
            SegmentType = "csv-reference-key",
            LineNumber = 1,
            OriginalText = "気丈",
            SymbolNamespace = "TALENT",
            OriginalSymbolKey = "気丈",
            IsReferenceBearingKey = true,
        };
    }
}
