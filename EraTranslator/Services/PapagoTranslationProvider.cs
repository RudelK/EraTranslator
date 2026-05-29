using System.Net;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class PapagoTranslationProvider(
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
        if (string.IsNullOrWhiteSpace(settings.PapagoClientId) || string.IsNullOrWhiteSpace(settings.PapagoClientSecret))
        {
            throw new TranslationProviderException(TranslationErrorKind.Configuration, "Papago Client ID와 Secret을 입력하세요.");
        }

        var client = httpClientFactory.CreateClient(nameof(PapagoTranslationProvider));
        var endpoint = string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? "https://openapi.naver.com/v1/papago/n2mt"
            : settings.BaseUrl;

        client.DefaultRequestHeaders.Add("X-Naver-Client-Id", settings.PapagoClientId);
        client.DefaultRequestHeaders.Add("X-Naver-Client-Secret", settings.PapagoClientSecret);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        foreach (var request in requests)
        {
            var requestPayload = new Dictionary<string, string>
            {
                ["source"] = settings.SourceLanguage.ToLowerInvariant(),
                ["target"] = settings.TargetLanguage.ToLowerInvariant(),
                ["text"] = ProviderPlaceholderMarker.MarkForPapago(request.Text, request.Placeholders),
            };

            requestResponseLogger?.LogRequest(
                "Papago",
                endpoint,
                JsonSerializer.Serialize(requestPayload, LogJsonOptions),
                new Dictionary<string, string>
                {
                    ["X-Naver-Client-Id"] = SensitiveDataMasker.MaskSecret(settings.PapagoClientId),
                    ["X-Naver-Client-Secret"] = SensitiveDataMasker.MaskSecret(settings.PapagoClientSecret),
                });

            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["source"] = requestPayload["source"],
                ["target"] = requestPayload["target"],
                ["text"] = requestPayload["text"],
            });

            try
            {
                using var response = await PostAsync(client, endpoint, form, cancellationToken, requestResponseLogger);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                requestResponseLogger?.LogResponse("Papago", endpoint, (int)response.StatusCode, body);
                using var json = ParseJson(body);

                var translatedText =
                    json.RootElement
                        .GetProperty("message")
                        .GetProperty("result")
                        .GetProperty("translatedText")
                        .GetString() ?? string.Empty;

                result.Translations[request.Id] = ProviderPlaceholderMarker.UnmarkFromPapago(translatedText, request.Placeholders);
            }
            catch (TranslationProviderException ex)
            {
                requestResponseLogger?.LogError("Papago", endpoint, ex.Message);
                result.Errors[request.Id] = new TranslationErrorDetail(
                    ex.Kind,
                    ex.Message,
                    ex.StatusCode is null ? null : (int)ex.StatusCode.Value);
            }
        }

        return result;
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string endpoint,
        HttpContent form,
        CancellationToken cancellationToken,
        IRequestResponseLogger? logger)
    {
        try
        {
            var response = await client.PostAsync(endpoint, form, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger?.LogResponse("Papago", endpoint, (int)response.StatusCode, body);
            response.Dispose();
            throw new TranslationProviderException(
                TranslationErrorKind.Http,
                BuildHttpErrorMessage(response.StatusCode, body),
                response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogError("Papago", endpoint, "Papago 요청이 시간 초과되었습니다.");
            throw new TranslationProviderException(TranslationErrorKind.Timeout, "Papago 요청이 시간 초과되었습니다.");
        }
        catch (HttpRequestException ex)
        {
            logger?.LogError("Papago", endpoint, $"Papago HTTP 요청 실패: {ex.Message}");
            throw new TranslationProviderException(TranslationErrorKind.Http, $"Papago HTTP 요청 실패: {ex.Message}", innerException: ex);
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
            throw new TranslationProviderException(TranslationErrorKind.Json, "Papago 응답 JSON 파싱에 실패했습니다.", innerException: ex);
        }
    }

    private static string BuildHttpErrorMessage(HttpStatusCode statusCode, string body)
    {
        var summary = string.IsNullOrWhiteSpace(body)
            ? "응답 본문 없음"
            : body.Length > 180 ? $"{body[..180]}..." : body;
        return $"HTTP {(int)statusCode} 오류: {summary}";
    }
}
