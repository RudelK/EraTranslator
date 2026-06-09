using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class ProjectContextFactory
{
    public ProjectContext Create(AppConfig config)
    {
        return config.ProjectMode == ProjectMode.Team
            ? CreateTeam(config)
            : CreateLocal(config);
    }

    public LocalProjectContext CreateLocal(AppConfig config)
    {
        var projectDataDirectory = ResolveLocalProjectDataDirectory(config.GameDirectory, config.OutputDirectory);
        return new LocalProjectContext(
            config.GameDirectory,
            config.OutputDirectory,
            projectDataDirectory,
            projectDataDirectory);
    }

    public TeamProjectContext CreateTeam(AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.TeamProjectId))
        {
            throw new InvalidOperationException("TeamProjectId is required for team mode.");
        }

        var workspaceRoot = string.IsNullOrWhiteSpace(config.TeamWorkspaceRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EraTranslator", "TeamWorkspaces")
            : config.TeamWorkspaceRoot;
        var projectRoot = Path.Combine(workspaceRoot, SanitizePathComponent(config.TeamProjectId));
        var sourceDirectory = Path.Combine(projectRoot, "source");
        var outputDirectory = Path.Combine(projectRoot, "output");
        var projectDataDirectory = Path.Combine(projectRoot, ".era-translator");
        var dictionaryDirectory = Path.Combine(projectDataDirectory, "dictionaries");

        return new TeamProjectContext(
            config.TeamServerUrl.TrimEnd('/'),
            config.TeamProjectId,
            config.TeamDisplayName,
            string.IsNullOrWhiteSpace(config.ClientId) ? Guid.NewGuid().ToString("N") : config.ClientId,
            workspaceRoot,
            sourceDirectory,
            outputDirectory,
            projectDataDirectory,
            dictionaryDirectory);
    }

    public void EnsureWorkspace(TeamProjectContext context)
    {
        Directory.CreateDirectory(context.SourceDirectory);
        Directory.CreateDirectory(context.TeamOutputDirectory);
        Directory.CreateDirectory(context.TeamProjectDataDirectory);
        Directory.CreateDirectory(context.TeamProjectDictionaryDirectory);
    }

    public static string ResolveLocalProjectDataDirectory(string gameDirectory, string outputDirectory)
    {
        return !string.IsNullOrWhiteSpace(outputDirectory)
            ? outputDirectory
            : gameDirectory;
    }

    private static string SanitizePathComponent(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? Guid.NewGuid().ToString("N") : sanitized;
    }
}
