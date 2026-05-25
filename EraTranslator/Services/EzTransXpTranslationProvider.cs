using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class EzTransXpTranslationProvider : ITranslationProvider, IAsyncDisposable
{
    private readonly EzTransXpInstallationService _installationService;
    private readonly IRequestResponseLogger? _logger;
    private readonly IEzTransXpWorkerClientFactory _workerClientFactory;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly List<IEzTransXpWorkerClient> _workers = [];
    private string _activeInstallationPath = string.Empty;
    private int _activeWorkerCount;
    private bool _disposed;

    public EzTransXpTranslationProvider(
        EzTransXpInstallationService? installationService = null,
        IRequestResponseLogger? logger = null,
        string? workerExecutablePath = null)
        : this(installationService, logger, null, workerExecutablePath)
    {
    }

    internal EzTransXpTranslationProvider(
        EzTransXpInstallationService? installationService,
        IRequestResponseLogger? logger,
        IEzTransXpWorkerClientFactory? workerClientFactory = null,
        string? workerExecutablePath = null)
    {
        _installationService = installationService ?? new EzTransXpInstallationService();
        _logger = logger;
        var resolvedWorkerExecutablePath = workerExecutablePath ?? Path.Combine(AppContext.BaseDirectory, "EraTranslator.EzTransWorker.exe");
        _workerClientFactory = workerClientFactory ?? new EzTransXpWorkerClientFactory(resolvedWorkerExecutablePath);
    }

    public async Task<TranslationProviderResult> TranslateAsync(
        IReadOnlyList<ProtectedSegment> requests,
        ProviderSettings settings,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var result = new TranslationProviderResult();
        if (requests.Count == 0)
        {
            return result;
        }

        if (!string.Equals(settings.SourceLanguage, "ja", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(settings.TargetLanguage, "ko", StringComparison.OrdinalIgnoreCase))
        {
            throw new TranslationProviderException(
                TranslationErrorKind.Configuration,
                "EzTransXP는 현재 일본어(ja) -> 한국어(ko) 번역만 지원합니다.");
        }

        var installationInfo = _installationService.Detect(settings.EzTransInstallationPath);
        if (!installationInfo.IsAvailable)
        {
            throw new TranslationProviderException(TranslationErrorKind.Configuration, installationInfo.StatusText);
        }

        await EnsureWorkersAsync(installationInfo, settings.EzTransProcessCount, cancellationToken);

        _logger?.LogRequest(
            "EzTransXP",
            installationInfo.EnginePath,
            BuildRequestLog(installationInfo, settings, requests));

        var chunks = CreateWorkerChunks(requests, _workers.Count);
        var workerTasks = chunks.Select(chunk => TranslateChunkAsync(chunk, cancellationToken)).ToArray();
        var chunkResults = await Task.WhenAll(workerTasks);

        foreach (var chunkResult in chunkResults)
        {
            if (chunkResult.Error is not null)
            {
                foreach (var request in chunkResult.Requests)
                {
                    result.Errors[request.Id] = new TranslationErrorDetail(
                        TranslationErrorKind.Configuration,
                        chunkResult.Error);
                }

                continue;
            }

            for (var index = 0; index < chunkResult.Requests.Count; index++)
            {
                var request = chunkResult.Requests[index];
                if (chunkResult.Translations is null || index >= chunkResult.Translations.Count)
                {
                    result.Errors[request.Id] = new TranslationErrorDetail(
                        TranslationErrorKind.MissingResult,
                        "EzTransXP 응답 개수가 요청과 일치하지 않습니다.");
                    continue;
                }

                var translated = chunkResult.Translations[index];
                if (string.IsNullOrWhiteSpace(translated))
                {
                    result.Errors[request.Id] = new TranslationErrorDetail(
                        TranslationErrorKind.MissingResult,
                        "EzTransXP가 빈 번역문을 반환했습니다.");
                    continue;
                }

                result.Translations[request.Id] = translated;
            }
        }

        _logger?.LogResponse(
            "EzTransXP",
            installationInfo.EnginePath,
            200,
            BuildResponseLog(result));

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var worker in _workers)
        {
            await worker.DisposeAsync();
        }

        _workers.Clear();
        _initializationGate.Dispose();
    }

    private async Task EnsureWorkersAsync(
        EzTransXpInstallationInfo installationInfo,
        int requestedWorkerCount,
        CancellationToken cancellationToken)
    {
        var effectiveWorkerCount = Math.Max(1, requestedWorkerCount);
        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (_workers.Count == effectiveWorkerCount
                && string.Equals(_activeInstallationPath, installationInfo.InstallationPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            foreach (var worker in _workers)
            {
                await worker.DisposeAsync();
            }

            _workers.Clear();
            for (var index = 0; index < effectiveWorkerCount; index++)
            {
                _workers.Add(_workerClientFactory.Create(installationInfo));
            }

            _activeInstallationPath = installationInfo.InstallationPath;
            _activeWorkerCount = effectiveWorkerCount;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private async Task<EzTransChunkResult> TranslateChunkAsync(EzTransChunk chunk, CancellationToken cancellationToken)
    {
        try
        {
            var translations = await chunk.Worker.TranslateAsync(chunk.Requests.Select(request => request.Text).ToArray(), cancellationToken);
            return new EzTransChunkResult(chunk.Requests, translations, null);
        }
        catch (Exception ex)
        {
            return new EzTransChunkResult(chunk.Requests, null, ex.Message);
        }
    }

    private IReadOnlyList<EzTransChunk> CreateWorkerChunks(IReadOnlyList<ProtectedSegment> requests, int workerCount)
    {
        var normalizedWorkerCount = Math.Min(Math.Max(1, workerCount), requests.Count);
        var chunkBuckets = Enumerable.Range(0, normalizedWorkerCount)
            .Select(_ => new List<ProtectedSegment>())
            .ToArray();

        for (var index = 0; index < requests.Count; index++)
        {
            chunkBuckets[index % normalizedWorkerCount].Add(requests[index]);
        }

        return chunkBuckets
            .Select((bucket, index) => new EzTransChunk(_workers[index], bucket))
            .Where(chunk => chunk.Requests.Count > 0)
            .ToArray();
    }

    private string BuildRequestLog(
        EzTransXpInstallationInfo installationInfo,
        ProviderSettings settings,
        IReadOnlyList<ProtectedSegment> requests)
    {
        return string.Join(
            Environment.NewLine,
            [
                $"installation: {installationInfo.InstallationPath}",
                $"engine: {Path.GetFileName(installationInfo.EnginePath)}",
                $"workers: {_activeWorkerCount}",
                $"source: {settings.SourceLanguage}",
                $"target: {settings.TargetLanguage}",
                .. requests.Select(request => $"[{request.Id}] {request.Text}"),
            ]);
    }

    private static string BuildResponseLog(TranslationProviderResult result)
    {
        var lines = new List<string>();
        lines.AddRange(result.Translations.Select(pair => $"[{pair.Key}] {pair.Value}"));
        lines.AddRange(result.Errors.Select(pair => $"[{pair.Key}] ERROR {pair.Value.Kind}: {pair.Value.Message}"));
        return string.Join(Environment.NewLine, lines);
    }

    private sealed record EzTransChunk(IEzTransXpWorkerClient Worker, IReadOnlyList<ProtectedSegment> Requests);

    private sealed record EzTransChunkResult(
        IReadOnlyList<ProtectedSegment> Requests,
        IReadOnlyList<string>? Translations,
        string? Error);
}
