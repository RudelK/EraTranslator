using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class DeepLTranslationProvider(
    ISimpleHttpClientFactory httpClientFactory,
    IRequestResponseLogger? requestResponseLogger = null) : ITranslationProvider
{
    private static readonly JsonSerializerOptions LogJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<TranslationProviderResult> TranslateAsync(
        IReadOnlyList<ProtectedSegment> requests,
        ProviderSettings settings,
        CancellationToken cancellationToken,
        IReadOnlyList<GlossaryHint>? glossaryHints = null)
    {
        var result = new TranslationProviderResult();
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new TranslationProviderException(TranslationErrorKind.Configuration, "DeepL API Key를 입력하세요.");
        }

        var client = httpClientFactory.CreateClient(nameof(DeepLTranslationProvider));
        var endpoint = string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? settings.ProviderType == TranslationProviderType.DeepLPro
                ? "https://api.deepl.com/v2/translate"
                : "https://api-free.deepl.com/v2/translate"
            : settings.BaseUrl;

        using var requestContent = BuildRequestContent(requests, settings);

        var endpointForLog = endpoint;
        requestResponseLogger?.LogRequest(
            "DeepL",
            endpointForLog,
            BuildRequestLog(requests, settings),
            new Dictionary<string, string>
            {
                ["Authorization"] = SensitiveDataMasker.MaskSecret($"DeepL-Auth-Key {settings.ApiKey}", visiblePrefixLength: 15),
            });

        using var response = await PostAsync(client, endpoint, requestContent, settings.ApiKey, cancellationToken, endpointForLog, requestResponseLogger);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        requestResponseLogger?.LogResponse("DeepL", endpointForLog, (int)response.StatusCode, body);
        using var json = ParseJson(body);

        JsonElement.ArrayEnumerator translationsNode;
        try
        {
            translationsNode = json.RootElement.GetProperty("translations").EnumerateArray();
        }
        catch (Exception ex)
        {
            throw new TranslationProviderException(TranslationErrorKind.Json, "DeepL 응답 구조가 예상과 다릅니다.", innerException: ex);
        }

        var translated = translationsNode
            .Select(item => item.TryGetProperty("text", out var textNode) ? textNode.GetString() ?? string.Empty : string.Empty)
            .ToArray();

        for (var index = 0; index < requests.Count; index++)
        {
            if (index >= translated.Length)
            {
                result.Errors[requests[index].Id] = new TranslationErrorDetail(
                    TranslationErrorKind.MissingResult,
                    "DeepL 응답 개수가 요청보다 적습니다.");
                continue;
            }

            result.Translations[requests[index].Id] = ProviderPlaceholderMarker.UnmarkFromDeepL(translated[index], requests[index].Placeholders);
        }

        return result;
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string endpoint,
        HttpContent content,
        string apiKey,
        CancellationToken cancellationToken,
        string endpointForLog,
        IRequestResponseLogger? logger)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content,
            };
            request.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {apiKey}");

            var response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger?.LogResponse("DeepL", endpointForLog, (int)response.StatusCode, body);
            response.Dispose();
            throw new TranslationProviderException(
                TranslationErrorKind.Http,
                BuildHttpErrorMessage(response.StatusCode, body),
                response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogError("DeepL", endpointForLog, "DeepL 요청이 시간 초과되었습니다.");
            throw new TranslationProviderException(TranslationErrorKind.Timeout, "DeepL 요청이 시간 초과되었습니다.");
        }
        catch (HttpRequestException ex)
        {
            logger?.LogError("DeepL", endpointForLog, $"DeepL HTTP 요청 실패: {ex.Message}");
            throw new TranslationProviderException(TranslationErrorKind.Http, $"DeepL HTTP 요청 실패: {ex.Message}", innerException: ex);
        }
    }

    private static JsonDocument ParseJson(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new TranslationProviderException(TranslationErrorKind.Json, "DeepL 응답 JSON 파싱에 실패했습니다.", innerException: ex);
        }
    }

    private static string BuildHttpErrorMessage(HttpStatusCode statusCode, string body)
    {
        var summary = string.IsNullOrWhiteSpace(body)
            ? "응답 본문 없음"
            : body.Length > 180 ? $"{body[..180]}..." : body;
        return $"HTTP {(int)statusCode} 오류: {summary}";
    }

    private static HttpContent BuildRequestContent(IReadOnlyList<ProtectedSegment> requests, ProviderSettings settings)
    {
        var values = new List<KeyValuePair<string, string>>
        {
            new("source_lang", settings.SourceLanguage.ToUpperInvariant()),
            new("target_lang", settings.TargetLanguage.ToUpperInvariant()),
            new("tag_handling", "xml"),
            new("ignore_tags", "era-ph"),
            new("split_sentences", "0"),
            new("preserve_formatting", "1"),
        };

        foreach (var request in requests)
        {
            values.Add(new KeyValuePair<string, string>(
                "text",
                ProviderPlaceholderMarker.MarkForDeepL(request.Text, request.Placeholders)));
        }

        return new FormUrlEncodedContent(values);
    }

    private static string BuildRequestLog(IReadOnlyList<ProtectedSegment> requests, ProviderSettings settings)
    {
        return JsonSerializer.Serialize(new
        {
            source_lang = settings.SourceLanguage.ToUpperInvariant(),
            target_lang = settings.TargetLanguage.ToUpperInvariant(),
            tag_handling = "xml",
            ignore_tags = "era-ph",
            split_sentences = "0",
            preserve_formatting = "1",
            text = requests.Select(request => ProviderPlaceholderMarker.MarkForDeepL(request.Text, request.Placeholders)).ToArray(),
        }, LogJsonOptions);
    }
}
