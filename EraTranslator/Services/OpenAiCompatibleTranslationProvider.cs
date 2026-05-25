using System.Net;
using System.Net.Http.Headers;
using System.Text;
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

    private static bool TryFinalizeTranslations(
        IReadOnlyDictionary<string, string> parsed,
        IReadOnlyList<ProtectedSegment> requests,
        out Dictionary<string, string> finalized)
    {
        finalized = [];

        foreach (var request in requests)
        {
            if (!parsed.TryGetValue(request.Id, out var translated) || string.IsNullOrWhiteSpace(translated))
            {
                return false;
            }

            if (!TryNormalizeTranslationCandidate(translated, request, out var normalized))
            {
                return false;
            }

            finalized[request.Id] = normalized;
        }

        return finalized.Count > 0;
    }

    private static bool TryNormalizeTranslationCandidate(string raw, ProtectedSegment request, out string normalized)
    {
        var working = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (string.IsNullOrWhiteSpace(working))
        {
            normalized = string.Empty;
            return false;
        }

        if (TryExtractSourcePipeInline(working, request, out var inlineTranslation))
        {
            working = inlineTranslation;
        }

        if (TryExtractRecoveredTranslation(working, request, out var recovered))
        {
            working = recovered;
        }

        working = StripWrappedPipes(working);
        working = TrimTrailingPipe(working).Trim();

        if (LooksLikePromptEchoLine(working) || LooksLikeExplanationLine(working))
        {
            normalized = string.Empty;
            return false;
        }

        if (LooksLikeUnrecoverableOutput(working, request))
        {
            normalized = string.Empty;
            return false;
        }

        normalized = working;
        return !string.IsNullOrWhiteSpace(normalized);
    }

    private static bool TryExtractRecoveredTranslation(string raw, ProtectedSegment request, out string recovered)
    {
        recovered = string.Empty;
        var lines = raw
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (lines.Count == 0)
        {
            return false;
        }

        var filtered = lines
            .Where(line => !LooksLikePromptEchoLine(line) && !LooksLikeExplanationLine(line))
            .ToList();

        if (filtered.Count == 0)
        {
            return false;
        }

        for (var index = 0; index < filtered.Count - 1; index++)
        {
            if (MatchesWrappedSource(filtered[index], request))
            {
                recovered = StripWrappedPipes(filtered[index + 1]).Trim();
                return !string.IsNullOrWhiteSpace(recovered);
            }
        }

        foreach (var line in filtered)
        {
            if (TryExtractSourcePipeInline(line, request, out var candidate))
            {
                recovered = candidate;
                return true;
            }
        }

        if (filtered.Count == 1)
        {
            recovered = StripWrappedPipes(filtered[0]).Trim();
            return !string.IsNullOrWhiteSpace(recovered);
        }

        var lastLine = StripWrappedPipes(filtered[^1]).Trim();
        if (!string.IsNullOrWhiteSpace(lastLine)
            && !MatchesSource(lastLine, request))
        {
            recovered = lastLine;
            return true;
        }

        return false;
    }

    private static bool TryExtractSourcePipeInline(string raw, ProtectedSegment request, out string translated)
    {
        translated = string.Empty;
        var line = raw.Trim();
        var separators = new[] { "|" };

        foreach (var separator in separators)
        {
            var separatorIndex = line.IndexOf(separator, StringComparison.Ordinal);
            if (separatorIndex <= 0 || separatorIndex >= line.Length - separator.Length)
            {
                continue;
            }

            var left = line[..separatorIndex].Trim().Trim('|');
            var right = line[(separatorIndex + separator.Length)..].Trim().Trim('|');
            if (string.IsNullOrWhiteSpace(right))
            {
                continue;
            }

            if (MatchesSource(left, request))
            {
                translated = right;
                return true;
            }
        }

        return false;
    }

    private static bool MatchesWrappedSource(string line, ProtectedSegment request)
    {
        return MatchesSource(StripWrappedPipes(line).Trim(), request);
    }

    private static bool MatchesSource(string value, ProtectedSegment request)
    {
        return string.Equals(value, request.OriginalText.Trim(), StringComparison.Ordinal)
            || string.Equals(value, request.Text.Trim(), StringComparison.Ordinal);
    }

    private static string StripWrappedPipes(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed.StartsWith('|') && trimmed.EndsWith('|')
            ? trimmed[1..^1].Trim()
            : trimmed;
    }

    private static string TrimTrailingPipe(string value)
    {
        return value.EndsWith("|", StringComparison.Ordinal)
            ? value[..^1]
            : value;
    }

    private static bool LooksLikePromptEchoLine(string line)
    {
        var normalized = line.Trim();
        return normalized.StartsWith("Target language:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("|Target language:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("대상 언어:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("|대상 언어:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Input segments:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Input JSON:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Return only", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("There is exactly one input item", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("translation engine for Emuera game scripts", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("SEGMENT ", StringComparison.Ordinal)
            || normalized.Equals("END SEGMENT", StringComparison.Ordinal);
    }

    private static bool LooksLikeExplanationLine(string line)
    {
        var normalized = line.Trim();
        return normalized.Contains("context dependent", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("depending on context", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("usually refers to", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("I will provide", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Since the prompt", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("*Note", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Note:", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("문맥에 따라", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeUnrecoverableOutput(string translated, ProtectedSegment request)
    {
        var source = request.OriginalText.Trim();
        var normalized = translated.Trim();

        if (!SourceContainsPipe(source) && normalized.Contains('|', StringComparison.Ordinal))
        {
            return true;
        }

        if (!ContainsAsciiWord(source) && ContainsAsciiWord(normalized))
        {
            return true;
        }

        return false;
    }

    private static bool SourceContainsPipe(string source) => source.Contains('|', StringComparison.Ordinal);

    private static bool ContainsAsciiWord(string value)
    {
        return AsciiWordPattern().IsMatch(value);
    }

    private static object BuildRequestPayload(
        string model,
        ProviderSettings settings,
        IReadOnlyList<ProtectedSegment> requests,
        bool includeJsonResponseFormat,
        bool useTokenizedProtocol,
        bool retryMode)
    {
        var systemPrompt = BuildSystemPrompt(settings, retryMode, useTokenizedProtocol, requests);

        return includeJsonResponseFormat
            ? new
            {
                model,
                temperature = retryMode ? 0 : settings.Temperature,
                response_format = new
                {
                    type = "json_object",
                },
                messages = BuildMessages(systemPrompt, requests, settings, useTokenizedProtocol: false),
            }
            : new
            {
                model,
                temperature = retryMode ? 0 : settings.Temperature,
                messages = BuildMessages(systemPrompt, requests, settings, useTokenizedProtocol),
            };
    }

    private static object[] BuildMessages(
        string systemPrompt,
        IReadOnlyList<ProtectedSegment> requests,
        ProviderSettings settings,
        bool useTokenizedProtocol)
    {
        return
        [
            new
            {
                role = "system",
                content = systemPrompt,
            },
            new
            {
                role = "user",
                content = useTokenizedProtocol
                    ? BuildTokenizedUserContent(requests, settings)
                    : BuildJsonCompatibleUserContent(requests, settings),
            },
        ];
    }

    private static async Task<string> SendChatRequestAsync(
        HttpClient client,
        object payload,
        CancellationToken cancellationToken,
        string providerName,
        string endpoint,
        IRequestResponseLogger? logger)
    {
        var serializedPayload = JsonSerializer.Serialize(payload, RequestJsonOptions);
        logger?.LogRequest(
            providerName,
            endpoint,
            serializedPayload,
            new Dictionary<string, string>
            {
                ["Authorization"] = client.DefaultRequestHeaders.Authorization is null
                    ? string.Empty
                    : MaskSecret(client.DefaultRequestHeaders.Authorization.ToString()),
            });

        using var requestContent = new StringContent(serializedPayload, Encoding.UTF8);
        requestContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await PostAsync(client, "chat/completions", requestContent, cancellationToken, providerName, endpoint, logger);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        logger?.LogResponse(providerName, endpoint, (int)response.StatusCode, body);
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
        IRequestResponseLogger? logger)
    {
        try
        {
            var response = await client.PostAsync(requestUri, content, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger?.LogResponse(providerName, endpoint, (int)response.StatusCode, body);
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

    private static bool TryParseTranslations(
        string content,
        bool preferTokenizedProtocol,
        IReadOnlyList<ProtectedSegment> requests,
        out Dictionary<string, string> translations)
    {
        if (preferTokenizedProtocol && TryParseTokenizedTranslations(content, requests, out translations))
        {
            return true;
        }

        translations = [];
        var cleaned = PrepareJsonEnvelopeContent(content);

        try
        {
            using var json = JsonDocument.Parse(cleaned);
            var array = json.RootElement.TryGetProperty("translations", out var translationsNode)
                ? translationsNode
                : json.RootElement;

            foreach (var item in array.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString();
                var translated = item.GetProperty("translated").GetString();
                if (!string.IsNullOrWhiteSpace(id) && translated is not null)
                {
                    translations[id] = translated;
                }
            }

            return translations.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildSystemPrompt(
        ProviderSettings settings,
        bool retryMode,
        bool useTokenizedProtocol,
        IReadOnlyList<ProtectedSegment> requests)
    {
        var targetLanguageLabel = LanguageDisplayService.ToInstructionLabel(settings.TargetLanguage);
        var hasProtectedPlaceholders = requests.Any(request => request.Placeholders.Count > 0);
        var placeholderInstruction = hasProtectedPlaceholders
            ? Environment.NewLine
              + "These inputs contain placeholder tokens such as __PH0__."
              + Environment.NewLine
              + "Treat every placeholder token as immutable script syntax, not as natural language."
              + Environment.NewLine
              + "Do not translate, rename, split, remove, duplicate, reorder, or add spaces inside placeholder tokens."
              + Environment.NewLine
              + "If a placeholder token would be broken, keep the source structure unchanged instead."
            : string.Empty;
        var termStyleInstruction =
            Environment.NewLine
            + "Choose exactly one final translation for each item."
            + Environment.NewLine
            + "Do not provide alternatives, substitutes, multiple candidates, slash-separated variants, pipe-separated variants, or fallback options."
            + Environment.NewLine
            + "Do not append explanatory parentheses, glosses, or notes unless the source text already contains them."
            + Environment.NewLine
            + $"For kanji-heavy labels, glossary entries, item names, and stat names translated into {targetLanguageLabel}, prefer a Hangul reading of the Japanese term over an explanatory replacement when uncertain.";

        if (!useTokenizedProtocol)
        {
            var rendered = TranslationPromptTemplates.Render(
                retryMode ? settings.RetryPromptTemplate : settings.SystemPromptTemplate,
                settings.TargetLanguage,
                settings.DisableThinking,
                retryMode);

            if (requests.Count == 1)
            {
                return rendered
                    + placeholderInstruction
                    + termStyleInstruction
                    + Environment.NewLine
                    + $"There is exactly one input item. In the output JSON, use id \"{requests[0].Id}\" for the single translation item.";
            }

            return rendered
                + placeholderInstruction
                + termStyleInstruction
                + Environment.NewLine
                + "The user message uses repeated |id| blocks. Read the text inside each block and return one JSON item per id.";
        }

        return
            $"""
            You are a translation engine for Emuera game scripts.
            Translate each input segment from Japanese into {targetLanguageLabel}.

            Output rules:
            1. Do not return JSON.
            2. Do not return markdown, code fences, prose, comments, or extra explanations.
            3. Preserve placeholder tokens such as __PH0__ exactly.
            4. Preserve line breaks and escape sequences exactly when they exist.
            5. Keep each segment id unchanged.
            6. If there is only one input segment, return only the translated text itself with no label, no separator, and no extra line.
            7. If there are multiple input segments, return exactly one output block for every input block.
            8. If a line is unsafe or ambiguous, copy the source text into the translated block instead of explaining.
            9. For multiple input segments, use this exact format only:
            |<id>|
            <translated text>
            |
            10. Do not write any text before the first output or after the last output.
            11. The translated text itself must be written in {targetLanguageLabel}. Do not answer in English unless the target language is English.
            """ +
            placeholderInstruction +
            termStyleInstruction +
            Environment.NewLine + Environment.NewLine +
            TranslationPromptTemplates.BuildThinkingInstruction(settings.DisableThinking);
    }

    private static string BuildJsonCompatibleUserContent(IReadOnlyList<ProtectedSegment> requests, ProviderSettings settings)
    {
        if (requests.Count == 1)
        {
            return requests[0].Text;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Target language: {LanguageDisplayService.ToInstructionLabel(settings.TargetLanguage)}");
        foreach (var request in requests)
        {
            builder.AppendLine($"|{request.Id}|");
            builder.AppendLine(request.Text);
            builder.AppendLine("|");
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildTokenizedUserContent(IReadOnlyList<ProtectedSegment> requests, ProviderSettings settings)
    {
        if (requests.Count == 1)
        {
            return
                $"Target language: {LanguageDisplayService.ToInstructionLabel(settings.TargetLanguage)}" + Environment.NewLine +
                "Return only the translated text with no label or separator." + Environment.NewLine +
                requests[0].Text;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Target language: {LanguageDisplayService.ToInstructionLabel(settings.TargetLanguage)}");
        builder.AppendLine("Input segments:");
        foreach (var request in requests)
        {
            builder.AppendLine($"|{request.Id}|");
            builder.AppendLine(request.Text);
            builder.AppendLine("|");
        }

        builder.Append("Return only |id| ... | blocks with the same ids and count. Write translated text in the target language.");
        return builder.ToString();
    }

    private static bool TryParseTokenizedTranslations(
        string content,
        IReadOnlyList<ProtectedSegment> requests,
        out Dictionary<string, string> translations)
    {
        translations = [];
        var cleaned = PrepareDelimitedEnvelopeContent(content);
        if (requests.Count == 1)
        {
            if (TryParsePipeDelimitedTranslations(cleaned, out translations)
                || TryParseLegacyTokenizedTranslations(cleaned, out translations))
            {
                return translations.Count > 0;
            }

            if (LooksLikeJsonEnvelope(cleaned))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                translations[requests[0].Id] = cleaned;
                return true;
            }

            return false;
        }

        if (TryParsePipeDelimitedTranslations(cleaned, out translations))
        {
            return true;
        }

        if (TryParseLegacyTokenizedTranslations(cleaned, out translations))
        {
            return true;
        }

        return false;
    }

    private static bool LooksLikeJsonEnvelope(string content)
    {
        var trimmed = content.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    private static bool TryParsePipeDelimitedTranslations(string content, out Dictionary<string, string> translations)
    {
        translations = [];
        var matches = PipeOutputBlockPattern().Matches(content);
        if (matches.Count == 0)
        {
            return false;
        }

        foreach (Match match in matches)
        {
            var id = match.Groups["id"].Value.Trim();
            var translated = match.Groups["text"].Value.TrimEnd();
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            translations[id] = translated;
        }

        return translations.Count > 0;
    }

    private static bool TryParseLegacyTokenizedTranslations(string content, out Dictionary<string, string> translations)
    {
        translations = [];
        var matches = LegacyOutputBlockPattern().Matches(content);
        if (matches.Count == 0)
        {
            return false;
        }

        foreach (Match match in matches)
        {
            var id = match.Groups["id"].Value.Trim();
            var translated = match.Groups["text"].Value.TrimEnd();
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            translations[id] = translated;
        }

        return translations.Count > 0;
    }

    private static string PrepareJsonEnvelopeContent(string content)
    {
        var cleaned = PrepareDelimitedEnvelopeContent(content);

        if (TryExtractJsonRegion(cleaned, '{', '}', out var objectJson))
        {
            return objectJson;
        }

        if (TryExtractJsonRegion(cleaned, '[', ']', out var arrayJson))
        {
            return arrayJson;
        }

        return cleaned;
    }

    private static string PrepareDelimitedEnvelopeContent(string content)
    {
        var cleaned = StripThinkingTags(content).Trim();
        if (cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBreak = cleaned.IndexOf('\n');
            var lastFence = cleaned.LastIndexOf("```", StringComparison.Ordinal);
            if (firstBreak >= 0 && lastFence > firstBreak)
            {
                cleaned = cleaned[(firstBreak + 1)..lastFence].Trim();
            }
        }

        return cleaned;
    }

    private static string StripThinkingTags(string content)
    {
        var withoutThinkTags = ThinkTagPattern().Replace(content, string.Empty);
        return withoutThinkTags.Replace("<thinking>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</thinking>", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryExtractJsonRegion(string content, char startChar, char endChar, out string json)
    {
        var startIndex = content.IndexOf(startChar);
        var endIndex = content.LastIndexOf(endChar);
        if (startIndex >= 0 && endIndex > startIndex)
        {
            json = content[startIndex..(endIndex + 1)].Trim();
            return true;
        }

        json = string.Empty;
        return false;
    }

    private static string MaskSecret(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (value.Length <= 10)
        {
            return "****";
        }

        return $"{value[..6]}****{value[^4..]}";
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
