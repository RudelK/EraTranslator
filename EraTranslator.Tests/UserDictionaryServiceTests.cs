using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class UserDictionaryServiceTests : IDisposable
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
    public void SaveAndLoadGlobal_RoundTripsNormalizedEntries()
    {
        var service = new UserDictionaryService(Path.Combine(_rootPath, "AppData"));

        service.SaveGlobal(
            [
                new UserDictionaryEntry { IsEnabled = true, Source = " 勇者 ", Target = " 용사 " },
                new UserDictionaryEntry { IsEnabled = true, Source = "", Target = "skip" },
            ]);

        var loaded = service.LoadGlobal();

        Assert.Single(loaded);
        Assert.Equal("勇者", loaded[0].Source);
        Assert.Equal("용사", loaded[0].Target);
    }

    [Fact]
    public void BuildEffectiveDictionary_ProjectEntryOverridesGlobalAndCanDisableIt()
    {
        var service = new UserDictionaryService(Path.Combine(_rootPath, "AppData"));

        var effective = service.BuildEffectiveDictionary(
            [
                new UserDictionaryEntry { IsEnabled = true, Source = "勇者", Target = "용사" },
                new UserDictionaryEntry { IsEnabled = true, Source = "魔王", Target = "마왕" },
            ],
            [
                new UserDictionaryEntry { IsEnabled = true, Source = "勇者", Target = "브레이브" },
                new UserDictionaryEntry { IsEnabled = false, Source = "魔王", Target = "무효" },
            ]);

        Assert.Single(effective);
        Assert.Equal("勇者", effective[0].Source);
        Assert.Equal("브레이브", effective[0].Target);
    }

    [Fact]
    public void SaveAndLoadProject_UsesProjectScopedPath()
    {
        var appDataPath = Path.Combine(_rootPath, "AppData");
        var projectPath = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(projectPath);
        var service = new UserDictionaryService(appDataPath);

        service.SaveProject(
            projectPath,
            [
                new UserDictionaryEntry { IsEnabled = true, Source = "魔界", Target = "마계" },
            ]);

        var dictionaryPath = service.GetProjectDictionaryPath(projectPath);
        var loaded = service.LoadProject(projectPath);

        Assert.NotNull(dictionaryPath);
        Assert.True(File.Exists(dictionaryPath));
        Assert.Single(loaded);
        Assert.Equal("마계", loaded[0].Target);
    }
}
