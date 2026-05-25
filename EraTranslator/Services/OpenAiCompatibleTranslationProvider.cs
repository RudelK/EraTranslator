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
        CancellationToken cancellationToken)
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

        var requestPayload = BuildRequestPayload(
            model,
            settings,
            requests,
            includeJsonResponseFormat: !isLmStudio,
            useTokenizedProtocol: isLmStudio,
            retryMode: false);
        var content = await SendChatRequestAsync(
            client,
            requestPayload,
            cancellationToken,
            providerName: isLmStudio ? "LM Studio" : "OpenAI",
            endpoint: $"{baseUrl}/chat/completions",
            requestResponseLogger);

        if (!TryParseTranslations(content, isLmStudio, requests, out var parsed))
        {
            var retryPayload = BuildRequestPayload(
                model,
                settings,
                requests,
                includeJsonResponseFormat: false,
                useTokenizedProtocol: isLmStudio,
                retryMode: true);
            var retryContent = await SendChatRequestAsync(
                client,
                retryPayload,
                cancellationToken,
                providerName: isLmStudio ? "LM Studio" : "OpenAI",
                endpoint: $"{baseUrl}/chat/completions",
                requestResponseLogger);
            if (!TryParseTranslations(retryContent, isLmStudio, requests, out parsed))
            {
                throw new TranslationProviderException(TranslationErrorKind.Json, "OpenAI/LM Studio 응답을 기대한 형식으로 파싱하지 못했습니다.");
            }
        }

        if (!TryFinalizeTranslations(parsed, requests, out parsed))
        {
            var retryPayload = BuildRequestPayload(
                model,
                settings,
                requests,
                includeJsonResponseFormat: false,
                useTokenizedProtocol: isLmStudio,
                retryMode: true);
            var retryContent = await SendChatRequestAsync(
                client,
                retryPayload,
                cancellationToken,
                providerName: isLmStudio ? "LM Studio" : "OpenAI",
                endpoint: $"{baseUrl}/chat/completions",
                requestResponseLogger);

            if (!TryParseTranslations(retryContent, isLmStudio, requests, out parsed)
                || !TryFinalizeTranslations(parsed, requests, out parsed))
            {
                throw new TranslationProviderException(TranslationErrorKind.Validation, "번역 응답에 원문, 설명, 후보군, 프롬프트 조각이 섞여 있어 안전하게 복구하지 못했습니다.");
            }
        }

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

    [GeneratedRegex(@"<think\b[^>]*>.*?</think>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ThinkTagPattern();

    [GeneratedRegex(@"(?ms)^\|(?<id>[^\r\n|]+)\|\r?\n(?<text>.*?)\r?\n\|\s*$", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex PipeOutputBlockPattern();

    [GeneratedRegex(@"(?ms)^SEGMENT (?<id>[^\r\n]+)\r?\n(?<text>.*?)\r?\nEND SEGMENT\s*$", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex LegacyOutputBlockPattern();

    [GeneratedRegex(@"\b[A-Za-z][A-Za-z'-]{2,}\b", RegexOptions.Compiled)]
    private static partial Regex AsciiWordPattern();
}
