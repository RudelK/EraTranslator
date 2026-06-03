using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EraTranslator.Services;

public interface INaverJapaneseDictionaryLookupService
{
    Task<NaverJapaneseDictionaryEntry?> TryLookupAsync(string surface, CancellationToken cancellationToken);
}

public sealed partial class NaverJapaneseDictionaryLookupService(
    ISimpleHttpClientFactory? httpClientFactory = null,
    NaverJapaneseDictionaryParser? parser = null) : INaverJapaneseDictionaryLookupService
{
    private static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromMilliseconds(500);
    private static readonly ConcurrentDictionary<string, Lazy<Task<NaverJapaneseDictionaryEntry?>>> InFlightLookups = new(StringComparer.Ordinal);
    private readonly ISimpleHttpClientFactory _httpClientFactory = httpClientFactory ?? new SimpleHttpClientFactory();
    private readonly NaverJapaneseDictionaryParser _parser = parser ?? new NaverJapaneseDictionaryParser();
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private DateTimeOffset _lastRequestAtUtc = DateTimeOffset.MinValue;

    public Task<NaverJapaneseDictionaryEntry?> TryLookupAsync(string surface, CancellationToken cancellationToken)
    {
        var normalizedSurface = (surface ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedSurface))
        {
            return Task.FromResult<NaverJapaneseDictionaryEntry?>(null);
        }

        var lazy = InFlightLookups.GetOrAdd(
            normalizedSurface,
            key => new Lazy<Task<NaverJapaneseDictionaryEntry?>>(
                () => TryLookupCoreAsync(key, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return RemoveWhenCompleteAsync(normalizedSurface, lazy);
    }

    private async Task<NaverJapaneseDictionaryEntry?> TryLookupCoreAsync(string surface, CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var elapsed = DateTimeOffset.UtcNow - _lastRequestAtUtc;
            if (elapsed < MinimumRequestInterval)
            {
                await Task.Delay(MinimumRequestInterval - elapsed, cancellationToken).ConfigureAwait(false);
            }

            _lastRequestAtUtc = DateTimeOffset.UtcNow;
        }
        finally
        {
            _requestGate.Release();
        }

        try
        {
            using var client = _httpClientFactory.CreateClient(nameof(NaverJapaneseDictionaryLookupService));
            client.Timeout = TimeSpan.FromSeconds(3);

            var autocompleteUrl = BuildAutocompleteUrl(surface);
            using (var autocompleteRequest = new HttpRequestMessage(HttpMethod.Get, autocompleteUrl))
            {
                ApplyChromeLikeHeaders(autocompleteRequest);
                using var autocompleteResponse = await client.SendAsync(autocompleteRequest, cancellationToken).ConfigureAwait(false);
                if (autocompleteResponse.IsSuccessStatusCode)
                {
                    var autocompleteContent = await autocompleteResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    var autocompleteEntry = _parser.TryParse(surface, autocompleteContent, autocompleteUrl);
                    if (autocompleteEntry is not null)
                    {
                        return autocompleteEntry;
                    }
                }
                else if (autocompleteResponse.StatusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429)
                {
                    return null;
                }
            }

            var sourceUrl = BuildSearchUrl(surface);
            using var request = new HttpRequestMessage(HttpMethod.Get, sourceUrl);
            ApplyChromeLikeHeaders(request);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return _parser.TryParse(surface, content, sourceUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException or RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static async Task<NaverJapaneseDictionaryEntry?> RemoveWhenCompleteAsync(
        string key,
        Lazy<Task<NaverJapaneseDictionaryEntry?>> lazy)
    {
        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        finally
        {
            InFlightLookups.TryRemove(key, out _);
        }
    }

    private static string BuildSearchUrl(string surface)
    {
        return $"https://ja.dict.naver.com/search.nhn?query={Uri.EscapeDataString(surface)}";
    }

    private static string BuildAutocompleteUrl(string surface)
    {
        return $"https://ac-dict.naver.com/jako/ac?n_katahira=0&st=11&r_lt=11&q={Uri.EscapeDataString(surface)}";
    }

    private static void ApplyChromeLikeHeaders(HttpRequestMessage request)
    {
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
        request.Headers.AcceptLanguage.ParseAdd("ko-KR,ko;q=0.9,ja-JP;q=0.8,ja;q=0.7,en-US;q=0.6,en;q=0.5");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        request.Headers.Pragma.ParseAdd("no-cache");
        request.Headers.Referrer = new Uri("https://ja.dict.naver.com/");
    }
}

public sealed partial class NaverJapaneseDictionaryParser
{
    public NaverJapaneseDictionaryEntry? TryParse(string expectedSurface, string content, string sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(expectedSurface) || string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        if (LooksLikeBlockedPage(content))
        {
            return null;
        }

        var trimmed = content.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            return TryParseJson(expectedSurface.Trim(), content, sourceUrl);
        }

        return TryParseHtml(expectedSurface.Trim(), content, sourceUrl);
    }

    private static NaverJapaneseDictionaryEntry? TryParseJson(string expectedSurface, string content, string sourceUrl)
    {
        var trimmed = content.TrimStart();
        if (!trimmed.StartsWith('{') && !trimmed.StartsWith('['))
        {
            return null;
        }

        using var document = JsonDocument.Parse(content);
        var autocompleteEntry = TryParseAutocompleteJson(expectedSurface, document.RootElement, sourceUrl);
        if (autocompleteEntry is not null)
        {
            return autocompleteEntry;
        }

        foreach (var node in EnumerateJsonObjects(document.RootElement))
        {
            var surface = FirstString(node, "surface", "entry", "headword", "word", "origin", "title");
            if (!string.Equals(NormalizeText(surface), expectedSurface, StringComparison.Ordinal))
            {
                continue;
            }

            var target = FirstString(node, "koTarget", "mean", "meaning", "definition", "means", "translate", "translation");
            target = NormalizeMeaning(target);
            if (string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            var reading = FirstString(node, "reading", "readingKana", "pronunciation", "yomi") ?? string.Empty;
            return new NaverJapaneseDictionaryEntry(
                expectedSurface,
                NormalizeText(reading),
                target,
                sourceUrl,
                IsReviewRequired(target));
        }

        return null;
    }

    private static NaverJapaneseDictionaryEntry? TryParseAutocompleteJson(
        string expectedSurface,
        JsonElement root,
        string sourceUrl)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var section in items.EnumerateArray())
        {
            if (section.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var candidate in section.EnumerateArray())
            {
                if (candidate.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var reading = ReadAutocompleteCandidateValue(candidate, 0);
                var surface = ReadAutocompleteCandidateValue(candidate, 1);
                var meaning = NormalizeMeaning(ReadAutocompleteCandidateValue(candidate, 3));
                var dictionary = ReadAutocompleteCandidateValue(candidate, 5);
                if (!string.Equals(NormalizeText(surface), expectedSurface, StringComparison.Ordinal)
                    || !string.Equals(dictionary, "jako", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(meaning))
                {
                    continue;
                }

                return new NaverJapaneseDictionaryEntry(
                    expectedSurface,
                    NormalizeText(reading),
                    meaning,
                    sourceUrl,
                    IsReviewRequired(meaning));
            }
        }

        return null;
    }

    private static string ReadAutocompleteCandidateValue(JsonElement candidate, int index)
    {
        if (candidate.ValueKind != JsonValueKind.Array || candidate.GetArrayLength() <= index)
        {
            return string.Empty;
        }

        var wrapper = candidate[index];
        if (wrapper.ValueKind == JsonValueKind.Array && wrapper.GetArrayLength() > 0)
        {
            var value = wrapper[0];
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
        }

        return wrapper.ValueKind == JsonValueKind.String ? wrapper.GetString() ?? string.Empty : string.Empty;
    }

    private static NaverJapaneseDictionaryEntry? TryParseHtml(string expectedSurface, string content, string sourceUrl)
    {
        var decoded = WebUtility.HtmlDecode(content);
        var surfaceIndex = decoded.IndexOf(expectedSurface, StringComparison.Ordinal);
        if (surfaceIndex < 0)
        {
            return null;
        }

        var windowStart = Math.Max(0, surfaceIndex - 1000);
        var windowLength = Math.Min(decoded.Length - windowStart, 6000);
        var window = decoded.Substring(windowStart, windowLength);
        foreach (Match match in KoreanMeaningPattern().Matches(window))
        {
            var meaning = NormalizeMeaning(match.Groups["meaning"].Value);
            if (string.IsNullOrWhiteSpace(meaning) || string.Equals(meaning, expectedSurface, StringComparison.Ordinal))
            {
                continue;
            }

            return new NaverJapaneseDictionaryEntry(
                expectedSurface,
                string.Empty,
                meaning,
                sourceUrl,
                IsReviewRequired(meaning));
        }

        return null;
    }

    private static IEnumerable<JsonElement> EnumerateJsonObjects(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
            foreach (var property in element.EnumerateObject())
            {
                foreach (var nested in EnumerateJsonObjects(property.Value))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in EnumerateJsonObjects(item))
                {
                    yield return nested;
                }
            }
        }
    }

    private static string? FirstString(JsonElement node, params string[] names)
    {
        foreach (var name in names)
        {
            if (node.TryGetProperty(name, out var value))
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }

                if (value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            return item.GetString();
                        }

                        if (item.ValueKind == JsonValueKind.Object)
                        {
                            var nested = FirstString(item, "value", "text", "meaning", "mean");
                            if (!string.IsNullOrWhiteSpace(nested))
                            {
                                return nested;
                            }
                        }
                    }
                }
            }
        }

        return null;
    }

    private static bool LooksLikeBlockedPage(string content)
    {
        return content.Contains("captcha", StringComparison.OrdinalIgnoreCase)
            || content.Contains("자동입력", StringComparison.OrdinalIgnoreCase)
            || content.Contains("비정상적인 접근", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeMeaning(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = TagPattern().Replace(WebUtility.HtmlDecode(value), " ");
        normalized = WhitespacePattern().Replace(normalized, " ").Trim();
        normalized = LeadingNumberPattern().Replace(normalized, string.Empty).Trim();
        return normalized;
    }

    private static string NormalizeText(string? value)
    {
        return WhitespacePattern().Replace(WebUtility.HtmlDecode(value ?? string.Empty), " ").Trim();
    }

    private static bool IsReviewRequired(string meaning)
    {
        return meaning.Contains(';')
            || meaning.Contains('/')
            || meaning.Contains('；')
            || meaning.Length > 18;
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex TagPattern();

    [GeneratedRegex("\\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex("^[0-9]+[.)]\\s*", RegexOptions.Compiled)]
    private static partial Regex LeadingNumberPattern();

    [GeneratedRegex("(?<meaning>[가-힣][가-힣\\s·,()（）\\-]{0,40})", RegexOptions.Compiled)]
    private static partial Regex KoreanMeaningPattern();
}
