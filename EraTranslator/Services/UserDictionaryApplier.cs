using System.Text;
using System.Text.RegularExpressions;
using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed partial class UserDictionaryApplier
{
    public ProtectedText Apply(
        string input,
        IReadOnlyList<string> existingPlaceholders,
        IReadOnlyList<UserDictionaryEntry> entries)
    {
        var normalizedEntries = entries
            .Where(entry => entry.IsEnabled)
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Source) && !string.IsNullOrWhiteSpace(entry.Target))
            .OrderByDescending(entry => entry.Source.Length)
            .ThenBy(entry => entry.Source, StringComparer.Ordinal)
            .ToList();

        if (normalizedEntries.Count == 0)
        {
            return new ProtectedText(input, existingPlaceholders.ToList());
        }

        var placeholders = existingPlaceholders.ToList();
        var working = input;

        foreach (var entry in normalizedEntries)
        {
            working = ReplaceOutsideProtectedTokens(working, entry.Source, () =>
            {
                var token = GetToken(placeholders.Count);
                placeholders.Add(entry.Target);
                return token;
            });
        }

        return new ProtectedText(working, placeholders);
    }

    private static string ReplaceOutsideProtectedTokens(string input, string source, Func<string> replacementFactory)
    {
        if (string.IsNullOrEmpty(source))
        {
            return input;
        }

        var builder = new StringBuilder();
        var currentIndex = 0;

        foreach (Match tokenMatch in EraTokenPattern().Matches(input))
        {
            AppendReplacedSegment(builder, input.AsSpan(currentIndex, tokenMatch.Index - currentIndex), source, replacementFactory);
            builder.Append(tokenMatch.Value);
            currentIndex = tokenMatch.Index + tokenMatch.Length;
        }

        AppendReplacedSegment(builder, input.AsSpan(currentIndex), source, replacementFactory);
        return builder.ToString();
    }

    private static void AppendReplacedSegment(
        StringBuilder builder,
        ReadOnlySpan<char> segment,
        string source,
        Func<string> replacementFactory)
    {
        if (segment.IsEmpty)
        {
            return;
        }

        var text = segment.ToString();
        var searchStart = 0;

        while (searchStart < text.Length)
        {
            var matchIndex = text.IndexOf(source, searchStart, StringComparison.Ordinal);
            if (matchIndex < 0)
            {
                builder.Append(text, searchStart, text.Length - searchStart);
                break;
            }

            builder.Append(text, searchStart, matchIndex - searchStart);
            builder.Append(replacementFactory());
            searchStart = matchIndex + source.Length;
        }
    }

    private static string GetToken(int index) => PlaceholderProtector.GetToken(index);

    [GeneratedRegex(@"__PH\d+__", RegexOptions.Compiled)]
    private static partial Regex EraTokenPattern();
}
