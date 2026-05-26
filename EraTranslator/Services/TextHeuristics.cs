using System.Text.RegularExpressions;

namespace EraTranslator.Services;

public static partial class TextHeuristics
{
    private static readonly HashSet<string> KnownColorWords =
    [
        "白",
        "黒",
        "赤",
        "青",
        "緑",
        "黄",
        "灰",
        "桃",
        "水色",
        "紫",
        "橙",
        "茶",
        "金",
        "銀",
    ];

    public static bool ContainsTranslatableText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (KnownColorWords.Contains(value.Trim()))
        {
            return false;
        }

        foreach (var rune in value.EnumerateRunes())
        {
            if (IsJapaneseRune(rune.Value))
            {
                return true;
            }
        }

        return false;
    }

    public static bool LooksLikeCodeOnly(string value)
    {
        var sanitized = PlaceholderOnlyPattern().Replace(value, string.Empty);
        sanitized = Regex.Replace(sanitized, @"[\s\[\]\(\)\-+*/\\,.;:!?<>='""@#&|]", string.Empty);
        return string.IsNullOrWhiteSpace(sanitized);
    }

    public static bool LooksLikeErbSymbolExpression(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var sanitized = ErbSymbolReferencePattern().Replace(value, string.Empty);
        if (string.Equals(sanitized, value, StringComparison.Ordinal))
        {
            return false;
        }

        sanitized = PlaceholderOnlyPattern().Replace(sanitized, string.Empty);
        sanitized = Regex.Replace(sanitized, @"[A-Za-z_][A-Za-z0-9_]*", string.Empty, RegexOptions.CultureInvariant);
        sanitized = Regex.Replace(sanitized, @"[\s\[\]\(\)\{\}\-+*/\\,.;:!?<>='""@#&|%×÷0-9０-９]", string.Empty);
        return string.IsNullOrWhiteSpace(sanitized);
    }

    public static bool IsNumericLike(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        return Regex.IsMatch(trimmed, @"^(0x[0-9A-Fa-f]+|[-+]?\d+(?:\.\d+)?(?:/\d+)*(?:%|％)?|TRUE|FALSE|ON|OFF)$", RegexOptions.CultureInvariant);
    }

    public static bool LooksLikeLookupKey(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        if (IsNumericLike(trimmed))
        {
            return true;
        }

        if (trimmed.Contains('／') || trimmed.Contains('/'))
        {
            return true;
        }

        return Regex.IsMatch(trimmed, @"^[\p{L}\p{N}_\-]+$", RegexOptions.CultureInvariant)
            && !ContainsTranslatableText(trimmed);
    }

    private static bool IsJapaneseRune(int value)
    {
        return value is >= 0x3040 and <= 0x30FF
            or >= 0x31F0 and <= 0x31FF
            or >= 0x4E00 and <= 0x9FFF
            or >= 0xFF01 and <= 0xFF60
            or >= 0xFFE0 and <= 0xFFE6;
    }

    [GeneratedRegex(@"(%[^%\r\n]+%|\{[^{}\r\n]+\}|<[^\r\n<>]+>)", RegexOptions.Compiled)]
    private static partial Regex PlaceholderOnlyPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}_])(?:CALLNAME|CFLAG|TFLAG|FLAG|CSTR|STR|ITEM|ITEMPRICE|BASE|ABL|PALAM|EXP|MARK|TALENT|SOURCE|JUEL|TEQUIP|NOWEX|EX|SAVESTR):(?:\{[^{}\r\n]+\}|[A-Za-z_][A-Za-z0-9_]*:[^\s,\)\(\]\[\+\-\*\/<>=!&|%""']+|[^\s,\)\(\]\[\+\-\*\/<>=!&|%""']+)", RegexOptions.Compiled)]
    private static partial Regex ErbSymbolReferencePattern();
}
