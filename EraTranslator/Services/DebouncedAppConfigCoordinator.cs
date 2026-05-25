namespace EraTranslator.Services;

public sealed class DebouncedAppConfigCoordinator : IDisposable
{
    private readonly AppConfigService _appConfigService;
    private readonly TimeSpan _debounceDelay;
    private readonly object _sync = new();
    private CancellationTokenSource? _saveCts;
    private AppConfig? _pendingConfig;

    public DebouncedAppConfigCoordinator(AppConfigService? appConfigService = null, TimeSpan? debounceDelay = null)
    {
        _appConfigService = appConfigService ?? new AppConfigService();
        _debounceDelay = debounceDelay ?? TimeSpan.FromMilliseconds(350);
    }

    public string ConfigPath => _appConfigService.ConfigPath;

    public string SecretPath => _appConfigService.SecretPath;

    public AppConfig Load()
    {
        return _appConfigService.Load();
    }

    public void ScheduleSave(AppConfig config)
    {
        CancellationTokenSource cancellationTokenSource;
        lock (_sync)
        {
            _pendingConfig = config;
            _saveCts?.Cancel();
            _saveCts?.Dispose();
            _saveCts = new CancellationTokenSource();
            cancellationTokenSource = _saveCts;
        }

        _ = PersistAfterDelayAsync(cancellationTokenSource);
    }

    public void FlushPendingSave()
    {
        AppConfig? pendingConfig;
        CancellationTokenSource? cancellationTokenSource;
        lock (_sync)
        {
            pendingConfig = _pendingConfig;
            _pendingConfig = null;
            cancellationTokenSource = _saveCts;
            _saveCts = null;
        }

        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();

        if (pendingConfig is not null)
        {
            _appConfigService.Save(pendingConfig);
        }
    }

    public void Dispose()
    {
        FlushPendingSave();
    }

    private async Task PersistAfterDelayAsync(CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            await Task.Delay(_debounceDelay, cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            cancellationTokenSource.Dispose();
            return;
        }

        AppConfig? pendingConfig;
        lock (_sync)
        {
            if (!ReferenceEquals(_saveCts, cancellationTokenSource))
            {
                cancellationTokenSource.Dispose();
                return;
            }

            pendingConfig = _pendingConfig;
            _pendingConfig = null;
            _saveCts = null;
        }

        cancellationTokenSource.Dispose();
        if (pendingConfig is not null)
        {
            _appConfigService.Save(pendingConfig);
        }
    }
}
