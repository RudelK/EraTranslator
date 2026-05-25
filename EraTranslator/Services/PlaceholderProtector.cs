using System.Text.RegularExpressions;

namespace EraTranslator.Services;

public sealed partial class PlaceholderProtector
{
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
        var restored = translated;

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

    private static List<Match> CollectMatches(string input)
    {
        var patterns = new[]
        {
            EscapeSequencePattern(),
            PercentPlaceholderPattern(),
            BracePlaceholderPattern(),
            AnglePlaceholderPattern(),
            ChoiceLabelPattern(),
            FullWidthSpecialCharacterPattern(),
        };
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

    [GeneratedRegex(@"\\(?:\\|[%/@#nd]|[A-Za-z]+)", RegexOptions.Compiled)]
    private static partial Regex EscapeSequencePattern();

    [GeneratedRegex(@"%[^%\r\n]+%", RegexOptions.Compiled)]
    private static partial Regex PercentPlaceholderPattern();

    [GeneratedRegex(@"\{[^{}\r\n]+\}", RegexOptions.Compiled)]
    private static partial Regex BracePlaceholderPattern();

    [GeneratedRegex(@"<[^\r\n<>]+>", RegexOptions.Compiled)]
    private static partial Regex AnglePlaceholderPattern();

    [GeneratedRegex(@"\[\s*\d+\s*\]", RegexOptions.Compiled)]
    private static partial Regex ChoiceLabelPattern();

    [GeneratedRegex(@"[／【】＜＞「」％]", RegexOptions.Compiled)]
    private static partial Regex FullWidthSpecialCharacterPattern();

    [GeneratedRegex(@"__PH\d+__", RegexOptions.Compiled)]
    private static partial Regex TokenPattern();
}

public sealed record ProtectedText(string Text, IReadOnlyList<string> Placeholders);
