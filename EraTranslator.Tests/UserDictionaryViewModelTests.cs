using EraTranslator.Services;
using EraTranslator.ViewModels;

namespace EraTranslator.Tests;

public sealed class UserDictionaryViewModelTests : IDisposable
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
    public void GlobalDictionary_EditsAreSavedImmediately()
    {
        var service = new UserDictionaryService(Path.Combine(_rootPath, "AppData"));
        var viewModel = new UserDictionaryViewModel(
            Path.Combine(_rootPath, "Game"),
            [],
            [],
            service);

        viewModel.AddGlobalEntry();
        var entry = Assert.Single(viewModel.GlobalEntries);
        entry.Source = "勇者";
        entry.Target = "용사";

        var loaded = service.LoadGlobal();

        Assert.Single(loaded);
        Assert.Equal("勇者", loaded[0].Source);
        Assert.Equal("용사", loaded[0].Target);
    }

    [Fact]
    public void ProjectDictionary_RemovePersistsWithoutConfirm()
    {
        var projectPath = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(projectPath);
        var service = new UserDictionaryService(Path.Combine(_rootPath, "AppData"));
        var viewModel = new UserDictionaryViewModel(
            projectPath,
            [],
            [new Models.UserDictionaryEntry { IsEnabled = true, Source = "魔界", Target = "마계" }],
            service);

        viewModel.SelectedProjectEntry = Assert.Single(viewModel.ProjectEntries);
        viewModel.RemoveSelectedProjectEntry();

        var loaded = service.LoadProject(projectPath);

        Assert.Empty(loaded);
    }
}
