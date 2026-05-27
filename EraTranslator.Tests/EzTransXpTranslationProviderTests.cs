using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class EzTransXpTranslationProviderTests : IDisposable
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
    public void ResolveDefaultWorkerExecutablePath_UsesInternalWorkerFolderWhenAvailable()
    {
        var workerDirectory = Path.Combine(AppContext.BaseDirectory, "workers", "EzTransXP");
        var workerPath = Path.Combine(workerDirectory, "EraTranslator.EzTransWorker.exe");
        Directory.CreateDirectory(workerDirectory);
        if (!File.Exists(workerPath))
        {
            File.WriteAllText(workerPath, string.Empty);
        }

        Assert.Equal(workerPath, EzTransXpTranslationProvider.ResolveDefaultWorkerExecutablePath());
    }

    [Fact]
    public async Task TranslateAsync_ReusesWorkerPoolAcrossCalls()
    {
        var installPath = CreateInstallationLayout();
        var workerFactory = new FakeWorkerClientFactory(
            () => new FakeWorkerClient(text => $"{text}-1"),
            () => new FakeWorkerClient(text => $"{text}-2"));
        var provider = new EzTransXpTranslationProvider(
            new EzTransXpInstallationService(),
            logger: null,
            workerClientFactory: workerFactory);

        var settings = BuildSettings(installPath, processCount: 2);
        var first = await provider.TranslateAsync(
            [
                new ProtectedSegment("a", "첫째", "첫째", []),
                new ProtectedSegment("b", "둘째", "둘째", []),
            ],
            settings,
            CancellationToken.None);
        var second = await provider.TranslateAsync(
            [
                new ProtectedSegment("c", "셋째", "셋째", []),
            ],
            settings,
            CancellationToken.None);

        Assert.Equal(2, workerFactory.CreatedCount);
        Assert.Equal("첫째-1", first.Translations["a"]);
        Assert.Equal("둘째-2", first.Translations["b"]);
        Assert.Equal("셋째-1", second.Translations["c"]);
    }

    [Fact]
    public async Task TranslateAsync_ReplacesDeadWorkerAndRetriesChunk()
    {
        var installPath = CreateInstallationLayout();
        var firstWorker = new FakeWorkerClient(_ => throw new InvalidOperationException("boom"))
        {
            Alive = false,
        };
        var replacementWorker = new FakeWorkerClient(text => $"{text}-ok");
        var workerFactory = new FakeWorkerClientFactory(
            () => firstWorker,
            () => replacementWorker);
        var provider = new EzTransXpTranslationProvider(
            new EzTransXpInstallationService(),
            logger: null,
            workerClientFactory: workerFactory);

        var result = await provider.TranslateAsync(
            [
                new ProtectedSegment("a", "첫째", "첫째", []),
            ],
            BuildSettings(installPath, processCount: 1),
            CancellationToken.None);

        Assert.Equal(2, workerFactory.CreatedCount);
        Assert.Equal("첫째-ok", result.Translations["a"]);
    }

    private string CreateInstallationLayout()
    {
        var installPath = Path.Combine(_rootPath, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(installPath, "Dat"));
        File.WriteAllText(Path.Combine(installPath, "J2KEngine.dll"), "stub");
        return installPath;
    }

    private static ProviderSettings BuildSettings(string installationPath, int processCount)
    {
        return new ProviderSettings
        {
            ProviderType = TranslationProviderType.EzTransXp,
            SourceLanguage = "ja",
            TargetLanguage = "ko",
            EzTransInstallationPath = installationPath,
            EzTransProcessCount = processCount,
        };
    }

    private sealed class FakeWorkerClientFactory(params Func<FakeWorkerClient>[] workers) : IEzTransXpWorkerClientFactory
    {
        private readonly Queue<Func<FakeWorkerClient>> _workers = new(workers);

        public int CreatedCount { get; private set; }

        public IEzTransXpWorkerClient Create(EzTransXpInstallationInfo installationInfo)
        {
            CreatedCount++;
            return _workers.Count > 0
                ? _workers.Dequeue().Invoke()
                : new FakeWorkerClient(text => text);
        }
    }

    private sealed class FakeWorkerClient(Func<string, string> translate) : IEzTransXpWorkerClient
    {
        public bool Alive { get; set; } = true;

        public bool IsAlive => Alive;

        public Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
        {
            if (!Alive)
            {
                throw new InvalidOperationException("dead");
            }

            return Task.FromResult<IReadOnlyList<string>>(texts.Select(translate).ToArray());
        }

        public ValueTask DisposeAsync()
        {
            Alive = false;
            return ValueTask.CompletedTask;
        }
    }
}
