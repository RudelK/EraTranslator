using System.Text.RegularExpressions;

namespace EraTranslator.Services;

public static partial class ErbSyntaxCatalog
{
    public static readonly string[] ReservedScriptVariables =
    [
        "LOCAL",
        "LOCALS",
        "ARG",
        "ARGS",
        "RESULT",
        "RESULTS",
    ];

    public static readonly string[] ProtectedCodeArgumentFunctionNames =
    [
        "GETCONFIG",
        "VARSIZE",
        "LOADTEXT",
        "SAVETEXT",
        "GCREATEFROMFILE",
        "GCREATEFROMFILE2",
        "GCREATEFROMFILE3",
        "GCREATEFROMFILE4",
        "GLOAD",
        "GLOADBASE64",
        "GDISPOSE",
        "FONTSTYLE",
        "FONTBOLD",
        "FONTITALIC",
        "FONTREGULAR",
        "PLAYSOUND",
        "PLAYBGM",
        "STOPSOUND",
        "OUTPUTLOG",
        "XML_CREATE",
        "XML_OPEN",
        "XML_LOAD",
        "XML_SAVE",
        "XML_GET",
        "XML_SET",
        "XML_REMOVE",
        "MAP_CREATE",
        "MAP_GET",
        "MAP_SET",
        "MAP_REMOVE",
        "DATATABLE_CREATE",
        "DATATABLE_LOAD",
        "DATATABLE_SAVE",
        "DATATABLE_GET",
        "DATATABLE_SET",
        "CALC_CHARA_SINGLE_DATA",
        "CALC_CHARA_SINGLE_DATA_RULED",
        "CALC_CHARA_MULTIPLE_DATA",
        "CALC_CHARA_MULTIPLE_DATA_BASE",
        "CALC_CHARA_RANGED_DATA",
        "GET_NONEXISTABLE_CHARA_NO_DEFAULTABLE_SINGLE_DATA",
        "GET_NONEXISTABLE_VALUES_BYNAME",
        "GET_NONEXISTABLE_TALENT_BYNAME",
        "GET_NONEXISTABLE_ABL_BYNAME",
        "GET_NONEXISTABLE_CFLAG_BYNAME",
        "GET_NONEXISTABLE_EXP_BYNAME",
        "GET_NONEXISTABLE_CSTR_BYNAME",
    ];

    public static readonly string[] ProtectedCodeArgumentCommandNames =
    [
        "LOADTEXT",
        "SAVETEXT",
        "PLAYSOUND",
        "PLAYBGM",
        "OUTPUTLOG",
        "PRINT_IMG",
        "PRINT_RECT",
        "PRINT_SPACE",
        "GCREATEFROMFILE",
        "HTML_PRINT",
        "XML_CREATE",
        "XML_OPEN",
        "XML_LOAD",
        "XML_SAVE",
        "MAP_CREATE",
        "DATATABLE_CREATE",
        "DATATABLE_LOAD",
        "DATATABLE_SAVE",
    ];

    public static readonly string[] PaletteLookupFunctionNames =
    [
        "BARCOLORSET",
        "BARCOLORSET_HTML",
        "カラーパレット",
        "カラーパレット_透明度込",
        "カラーパレット_HTML",
    ];

    public static readonly string[] BuiltInNamespaces =
    [
        "CALLNAME",
        "NAME",
        "NICKNAME",
        "MASTERNAME",
        "CFLAG",
        "TFLAG",
        "FLAG",
        "CSTR",
        "STR",
        "TSTR",
        "SAVESTR",
        "GLOBALS",
        "GLOBAL",
        "ITEM",
        "ITEMPRICE",
        "ITEMSALES",
        "BASE",
        "MAXBASE",
        "DOWNBASE",
        "ABL",
        "CUP",
        "UP",
        "DOWN",
        "PALAM",
        "JUEL",
        "GOTJUEL",
        "CDOWN",
        "EXP",
        "MARK",
        "TALENT",
        "SOURCE",
        "EX",
        "NOWEX",
        "TEQUIP",
        "EQUIP",
        "STAIN",
        "RELATION",
        "TCVAR",
        "CDFLAG",
        "DAY",
        "DAYNAME",
        "TIME",
        "TIMENAME",
        "MONEY",
        "MONEYNAME",
    ];

