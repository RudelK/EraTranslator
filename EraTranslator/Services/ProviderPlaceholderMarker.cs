using System.Text.RegularExpressions;
using System.Security;

namespace EraTranslator.Services;

public static partial class ProviderPlaceholderMarker
{
    public static string MarkForDeepL(string text, IReadOnlyList<string> placeholders)
    {
        var escaped = SecurityElement.Escape(text) ?? string.Empty;
        return ReplaceTokens(escaped, placeholders.Count, index => $"<era-ph idx=\"{index}\"/>");
    }

    public static string UnmarkFromDeepL(string text, IReadOnlyList<string> placeholders)
    {
        var restored = text;
        for (var index = 0; index < placeholders.Count; index++)
        {
            restored = restored.Replace($"<era-ph idx=\"{index}\"/>", GetToken(index), StringComparison.Ordinal);
            restored = restored.Replace($"<era-ph idx='{index}'/>", GetToken(index), StringComparison.Ordinal);
            restored = restored.Replace($"<era-ph idx=\"{index}\"></era-ph>", GetToken(index), StringComparison.Ordinal);
            restored = restored.Replace($"<era-ph idx='{index}'></era-ph>", GetToken(index), StringComparison.Ordinal);
        }

        return restored;
    }

    public static string MarkForPapago(string text, IReadOnlyList<string> placeholders)
    {
        return ReplaceTokens(text, placeholders.Count, index => $"ERAPHTOKEN{index}SAFE");
    }

    public static string UnmarkFromPapago(string text, IReadOnlyList<string> placeholders)
    {
        var restored = text;
        for (var index = 0; index < placeholders.Count; index++)
        {
            restored = restored.Replace($"ERAPHTOKEN{index}SAFE", GetToken(index), StringComparison.Ordinal);
        }

        return restored;
    }

    public static bool ContainsMarkedTokens(string text)
    {
        return TokenPattern().IsMatch(text);
    }

    private static string ReplaceTokens(string text, int placeholderCount, Func<int, string> markerFactory)
    {
        var replaced = text;
        for (var index = 0; index < placeholderCount; index++)
        {
            replaced = replaced.Replace(GetToken(index), markerFactory(index), StringComparison.Ordinal);
        }

        return replaced;
    }

    private static string GetToken(int index) => PlaceholderProtector.GetToken(index);

    [GeneratedRegex(@"__PH\d+__", RegexOptions.Compiled)]
    private static partial Regex TokenPattern();
}
