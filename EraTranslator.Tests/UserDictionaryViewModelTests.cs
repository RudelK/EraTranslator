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

    [Fact]
    public void RestorePersistedEntries_RevertsImmediateEditsBackToOriginalSnapshot()
    {
        var projectPath = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(projectPath);
        var service = new UserDictionaryService(Path.Combine(_rootPath, "AppData"));
        service.SaveGlobal([new Models.UserDictionaryEntry { IsEnabled = true, Source = "勇者", Target = "용사" }]);
        service.SaveProject(projectPath, [new Models.UserDictionaryEntry { IsEnabled = true, Source = "魔界", Target = "마계" }]);

        var viewModel = new UserDictionaryViewModel(
            projectPath,
            service.LoadGlobal(),
            service.LoadProject(projectPath),
            service);

        var globalEntry = Assert.Single(viewModel.GlobalEntries);
        globalEntry.Target = "바뀐 용사";
        viewModel.AddProjectEntry();
        var addedProjectEntry = viewModel.ProjectEntries.Last();
        addedProjectEntry.Source = "王";
        addedProjectEntry.Target = "왕";

        viewModel.RestorePersistedEntries();

        var restoredGlobal = service.LoadGlobal();
        var restoredProject = service.LoadProject(projectPath);

        Assert.Single(restoredGlobal);
        Assert.Equal("용사", restoredGlobal[0].Target);
        Assert.Single(restoredProject);
        Assert.Equal("魔界", restoredProject[0].Source);
        Assert.Equal("마계", restoredProject[0].Target);
    }

    [Fact]
    public void ProtectedFullWidthCharacters_RoundTripsThroughViewModel()
    {
        var service = new UserDictionaryService(Path.Combine(_rootPath, "AppData"));
        var viewModel = new UserDictionaryViewModel(
            Path.Combine(_rootPath, "Game"),
            [],
            [],
            service,
            "（）");

        viewModel.ProtectedFullWidthCharacters = "『』";

        Assert.Equal("『』", viewModel.ProtectedFullWidthCharacters);
    }
}
