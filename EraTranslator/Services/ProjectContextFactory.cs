using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class ProjectContextFactory(string? baseDirectory = null, string teamWorkspaceFolderName = "TeamWorkspaces")
{
    private readonly string _baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
        ? AppContext.BaseDirectory
        : baseDirectory;

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
            ? GetDefaultTeamWorkspaceRoot()
            : config.TeamWorkspaceRoot;
        if (string.IsNullOrWhiteSpace(config.TeamWorkspaceRoot))
        {
            MoveDirectoryIfMissing(workspaceRoot, GetLegacyDefaultTeamWorkspaceRoot());
        }

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

    public string GetDefaultTeamWorkspaceRoot()
    {
        return Path.Combine(_baseDirectory, teamWorkspaceFolderName);
    }

    public static string GetLegacyDefaultTeamWorkspaceRoot()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EraTranslator", "TeamWorkspaces");
    }

    private static string SanitizePathComponent(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? Guid.NewGuid().ToString("N") : sanitized;
    }

    private static void MoveDirectoryIfMissing(string targetPath, string sourcePath)
    {
        if (Directory.Exists(targetPath)
            || !Directory.Exists(sourcePath)
            || string.Equals(Path.GetFullPath(targetPath), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var parentDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        try
        {
            Directory.Move(sourcePath, targetPath);
        }
        catch
        {
            CopyDirectory(sourcePath, targetPath);
            try
            {
                Directory.Delete(sourcePath, recursive: true);
            }
            catch
            {
                // Leave the legacy workspace in place if cleanup is blocked.
            }
        }
    }

    private static void CopyDirectory(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(targetPath);
        foreach (var directory in Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(targetPath, Path.GetRelativePath(sourcePath, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var targetFile = Path.Combine(targetPath, Path.GetRelativePath(sourcePath, file));
            var targetDirectory = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.Copy(file, targetFile, overwrite: false);
        }
    }
}
