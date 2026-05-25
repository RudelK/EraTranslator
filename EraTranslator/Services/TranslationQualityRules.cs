using System.Text.RegularExpressions;

namespace EraTranslator.Services;

public static partial class TranslationQualityRules
{
    public static string NormalizeTranslatedText(string fileType, string text)
    {
        var normalized = NormalizeProtectedCharacterSpacing(text);
        return string.Equals(fileType, "CSV", StringComparison.OrdinalIgnoreCase)
            ? RemoveSpacesForCsv(normalized)
            : normalized;
    }

    public static string NormalizeProtectedCharacterSpacing(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return ProtectedFullWidthCharacterSpacingPattern().Replace(text, "$1");
    }

    public static bool RequiresLengthReview(string originalText, string translatedText)
    {
        var originalLength = GetMeaningfulLength(originalText);
        var translatedLength = GetMeaningfulLength(translatedText);
        if (originalLength == 0 || translatedLength == 0)
        {
            return false;
        }

        return translatedLength >= originalLength * 1.5
            || originalLength >= translatedLength * 1.5;
    }

    public static string? GetReviewReason(string originalText, string translatedText)
    {
        var normalizedSource = originalText.Trim();
        var normalizedTranslated = translatedText.Trim();

        if (!SourceContainsSlashLike(normalizedSource) && SlashSeparatedAlternativePattern().IsMatch(normalizedTranslated))
        {
            return "대체 후보가 함께 출력되어 검토가 필요합니다.";
        }

        if (!SourceContainsParentheses(normalizedSource) && AddedExplanationParenthesesPattern().IsMatch(normalizedTranslated))
        {
            return "설명 괄호가 추가되어 검토가 필요합니다.";
        }

        if (RequiresLengthReview(originalText, translatedText))
        {
            return "원문과 번역문의 길이 차이가 커서 검토가 필요합니다.";
        }

        return null;
    }

    private static int GetMeaningfulLength(string text)
    {
        return text.Count(static character => !char.IsWhiteSpace(character));
    }

    private static string RemoveSpacesForCsv(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return CsvWhitespacePattern().Replace(text, string.Empty);
    }

    private static bool SourceContainsSlashLike(string source)
    {
        return source.Contains('/', StringComparison.Ordinal) || source.Contains('／', StringComparison.Ordinal);
    }

    private static bool SourceContainsParentheses(string source)
    {
        return source.Contains('(', StringComparison.Ordinal)
            || source.Contains(')', StringComparison.Ordinal)
            || source.Contains('（', StringComparison.Ordinal)
            || source.Contains('）', StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\s*([／【】＜＞「」％])\s*", RegexOptions.Compiled)]
    private static partial Regex ProtectedFullWidthCharacterSpacingPattern();

    [GeneratedRegex(@"\S+\s*(?:/|／)\s*\S+", RegexOptions.Compiled)]
    private static partial Regex SlashSeparatedAlternativePattern();

    [GeneratedRegex(@"[\(\（][^)\）\r\n]{2,}[\)\）]", RegexOptions.Compiled)]
    private static partial Regex AddedExplanationParenthesesPattern();

    [GeneratedRegex(@"[ \t\u3000]+", RegexOptions.Compiled)]
    private static partial Regex CsvWhitespacePattern();
}
