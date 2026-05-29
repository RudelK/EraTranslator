using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed partial class OpenAiCompatibleTranslationProvider(
    ISimpleHttpClientFactory httpClientFactory,
    bool isLmStudio,
    IRequestResponseLogger? requestResponseLogger = null) : ITranslationProvider
{
    private static readonly JsonSerializerOptions RequestJsonOptions = new()
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
        if (requests.Count == 0)
        {
            return result;
        }

        if (!isLmStudio && string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new TranslationProviderException(TranslationErrorKind.Configuration, "OpenAI API Key를 입력하세요.");
        }

        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? (isLmStudio ? "http://127.0.0.1:1234/v1" : "https://api.openai.com/v1")
            : settings.BaseUrl.TrimEnd('/');
        var model = string.IsNullOrWhiteSpace(settings.Model)
            ? (isLmStudio ? "local-model" : "gpt-4o-mini")
            : settings.Model;

        var client = httpClientFactory.CreateClient(nameof(OpenAiCompatibleTranslationProvider));
        client.BaseAddress = new Uri($"{baseUrl}/");
        if (!isLmStudio)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        }

        var providerName = isLmStudio ? "LM Studio" : "OpenAI";
        var endpoint = $"{baseUrl}/chat/completions";

        return isLmStudio
            ? await TranslateLmStudioAsync(client, model, settings, requests, cancellationToken, providerName, endpoint, glossaryHints)
            : await TranslateOpenAiAsync(client, model, settings, requests, cancellationToken, providerName, endpoint, glossaryHints);
    }

    private async Task<TranslationProviderResult> TranslateOpenAiAsync(
        HttpClient client,
        string model,
        ProviderSettings settings,
        IReadOnlyList<ProtectedSegment> requests,
        CancellationToken cancellationToken,
        string providerName,
        string endpoint,
        IReadOnlyList<GlossaryHint>? glossaryHints)
    {
        var (requestPayload, requestMetadata) = BuildRequestPayload(
            model,
            settings,
            requests,
            ResponseMode.JsonObject,
            includeLmStudioSamplingParameters: false,
            glossaryHints);
        var content = await SendChatRequestAsync(
            client,
            requestPayload,
            ResponseMode.JsonObject,
            model,
            settings,
            requestMetadata,
            cancellationToken,
            providerName,
            endpoint,
            requestResponseLogger);

        if (!TryParseTranslations(content, preferTokenizedProtocol: false, requests, out var parsed))
        {
            parsed = await TranslateWithModeAsync(
                client,
                model,
                settings,
                requests,
                ResponseMode.JsonTextRetry,
                cancellationToken,
                providerName,
                endpoint,
                parseFailureKind: TranslationErrorKind.Json,
                glossaryHints);
        }

        if (!TryFinalizeTranslations(parsed, requests, out parsed))
        {
            parsed = await TranslateWithModeAsync(
                client,
                model,
                settings,
                requests,
                ResponseMode.JsonTextRetry,
                cancellationToken,
                providerName,
                endpoint,
                parseFailureKind: TranslationErrorKind.Validation,
                glossaryHints);
        }

        return BuildResult(parsed, requests);
    }

    private async Task<TranslationProviderResult> TranslateLmStudioAsync(
        HttpClient client,
        string model,
        ProviderSettings settings,
        IReadOnlyList<ProtectedSegment> requests,
        CancellationToken cancellationToken,
        string providerName,
        string endpoint,
        IReadOnlyList<GlossaryHint>? glossaryHints)
    {
        var attempts = new[]
        {
            ResponseMode.JsonSchema,
            ResponseMode.JsonSchemaRetry,
            ResponseMode.TokenizedFallback,
        };

        TranslationProviderException? lastException = null;

        for (var index = 0; index < attempts.Length; index++)
        {
            try
            {
                var parsed = await TranslateWithModeAsync(
                    client,
                    model,
                    settings,
                    requests,
                    attempts[index],
                    cancellationToken,
                    providerName,
                    endpoint,
                    parseFailureKind: attempts[index] == ResponseMode.TokenizedFallback
                        ? TranslationErrorKind.Validation
                        : TranslationErrorKind.Json,
                    glossaryHints);
                return BuildResult(parsed, requests);
            }
            catch (TranslationProviderException ex) when (index < attempts.Length - 1)
            {
                lastException = ex;
                requestResponseLogger?.LogError(
                    providerName,
                    endpoint,
                    $"응답 모드 {GetResponseModeLabel(attempts[index])} 실패: {ex.Message}. 다음 모드 {GetResponseModeLabel(attempts[index + 1])}로 재시도합니다.");
            }
        }

        throw lastException ?? new TranslationProviderException(TranslationErrorKind.Json, "LM Studio 응답을 처리하지 못했습니다.");
    }

    private async Task<Dictionary<string, string>> TranslateWithModeAsync(
        HttpClient client,
        string model,
        ProviderSettings settings,
        IReadOnlyList<ProtectedSegment> requests,
        ResponseMode responseMode,
        CancellationToken cancellationToken,
        string providerName,
        string endpoint,
        TranslationErrorKind parseFailureKind,
        IReadOnlyList<GlossaryHint>? glossaryHints)
    {
        var (requestPayload, requestMetadata) = BuildRequestPayload(
            model,
            settings,
            requests,
            responseMode,
            includeLmStudioSamplingParameters: isLmStudio,
            glossaryHints);
        var content = await SendChatRequestAsync(
            client,
            requestPayload,
            responseMode,
            model,
            settings,
            requestMetadata,
            cancellationToken,
            providerName,
            endpoint,
            requestResponseLogger);

        if (!TryParseTranslations(content, UsesTokenizedProtocol(responseMode), requests, out var parsed))
        {
            throw new TranslationProviderException(parseFailureKind, DescribeParseFailure(content, responseMode));
        }

        if (!TryFinalizeTranslations(parsed, requests, out parsed))
        {
            throw new TranslationProviderException(TranslationErrorKind.Validation, DescribeValidationFailure(content, responseMode));
        }

        return parsed;
    }

    private static TranslationProviderResult BuildResult(
        IReadOnlyDictionary<string, string> parsed,
        IReadOnlyList<ProtectedSegment> requests)
    {
        var result = new TranslationProviderResult();
        foreach (var request in requests)
        {
            if (!parsed.TryGetValue(request.Id, out var translated) || string.IsNullOrWhiteSpace(translated))
            {
                result.Errors[request.Id] = new TranslationErrorDetail(
                    TranslationErrorKind.MissingResult,
                    "응답에서 해당 ID의 번역을 찾지 못했습니다.");
                continue;
            }

            result.Translations[request.Id] = translated;
        }

        return result;
    }

    private static string GetResponseModeLabel(ResponseMode responseMode)
    {
        return responseMode switch
        {
            ResponseMode.JsonObject => "json_object",
            ResponseMode.JsonTextRetry => "json_text_retry",
            ResponseMode.JsonSchema => "json_schema",
            ResponseMode.JsonSchemaRetry => "json_schema_retry",
            ResponseMode.TokenizedFallback => "tokenized_fallback",
            _ => "unknown",
        };
    }

    private static string DescribeParseFailure(string content, ResponseMode responseMode)
    {
        if (responseMode == ResponseMode.TokenizedFallback)
        {
            return "LM Studio tokenized fallback 응답을 파싱하지 못했습니다.";
        }

        var cleaned = content.Trim();
        if (LooksLikeStructuredOutputContamination(cleaned))
        {
            return "LM Studio structured output 응답에 reasoning 또는 stray text가 섞여 있습니다.";
        }

        if (LooksLikeIncompleteStructuredOutput(cleaned))
        {
            return "LM Studio structured output 응답이 중간에 끊기거나 닫히지 않았습니다.";
        }

        return "OpenAI/LM Studio 응답을 기대한 형식으로 파싱하지 못했습니다.";
    }

    private static string DescribeValidationFailure(string content, ResponseMode responseMode)
    {
        if (responseMode == ResponseMode.TokenizedFallback)
        {
            return "LM Studio tokenized fallback 응답에 원문, 설명, 후보군이 섞여 있어 안전하게 복구하지 못했습니다.";
        }

        return LooksLikeStructuredOutputContamination(content)
            ? "LM Studio structured output 응답에 설명문 또는 reasoning 흔적이 섞여 있어 검증에 실패했습니다."
            : "번역 응답에 원문, 설명, 후보군, 프롬프트 조각이 섞여 있어 안전하게 복구하지 못했습니다.";
    }

    private static bool LooksLikeStructuredOutputContamination(string content)
    {
        var normalized = content.Trim();
        return normalized.Contains("Reasoning:", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Analysis:", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Final translation:", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Translated text:", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("<think", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeIncompleteStructuredOutput(string content)
    {
        var normalized = content.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var openBraces = normalized.Count(character => character == '{');
        var closeBraces = normalized.Count(character => character == '}');
        var openBrackets = normalized.Count(character => character == '[');
        var closeBrackets = normalized.Count(character => character == ']');
        return openBraces > closeBraces || openBrackets > closeBrackets;
    }

    [GeneratedRegex(@"<think\b[^>]*>.*?</think>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ThinkTagPattern();

    [GeneratedRegex(@"(?ms)^\|(?<id>[^\r\n|]+)\|\r?\n(?<text>.*?)\r?\n\|\s*$", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex PipeOutputBlockPattern();

    [GeneratedRegex(@"(?ms)^SEGMENT (?<id>[^\r\n]+)\r?\n(?<text>.*?)\r?\nEND SEGMENT\s*$", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex LegacyOutputBlockPattern();
}
