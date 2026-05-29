using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EraTranslator.Services;

public sealed partial class OpenAiCompatibleTranslationProvider
{
    private static async Task<string> SendChatRequestAsync(
        HttpClient client,
        object payload,
        ResponseMode responseMode,
        string model,
        ProviderSettings settings,
        RequestBuildMetadata requestMetadata,
        CancellationToken cancellationToken,
        string providerName,
        string endpoint,
        IRequestResponseLogger? logger)
    {
        var serializedPayload = JsonSerializer.Serialize(payload, RequestJsonOptions);
        var requestHeaders = BuildRequestLogHeaders(client, responseMode, model, settings, requestMetadata);
        logger?.LogRequest(
            providerName,
            endpoint,
            serializedPayload,
            requestHeaders);

        using var requestContent = new StringContent(serializedPayload, Encoding.UTF8);
        requestContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await PostAsync(client, "chat/completions", requestContent, cancellationToken, providerName, endpoint, logger, requestHeaders);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        logger?.LogResponse(providerName, endpoint, (int)response.StatusCode, body, BuildResponseLogHeaders(responseMode, model, settings, requestMetadata));
        using var json = ParseJson(body, "OpenAI/LM Studio 응답 본문");

        try
        {
            var content = json.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new TranslationProviderException(TranslationErrorKind.Json, "번역 응답이 비어 있습니다.");
            }

            return content;
        }
        catch (TranslationProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new TranslationProviderException(TranslationErrorKind.Json, "OpenAI/LM Studio 응답 구조가 예상과 다릅니다.", innerException: ex);
        }
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string requestUri,
        HttpContent content,
        CancellationToken cancellationToken,
        string providerName,
        string endpoint,
        IRequestResponseLogger? logger,
        IReadOnlyDictionary<string, string>? requestHeaders)
    {
        try
        {
            var response = await client.PostAsync(requestUri, content, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger?.LogResponse(providerName, endpoint, (int)response.StatusCode, body, requestHeaders);
            response.Dispose();
            throw new TranslationProviderException(
                TranslationErrorKind.Http,
                BuildHttpErrorMessage(response.StatusCode, body),
                response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogError(providerName, endpoint, "OpenAI/LM Studio 요청이 시간 초과되었습니다.");
            throw new TranslationProviderException(TranslationErrorKind.Timeout, "OpenAI/LM Studio 요청이 시간 초과되었습니다.");
        }
        catch (HttpRequestException ex)
        {
            logger?.LogError(providerName, endpoint, $"OpenAI/LM Studio HTTP 요청 실패: {ex.Message}");
            throw new TranslationProviderException(TranslationErrorKind.Http, $"OpenAI/LM Studio HTTP 요청 실패: {ex.Message}", innerException: ex);
        }
    }

    private static JsonDocument ParseJson(string body, string context)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new TranslationProviderException(TranslationErrorKind.Json, $"{context} JSON 파싱에 실패했습니다.", innerException: ex);
        }
    }

    private static string BuildHttpErrorMessage(HttpStatusCode statusCode, string body)
    {
        var summary = string.IsNullOrWhiteSpace(body)
            ? "응답 본문 없음"
            : body.Length > 180 ? $"{body[..180]}..." : body;
        return $"HTTP {(int)statusCode} 오류: {summary}";
    }

    private static Dictionary<string, string> BuildRequestLogHeaders(
        HttpClient client,
        ResponseMode responseMode,
        string model,
        ProviderSettings settings,
        RequestBuildMetadata requestMetadata)
    {
        var headers = BuildResponseLogHeaders(responseMode, model, settings, requestMetadata);
        headers["Authorization"] = client.DefaultRequestHeaders.Authorization is null
            ? string.Empty
            : SensitiveDataMasker.MaskSecret(client.DefaultRequestHeaders.Authorization.ToString(), visiblePrefixLength: 6);
        return headers;
    }

    private static Dictionary<string, string> BuildResponseLogHeaders(
        ResponseMode responseMode,
        string model,
        ProviderSettings settings,
        RequestBuildMetadata requestMetadata)
    {
        var effectiveTemperature = GetEffectiveTemperature(settings, responseMode);
        var headers = new Dictionary<string, string>
        {
            ["Mode"] = GetResponseModeLabel(responseMode),
            ["Model"] = model,
            ["ModelFamily"] = requestMetadata.ModelFamily.ToString().ToLowerInvariant(),
            ["ThinkingControl"] = GetThinkingControlLabel(requestMetadata.ThinkingControlMode),
            ["Temperature"] = effectiveTemperature.ToString("0.##"),
            ["TopP"] = settings.TopP?.ToString("0.##") ?? string.Empty,
            ["TopK"] = settings.TopK?.ToString() ?? string.Empty,
            ["RepeatPenalty"] = settings.RepeatPenalty?.ToString("0.##") ?? string.Empty,
            ["PresencePenalty"] = settings.PresencePenalty?.ToString("0.##") ?? string.Empty,
            ["Seed"] = settings.Seed?.ToString() ?? string.Empty,
            ["MaxTokens"] = requestMetadata.MaxTokens?.ToString() ?? string.Empty,
            ["FallbackUsed"] = requestMetadata.FallbackUsed ? "true" : "false",
        };

        return headers;
    }

    private static string GetThinkingControlLabel(LmStudioThinkingControlMode thinkingControlMode)
    {
        return thinkingControlMode switch
        {
            LmStudioThinkingControlMode.ApiCustomField => "api_custom_field",
            LmStudioThinkingControlMode.PromptFallback => "prompt_fallback",
            _ => "none",
        };
    }
}
