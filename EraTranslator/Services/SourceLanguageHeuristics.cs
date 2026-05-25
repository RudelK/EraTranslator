namespace EraTranslator.Services;

public static class SourceLanguageHeuristics
{
    public static bool IsLikelySourceText(string text, string sourceLanguage)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var meaningfulChars = text.Where(static ch => !char.IsWhiteSpace(ch) && !char.IsPunctuation(ch) && !char.IsDigit(ch)).ToList();
        if (meaningfulChars.Count == 0)
        {
            return false;
        }

        var normalizedLanguage = (sourceLanguage ?? string.Empty).Trim().ToLowerInvariant();
        return normalizedLanguage switch
        {
            "ja" or "jp" => IsLikelyJapanese(meaningfulChars),
            "ko" => IsLikelyKorean(meaningfulChars),
            "en" => IsLikelyEnglish(meaningfulChars),
            _ => true,
        };
    }

    private static bool IsLikelyJapanese(IReadOnlyList<char> chars)
    {
        var japaneseCount = chars.Count(IsJapaneseChar);
        var hangulCount = chars.Count(IsHangulChar);
        var latinCount = chars.Count(IsLatinChar);
        return japaneseCount > 0 && japaneseCount >= hangulCount && japaneseCount >= latinCount;
    }

    private static bool IsLikelyKorean(IReadOnlyList<char> chars)
    {
        var hangulCount = chars.Count(IsHangulChar);
        return hangulCount > 0 && hangulCount >= chars.Count(IsJapaneseChar);
    }

    private static bool IsLikelyEnglish(IReadOnlyList<char> chars)
    {
        var latinCount = chars.Count(IsLatinChar);
        return latinCount > 0 && latinCount >= chars.Count(IsJapaneseChar) && latinCount >= chars.Count(IsHangulChar);
    }

    private static bool IsJapaneseChar(char ch)
    {
        return ch is >= '\u3040' and <= '\u309F'
            or >= '\u30A0' and <= '\u30FF'
            or >= '\u31F0' and <= '\u31FF'
            or >= '\u4E00' and <= '\u9FFF'
            or '々'
            or 'ヶ'
            or 'ヵ'
            or '〆';
    }

    private static bool IsHangulChar(char ch)
    {
        return ch is >= '\u1100' and <= '\u11FF'
            or >= '\u3130' and <= '\u318F'
            or >= '\uAC00' and <= '\uD7AF';
    }

    private static bool IsLatinChar(char ch)
    {
        return ch is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z';
    }
}
