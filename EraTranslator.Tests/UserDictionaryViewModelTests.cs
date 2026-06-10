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
    public void GlobalDictionary_RemoveMultipleEntriesPersists()
    {
        var service = new UserDictionaryService(Path.Combine(_rootPath, "AppData"));
        var viewModel = new UserDictionaryViewModel(
            Path.Combine(_rootPath, "Game"),
            [
                new Models.UserDictionaryEntry { IsEnabled = true, Source = "A", Target = "가" },
                new Models.UserDictionaryEntry { IsEnabled = true, Source = "B", Target = "나" },
                new Models.UserDictionaryEntry { IsEnabled = true, Source = "C", Target = "다" },
            ],
            [],
            service);

        viewModel.RemoveGlobalEntries([viewModel.GlobalEntries[0], viewModel.GlobalEntries[1]]);

        var loaded = service.LoadGlobal();

        Assert.Single(loaded);
        Assert.Equal("C", loaded[0].Source);
    }

    [Fact]
    public void ProjectDictionary_RemoveMultipleEntriesPersists()
    {
        var projectPath = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(projectPath);
        var service = new UserDictionaryService(Path.Combine(_rootPath, "AppData"));
        var viewModel = new UserDictionaryViewModel(
            projectPath,
            [],
            [
                new Models.UserDictionaryEntry { IsEnabled = true, Source = "A", Target = "가" },
                new Models.UserDictionaryEntry { IsEnabled = true, Source = "B", Target = "나" },
            ],
            service);

        viewModel.RemoveProjectEntries(viewModel.ProjectEntries.ToList());

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

    [Fact]
    public void ImportGlobalDictionary_MergesBySourceAndPersists()
    {
        Directory.CreateDirectory(_rootPath);
        var importPath = Path.Combine(_rootPath, "dictionary.etdict");
        File.WriteAllText(
            importPath,
            """
# EraTranslator User Dictionary v1
勇者	브레이브	프롬프팅	사용
魔王	마왕	치환	미사용
""");
        var service = new UserDictionaryService(Path.Combine(_rootPath, "AppData"));
        var viewModel = new UserDictionaryViewModel(
            Path.Combine(_rootPath, "Game"),
            [new Models.UserDictionaryEntry { IsEnabled = true, Source = "勇者", Target = "용사" }],
            [],
            service);

        var result = viewModel.ImportGlobalDictionary(importPath);
        var loaded = service.LoadGlobal();

        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Updated);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, entry =>
            entry.Source == "勇者"
            && entry.Target == "브레이브"
            && entry.ApplyMode == Models.UserDictionaryApplyMode.Prompting
            && entry.IsEnabled);
        Assert.Contains(loaded, entry =>
            entry.Source == "魔王"
            && entry.Target == "마왕"
            && !entry.IsEnabled);
    }

    [Fact]
    public void ImportProjectDictionary_ImportsSimpleSrsAndPersists()
    {
        var projectPath = Path.Combine(_rootPath, "Game");
        Directory.CreateDirectory(projectPath);
        var importPath = Path.Combine(_rootPath, "dictionary.simplesrs");
        File.WriteAllText(
            importPath,
            """
"強化剤"
"강화제"
""");
        var service = new UserDictionaryService(Path.Combine(_rootPath, "AppData"));
        var viewModel = new UserDictionaryViewModel(projectPath, [], [], service);

        var result = viewModel.ImportProjectDictionary(importPath);
        var loaded = service.LoadProject(projectPath);

        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.Updated);
        Assert.Single(loaded);
        Assert.Equal("強化剤", loaded[0].Source);
        Assert.Equal("강화제", loaded[0].Target);
        Assert.Equal(Models.UserDictionaryApplyMode.Prompting, loaded[0].ApplyMode);
        Assert.True(loaded[0].IsEnabled);
    }

    [Fact]
    public void ExportGlobalDictionary_WritesCurrentEntries()
    {
        Directory.CreateDirectory(_rootPath);
        var exportPath = Path.Combine(_rootPath, "dictionary.etdict");
        var service = new UserDictionaryService(Path.Combine(_rootPath, "AppData"));
        var viewModel = new UserDictionaryViewModel(
            Path.Combine(_rootPath, "Game"),
            [new Models.UserDictionaryEntry { IsEnabled = true, Source = "勇者", Target = "용사" }],
            [],
            service);

        viewModel.ExportGlobalDictionary(exportPath);

        var text = File.ReadAllText(exportPath);
        Assert.Contains("勇者\t용사\t치환\t사용", text, StringComparison.Ordinal);
    }
}
