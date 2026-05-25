using System.Net;
using System.Text;
using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class ModelCatalogServiceTests
{
    [Fact]
    public async Task LoadModelsAsync_ParsesOpenAiCompatibleModelList()
    {
        var service = new ModelCatalogService(new FakeHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
{
  "data": [
    { "id": "gpt-4o-mini" },
    { "id": "gpt-4.1-mini" }
  ]
}
""", Encoding.UTF8, "application/json"),
        }));

        var models = await service.LoadModelsAsync(new ProviderSettings
        {
            ProviderType = TranslationProviderType.OpenAi,
            BaseUrl = "https://api.openai.com/v1",
            ApiKey = "test-key",
        }, CancellationToken.None);

        Assert.Equal(["gpt-4.1-mini", "gpt-4o-mini"], models);
    }

    [Fact]
    public async Task LoadModelsAsync_RejectsUnsupportedProviders()
    {
        var service = new ModelCatalogService();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoadModelsAsync(new ProviderSettings
        {
            ProviderType = TranslationProviderType.DeepLFree,
        }, CancellationToken.None));

        Assert.Contains("지원하지 않습니다", exception.Message, StringComparison.Ordinal);
    }

    private sealed class FakeHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responder) : ISimpleHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FakeHttpMessageHandler(responder));
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }
}
