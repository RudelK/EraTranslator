namespace EraTranslator.Models;

public abstract record ProjectContext(
    ProjectMode Mode,
    string GameDirectory,
    string OutputDirectory,
    string ProjectDataDirectory,
    string ProjectDictionaryDirectory);

public sealed record LocalProjectContext(
    string GameDirectory,
    string OutputDirectory,
    string ProjectDataDirectory,
    string ProjectDictionaryDirectory)
    : ProjectContext(ProjectMode.Local, GameDirectory, OutputDirectory, ProjectDataDirectory, ProjectDictionaryDirectory);

public sealed record TeamProjectContext(
    string ServerUrl,
    string ProjectId,
    string DisplayName,
    string ClientId,
    string WorkspaceRoot,
    string SourceDirectory,
    string TeamOutputDirectory,
    string TeamProjectDataDirectory,
    string TeamProjectDictionaryDirectory,
    string? LastSyncedScanRevisionId = null,
    string? LocalSourceScanRevisionId = null)
    : ProjectContext(ProjectMode.Team, SourceDirectory, TeamOutputDirectory, TeamProjectDataDirectory, TeamProjectDictionaryDirectory);