    public static readonly IReadOnlyDictionary<string, string> BuiltInFileNamespaceByFileName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Cflag.csv"] = "CFLAG",
            ["Tflag.csv"] = "TFLAG",
            ["Flag.csv"] = "FLAG",
            ["Cstr.csv"] = "CSTR",
            ["Str.csv"] = "STR",
            ["Tstr.csv"] = "TSTR",
            ["Savestr.csv"] = "SAVESTR",
            ["Global.csv"] = "GLOBAL",
            ["Globals.csv"] = "GLOBALS",
            ["Item.csv"] = "ITEM",
            ["ItemPrice.csv"] = "ITEMPRICE",
            ["ItemSales.csv"] = "ITEMSALES",
            ["Base.csv"] = "BASE",
            ["Abl.csv"] = "ABL",
            ["Palam.csv"] = "PALAM",
            ["Exp.csv"] = "EXP",
            ["Mark.csv"] = "MARK",
            ["Talent.csv"] = "TALENT",
            ["Source.csv"] = "SOURCE",
            ["Juel.csv"] = "JUEL",
            ["Tequip.csv"] = "TEQUIP",
            ["Equip.csv"] = "EQUIP",
            ["Stain.csv"] = "STAIN",
            ["Relation.csv"] = "RELATION",
            ["Nowex.csv"] = "NOWEX",
            ["Ex.csv"] = "EX",
            ["Tcvar.csv"] = "TCVAR",
            ["Cdflag.csv"] = "CDFLAG",
            ["Day.csv"] = "DAY",
            ["DayName.csv"] = "DAYNAME",
            ["Time.csv"] = "TIME",
            ["TimeName.csv"] = "TIMENAME",
            ["Money.csv"] = "MONEY",
            ["MoneyName.csv"] = "MONEYNAME",
        };

    public static readonly HashSet<string> ResourcePathExtensions =
        new(StringComparer.OrdinalIgnoreCase)
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
            ".ttf",
            ".otf",
            ".woff",
            ".woff2",
            ".txt",
            ".dat",
            ".csv",
            ".xml",
            ".html",
            ".htm",
            ".json",
        };

    public static Regex CreateSymbolReferencePattern(SymbolNamespaceRegistry namespaceRegistry)
    {
        var namespacePattern = string.Join("|", namespaceRegistry.OrderedNamespaces.Select(Regex.Escape));
        var pattern = $@"(?<![\p{{L}}\p{{N}}_])(?:{namespacePattern}):(?:\{{[^{{}}\r\n]+\}}|[A-Za-z_][A-Za-z0-9_]*:[^\s,\)\(\]\[\+\-\*\/<>=!&|%""']+|[^\s,\)\(\]\[\+\-\*\/<>=!&|%""']+)";
        return new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    public static Regex CreateScriptSyntaxTokenPattern(SymbolNamespaceRegistry namespaceRegistry)
    {
        var namespacePattern = string.Join("|", namespaceRegistry.OrderedNamespaces.Select(Regex.Escape));
        var headPattern = string.IsNullOrWhiteSpace(namespacePattern)
            ? string.Join("|", ReservedScriptVariables)
            : $"{namespacePattern}|{string.Join("|", ReservedScriptVariables)}";
        var pattern =
            $@"(%[^%\r\n]+%|\{{[^{{}}\r\n]+\}}|<[^\r\n<>]+>|(?<![\p{{L}}\p{{N}}_])(?:{headPattern}):(?:\{{[^{{}}\r\n]+\}}|[\p{{L}}_][\p{{L}}\p{{N}}_]*:[^\s,\)\(\]\[\+\-\*\/<>=!&|%""']+|[^\s,\)\(\]\[\+\-\*\/<>=!&|%""']+)|(?<![\p{{L}}\p{{N}}_])[\p{{L}}_][\p{{L}}\p{{N}}_]*\s*\([^()\r\n]*\)|(?<![\p{{L}}\p{{N}}_])[\p{{L}}_][\p{{L}}\p{{N}}_]*(?::[^\s,\)\(\]\[\+\-\*\/<>=!&|%""']+)+)";
        return new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    public static bool TryNormalizeSpecialCommentLine(string line, out string normalizedLine)
    {
        normalizedLine = line;
        var trimmedStart = 0;
        while (trimmedStart < line.Length && char.IsWhiteSpace(line[trimmedStart]))
        {
            trimmedStart++;
        }

        if (line.Length - trimmedStart < 3 || line[trimmedStart] != ';')
        {
            return false;
        }

        var marker = line.Substring(trimmedStart, 3);
        if (!marker.Equals(";!;", StringComparison.Ordinal)
            && !marker.Equals(";#;", StringComparison.Ordinal)
            && !marker.Equals(";^;", StringComparison.Ordinal))
        {
            return false;
        }

        normalizedLine = line[..trimmedStart] + new string(' ', marker.Length) + line[(trimmedStart + marker.Length)..];
        return true;
    }

    public static string NormalizeContinuationLine(string line)
    {
        if (TryNormalizeSpecialCommentLine(line, out var specialCommentCodeLine))
        {
            return specialCommentCodeLine;
        }

        return IsRegularCommentLine(line)
            ? new string(' ', line.Length)
            : line;
    }

    public static bool IsRegularCommentLine(string line)
    {
        var trimmedStart = 0;
        while (trimmedStart < line.Length && char.IsWhiteSpace(line[trimmedStart]))
        {
            trimmedStart++;
        }

        return trimmedStart < line.Length
            && line[trimmedStart] == ';'
            && !TryNormalizeSpecialCommentLine(line, out _);
    }

    public static bool IsHtmlEntity(string value)
    {
        return HtmlEntityPattern().IsMatch(value);
    }

    public static bool HasOpenBraceContinuation(string value)
    {
        var quote = false;
        var braceDepth = 0;
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (ch == '"')
            {
                quote = !quote;
                continue;
            }

            if (quote)
            {
                continue;
            }

            if (ch == '{')
            {
                braceDepth++;
            }
            else if (ch == '}' && braceDepth > 0)
            {
                braceDepth--;
            }
        }

        return braceDepth > 0;
    }

    [GeneratedRegex(@"&(?:[A-Za-z][A-Za-z0-9]+|#\d+|#x[0-9A-Fa-f]+);", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    public static partial Regex HtmlEntityPattern();
}
