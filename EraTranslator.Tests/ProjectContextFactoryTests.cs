using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class ProjectContextFactoryTests : IDisposable
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
    public void Create_LocalMode_UsesOutputDirectoryAsProjectDataDirectory()
    {
        var gameDirectory = Path.Combine(_rootPath, "game");
        var outputDirectory = Path.Combine(_rootPath, "out");
        var context = new ProjectContextFactory().Create(new AppConfig
        {
            GameDirectory = gameDirectory,
            OutputDirectory = outputDirectory,
        });

        var localContext = Assert.IsType<LocalProjectContext>(context);
        Assert.Equal(ProjectMode.Local, localContext.Mode);
        Assert.Equal(gameDirectory, localContext.GameDirectory);
        Assert.Equal(outputDirectory, localContext.OutputDirectory);
        Assert.Equal(outputDirectory, localContext.ProjectDataDirectory);
    }

    [Fact]
    public void Create_TeamMode_SeparatesSourceOutputAndStateDirectories()
    {
        var workspaceRoot = Path.Combine(_rootPath, "team");
        var context = new ProjectContextFactory().Create(new AppConfig
        {
            ProjectMode = ProjectMode.Team,
            TeamServerUrl = "http://localhost:8000",
            TeamProjectId = "era/project:alpha",
            TeamDisplayName = "translator",
            ClientId = "client-1",
            TeamWorkspaceRoot = workspaceRoot,
        });

        var teamContext = Assert.IsType<TeamProjectContext>(context);
        Assert.Equal(ProjectMode.Team, teamContext.Mode);
        Assert.Equal("http://localhost:8000", teamContext.ServerUrl);
        Assert.Equal("era/project:alpha", teamContext.ProjectId);
        Assert.Equal(Path.Combine(workspaceRoot, "era_project_alpha", "source"), teamContext.SourceDirectory);
        Assert.Equal(Path.Combine(workspaceRoot, "era_project_alpha", "output"), teamContext.TeamOutputDirectory);
        Assert.Equal(Path.Combine(workspaceRoot, "era_project_alpha", ".era-translator"), teamContext.TeamProjectDataDirectory);
        Assert.Equal(Path.Combine(workspaceRoot, "era_project_alpha", ".era-translator", "dictionaries"), teamContext.TeamProjectDictionaryDirectory);
    }

    [Fact]
    public void Create_TeamMode_UsesProgramTeamWorkspacesFolderByDefault()
    {
        var factory = new ProjectContextFactory(_rootPath);

        var context = factory.Create(new AppConfig
        {
            ProjectMode = ProjectMode.Team,
            TeamServerUrl = "http://localhost:8000",
            TeamProjectId = "team-1",
            ClientId = "client-1",
        });

        var teamContext = Assert.IsType<TeamProjectContext>(context);
        Assert.Equal(Path.Combine(_rootPath, "TeamWorkspaces"), teamContext.WorkspaceRoot);
        Assert.Equal(Path.Combine(_rootPath, "TeamWorkspaces", "team-1", "source"), teamContext.SourceDirectory);
    }
}
