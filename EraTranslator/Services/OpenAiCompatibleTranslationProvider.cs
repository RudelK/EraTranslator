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
    private readonly TranslationProviderType _providerType = isLmStudio ? TranslationProviderType.LmStudio : TranslationProviderType.OpenAi;

    public OpenAiCompatibleTranslationProvider(
        ISimpleHttpClientFactory httpClientFactory,
        TranslationProviderType providerType,
        IRequestResponseLogger? requestResponseLogger = null)
        : this(httpClientFactory, providerType == TranslationProviderType.LmStudio, requestResponseLogger)
    {
        _providerType = providerType;
    }

    private bool IsLmStudio => _providerType == TranslationProviderType.LmStudio;
    private bool IsLemonade => _providerType == TranslationProviderType.Lemonade;
    private bool IsXiaomiMiMo => _providerType == TranslationProviderType.XiaomiMiMo;
    private bool IsOpenAi => _providerType == TranslationProviderType.OpenAi;

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

        if (IsOpenAi && string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new TranslationProviderException(TranslationErrorKind.Configuration, "OpenAI API Key를 입력하세요.");
        }

        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? _providerType switch
            {
                TranslationProviderType.LmStudio => "http://127.0.0.1:1234/v1",
                TranslationProviderType.XiaomiMiMo => "https://api.xiaomimimo.com/v1",
                TranslationProviderType.Lemonade => "http://127.0.0.1:13305/v1",
                _ => "https://api.openai.com/v1",
            }
            : settings.BaseUrl.TrimEnd('/');
        var model = string.IsNullOrWhiteSpace(settings.Model)
            ? (IsOpenAi ? "gpt-4o-mini" : IsXiaomiMiMo ? "mimo-v2.5-pro" : "local-model")
            : settings.Model;

        var client = httpClientFactory.CreateClient(nameof(OpenAiCompatibleTranslationProvider));
        client.BaseAddress = new Uri($"{baseUrl}/");
        if (IsOpenAi || IsXiaomiMiMo)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        }

        var providerName = _providerType switch
        {
            TranslationProviderType.LmStudio => "LM Studio",
            TranslationProviderType.XiaomiMiMo => "Xiaomi MiMo",
            TranslationProviderType.Lemonade => "Lemonade",
            _ => "OpenAI",
        };
        var endpoint = $"{baseUrl}/chat/completions";

        if (IsLmStudio)
        {
            return await TranslateLmStudioAsync(client, model, settings, requests, cancellationToken, providerName, endpoint, glossaryHints);
        }

        if (IsLemonade)
        {
            return await TranslateLemonadeAsync(client, model, settings, requests, cancellationToken, providerName, endpoint, glossaryHints);
        }

        if (IsXiaomiMiMo)
        {
            return await TranslateXiaomiMiMoAsync(client, model, settings, requests, cancellationToken, providerName, endpoint, glossaryHints);
        }

        return
            await TranslateOpenAiAsync(client, model, settings, requests, cancellationToken, providerName, endpoint, glossaryHints);
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
            includeAdvancedSamplingParameters: false,
            allowApiThinkingControl: false,
            glossaryHints: glossaryHints,
            allowPresencePenalty: true,
            allowSeed: true);
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
        if (LmStudioSamplingDefaults.DetectModelFamily(model) == LmStudioModelFamily.TranslateGemma)
        {
            var parsed = await TranslateTranslateGemmaAsync(
                client,
                model,
                settings,
                requests,
                cancellationToken,
                providerName,
                endpoint);
            return BuildResult(parsed, requests);
        }

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

    private async Task<TranslationProviderResult> TranslateLemonadeAsync(
        HttpClient client,
        string model,
        ProviderSettings settings,
        IReadOnlyList<ProtectedSegment> requests,
        CancellationToken cancellationToken,
        string providerName,
        string endpoint,
        IReadOnlyList<GlossaryHint>? glossaryHints)
    {
        if (LmStudioSamplingDefaults.DetectModelFamily(model) == LmStudioModelFamily.TranslateGemma)
        {
            var parsed = await TranslateTranslateGemmaAsync(
                client,
                model,
                settings,
                requests,
                cancellationToken,
                providerName,
                endpoint);
            return BuildResult(parsed, requests);
        }

        var attempts = new[]
        {
            ResponseMode.JsonTextRetry,
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
                    glossaryHints,
                    includeAdvancedSamplingParameters: true,
                    allowApiThinkingControl: false,
                    allowPresencePenalty: false,
                    allowSeed: false);
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

        throw lastException ?? new TranslationProviderException(TranslationErrorKind.Json, "Lemonade 응답을 처리하지 못했습니다.");
    }

    private async Task<TranslationProviderResult> TranslateXiaomiMiMoAsync(
        HttpClient client,
        string model,
        ProviderSettings settings,
        IReadOnlyList<ProtectedSegment> requests,
        CancellationToken cancellationToken,
        string providerName,
        string endpoint,
        IReadOnlyList<GlossaryHint>? glossaryHints)
    {
        var (requestPayload, requestMetadata) = BuildXiaomiMiMoRequestPayload(
            model,
            settings,
            requests,
            ResponseMode.JsonObject,
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
            var (retryPayload, retryMetadata) = BuildXiaomiMiMoRequestPayload(
                model,
                settings,
                requests,
                ResponseMode.JsonTextRetry,
                glossaryHints);
            var retryContent = await SendChatRequestAsync(
                client,
                retryPayload,
                ResponseMode.JsonTextRetry,
                model,
                settings,
                retryMetadata,
                cancellationToken,
                providerName,
                endpoint,
                requestResponseLogger);

            if (!TryParseTranslations(retryContent, preferTokenizedProtocol: false, requests, out parsed))
            {
                throw new TranslationProviderException(TranslationErrorKind.Json, DescribeParseFailure(retryContent, ResponseMode.JsonTextRetry));
            }
        }

        if (!TryFinalizeTranslations(parsed, requests, out parsed))
        {
            var (retryPayload, retryMetadata) = BuildXiaomiMiMoRequestPayload(
                model,
                settings,
                requests,
                ResponseMode.JsonTextRetry,
                glossaryHints);
            var retryContent = await SendChatRequestAsync(
                client,
                retryPayload,
                ResponseMode.JsonTextRetry,
                model,
                settings,
                retryMetadata,
                cancellationToken,
                providerName,
                endpoint,
                requestResponseLogger);

            if (!TryParseTranslations(retryContent, preferTokenizedProtocol: false, requests, out parsed))
            {
                throw new TranslationProviderException(TranslationErrorKind.Json, DescribeParseFailure(retryContent, ResponseMode.JsonTextRetry));
            }

            if (!TryFinalizeTranslations(parsed, requests, out parsed))
            {
                throw new TranslationProviderException(TranslationErrorKind.Validation, DescribeValidationFailure(retryContent, ResponseMode.JsonTextRetry));
            }
        }

        return BuildResult(parsed, requests);
    }

    private async Task<Dictionary<string, string>> TranslateTranslateGemmaAsync(
        HttpClient client,
        string model,
        ProviderSettings settings,
        IReadOnlyList<ProtectedSegment> requests,
        CancellationToken cancellationToken,
        string providerName,
        string endpoint)
    {
        var (requestPayload, requestMetadata) = BuildRequestPayload(
            model,
            settings,
            requests,
            ResponseMode.TranslateGemmaDedicated,
            includeAdvancedSamplingParameters: true,
            allowApiThinkingControl: false,
            glossaryHints: null,
            allowPresencePenalty: !IsLemonade,
            allowSeed: !IsLemonade);
        var content = await SendChatRequestAsync(
            client,
            requestPayload,
            ResponseMode.TranslateGemmaDedicated,
            model,
            settings,
            requestMetadata,
            cancellationToken,
            providerName,
            endpoint,
            requestResponseLogger);

        if (requests.Count != 1)
        {
            throw new TranslationProviderException(TranslationErrorKind.Validation, "TranslateGemma 전용 응답은 단일 세그먼트 요청만 지원합니다.");
        }

        if (!TryNormalizeTranslationCandidate(content, requests[0], out var normalized))
        {
            throw new TranslationProviderException(TranslationErrorKind.Validation, "TranslateGemma 응답을 단일 번역문으로 정규화하지 못했습니다.");
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [requests[0].Id] = normalized,
        };
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
        IReadOnlyList<GlossaryHint>? glossaryHints,
        bool? includeAdvancedSamplingParameters = null,
        bool? allowApiThinkingControl = null,
        bool? allowPresencePenalty = null,
        bool? allowSeed = null)
    {
        var (requestPayload, requestMetadata) = BuildRequestPayload(
            model,
            settings,
            requests,
            responseMode,
            includeAdvancedSamplingParameters: includeAdvancedSamplingParameters ?? IsLmStudio,
            allowApiThinkingControl: allowApiThinkingControl ?? IsLmStudio,
            glossaryHints: glossaryHints,
            allowPresencePenalty: allowPresencePenalty ?? true,
            allowSeed: allowSeed ?? true);
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
            ResponseMode.TranslateGemmaDedicated => "translategemma_dedicated",
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
