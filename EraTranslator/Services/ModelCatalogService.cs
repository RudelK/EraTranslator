using System.Net.Http.Headers;
using System.Text.Json;

namespace EraTranslator.Services;

public sealed class ModelCatalogService
{
    private readonly ISimpleHttpClientFactory _httpClientFactory;

    public ModelCatalogService(ISimpleHttpClientFactory? httpClientFactory = null)
    {
        _httpClientFactory = httpClientFactory ?? new SimpleHttpClientFactory();
    }

    public bool SupportsModelCatalog(TranslationProviderType providerType)
    {
        return providerType is TranslationProviderType.OpenAi or TranslationProviderType.LmStudio or TranslationProviderType.Lemonade;
    }

    public async Task<IReadOnlyList<string>> LoadModelsAsync(ProviderSettings settings, CancellationToken cancellationToken)
    {
        if (!SupportsModelCatalog(settings.ProviderType))
        {
            throw new InvalidOperationException("현재 공급자는 모델 목록 조회를 지원하지 않습니다.");
        }

        if (settings.ProviderType == TranslationProviderType.OpenAi && string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("OpenAI API Key를 입력하세요.");
        }

        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? settings.ProviderType switch
            {
                TranslationProviderType.LmStudio => "http://127.0.0.1:1234/v1",
                TranslationProviderType.Lemonade => "http://127.0.0.1:13305/v1",
                _ => "https://api.openai.com/v1",
            }
            : settings.BaseUrl.TrimEnd('/');

        var client = _httpClientFactory.CreateClient(nameof(ModelCatalogService));
        client.BaseAddress = new Uri($"{baseUrl}/");

        if (settings.ProviderType == TranslationProviderType.OpenAi)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        }

        try
        {
            using var response = await client.GetAsync("models", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"모델 목록 조회 실패: HTTP {(int)response.StatusCode} {response.ReasonPhrase} {TrimBody(body)}");
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(content);
            if (!json.RootElement.TryGetProperty("data", out var dataNode) || dataNode.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("모델 목록 응답 형식이 올바르지 않습니다.");
            }

            var models = dataNode
                .EnumerateArray()
                .Select(item => item.TryGetProperty("id", out var idNode) ? idNode.GetString() : null)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToList();

            if (models.Count == 0)
            {
                throw new InvalidOperationException("사용 가능한 모델이 없습니다.");
            }

            return models;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("모델 목록 JSON을 해석하지 못했습니다.", ex);
        }
    }

    private static string TrimBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        return body.Length > 140 ? $"{body[..140]}..." : body;
    }
}
