using System.Net.Http;

namespace EraTranslator.Services;

public interface ITranslationProviderFactory
{
    ITranslationProvider Create(ProviderSettings settings);
}

public sealed class TranslationProviderFactory : ITranslationProviderFactory
{
    private readonly ISimpleHttpClientFactory _httpClientFactory = new SimpleHttpClientFactory();
    private readonly IRequestResponseLogger _requestResponseLogger = new FileRequestResponseLogger();

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
            TranslationProviderType.EzTransXp => new EzTransXpTranslationProvider(logger: logger),
            _ => throw new NotSupportedException($"지원되지 않는 공급자입니다: {settings.ProviderType}"),
        };
    }
}

internal sealed class SimpleHttpClientFactory : ISimpleHttpClientFactory
{
    public HttpClient CreateClient(string name) => new()
    {
        Timeout = TimeSpan.FromSeconds(90),
    };
}
