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
    public async Task LoadModelsAsync_LemonadeUsesOpenAiCompatibleModelListWithoutApiKey()
    {
        Uri? capturedUri = null;
        string? capturedAuthorization = null;
        var service = new ModelCatalogService(new FakeHttpClientFactory(request =>
        {
            capturedUri = request.RequestUri;
            capturedAuthorization = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
{
  "data": [
    { "id": "google/gemma-4-e4b" },
    { "id": "tencent/Hy-MT2-7B" }
  ]
}
""", Encoding.UTF8, "application/json"),
            };
        }));

        var models = await service.LoadModelsAsync(new ProviderSettings
        {
            ProviderType = TranslationProviderType.Lemonade,
        }, CancellationToken.None);

        Assert.Equal(["google/gemma-4-e4b", "tencent/Hy-MT2-7B"], models);
        Assert.Equal("http://127.0.0.1:13305/v1/models", capturedUri?.ToString());
        Assert.Null(capturedAuthorization);
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
