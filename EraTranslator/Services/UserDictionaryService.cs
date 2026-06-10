using System.Text.Json;
using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class UserDictionaryService(
    string? appDataRoot = null,
    string? baseDirectory = null,
    string projectFolderName = ".era-translator",
    string globalFolderName = "UserDictionaries",
    string globalFileName = "global-user-dictionary.json",
    string projectFileName = "project-user-dictionary.json")
{
    private readonly string _appDataRoot = string.IsNullOrWhiteSpace(appDataRoot)
        ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        : appDataRoot;
    private readonly string _baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
        ? AppContext.BaseDirectory
        : baseDirectory;
    private readonly string _projectFolderName = projectFolderName;
    private readonly string _globalFolderName = globalFolderName;
    private readonly string _globalFileName = globalFileName;
    private readonly string _projectFileName = projectFileName;

    public string GetGlobalDictionaryPath()
    {
        return Path.Combine(_baseDirectory, _globalFolderName, _globalFileName);
    }

    public string GetLegacyGlobalDictionaryPath()
    {
        return Path.Combine(_appDataRoot, "EraTranslator", _globalFileName);
    }

    public string? GetProjectDictionaryPath(string? gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            return null;
        }

        return Path.Combine(gameDirectory, _projectFolderName, _projectFileName);
    }

    public List<UserDictionaryEntry> LoadGlobal()
    {
        var currentPath = GetGlobalDictionaryPath();
        var currentEntries = LoadFromPath(currentPath);
        if (currentEntries.Count > 0 || File.Exists(currentPath))
        {
            return currentEntries;
        }

        MoveFileIfMissing(currentPath, GetLegacyGlobalDictionaryPath());
        return LoadFromPath(currentPath);
    }

    public List<UserDictionaryEntry> LoadProject(string? gameDirectory)
    {
        var path = GetProjectDictionaryPath(gameDirectory);
        return path is null ? [] : LoadFromPath(path);
    }

    public void SaveGlobal(IEnumerable<UserDictionaryEntry> entries)
    {
        SaveToPath(GetGlobalDictionaryPath(), entries);
    }

    public void SaveProject(string? gameDirectory, IEnumerable<UserDictionaryEntry> entries)
    {
        var path = GetProjectDictionaryPath(gameDirectory);
        if (path is null)
        {
            return;
        }

        SaveToPath(path, entries);
    }

    public IReadOnlyList<UserDictionaryEntry> BuildEffectiveDictionary(
        IEnumerable<UserDictionaryEntry> globalEntries,
        IEnumerable<UserDictionaryEntry> projectEntries)
    {
        var merged = new Dictionary<string, UserDictionaryEntry>(StringComparer.Ordinal);

        foreach (var entry in NormalizeEntries(globalEntries))
        {
            merged[entry.Source] = entry;
        }

        foreach (var entry in NormalizeEntries(projectEntries))
        {
            merged[entry.Source] = entry;
        }

        return merged.Values
            .Where(entry => entry.IsEnabled)
            .OrderByDescending(entry => entry.Source.Length)
            .ThenBy(entry => entry.Source, StringComparer.Ordinal)
            .Select(entry => entry.Clone())
            .ToList();
    }

    private static List<UserDictionaryEntry> LoadFromPath(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var store = JsonSerializer.Deserialize<UserDictionaryStore>(json);
            return NormalizeEntries(store?.Entries ?? []).ToList();
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<UserDictionaryEntry> NormalizeEntries(IEnumerable<UserDictionaryEntry> entries)
    {
        foreach (var entry in entries)
        {
            var source = (entry.Source ?? string.Empty).Trim();
            var target = (entry.Target ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            yield return new UserDictionaryEntry
            {
                IsEnabled = entry.IsEnabled,
                Source = source,
                Target = target,
                ApplyMode = entry.ApplyMode,
            };
        }
    }

    private static void SaveToPath(string path, IEnumerable<UserDictionaryEntry> entries)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var store = new UserDictionaryStore
        {
            Entries = NormalizeEntries(entries).ToList(),
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
        };
        var json = JsonSerializer.Serialize(store, options);
        File.WriteAllText(path, json);
    }

    private static void MoveFileIfMissing(string targetPath, string sourcePath)
    {
        if (File.Exists(targetPath)
            || !File.Exists(sourcePath)
            || string.Equals(Path.GetFullPath(targetPath), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        try
        {
            File.Move(sourcePath, targetPath);
        }
        catch
        {
            try
            {
                File.Copy(sourcePath, targetPath, overwrite: false);
                File.Delete(sourcePath);
            }
            catch
            {
                // Keep the legacy file untouched if migration fails.
            }
        }
    }

    private sealed class UserDictionaryStore
    {
        public List<UserDictionaryEntry> Entries { get; init; } = [];
    }
}
