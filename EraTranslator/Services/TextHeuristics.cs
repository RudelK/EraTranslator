using System.Globalization;
using System.Text.RegularExpressions;

namespace EraTranslator.Services;

public static partial class TextHeuristics
{
    private static readonly HashSet<string> ResourcePathExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".gif",
        ".webp",
        ".bmp",
        ".apng",
        ".ogg",
        ".wav",
        ".mp3",
        ".mid",
        ".midi",
        ".txt",
        ".dat",
        ".xml",
    };

    public static bool ContainsTranslatableText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
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
        sanitized = RemoveNonLexicalCharacters(sanitized);
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
        sanitized = RemoveNonLexicalCharacters(sanitized);
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

    public static bool LooksLikeResourcePathLiteral(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith("@\"", StringComparison.Ordinal) && normalized.EndsWith('"'))
        {
            normalized = normalized[2..^1];
        }
        else if (normalized.Length >= 2
                 && normalized[0] == '"'
                 && normalized[^1] == '"')
        {
            normalized = normalized[1..^1];
        }

        normalized = PlaceholderOnlyPattern().Replace(normalized, "SEG");
        if (!normalized.Contains('/') && !normalized.Contains('\\'))
        {
            return false;
        }

        var path = normalized.Replace('\\', '/');
        var lastSlashIndex = path.LastIndexOf('/');
        if (lastSlashIndex < 0 || lastSlashIndex == path.Length - 1)
        {
            return false;
        }

        var fileName = path[(lastSlashIndex + 1)..].Trim();
        if (fileName.Length == 0)
        {
            return false;
        }

        var extension = Path.GetExtension(fileName);
        return extension.Length > 0 && ResourcePathExtensions.Contains(extension);
    }

    private static bool IsJapaneseRune(int value)
    {
        return value is >= 0x3040 and <= 0x30FF
            or >= 0x31F0 and <= 0x31FF
            or >= 0x4E00 and <= 0x9FFF
            or >= 0xFF01 and <= 0xFF60
            or >= 0xFFE0 and <= 0xFFE6;
    }

    private static string RemoveNonLexicalCharacters(string value)
    {
        return new string(value.Where(static ch => !IsNonLexicalCharacter(ch)).ToArray());
    }

    private static bool IsNonLexicalCharacter(char ch)
    {
        if (char.IsWhiteSpace(ch) || char.IsDigit(ch) || char.IsPunctuation(ch))
        {
            return true;
        }

        var category = char.GetUnicodeCategory(ch);
        return category is UnicodeCategory.MathSymbol
            or UnicodeCategory.CurrencySymbol
            or UnicodeCategory.ModifierSymbol
            or UnicodeCategory.OtherSymbol;
    }

    [GeneratedRegex(@"(%[^%\r\n]+%|\{[^{}\r\n]+\}|<[^\r\n<>]+>)", RegexOptions.Compiled)]
    private static partial Regex PlaceholderOnlyPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}_])(?:CALLNAME|CFLAG|TFLAG|FLAG|CSTR|STR|ITEM|ITEMPRICE|ITEMSALES|BASE|MAXBASE|DOWNBASE|ABL|CUP|PALAM|CDOWN|EXP|MARK|TALENT|SOURCE|JUEL|TEQUIP|NOWEX|EX|TCVAR|SAVESTR):(?:\{[^{}\r\n]+\}|[A-Za-z_][A-Za-z0-9_]*:[^\s,\)\(\]\[\+\-\*\/<>=!&|%""']+|[^\s,\)\(\]\[\+\-\*\/<>=!&|%""']+)", RegexOptions.Compiled)]
    private static partial Regex ErbSymbolReferencePattern();
}
