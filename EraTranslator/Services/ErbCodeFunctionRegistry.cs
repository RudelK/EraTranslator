using System.Text.RegularExpressions;

namespace EraTranslator.Services;

public sealed partial class ErbCodeFunctionRegistry
{
    private readonly HashSet<string> _functionNames;

    public static ErbCodeFunctionRegistry Empty { get; } = new([]);

    private ErbCodeFunctionRegistry(IEnumerable<string> functionNames)
    {
        _functionNames = new HashSet<string>(
            functionNames.Where(static name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.OrdinalIgnoreCase);
    }

    public static ErbCodeFunctionRegistry FromNames(IEnumerable<string> functionNames)
    {
        return new ErbCodeFunctionRegistry(functionNames);
    }

    public bool Contains(string functionName)
    {
        return _functionNames.Contains(functionName);
    }

    public static ErbCodeFunctionRegistry BuildFromDocuments(IEnumerable<string> contents)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var content in contents)
        {
            CollectFunctionNames(content, names);
        }

        return FromNames(names);
    }

    private static void CollectFunctionNames(string content, ISet<string> names)
    {
        var lines = content.Split('\n');
        foreach (var line in lines)
        {
            var sourceLine = StripInlineComment(line.TrimEnd('\r'));
            if (string.IsNullOrWhiteSpace(sourceLine))
            {
                continue;
            }

            var protectedRanges = CollectQuotedRanges(sourceLine);
            var definition = FunctionDefinitionPattern().Match(sourceLine);
            if (definition.Success)
            {
                names.Add(definition.Groups["name"].Value);
            }

            if (PrintTextCommandPattern().IsMatch(sourceLine))
            {
                continue;
            }

            foreach (Match match in FunctionCallPattern().Matches(sourceLine))
            {
                var start = match.Groups["name"].Index;
                var end = start + match.Groups["name"].Length;
                if (protectedRanges.Any(range => start < range.end && range.start < end))
                {
                    continue;
                }

                names.Add(match.Groups["name"].Value);
            }
        }
    }

    private static List<(int start, int end)> CollectQuotedRanges(string line)
    {
        var ranges = new List<(int start, int end)>();
        var index = 0;
        while (index < line.Length)
        {
            if (line[index] != '"')
            {
                index++;
                continue;
            }

            var start = index++;
            while (index < line.Length)
            {
                if (line[index] == '"' && index + 1 < line.Length && line[index + 1] == '"')
                {
                    index += 2;
                    continue;
                }

                if (line[index] == '"')
                {
                    index++;
                    break;
                }

                index++;
            }

            ranges.Add((start, Math.Min(index, line.Length)));
        }

        return ranges;
    }

    private static string StripInlineComment(string value)
    {
        var quote = false;
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (ch == '"')
            {
                quote = !quote;
                continue;
            }

            if (!quote && ch == ';')
            {
                return value[..index];
            }
        }

        return value;
    }

    [GeneratedRegex(@"^\s*@(?<name>[\p{L}_][\p{L}\p{N}_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex FunctionDefinitionPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}_])(?<name>[\p{L}_][\p{L}\p{N}_]*)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex FunctionCallPattern();

    [GeneratedRegex(@"^\s*PRINT[A-Z_]*\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PrintTextCommandPattern();
}
