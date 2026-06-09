using System.Text.Json;
using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class TeamProjectStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public string GetStatePath(TeamProjectContext context)
    {
        return Path.Combine(context.TeamProjectDataDirectory, "team-state.json");
    }

    public TeamProjectState Load(TeamProjectContext context)
    {
        var path = GetStatePath(context);
        if (!File.Exists(path))
        {
            return new TeamProjectState
            {
                TeamProjectDictionaryPath = context.TeamProjectDictionaryDirectory,
            };
        }

        try
        {
            var json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<TeamProjectState>(json, JsonOptions);
            return state is null
                ? new TeamProjectState { TeamProjectDictionaryPath = context.TeamProjectDictionaryDirectory }
                : state;
        }
        catch (JsonException)
        {
            return new TeamProjectState
            {
                TeamProjectDictionaryPath = context.TeamProjectDictionaryDirectory,
            };
        }
    }

    public void Save(TeamProjectContext context, TeamProjectState state)
    {
        Directory.CreateDirectory(context.TeamProjectDataDirectory);
        var normalizedState = new TeamProjectState
        {
            LastSyncedScanRevisionId = state.LastSyncedScanRevisionId,
            LocalSourceScanRevisionId = state.LocalSourceScanRevisionId,
            TeamProjectDictionaryPath = string.IsNullOrWhiteSpace(state.TeamProjectDictionaryPath)
                ? context.TeamProjectDictionaryDirectory
                : state.TeamProjectDictionaryPath,
            SourceArchiveSha256 = state.SourceArchiveSha256,
            WorkItemsBySegmentId = state.WorkItemsBySegmentId,
            SharedKeysByLookupKey = state.SharedKeysByLookupKey,
            ConflictIdsByTargetId = state.ConflictIdsByTargetId,
            OfflineSubmissionQueue = state.OfflineSubmissionQueue,
        };
        File.WriteAllText(GetStatePath(context), JsonSerializer.Serialize(normalizedState, JsonOptions));
    }
}
