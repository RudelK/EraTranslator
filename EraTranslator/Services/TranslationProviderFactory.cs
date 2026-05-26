using System.Net.Http;

namespace EraTranslator.Services;

public interface ITranslationProviderFactory
{
    ITranslationProvider Create(ProviderSettings settings);
}

public sealed class TranslationProviderFactory : ITranslationProviderFactory, IDisposable
{
    private readonly ISimpleHttpClientFactory _httpClientFactory = new SimpleHttpClientFactory();
    private readonly IRequestResponseLogger _requestResponseLogger = new FileRequestResponseLogger();
    private EzTransXpTranslationProvider? _ezTransProvider;

    public ITranslationProvider Create(ProviderSettings settings)
    {
        var logger = settings.EnableRequestResponseLogging ? _requestResponseLogger : null;

        return settings.ProviderType switch
        {
            TranslationProviderType.OpenAi => new OpenAiCompatibleTranslationProvider(_httpClientFactory, false, logger),
            TranslationProviderType.LmStudio => new OpenAiCompatibleTranslationProvider(_httpClientFactory, true, logger),
            TranslationProviderType.DeepLFree => new DeepLTranslationProvider(_httpClientFactory, logger),
            TranslationProviderType.DeepLPro => new DeepLTranslationProvider(_httpClientFactory, logger),
            TranslationProviderType.Papago => new PapagoTranslationProvider(_httpClientFactory, logger),
            TranslationProviderType.EzTransXp => _ezTransProvider ??= new EzTransXpTranslationProvider(logger: _requestResponseLogger),
            _ => throw new NotSupportedException($"지원되지 않는 공급자입니다: {settings.ProviderType}"),
        };
    }

    public void Dispose()
    {
        if (_ezTransProvider is null)
        {
            return;
        }

        Task.Run(async () => await _ezTransProvider.DisposeAsync().ConfigureAwait(false))
            .GetAwaiter()
            .GetResult();
        _ezTransProvider = null;
    }
}

internal sealed class SimpleHttpClientFactory : ISimpleHttpClientFactory
{
    public HttpClient CreateClient(string name) => new()
    {
        Timeout = TimeSpan.FromSeconds(90),
    };
}
