using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class DebouncedAppConfigCoordinatorTests : IDisposable
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
    public async Task ScheduleSave_PersistsLatestSnapshotAfterDebounce()
    {
        Directory.CreateDirectory(_rootPath);
        var appConfigService = new AppConfigService(_rootPath);
        using var coordinator = new DebouncedAppConfigCoordinator(appConfigService, TimeSpan.FromMilliseconds(25));

        coordinator.ScheduleSave(new AppConfig
        {
            GameDirectory = "first",
            Model = "model-1",
        });
        coordinator.ScheduleSave(new AppConfig
        {
            GameDirectory = "second",
            Model = "model-2",
        });

        await Task.Delay(120);
        var loaded = appConfigService.Load();

        Assert.Equal("second", loaded.GameDirectory);
        Assert.Equal("model-2", loaded.Model);
    }

    [Fact]
    public void FlushPendingSave_PersistsImmediately()
    {
        Directory.CreateDirectory(_rootPath);
        var appConfigService = new AppConfigService(_rootPath);
        using var coordinator = new DebouncedAppConfigCoordinator(appConfigService, TimeSpan.FromMinutes(1));

        coordinator.ScheduleSave(new AppConfig
        {
            GameDirectory = "flush-now",
            Model = "model-flush",
        });

        coordinator.FlushPendingSave();
        var loaded = appConfigService.Load();

        Assert.Equal("flush-now", loaded.GameDirectory);
        Assert.Equal("model-flush", loaded.Model);
    }
}
