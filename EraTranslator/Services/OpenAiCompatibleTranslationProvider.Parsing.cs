using System.Text.Json;
using System.Text.RegularExpressions;
using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed partial class OpenAiCompatibleTranslationProvider
{
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
        working = NormalizeSingleSegmentPipeArtifacts(working, request);

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

    private static string NormalizeSingleSegmentPipeArtifacts(string value, ProtectedSegment request)
    {
        if (string.IsNullOrWhiteSpace(value) || SourceContainsPipe(request.OriginalText))
        {
            return value;
        }

        var normalized = value.Trim();
        normalized = LeadingPipeBeforePlaceholderPattern().Replace(normalized, string.Empty);
        normalized = PlaceholderBoundaryPipePattern().Replace(normalized, string.Empty);
        normalized = PipeBeforePlaceholderPattern().Replace(normalized, string.Empty);
        normalized = PipeAfterPlaceholderPattern().Replace(normalized, string.Empty);
        normalized = TrailingPipePattern().Replace(normalized, string.Empty);
        return normalized.Trim();
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
            || normalized.StartsWith("문맥에 따라", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("*문맥에 따라", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("주:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeUnrecoverableOutput(string translated, ProtectedSegment request)
    {
        var source = request.OriginalText.Trim();
        var normalized = translated.Trim();

        if (!SourceContainsPipe(source) && normalized.Contains('|', StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static bool SourceContainsPipe(string source)
    {
        return source.Contains('|', StringComparison.Ordinal)
            || source.Contains('｜', StringComparison.Ordinal);
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
        if (!trimmed.StartsWith('{') && !trimmed.StartsWith('['))
        {
            return false;
        }

        try
        {
            using var _ = JsonDocument.Parse(trimmed);
            return true;
        }
        catch
        {
            return false;
        }
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

    [GeneratedRegex(@"^\|(?=__PH\d+__)", RegexOptions.Compiled)]
    private static partial Regex LeadingPipeBeforePlaceholderPattern();

    [GeneratedRegex(@"\|(?=__PH\d+__)", RegexOptions.Compiled)]
    private static partial Regex PipeBeforePlaceholderPattern();

    [GeneratedRegex(@"__\|(?=__PH\d+__)", RegexOptions.Compiled)]
    private static partial Regex PlaceholderBoundaryPipePattern();

    [GeneratedRegex(@"(?<=__PH\d+__)\|", RegexOptions.Compiled)]
    private static partial Regex PipeAfterPlaceholderPattern();

    [GeneratedRegex(@"\|$", RegexOptions.Compiled)]
    private static partial Regex TrailingPipePattern();
}
