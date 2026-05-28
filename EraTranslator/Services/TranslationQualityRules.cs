using System.Text.RegularExpressions;

namespace EraTranslator.Services;

public static partial class TranslationQualityRules
{
    public static string NormalizeTranslatedText(string fileType, string text, bool preserveWhitespace = false)
    {
        if (string.Equals(fileType, "CSV", StringComparison.OrdinalIgnoreCase))
        {
            var normalizedCsv = NormalizeCsvPunctuation(text);
            normalizedCsv = NormalizeProtectedCharacterSpacing(normalizedCsv);
            return RemoveSpacesForCsv(normalizedCsv, preserveWhitespace);
        }

        return NormalizeErbFunctionArgumentSeparators(NormalizeProtectedCharacterSpacing(text));
    }

    public static string NormalizeErbFunctionArgumentSeparators(string text)
    {
        if (string.IsNullOrWhiteSpace(text)
            || (!text.Contains('(') && !text.Contains('{'))
            || !text.Contains('、'))
        {
            return text;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        var quote = false;
        var rewriteContextDepth = 0;
        var rewriteContextStack = new Stack<(char closingCharacter, bool rewriteSeparators)>();

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                quote = !quote;
                builder.Append(character);
                continue;
            }

            if (!quote && (character == '(' || character == '{'))
            {
                var rewriteSeparators = character == '('
                    ? LooksLikeErbFunctionCall(text, index)
                    : LooksLikeErbBraceExpression(text, index);
                rewriteContextStack.Push((character == '(' ? ')' : '}', rewriteSeparators));
                if (rewriteSeparators)
                {
                    rewriteContextDepth++;
                }

                builder.Append(character);
                continue;
            }

            if (!quote && (character == ')' || character == '}'))
            {
                if (rewriteContextStack.Count > 0
                    && rewriteContextStack.Peek().closingCharacter == character)
                {
                    var context = rewriteContextStack.Pop();
                    if (context.rewriteSeparators)
                    {
                        rewriteContextDepth = Math.Max(0, rewriteContextDepth - 1);
                    }
                }

                builder.Append(character);
                continue;
            }

            if (!quote && character == '、' && rewriteContextDepth > 0)
            {
                builder.Append(',');
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
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

        return translatedLength < originalLength
            || translatedLength >= originalLength * 1.5;
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

        if (!ContainsAsciiWord(normalizedSource) && ContainsAsciiWord(normalizedTranslated))
        {
            return "영어 또는 로마자 잡음이 섞여 있어 검토가 필요합니다.";
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

    private static string RemoveSpacesForCsv(string text, bool preserveWhitespace)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        if (preserveWhitespace)
        {
            return text;
        }

        return CsvWhitespacePattern().Replace(text, string.Empty);
    }

    private static string NormalizeCsvPunctuation(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return CsvAsciiCommaPattern().Replace(text, "、");
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

    private static bool ContainsAsciiWord(string value)
    {
        return AsciiWordPattern().IsMatch(value);
    }

    private static bool LooksLikeErbFunctionCall(string text, int openParenIndex)
    {
        var end = openParenIndex - 1;
        while (end >= 0 && char.IsWhiteSpace(text[end]))
        {
            end--;
        }

        if (end < 0)
        {
            return false;
        }

        var start = end;
        while (start >= 0 && IsFunctionTokenCharacter(text[start]))
        {
            start--;
        }

        var token = text[(start + 1)..(end + 1)];
        if (token.Length == 0)
        {
            return false;
        }

        return token is "조사처리" or "조사만처리" or "조사선택" or "조사만선택"
            || token.Any(static character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_');
    }

    private static bool LooksLikeErbBraceExpression(string text, int openBraceIndex)
    {
        var quote = false;
        var depth = 0;
        var hasSeparator = false;
        var hasToken = false;

        for (var index = openBraceIndex + 1; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                quote = !quote;
                continue;
            }

            if (quote)
            {
                continue;
            }

            if (character == '{')
            {
                depth++;
                continue;
            }

            if (character == '}')
            {
                if (depth == 0)
                {
                    break;
                }

                depth--;
                continue;
            }

            if (character is '、' or ',')
            {
                hasSeparator = true;
                continue;
            }

            if (IsBraceExpressionTokenCharacter(character))
            {
                hasToken = true;
            }
        }

        return hasSeparator && hasToken;
    }

    private static bool IsFunctionTokenCharacter(char character)
    {
        return char.IsLetterOrDigit(character) || character == '_';
    }

    private static bool IsBraceExpressionTokenCharacter(char character)
    {
        return character is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '_'
            or ':';
    }

    [GeneratedRegex(@"\s*([、。，．：；！？…‥・／＼｜【】〈〉《》「」『』（）［］｛｝＜＞％])\s*", RegexOptions.Compiled)]
    private static partial Regex ProtectedFullWidthCharacterSpacingPattern();

    [GeneratedRegex(@"\S+\s*(?:/|／)\s*\S+", RegexOptions.Compiled)]
    private static partial Regex SlashSeparatedAlternativePattern();

    [GeneratedRegex(@"[\(\（][^)\）\r\n]{2,}[\)\）]", RegexOptions.Compiled)]
    private static partial Regex AddedExplanationParenthesesPattern();

    [GeneratedRegex(@"(?<![A-Za-z])[A-Za-z][A-Za-z'-]{2,}(?![A-Za-z])", RegexOptions.Compiled)]
    private static partial Regex AsciiWordPattern();

    [GeneratedRegex(@",", RegexOptions.Compiled)]
    private static partial Regex CsvAsciiCommaPattern();

    [GeneratedRegex(@"[ \t\u3000]+", RegexOptions.Compiled)]
    private static partial Regex CsvWhitespacePattern();
}
