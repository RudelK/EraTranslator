using System.Text.RegularExpressions;

namespace EraTranslator.Services;

public sealed partial class PlaceholderProtector
{
    public const string DefaultFullWidthSpecialCharacters = "／【】＜＞「」（）『』％：";

    private readonly Regex? _fullWidthSpecialCharacterPattern;

    public PlaceholderProtector(string? fullWidthSpecialCharacters = null)
    {
        var normalizedCharacters = NormalizeFullWidthSpecialCharacters(fullWidthSpecialCharacters ?? DefaultFullWidthSpecialCharacters);
        _fullWidthSpecialCharacterPattern = normalizedCharacters.Length == 0
            ? null
            : new Regex($"[{Regex.Escape(normalizedCharacters)}]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    public static string GetToken(int index) => $"__PH{index}__";

    public ProtectedText Protect(string input)
    {
        var placeholders = new List<string>();
        var matches = CollectMatches(input);
        if (matches.Count == 0)
        {
            return new ProtectedText(input, placeholders);
        }

        var builder = new System.Text.StringBuilder(input.Length);
        var currentIndex = 0;

        foreach (var match in matches.OrderBy(static match => match.Index))
        {
            builder.Append(input, currentIndex, match.Index - currentIndex);
            var token = GetToken(placeholders.Count);
            placeholders.Add(match.Value);
            builder.Append(token);
            currentIndex = match.Index + match.Length;
        }

        builder.Append(input, currentIndex, input.Length - currentIndex);
        return new ProtectedText(builder.ToString(), placeholders);
    }

    public string Restore(string translated, IReadOnlyList<string> placeholders)
    {
        var restored = NormalizeTokenCandidates(translated, placeholders);

        for (var index = 0; index < placeholders.Count; index++)
        {
            restored = restored.Replace(GetToken(index), placeholders[index], StringComparison.Ordinal);
        }

        return restored;
    }

    public bool HasAllTokens(string translated, IReadOnlyList<string> placeholders, out string error)
    {
        var matches = TokenPattern().Matches(translated);
        if (matches.Count != placeholders.Count)
        {
            error = $"보호 토큰 개수가 일치하지 않습니다. 예상 {placeholders.Count}개, 실제 {matches.Count}개입니다.";
            return false;
        }

        for (var index = 0; index < placeholders.Count; index++)
        {
            var expectedToken = GetToken(index);
            if (!string.Equals(matches[index].Value, expectedToken, StringComparison.Ordinal))
            {
                error = $"보호 토큰 순서 또는 값이 손상되었습니다. 예상 {expectedToken}, 실제 {matches[index].Value}입니다.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    public string NormalizeTokenCandidates(string translated, IReadOnlyList<string> placeholders)
    {
        if (string.IsNullOrWhiteSpace(translated) || placeholders.Count == 0)
        {
            return translated;
        }

        var normalized = ReplaceLooseTokenPattern(translated, MissingTrailingUnderscorePattern(), placeholders);
        normalized = ReplaceLooseTokenPattern(normalized, MissingLeadingUnderscorePattern(), placeholders);
        normalized = ReplaceLooseTokenPattern(normalized, MissingBothUnderscoresPattern(), placeholders);
        return normalized;
    }

    private static string ReplaceLooseTokenPattern(string translated, Regex pattern, IReadOnlyList<string> placeholders)
    {
        return pattern.Replace(
            translated,
            match =>
            {
                if (!int.TryParse(match.Groups["index"].Value, out var index)
                    || index < 0
                    || index >= placeholders.Count)
                {
                    return match.Value;
                }

                return GetToken(index);
            });
    }

    private List<Match> CollectMatches(string input)
    {
        var patterns = new List<Regex>
        {
            EscapeSequencePattern(),
            PercentPlaceholderPattern(),
            BracePlaceholderPattern(),
            AnglePlaceholderPattern(),
            ErbSyntaxCatalog.HtmlEntityPattern(),
            ErbIndexedFunctionReferencePattern(),
            ErbSyntaxCatalog.CreateSymbolReferencePattern(SymbolNamespaceRegistry.Default),
            ChoiceLabelPattern(),
        };
        if (_fullWidthSpecialCharacterPattern is not null)
        {
            patterns.Add(_fullWidthSpecialCharacterPattern);
        }

        var selected = new List<Match>();

        foreach (var pattern in patterns)
        {
            foreach (Match match in pattern.Matches(input))
            {
                if (selected.Any(existing => RangesOverlap(existing.Index, existing.Length, match.Index, match.Length)))
                {
                    continue;
                }

                selected.Add(match);
            }
        }

        return selected;
    }

    private static bool RangesOverlap(int leftStart, int leftLength, int rightStart, int rightLength)
    {
        var leftEnd = leftStart + leftLength;
        var rightEnd = rightStart + rightLength;
        return leftStart < rightEnd && rightStart < leftEnd;
    }

    private static string NormalizeFullWidthSpecialCharacters(string characters)
    {
        var seen = new HashSet<char>();
        var normalized = new System.Text.StringBuilder(characters.Length);
        foreach (var ch in characters)
        {
            if (ch is '\r' or '\n' || !seen.Add(ch))
            {
                continue;
            }

            normalized.Append(ch);
        }

        return normalized.ToString();
    }

    [GeneratedRegex(@"\\(?:\\|[%/@#nd]|[A-Za-z]+)", RegexOptions.Compiled)]
    private static partial Regex EscapeSequencePattern();

    [GeneratedRegex(@"%[^%\r\n]+%", RegexOptions.Compiled)]
    private static partial Regex PercentPlaceholderPattern();

    [GeneratedRegex(@"\{[^{}\r\n]+\}", RegexOptions.Compiled)]
    private static partial Regex BracePlaceholderPattern();

    [GeneratedRegex(@"<[^\r\n<>]+>", RegexOptions.Compiled)]
    private static partial Regex AnglePlaceholderPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}_])[\p{L}_][\p{L}\p{N}_]*:\((?:[^()\r\n]|\([^()\r\n]*\))*\)", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex ErbIndexedFunctionReferencePattern();

    [GeneratedRegex(@"\[\s*\d+\s*\]", RegexOptions.Compiled)]
    private static partial Regex ChoiceLabelPattern();

    [GeneratedRegex(@"(?<!_)__PH\s*(?<index>\d+)_(?!_)", RegexOptions.Compiled)]
    private static partial Regex MissingTrailingUnderscorePattern();

    [GeneratedRegex(@"(?<!_)_PH\s*(?<index>\d+)__(?!_)", RegexOptions.Compiled)]
    private static partial Regex MissingLeadingUnderscorePattern();

    [GeneratedRegex(@"(?<!_)_PH\s*(?<index>\d+)_(?!_)", RegexOptions.Compiled)]
    private static partial Regex MissingBothUnderscoresPattern();

    [GeneratedRegex(@"__PH\d+__", RegexOptions.Compiled)]
    private static partial Regex TokenPattern();
}

public sealed record ProtectedText(string Text, IReadOnlyList<string> Placeholders);
