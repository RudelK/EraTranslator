using System.Text.RegularExpressions;

namespace EraTranslator.Services;

public sealed partial class ErbDimsLookupRegistry
{
    private readonly Dictionary<string, Dictionary<int, string>> _functionArgumentNamespaces;
    private readonly HashSet<string> _lookupArrays;

    private ErbDimsLookupRegistry(
        HashSet<string> lookupArrays,
        Dictionary<string, Dictionary<int, string>> functionArgumentNamespaces)
    {
        _lookupArrays = lookupArrays;
        _functionArgumentNamespaces = functionArgumentNamespaces;
    }

    public static ErbDimsLookupRegistry Empty { get; } = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase));

    public static ErbDimsLookupRegistry BuildFromDocuments(IEnumerable<string> contents)
    {
        var contentList = contents.ToList();
        var declaredArrays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var functions = new List<FunctionInfo>();
        foreach (var content in contentList)
        {
            foreach (var arrayName in ParseDimArrayNames(content))
            {
                declaredArrays.Add(arrayName);
            }

            functions.AddRange(ParseFunctions(content));
        }

        var lookupArrays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var argumentNamespaces = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var function in functions)
        {
            if (!argumentNamespaces.ContainsKey(function.Name))
            {
                argumentNamespaces[function.Name] = new Dictionary<int, string>();
            }
        }

        foreach (var function in functions)
        {
            foreach (var call in function.Calls.Where(call =>
                         string.Equals(call.Name, "FINDELEMENT", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(call.Name, "FINDLASTELEMENT", StringComparison.OrdinalIgnoreCase)))
            {
                if (call.Arguments.Count < 2
                    || !TryReadIdentifier(call.Arguments[0].Text, out var arrayName)
                    || !TryReadIdentifier(call.Arguments[1].Text, out var parameterName))
                {
                    continue;
                }

                var parameterIndex = function.Parameters.FindIndex(parameter =>
                    string.Equals(parameter, parameterName, StringComparison.OrdinalIgnoreCase));
                if (parameterIndex < 0)
                {
                    continue;
                }

                lookupArrays.Add(arrayName);
                argumentNamespaces[function.Name][parameterIndex] = ToNamespace(arrayName);
            }
        }

        foreach (var content in contentList)
        {
            AddDirectLookupArrayUsages(content, declaredArrays, lookupArrays);
        }

        for (var iteration = 0; iteration < 8; iteration++)
        {
            var changed = false;
            foreach (var function in functions)
            {
                foreach (var call in function.Calls)
                {
                    if (!argumentNamespaces.TryGetValue(call.Name, out var calleeMap))
                    {
                        continue;
                    }

                    foreach (var pair in calleeMap)
                    {
                        var calleeArgumentIndex = pair.Key;
                        if (calleeArgumentIndex < 0 || calleeArgumentIndex >= call.Arguments.Count)
                        {
                            continue;
                        }

                        if (!TryReadIdentifier(call.Arguments[calleeArgumentIndex].Text, out var forwardedParameter))
                        {
                            continue;
                        }

                        var callerParameterIndex = function.Parameters.FindIndex(parameter =>
                            string.Equals(parameter, forwardedParameter, StringComparison.OrdinalIgnoreCase));
                        if (callerParameterIndex < 0)
                        {
                            continue;
                        }

                        var currentMap = argumentNamespaces[function.Name];
                        if (currentMap.TryGetValue(callerParameterIndex, out var existing)
                            && string.Equals(existing, pair.Value, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        currentMap[callerParameterIndex] = pair.Value;
                        changed = true;
                    }
                }
            }

            if (!changed)
            {
                break;
            }
        }

        return new ErbDimsLookupRegistry(lookupArrays, argumentNamespaces);
    }

    private static IEnumerable<string> ParseDimArrayNames(string content)
    {
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith(';'))
            {
                continue;
            }

            var match = DimArrayNamePattern().Match(line);
            if (match.Success)
            {
                yield return match.Groups["name"].Value;
            }
        }
    }

    private static void AddDirectLookupArrayUsages(
        string content,
        HashSet<string> declaredArrays,
        HashSet<string> lookupArrays)
    {
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith(';'))
            {
                continue;
            }

            var selectCaseMatch = SelectCaseDimsArrayPattern().Match(line);
            if (selectCaseMatch.Success
                && declaredArrays.Contains(selectCaseMatch.Groups["array"].Value))
            {
                lookupArrays.Add(selectCaseMatch.Groups["array"].Value);
            }

            foreach (Match comparisonMatch in DimsArrayComparisonPattern().Matches(line))
            {
                var arrayName = comparisonMatch.Groups["array"].Value;
                if (declaredArrays.Contains(arrayName))
                {
                    lookupArrays.Add(arrayName);
                }
            }

            foreach (var call in EnumerateFunctionCalls(line))
            {
                if (!IsElementLookupFunction(call.Name)
                    || call.Arguments.Count < 2
                    || !TryReadIdentifier(call.Arguments[0].Text, out var arrayName)
                    || !declaredArrays.Contains(arrayName))
                {
                    continue;
                }

                lookupArrays.Add(arrayName);
            }
        }
    }

    public bool IsLookupArray(string arrayName)
    {
        return _lookupArrays.Contains(arrayName);
    }

    public bool TryGetLookupNamespace(string functionName, int argumentIndex, out string symbolNamespace)
    {
        symbolNamespace = string.Empty;
        if (!_functionArgumentNamespaces.TryGetValue(functionName, out var arguments)
            || !arguments.TryGetValue(argumentIndex, out var resolvedNamespace))
        {
            return false;
        }

        symbolNamespace = resolvedNamespace;
        return true;
    }

    public bool IsLookupFunctionArgument(string line, int start, int length)
    {
        foreach (var call in EnumerateFunctionCalls(line))
        {
            if (IsDirectLookupFunctionArgument(call, start, length))
            {
                return true;
            }

            for (var index = 0; index < call.Arguments.Count; index++)
            {
                var argument = call.Arguments[index];
                if (!TryGetLookupNamespace(call.Name, index, out _))
                {
                    continue;
                }

                if (start >= argument.AbsoluteStart && start + length <= argument.AbsoluteStart + argument.Text.Length)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsDirectLookupFunctionArgument(FunctionCallInfo call, int start, int length)
    {
        if (!IsElementLookupFunction(call.Name)
            || call.Arguments.Count < 2
            || !TryReadIdentifier(call.Arguments[0].Text, out var arrayName)
            || !IsLookupArray(arrayName))
        {
            return false;
        }

        var argument = call.Arguments[1];
        return start >= argument.AbsoluteStart && start + length <= argument.AbsoluteStart + argument.Text.Length;
    }

    public static string ToNamespace(string arrayName)
    {
        return $"DIMS:{arrayName}";
    }

    private static List<FunctionInfo> ParseFunctions(string content)
    {
        var functions = new List<FunctionInfo>();
        FunctionInfo? current = null;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith(';'))
            {
                continue;
            }

            var definition = FunctionDefinitionPattern().Match(trimmed);
            if (definition.Success)
            {
                current = new FunctionInfo(
                    definition.Groups["name"].Value,
                    ParseFunctionParameters(definition.Groups["tail"].Value),
                    []);
                functions.Add(current);
                continue;
            }

            current?.Calls.AddRange(EnumerateFunctionCalls(line));
        }

        return functions;
    }

    private static List<string> ParseFunctionParameters(string tail)
    {
        var parameters = new List<string>();
        var text = tail.Trim();
        if (text.StartsWith('(') && text.EndsWith(')'))
        {
            text = text[1..^1];
        }
        else if (text.StartsWith(','))
        {
            text = text[1..];
        }
        else
        {
            return parameters;
        }

        foreach (var argument in SplitFunctionArguments(text, 0))
        {
            var parameter = argument.Text;
            var equalsIndex = parameter.IndexOf('=');
            if (equalsIndex >= 0)
            {
                parameter = parameter[..equalsIndex];
            }

            if (TryReadIdentifier(parameter.Trim(), out var name))
            {
                parameters.Add(name);
            }
        }

        return parameters;
    }

    private static IEnumerable<FunctionCallInfo> EnumerateFunctionCalls(string line)
    {
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] == '"')
            {
                var quoteEnd = index + 1;
                while (quoteEnd < line.Length)
                {
                    if (line[quoteEnd] == '"' && !IsEscapedQuote(line, quoteEnd))
                    {
                        break;
                    }

                    quoteEnd++;
                }

                index = quoteEnd;
                continue;
            }

            if (!IsIdentifierStart(line[index]))
            {
                continue;
            }

            var nameStart = index;
            var nameEnd = index + 1;
            while (nameEnd < line.Length && IsIdentifierCharacter(line[nameEnd]))
            {
                nameEnd++;
            }

            var cursor = nameEnd;
            SkipWhitespace(line, ref cursor);
            if (cursor >= line.Length || line[cursor] != '(')
            {
                index = nameEnd - 1;
                continue;
            }

            var close = FindMatchingParen(line, cursor);
            if (close < 0)
            {
                index = nameEnd - 1;
                continue;
            }

            var argumentText = line[(cursor + 1)..close];
            yield return new FunctionCallInfo(
                line[nameStart..nameEnd],
                SplitFunctionArguments(argumentText, cursor + 1).ToList());

            // Continue through the argument list to discover nested wrapper calls.
            index = nameEnd - 1;
        }
    }

    private static int FindMatchingParen(string line, int openIndex)
    {
        var depth = 0;
        var quote = false;
        for (var index = openIndex; index < line.Length; index++)
        {
            var ch = line[index];
            if (ch == '"' && !IsEscapedQuote(line, index))
            {
                quote = !quote;
                continue;
            }

            if (quote)
            {
                continue;
            }

            if (ch == '(')
            {
                depth++;
                continue;
            }

            if (ch == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static IEnumerable<FunctionArgumentInfo> SplitFunctionArguments(string expression, int absoluteStart)
    {
        var quote = false;
        var depth = 0;
        var start = 0;
        for (var index = 0; index < expression.Length; index++)
        {
            var ch = expression[index];
            if (ch == '"' && !IsEscapedQuote(expression, index))
            {
                quote = !quote;
                continue;
            }

            if (quote)
            {
                continue;
            }

            if (ch == '(')
            {
                depth++;
                continue;
            }

            if (ch == ')' && depth > 0)
            {
                depth--;
                continue;
            }

            if (ch == ',' && depth == 0)
            {
                yield return BuildArgument(expression, start, index, absoluteStart);
                start = index + 1;
            }
        }

        yield return BuildArgument(expression, start, expression.Length, absoluteStart);
    }

    private static FunctionArgumentInfo BuildArgument(string expression, int start, int end, int absoluteStart)
    {
        while (start < end && char.IsWhiteSpace(expression[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(expression[end - 1]))
        {
            end--;
        }

        return new FunctionArgumentInfo(expression[start..end], absoluteStart + start);
    }

    private static bool TryReadIdentifier(string text, out string identifier)
    {
        identifier = string.Empty;
        var trimmed = text.Trim();
        if (!IdentifierPattern().IsMatch(trimmed))
        {
            return false;
        }

        identifier = trimmed;
        return true;
    }

    private static bool IsEscapedQuote(string text, int index)
    {
        return index + 1 < text.Length && text[index + 1] == '"'
            || index > 0 && text[index - 1] == '"';
    }

    private static void SkipWhitespace(string text, ref int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }
    }

    private static bool IsIdentifierStart(char character)
    {
        return character == '_' || char.IsLetter(character);
    }

    private static bool IsIdentifierCharacter(char character)
    {
        return character == '_' || char.IsLetterOrDigit(character);
    }

    private static bool IsElementLookupFunction(string functionName)
    {
        return functionName.Equals("FINDELEMENT", StringComparison.OrdinalIgnoreCase)
            || functionName.Equals("FINDLASTELEMENT", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record FunctionInfo(string Name, List<string> Parameters, List<FunctionCallInfo> Calls);

    private readonly record struct FunctionCallInfo(string Name, List<FunctionArgumentInfo> Arguments);

    private readonly record struct FunctionArgumentInfo(string Text, int AbsoluteStart);

    [GeneratedRegex(@"^@\s*(?<name>[\p{L}_][\p{L}\p{N}_]*)(?<tail>.*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex FunctionDefinitionPattern();

    [GeneratedRegex(@"^[\p{L}_][\p{L}\p{N}_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(@"^\s*#DIMS?\s+(?:(?:CONST|SAVEDATA|DYNAMIC|GLOBAL|REF|CHARADATA)\s+|,\s*)*(?<name>[\p{L}_][\p{L}\p{N}_]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DimArrayNamePattern();

    [GeneratedRegex("""^\s*SELECTCASE\s+(?<array>[\p{L}_][\p{L}\p{N}_]*)\s*:""", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SelectCaseDimsArrayPattern();

    [GeneratedRegex(@"(?<array>[\p{L}_][\p{L}\p{N}_]*)\s*:[^""'\r\n=<>!]+\s*(?:==|!=|<>)\s*@?""(?<value>(?:[^""]|"""")*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DimsArrayComparisonPattern();
}
