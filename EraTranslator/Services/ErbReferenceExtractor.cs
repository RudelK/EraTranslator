using System.Text.RegularExpressions;
using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed partial class ErbReferenceExtractor
{
    private readonly SymbolNamespaceRegistry _namespaceRegistry;
    private readonly ErbDimsLookupRegistry _dimsLookupRegistry;

    public ErbReferenceExtractor()
        : this(SymbolNamespaceRegistry.Default)
    {
    }

    public ErbReferenceExtractor(SymbolNamespaceRegistry namespaceRegistry)
        : this(namespaceRegistry, ErbDimsLookupRegistry.Empty)
    {
    }

    public ErbReferenceExtractor(SymbolNamespaceRegistry namespaceRegistry, ErbDimsLookupRegistry dimsLookupRegistry)
    {
        _namespaceRegistry = namespaceRegistry;
        _dimsLookupRegistry = dimsLookupRegistry;
    }

    public (List<ErbSymbolReference> references, List<ErbVariableLiteralOccurrence> variableLiterals) Extract(string documentId, string content)
    {
        var lines = content.Split('\n');
        var assignments = new List<AssignmentInfo>();
        var absoluteOffset = 0;

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            var normalizedLine = line.TrimEnd('\r');
            var trimmed = normalizedLine.TrimStart();
            if (trimmed.StartsWith(';') || trimmed.StartsWith('#'))
            {
                absoluteOffset += line.Length + 1;
                continue;
            }

            var assignmentMatch = AssignmentPattern().Match(normalizedLine);
            if (assignmentMatch.Success)
            {
                assignments.Add(new AssignmentInfo(
                    assignmentMatch.Groups["var"].Value,
                    assignmentMatch.Groups["expr"].Value,
                    absoluteOffset + assignmentMatch.Groups["expr"].Index,
                    lineIndex + 1));
            }

            absoluteOffset += line.Length + 1;
        }

        var resolvedVariables = ResolveAssignments(assignments);
        var variableLiterals = CollectVariableLiterals(documentId, assignments, resolvedVariables);
        var references = ExtractReferences(documentId, lines, resolvedVariables);
        return (references, variableLiterals);
    }

    private static Dictionary<string, HashSet<string>> ResolveAssignments(IEnumerable<AssignmentInfo> assignments)
    {
        var resolved = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var assignmentList = assignments.ToList();

        for (var iteration = 0; iteration < 8; iteration++)
        {
            var changed = false;
            foreach (var assignment in assignmentList)
            {
                var values = ResolveExpressionValues(assignment.ExpressionText, assignment.ExpressionAbsoluteStart, resolved).Values;
                if (values.Count == 0)
                {
                    continue;
                }

                if (!resolved.TryGetValue(assignment.VariableName, out var existing))
                {
                    existing = new HashSet<string>(StringComparer.Ordinal);
                    resolved[assignment.VariableName] = existing;
                }

                foreach (var value in values)
                {
                    changed |= existing.Add(value);
                }
            }

            if (!changed)
            {
                break;
            }
        }

        return resolved;
    }

    private static List<ErbVariableLiteralOccurrence> CollectVariableLiterals(
        string documentId,
        IEnumerable<AssignmentInfo> assignments,
        IReadOnlyDictionary<string, HashSet<string>> resolvedVariables)
    {
        var occurrences = new List<ErbVariableLiteralOccurrence>();

        foreach (var assignment in assignments)
        {
            var result = ResolveExpressionValues(assignment.ExpressionText, assignment.ExpressionAbsoluteStart, resolvedVariables);
            foreach (var occurrence in result.ExactLiteralOccurrences)
            {
                occurrences.Add(new ErbVariableLiteralOccurrence
                {
                    DocumentId = documentId,
                    VariableName = assignment.VariableName,
                    LiteralValue = occurrence.LiteralValue,
                    AbsoluteStart = occurrence.AbsoluteStart,
                    Length = occurrence.Length,
                    LineNumber = assignment.LineNumber,
                    IsExactValue = true,
                });
            }
        }

        return occurrences;
    }

    private List<ErbSymbolReference> ExtractReferences(
        string documentId,
        IReadOnlyList<string> lines,
        IReadOnlyDictionary<string, HashSet<string>> resolvedVariables)
    {
        var results = new List<ErbSymbolReference>();
        var absoluteOffset = 0;
        var selectCaseLookupNamespaces = new Stack<string>();
        var selectCaseCsvNameNamespaces = new Stack<string>();

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var normalizedLine = line.TrimEnd('\r');
            var trimmed = normalizedLine.TrimStart();
            if (trimmed.StartsWith(';') || trimmed.StartsWith('#'))
            {
                absoluteOffset += line.Length + 1;
                continue;
            }

            if (TryReadSelectCaseLookupNamespace(normalizedLine, out var selectCaseNamespace))
            {
                selectCaseLookupNamespaces.Push(selectCaseNamespace);
            }
            else if (IsSelectCaseLine(normalizedLine))
            {
                selectCaseLookupNamespaces.Push(string.Empty);
            }

            if (TryReadSelectCaseCsvNameNamespace(normalizedLine, out var selectCaseCsvNameNamespace))
            {
                selectCaseCsvNameNamespaces.Push(selectCaseCsvNameNamespace);
            }
            else if (IsSelectCaseLine(normalizedLine))
            {
                selectCaseCsvNameNamespaces.Push(string.Empty);
            }

            if (IsCaseLabelLine(normalizedLine)
                && selectCaseLookupNamespaces.Count > 0
                && !string.IsNullOrWhiteSpace(selectCaseLookupNamespaces.Peek()))
            {
                AddDimsCaseLabelReferences(
                    documentId,
                    selectCaseLookupNamespaces.Peek(),
                    normalizedLine,
                    absoluteOffset,
                    lineIndex + 1,
                    results);
            }

            if (IsCaseLabelLine(normalizedLine)
                && selectCaseCsvNameNamespaces.Count > 0
                && !string.IsNullOrWhiteSpace(selectCaseCsvNameNamespaces.Peek()))
            {
                AddCsvNameCaseLabelReferences(
                    documentId,
                    selectCaseCsvNameNamespaces.Peek(),
                    normalizedLine,
                    absoluteOffset,
                    lineIndex + 1,
                    results);
            }

            AddGetNumReferences(documentId, normalizedLine, absoluteOffset, lineIndex + 1, resolvedVariables, results);
            AddKeyListFunctionReferences(documentId, normalizedLine, absoluteOffset, lineIndex + 1, results);
            AddDimsLookupFunctionReferences(documentId, normalizedLine, absoluteOffset, lineIndex + 1, results);
            AddDimsArrayComparisonReferences(documentId, normalizedLine, absoluteOffset, lineIndex + 1, results);

            for (var index = 0; index < normalizedLine.Length; index++)
            {
                foreach (var symbolNamespace in _namespaceRegistry.OrderedNamespaces)
                {
                    if (!MatchesNamespace(normalizedLine, index, symbolNamespace))
                    {
                        continue;
                    }

                    var cursor = index + symbolNamespace.Length + 1;
                    SkipWhitespace(normalizedLine, ref cursor);
                    if (cursor >= normalizedLine.Length)
                    {
                        continue;
                    }

                    var components = ReadReferenceComponents(normalizedLine, cursor);
                    if (components.Count == 0)
                    {
                        continue;
                    }

                    AddComponentReferences(
                        documentId,
                        symbolNamespace,
                        normalizedLine,
                        components,
                        resolvedVariables,
                        absoluteOffset,
                        lineIndex + 1,
                        results);

                    // Keep scanning inside expression components like ABL:ARG:(TCVAR:ARG:部位),
                    // otherwise nested namespace references are skipped entirely.
                    if (components.All(component => !component.IsExpression))
                    {
                        index = components[^1].End - 1;
                    }
                }
            }

            if (IsEndSelectLine(normalizedLine) && selectCaseLookupNamespaces.Count > 0)
            {
                selectCaseLookupNamespaces.Pop();
            }

            if (IsEndSelectLine(normalizedLine) && selectCaseCsvNameNamespaces.Count > 0)
            {
                selectCaseCsvNameNamespaces.Pop();
            }

            absoluteOffset += line.Length + 1;
        }

        return results;
    }

    private void AddKeyListFunctionReferences(
        string documentId,
        string line,
        int absoluteOffset,
        int lineNumber,
        List<ErbSymbolReference> results)
    {
        foreach (var call in EnumerateFunctionCalls(line))
        {
            if (TryGetNamespaceFromFirstArgumentFunction(call, out var namespaceName, out var keyListArgumentIndex))
            {
                AddKeyListArgumentReferences(documentId, namespaceName, call, keyListArgumentIndex, absoluteOffset, lineNumber, results);
                continue;
            }

            if (TryGetNamespaceFromSecondArgumentFunction(call, out namespaceName, out keyListArgumentIndex))
            {
                AddKeyListArgumentReferences(documentId, namespaceName, call, keyListArgumentIndex, absoluteOffset, lineNumber, results);
                continue;
            }

            if (TryGetNamespaceFromFunctionName(call.Name, out namespaceName))
            {
                AddKeyListArgumentReferences(documentId, namespaceName, call, 0, absoluteOffset, lineNumber, results);
            }
        }
    }

    private void AddDimsLookupFunctionReferences(
        string documentId,
        string line,
        int absoluteOffset,
        int lineNumber,
        List<ErbSymbolReference> results)
    {
        foreach (var call in EnumerateFunctionCalls(line))
        {
            if (TryGetDirectDimsLookupNamespace(call, out var directLookupNamespace)
                && TryReadQuotedArgument(call.Arguments[1], out var directLiteral, out var directLiteralStart)
                && ShouldTreatAsSymbolKey(directLiteral))
            {
                AddDirectReference(
                    documentId,
                    directLookupNamespace,
                    directLiteral,
                    absoluteOffset + directLiteralStart,
                    directLiteral.Length,
                    lineNumber,
                    results);
            }

            for (var argumentIndex = 0; argumentIndex < call.Arguments.Count; argumentIndex++)
            {
                if (!_dimsLookupRegistry.TryGetLookupNamespace(call.Name, argumentIndex, out var symbolNamespace))
                {
                    continue;
                }

                var argument = call.Arguments[argumentIndex];
                if (!TryReadQuotedArgument(argument, out var literal, out var literalStart)
                    || !ShouldTreatAsSymbolKey(literal))
                {
                    continue;
                }

                AddDirectReference(
                    documentId,
                    symbolNamespace,
                    literal,
                    absoluteOffset + literalStart,
                    literal.Length,
                    lineNumber,
                    results);
            }
        }
    }

    private bool TryGetDirectDimsLookupNamespace(FunctionCallInfo call, out string symbolNamespace)
    {
        symbolNamespace = string.Empty;
        if (!IsElementLookupFunction(call.Name)
            || call.Arguments.Count < 2
            || !TryReadIdentifier(call.Arguments[0].Text, out var arrayName)
            || !_dimsLookupRegistry.IsLookupArray(arrayName))
        {
            return false;
        }

        symbolNamespace = ErbDimsLookupRegistry.ToNamespace(arrayName);
        return true;
    }

    private void AddDimsCaseLabelReferences(
        string documentId,
        string symbolNamespace,
        string line,
        int absoluteOffset,
        int lineNumber,
        List<ErbSymbolReference> results)
    {
        foreach (Match match in QuotedLiteralInLinePattern().Matches(line))
        {
            var literal = match.Groups["value"].Value;
            if (!ShouldTreatAsSymbolKey(literal))
            {
                continue;
            }

            AddDirectReference(
                documentId,
                symbolNamespace,
                literal,
                absoluteOffset + match.Groups["value"].Index,
                literal.Length,
                lineNumber,
                results);
        }
    }

    private static void AddCsvNameCaseLabelReferences(
        string documentId,
        string symbolNamespace,
        string line,
        int absoluteOffset,
        int lineNumber,
        List<ErbSymbolReference> results)
    {
        foreach (Match match in QuotedLiteralInLinePattern().Matches(line))
        {
            var literal = match.Groups["value"].Value;
            if (!ShouldTreatAsSymbolKey(literal))
            {
                continue;
            }

            AddDirectReference(
                documentId,
                symbolNamespace,
                literal,
                absoluteOffset + match.Groups["value"].Index,
                literal.Length,
                lineNumber,
                results);
        }
    }

    private void AddDimsArrayComparisonReferences(
        string documentId,
        string line,
        int absoluteOffset,
        int lineNumber,
        List<ErbSymbolReference> results)
    {
        foreach (Match match in DimsArrayComparisonPattern().Matches(line))
        {
            var arrayName = match.Groups["array"].Value;
            if (!_dimsLookupRegistry.IsLookupArray(arrayName))
            {
                continue;
            }

            var literal = match.Groups["value"].Value;
            if (!ShouldTreatAsSymbolKey(literal))
            {
                continue;
            }

            AddDirectReference(
                documentId,
                ErbDimsLookupRegistry.ToNamespace(arrayName),
                literal,
                absoluteOffset + match.Groups["value"].Index,
                literal.Length,
                lineNumber,
                results);
        }
    }

    private void AddKeyListArgumentReferences(
        string documentId,
        string namespaceName,
        FunctionCallInfo call,
        int argumentIndex,
        int absoluteOffset,
        int lineNumber,
        List<ErbSymbolReference> results)
    {
        if (!_namespaceRegistry.TryResolveNamespace(namespaceName, out var resolvedNamespace)
            || argumentIndex < 0
            || argumentIndex >= call.Arguments.Count)
        {
            return;
        }

        var argument = call.Arguments[argumentIndex];
        if (!TryReadQuotedArgument(argument, out var literal, out var literalStart))
        {
            return;
        }

        foreach (var key in EnumerateWeightedKeyList(literal, literalStart, call.Name))
        {
            if (!ShouldTreatAsSymbolKey(key.Value))
            {
                continue;
            }

            AddDirectReference(
                documentId,
                resolvedNamespace,
                key.Value,
                absoluteOffset + key.AbsoluteStart,
                key.Value.Length,
                lineNumber,
                results);
        }
    }

    private static List<ComponentInfo> ReadReferenceComponents(string line, int cursor)
    {
        var components = new List<ComponentInfo>();

        while (cursor < line.Length)
        {
            SkipWhitespace(line, ref cursor);
            var component = ReadComponent(line, cursor);
            if (component.Length == 0)
            {
                break;
            }

            components.Add(component);
            cursor = component.End;
            SkipWhitespace(line, ref cursor);
            if (cursor >= line.Length || line[cursor] != ':')
            {
                break;
            }

            cursor++;
        }

        return components;
    }

    private static void AddComponentReferences(
        string documentId,
        string symbolNamespace,
        string line,
        IReadOnlyList<ComponentInfo> components,
        IReadOnlyDictionary<string, HashSet<string>> resolvedVariables,
        int absoluteOffset,
        int lineNumber,
        List<ErbSymbolReference> results)
    {
        var addedDirectRanges = new HashSet<(int Start, int Length, string Key)>();

        for (var startIndex = 0; startIndex < components.Count; startIndex++)
        {
            var first = components[startIndex];
            if (startIndex == 0 && components.Count > 1 && LooksLikeIndexComponent(first))
            {
                continue;
            }

            var last = components[^1];
            var rangeStart = first.Start;
            var rangeEnd = last.Start + last.Length;
            if (rangeEnd <= rangeStart)
            {
                continue;
            }

            var candidateComponents = components.Skip(startIndex).ToList();
            if (candidateComponents.Count > 1 && candidateComponents[^1].IsExpression)
            {
                continue;
            }

            var candidateKey = line[rangeStart..rangeEnd].Trim();
            if (candidateComponents.Any(component => component.IsExpression)
                && !LooksLikeLiteralSymbolWithDecorativePunctuation(candidateKey))
            {
                continue;
            }

            if (!ShouldTreatAsSymbolKey(candidateKey))
            {
                continue;
            }

            var valueStart = line.IndexOf(candidateKey, rangeStart, StringComparison.Ordinal);
            if (valueStart < 0)
            {
                valueStart = rangeStart;
            }

            if (addedDirectRanges.Add((valueStart, candidateKey.Length, candidateKey)))
            {
                AddDirectReference(
                    documentId,
                    symbolNamespace,
                    candidateKey,
                    absoluteOffset + valueStart,
                    candidateKey.Length,
                    lineNumber,
                    results);
            }
        }

        var target = components[^1];
        if (target.IsExpression && ShouldTreatAsIndirectKeyExpression(target))
        {
            AddIndirectReference(
                documentId,
                symbolNamespace,
                target,
                resolvedVariables,
                absoluteOffset,
                lineNumber,
                results);
        }
    }

    private static void AddDirectReference(
        string documentId,
        string symbolNamespace,
        string originalKey,
        int absoluteStart,
        int length,
        int lineNumber,
        List<ErbSymbolReference> results)
    {
        results.Add(new ErbSymbolReference
        {
            DocumentId = documentId,
            Namespace = symbolNamespace,
            Kind = ErbSymbolReferenceKind.DirectLiteral,
            ResolutionKind = SymbolReferenceResolutionKind.Direct,
            OriginalKey = originalKey,
            AbsoluteStart = absoluteStart,
            Length = length,
            LineNumber = lineNumber,
            CandidateKeys = [originalKey],
        });
    }

    private static void AddIndirectReference(
        string documentId,
        string symbolNamespace,
        ComponentInfo target,
        IReadOnlyDictionary<string, HashSet<string>> resolvedVariables,
        int absoluteOffset,
        int lineNumber,
        List<ErbSymbolReference> results)
    {
        var variableName = ExtractVariableName(target.Value);
        var candidateKeys = variableName.Length > 0 && resolvedVariables.TryGetValue(variableName, out var values)
            ? values.OrderBy(value => value, StringComparer.Ordinal).ToList()
            : [];
        var resolution = candidateKeys.Count switch
        {
            0 => SymbolReferenceResolutionKind.Unresolved,
            1 => SymbolReferenceResolutionKind.Resolved,
            _ => SymbolReferenceResolutionKind.Ambiguous,
        };

        results.Add(new ErbSymbolReference
        {
            DocumentId = documentId,
            Namespace = symbolNamespace,
            Kind = ErbSymbolReferenceKind.IndirectVariable,
            ResolutionKind = resolution,
            VariableName = variableName,
            ExpressionText = target.Value,
            AbsoluteStart = absoluteOffset + target.Start,
            Length = target.Length,
            LineNumber = lineNumber,
            CandidateKeys = candidateKeys,
        });
    }

    private void AddGetNumReferences(
        string documentId,
        string line,
        int absoluteOffset,
        int lineNumber,
        IReadOnlyDictionary<string, HashSet<string>> resolvedVariables,
        List<ErbSymbolReference> results)
    {
        var searchIndex = 0;
        while (searchIndex < line.Length)
        {
            var functionIndex = line.IndexOf("GETNUM", searchIndex, StringComparison.OrdinalIgnoreCase);
            if (functionIndex < 0)
            {
                break;
            }

            searchIndex = functionIndex + "GETNUM".Length;
            if (!IsStandaloneIdentifier(line, functionIndex, "GETNUM".Length))
            {
                continue;
            }

            var cursor = searchIndex;
            SkipWhitespace(line, ref cursor);
            if (cursor >= line.Length || line[cursor] != '(')
            {
                continue;
            }

            cursor++;
            SkipWhitespace(line, ref cursor);
            var namespaceStart = cursor;
            while (cursor < line.Length && !char.IsWhiteSpace(line[cursor]) && line[cursor] is not ',' and not ')')
            {
                cursor++;
            }

            if (!_namespaceRegistry.TryResolveNamespace(line[namespaceStart..cursor], out var symbolNamespace))
            {
                continue;
            }

            SkipWhitespace(line, ref cursor);
            if (cursor >= line.Length || line[cursor] != ',')
            {
                continue;
            }

            cursor++;
            SkipWhitespace(line, ref cursor);
            if (!TryReadGetNumKeyArgument(line, ref cursor, out var argumentText, out var argumentStart, out var argumentLength))
            {
                continue;
            }

            var resolved = ResolveExpressionValues(argumentText, absoluteOffset + argumentStart, resolvedVariables);
            var addedDirectReference = false;
            foreach (var occurrence in resolved.ExactLiteralOccurrences)
            {
                if (!ShouldTreatAsSymbolKey(occurrence.LiteralValue))
                {
                    continue;
                }

                AddDirectReference(
                    documentId,
                    symbolNamespace,
                    occurrence.LiteralValue,
                    occurrence.AbsoluteStart,
                    occurrence.Length,
                    lineNumber,
                    results);
                addedDirectReference = true;
            }

            if (addedDirectReference)
            {
                continue;
            }

            var targetValue = argumentText.Trim();
            if (targetValue.Length == 0)
            {
                continue;
            }

            var trimmedStart = line.IndexOf(targetValue, argumentStart, StringComparison.Ordinal);
            var target = new ComponentInfo(
                targetValue,
                trimmedStart < 0 ? argumentStart : trimmedStart,
                targetValue.Length,
                argumentStart + argumentLength,
                IsExpressionComponent(targetValue));
            if (!target.IsExpression && !VariableNamePattern().IsMatch(targetValue))
            {
                continue;
            }

            AddIndirectReference(
                documentId,
                symbolNamespace,
                target,
                resolvedVariables,
                absoluteOffset,
                lineNumber,
                results);
        }
    }

    private static bool TryReadGetNumKeyArgument(
        string line,
        ref int cursor,
        out string argumentText,
        out int argumentStart,
        out int argumentLength)
    {
        argumentText = string.Empty;
        argumentStart = 0;
        argumentLength = 0;
        if (cursor >= line.Length)
        {
            return false;
        }

        var start = cursor;
        var quote = false;
        var parenDepth = 0;
        var braceDepth = 0;
        var bracketDepth = 0;
        var verbatimQuote = false;

        while (cursor < line.Length)
        {
            var ch = line[cursor];
            if (ch == '"' && cursor > start && line[cursor - 1] == '@' && !quote)
            {
                verbatimQuote = true;
                quote = true;
                cursor++;
                continue;
            }

            if (ch == '"')
            {
                if (verbatimQuote && quote && cursor + 1 < line.Length && line[cursor + 1] == '"')
                {
                    cursor += 2;
                    continue;
                }

                quote = !quote;
                if (!quote)
                {
                    verbatimQuote = false;
                }

                cursor++;
                continue;
            }

            if (!quote)
            {
                switch (ch)
                {
                    case '(':
                        parenDepth++;
                        break;
                    case ')':
                        if (parenDepth == 0 && braceDepth == 0 && bracketDepth == 0)
                        {
                            goto Done;
                        }

                        parenDepth = Math.Max(parenDepth - 1, 0);
                        break;
                    case '{':
                        braceDepth++;
                        break;
                    case '}':
                        braceDepth = Math.Max(braceDepth - 1, 0);
                        break;
                    case '[':
                        bracketDepth++;
                        break;
                    case ']':
                        bracketDepth = Math.Max(bracketDepth - 1, 0);
                        break;
                    case ',' when parenDepth == 0 && braceDepth == 0 && bracketDepth == 0:
                        goto Done;
                }
            }

            cursor++;
        }

Done:
        var rawText = line[start..cursor];
        var trimmed = rawText.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var offset = rawText.IndexOf(trimmed, StringComparison.Ordinal);
        argumentStart = start + Math.Max(offset, 0);
        argumentLength = trimmed.Length;
        argumentText = trimmed;
        return true;
    }

    private static ExpressionResolutionResult ResolveExpressionValues(
        string expression,
        int absoluteStart,
        IReadOnlyDictionary<string, HashSet<string>> resolvedVariables)
    {
        var trimmed = expression.Trim();
        var trimOffset = expression.IndexOf(trimmed, StringComparison.Ordinal);
        var startOffset = absoluteStart + Math.Max(trimOffset, 0);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return ExpressionResolutionResult.Empty;
        }

        if (TryResolveQuotedLiteral(trimmed, startOffset, out var literalResult))
        {
            return literalResult;
        }

        var ternaryParts = SplitTernary(trimmed, startOffset);
        if (ternaryParts is not null)
        {
            var left = ResolveExpressionValues(ternaryParts.Value.left, ternaryParts.Value.leftStart, resolvedVariables);
            var right = ResolveExpressionValues(ternaryParts.Value.right, ternaryParts.Value.rightStart, resolvedVariables);
            return left.Union(right);
        }

        var concatParts = SplitTopLevel(trimmed, '+', startOffset);
        if (concatParts.Count > 1)
        {
            var values = new HashSet<string>(StringComparer.Ordinal) { string.Empty };
            foreach (var part in concatParts)
            {
                var resolvedPart = ResolveExpressionValues(part.Text, part.AbsoluteStart, resolvedVariables);
                if (resolvedPart.Values.Count == 0)
                {
                    return new ExpressionResolutionResult(
                        [],
                        resolvedPart.ExactLiteralOccurrences);
                }

                var combined = new HashSet<string>(StringComparer.Ordinal);
                foreach (var prefix in values)
                {
                    foreach (var suffix in resolvedPart.Values)
                    {
                        if (combined.Count >= 32)
                        {
                            break;
                        }

                        combined.Add(prefix + suffix);
                    }
                }

                values = combined;
            }

            return new ExpressionResolutionResult(values, []);
        }

        if (resolvedVariables.TryGetValue(trimmed, out var existing))
        {
            return new ExpressionResolutionResult(existing, []);
        }

        return ExpressionResolutionResult.Empty;
    }

    private static bool TryResolveQuotedLiteral(string expression, int absoluteStart, out ExpressionResolutionResult result)
    {
        var match = QuotedLiteralPattern().Match(expression);
        if (match.Success && match.Length == expression.Length)
        {
            var literal = match.Groups["value"].Value;
            result = new ExpressionResolutionResult(
                [literal],
                [new LiteralOccurrence(literal, absoluteStart + match.Groups["value"].Index, literal.Length)]);
            return true;
        }

        result = ExpressionResolutionResult.Empty;
        return false;
    }

    private static (string left, int leftStart, string right, int rightStart)? SplitTernary(string expression, int absoluteStart)
    {
        var question = -1;
        var hash = -1;
        var quote = false;

        for (var index = 0; index < expression.Length; index++)
        {
            var ch = expression[index];
            if (ch == '"')
            {
                quote = !quote;
                continue;
            }

            if (quote)
            {
                continue;
            }

            if (question < 0 && ch == '?')
            {
                question = index;
                continue;
            }

            if (question >= 0 && ch == '#')
            {
                hash = index;
                break;
            }
        }

        if (question < 0 || hash <= question)
        {
            return null;
        }

        var left = expression[(question + 1)..hash].Trim();
        var right = expression[(hash + 1)..].Trim();
        if (left.Length == 0 || right.Length == 0)
        {
            return null;
        }

        return (
            left,
            absoluteStart + expression.IndexOf(left, question + 1, StringComparison.Ordinal),
            right,
            absoluteStart + expression.LastIndexOf(right, StringComparison.Ordinal));
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

            // Keep scanning inside the argument list so nested calls like
            // OUTER(GET_LOOKUP("key")) also produce references.
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

    private static bool TryReadQuotedArgument(FunctionArgumentInfo argument, out string literal, out int literalStart)
    {
        literal = string.Empty;
        literalStart = 0;
        var match = QuotedLiteralPattern().Match(argument.Text);
        if (!match.Success || match.Length != argument.Text.Length)
        {
            return false;
        }

        literal = match.Groups["value"].Value;
        literalStart = argument.AbsoluteStart + match.Groups["value"].Index;
        return true;
    }

    private static IEnumerable<KeyOccurrence> EnumerateWeightedKeyList(string literal, int absoluteStart, string functionName)
    {
        var entrySeparator = functionName.EndsWith("_RULED", StringComparison.OrdinalIgnoreCase) ? '|' : ',';
        foreach (var entry in SplitKeyListEntries(literal, absoluteStart, entrySeparator))
        {
            var text = entry.Text.TrimStart();
            var keyStart = entry.AbsoluteStart + entry.Text.Length - text.Length;
            if (text.Length == 0)
            {
                continue;
            }

            var keyLength = 0;
            while (keyLength < text.Length && text[keyLength] is not '*' and not ',' and not '|')
            {
                keyLength++;
            }

            while (keyLength > 0 && char.IsWhiteSpace(text[keyLength - 1]))
            {
                keyLength--;
            }

            if (keyLength <= 0)
            {
                continue;
            }

            yield return new KeyOccurrence(text[..keyLength], keyStart);
        }
    }

    private static IEnumerable<FunctionArgumentInfo> SplitKeyListEntries(string text, int absoluteStart, char separator)
    {
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != separator)
            {
                continue;
            }

            yield return new FunctionArgumentInfo(text[start..index], absoluteStart + start);
            start = index + 1;
        }

        yield return new FunctionArgumentInfo(text[start..], absoluteStart + start);
    }

    private static bool TryGetNamespaceFromFirstArgumentFunction(
        FunctionCallInfo call,
        out string namespaceName,
        out int keyListArgumentIndex)
    {
        namespaceName = string.Empty;
        keyListArgumentIndex = -1;
        var normalizedName = call.Name.ToUpperInvariant();
        if (normalizedName is not ("CALC_CHARA_SINGLE_DATA"
            or "CALC_CHARA_SINGLE_DATA_RULED"
            or "CALC_CHARA_MULTIPLE_DATA"
            or "CALC_CHARA_RANGED_DATA"
            or "GET_NONEXISTABLE_CHARA_NO_DEFAULTABLE_SINGLE_DATA"
            or "GET_NONEXISTABLE_VALUES_BYNAME"
            or "GET_NONEXISTABLE_CHARA_VALUES_NO_DEFAULTABLE"))
        {
            return false;
        }

        if (call.Arguments.Count < 2 || !TryReadQuotedArgument(call.Arguments[0], out namespaceName, out _))
        {
            return false;
        }

        keyListArgumentIndex = normalizedName switch
        {
            "GET_NONEXISTABLE_VALUES_BYNAME" => 1,
            "GET_NONEXISTABLE_CHARA_VALUES_NO_DEFAULTABLE" => 2,
            _ => 2,
        };
        return true;
    }

    private static bool TryGetNamespaceFromSecondArgumentFunction(
        FunctionCallInfo call,
        out string namespaceName,
        out int keyListArgumentIndex)
    {
        namespaceName = string.Empty;
        keyListArgumentIndex = -1;
        var normalizedName = call.Name.ToUpperInvariant();
        if (normalizedName is not "CALC_CHARA_MULTIPLE_DATA_BASE")
        {
            return false;
        }

        if (call.Arguments.Count < 4 || !TryReadQuotedArgument(call.Arguments[1], out namespaceName, out _))
        {
            return false;
        }

        keyListArgumentIndex = 3;
        return true;
    }

    private static bool TryGetNamespaceFromFunctionName(string functionName, out string namespaceName)
    {
        namespaceName = functionName.ToUpperInvariant() switch
        {
            "GET_NONEXISTABLE_TALENT_BYNAME" => "TALENT",
            "GET_NONEXISTABLE_ABL_BYNAME" => "ABL",
            "GET_NONEXISTABLE_CFLAG_BYNAME" => "CFLAG",
            "GET_NONEXISTABLE_EXP_BYNAME" => "EXP",
            "GET_NONEXISTABLE_CSTR_BYNAME" => "CSTR",
            _ => string.Empty,
        };
        return namespaceName.Length > 0;
    }

    private static bool IsEscapedQuote(string text, int index)
    {
        return index + 1 < text.Length && text[index + 1] == '"'
            || index > 0 && text[index - 1] == '"';
    }

    private static bool IsIdentifierStart(char character)
    {
        return character == '_' || char.IsLetter(character);
    }

    private static bool IsIdentifierCharacter(char character)
    {
        return character == '_' || char.IsLetterOrDigit(character);
    }

    private static List<ExpressionPart> SplitTopLevel(string expression, char separator, int absoluteStart)
    {
        var parts = new List<ExpressionPart>();
        var start = 0;
        var quote = false;

        for (var index = 0; index < expression.Length; index++)
        {
            var ch = expression[index];
            if (ch == '"')
            {
                quote = !quote;
                continue;
            }

            if (!quote && ch == separator)
            {
                AddPart(start, index);
                start = index + 1;
            }
        }

        AddPart(start, expression.Length);
        return parts;

        void AddPart(int rawStart, int rawEnd)
        {
            var text = expression[rawStart..rawEnd].Trim();
            if (text.Length == 0)
            {
                return;
            }

            var partStart = expression.IndexOf(text, rawStart, StringComparison.Ordinal);
            parts.Add(new ExpressionPart(text, absoluteStart + partStart));
        }
    }

    private static bool MatchesNamespace(string line, int index, string symbolNamespace)
    {
        if (index + symbolNamespace.Length >= line.Length)
        {
            return false;
        }

        if (!line.AsSpan(index, symbolNamespace.Length).Equals(symbolNamespace, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var previous = index == 0 ? '\0' : line[index - 1];
        if (char.IsLetterOrDigit(previous) || previous == '_')
        {
            return false;
        }

        return line[index + symbolNamespace.Length] == ':';
    }

    private static bool IsStandaloneIdentifier(string line, int index, int length)
    {
        var previous = index == 0 ? '\0' : line[index - 1];
        if (char.IsLetterOrDigit(previous) || previous == '_')
        {
            return false;
        }

        var nextIndex = index + length;
        if (nextIndex >= line.Length)
        {
            return true;
        }

        var next = line[nextIndex];
        return !char.IsLetterOrDigit(next) && next != '_';
    }

    private static void SkipWhitespace(string line, ref int cursor)
    {
        while (cursor < line.Length && char.IsWhiteSpace(line[cursor]))
        {
            cursor++;
        }
    }

    private static ComponentInfo ReadComponent(string line, int start)
    {
        if (start >= line.Length)
        {
            return ComponentInfo.Empty;
        }

        if (line[start] == '{')
        {
            var endBrace = FindMatchingDelimiter(line, start, '{', '}');
            if (endBrace < 0)
            {
                return ComponentInfo.Empty;
            }

            return new ComponentInfo(
                line[(start + 1)..endBrace].Trim(),
                start + 1,
                endBrace - start - 1,
                endBrace + 1,
                true);
        }

        var end = start;
        var parenDepth = 0;
        var braceDepth = 0;
        var bracketDepth = 0;
        var quote = false;

        while (end < line.Length)
        {
            var ch = line[end];
            if (ch == '"')
            {
                if (!quote && parenDepth == 0 && braceDepth == 0 && bracketDepth == 0 && end > start)
                {
                    break;
                }

                quote = !quote;
                end++;
                continue;
            }

            if (!quote)
            {
                switch (ch)
                {
                    case '(':
                        parenDepth++;
                        end++;
                        continue;
                    case ')':
                        if (parenDepth == 0 && braceDepth == 0 && bracketDepth == 0)
                        {
                            goto Done;
                        }

                        parenDepth = Math.Max(parenDepth - 1, 0);
                        end++;
                        continue;
                    case '{':
                        braceDepth++;
                        end++;
                        continue;
                    case '}':
                        if (parenDepth == 0 && braceDepth == 0 && bracketDepth == 0)
                        {
                            goto Done;
                        }

                        braceDepth = Math.Max(braceDepth - 1, 0);
                        end++;
                        continue;
                    case '[':
                        bracketDepth++;
                        end++;
                        continue;
                    case ']':
                        if (parenDepth == 0 && braceDepth == 0 && bracketDepth == 0)
                        {
                            goto Done;
                        }

                        bracketDepth = Math.Max(bracketDepth - 1, 0);
                        end++;
                        continue;
                }

                if (parenDepth == 0 && braceDepth == 0 && bracketDepth == 0 && IsReferenceDelimiter(ch))
                {
                    break;
                }
            }

            end++;
        }

Done:
        var rawValue = line[start..end].Trim();
        if (rawValue.Length == 0)
        {
            return ComponentInfo.Empty;
        }

        var valueStart = line.IndexOf(rawValue, start, StringComparison.Ordinal);
        var isExpression = IsExpressionComponent(rawValue);
        return new ComponentInfo(rawValue, valueStart, rawValue.Length, end, isExpression);
    }

    private static bool IsReferenceDelimiter(char ch)
    {
        return char.IsWhiteSpace(ch)
            || ch is ':' or ',' or ';' or '+' or '-' or '*' or '/' or '<' or '>' or '=' or '!' or '&' or '|' or '%' or '"' or '\'';
    }

    private static int FindMatchingDelimiter(string text, int start, char open, char close)
    {
        var depth = 0;
        var quote = false;
        for (var index = start; index < text.Length; index++)
        {
            var ch = text[index];
            if (ch == '"')
            {
                quote = !quote;
                continue;
            }

            if (quote)
            {
                continue;
            }

            if (ch == open)
            {
                depth++;
            }
            else if (ch == close)
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

    private static bool IsExpressionComponent(string value)
    {
        return value.StartsWith('(')
            || value.StartsWith('{')
            || value.StartsWith('[')
            || value.Contains('(')
            || value.Contains('{')
            || value.Contains('[')
            || value.Contains('+')
            || value.Contains('-')
            || value.Contains('*')
            || value.Contains('/')
            || value.Contains(':');
    }

    private static bool LooksLikeIndexComponent(ComponentInfo component)
    {
        var value = component.Value.Trim();
        if (string.IsNullOrWhiteSpace(value) || TextHeuristics.IsNumericLike(value))
        {
            return true;
        }

        if (component.IsExpression)
        {
            return true;
        }

        return IndexVariablePattern().IsMatch(value);
    }

    private static bool LooksLikeLiteralSymbolWithDecorativePunctuation(string value)
    {
        var trimmed = value.Trim();
        return !string.IsNullOrWhiteSpace(trimmed)
            && !trimmed.StartsWith('{')
            && !VariableNamePattern().IsMatch(trimmed)
            && trimmed.Any(static ch => !char.IsAsciiLetterOrDigit(ch) && ch != '_' && !char.IsWhiteSpace(ch));
    }

    private static bool ShouldTreatAsIndirectKeyExpression(ComponentInfo component)
    {
        var value = component.Value.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (VariableNamePattern().IsMatch(value))
        {
            return true;
        }

        return value.StartsWith('{') || value.StartsWith('[');
    }

    private bool TryReadSelectCaseLookupNamespace(string line, out string symbolNamespace)
    {
        symbolNamespace = string.Empty;
        var match = SelectCaseDimsArrayPattern().Match(line);
        if (!match.Success)
        {
            return false;
        }

        var arrayName = match.Groups["array"].Value;
        if (!_dimsLookupRegistry.IsLookupArray(arrayName))
        {
            return false;
        }

        symbolNamespace = ErbDimsLookupRegistry.ToNamespace(arrayName);
        return true;
    }

    private bool TryReadSelectCaseCsvNameNamespace(string line, out string symbolNamespace)
    {
        symbolNamespace = string.Empty;
        var match = SelectCaseCsvNamePattern().Match(line);
        if (!match.Success)
        {
            return false;
        }

        return _namespaceRegistry.TryResolveNamespace(match.Groups["namespace"].Value, out symbolNamespace);
    }

    private static bool IsSelectCaseLine(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.Length > "SELECTCASE".Length
            && trimmed.StartsWith("SELECTCASE", StringComparison.OrdinalIgnoreCase)
            && char.IsWhiteSpace(trimmed["SELECTCASE".Length]);
    }

    private static bool IsCaseLabelLine(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.Length > "CASE".Length
            && trimmed.StartsWith("CASE", StringComparison.OrdinalIgnoreCase)
            && char.IsWhiteSpace(trimmed["CASE".Length]);
    }

    private static bool IsEndSelectLine(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.Equals("ENDSELECT", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsElementLookupFunction(string functionName)
    {
        return functionName.Equals("FINDELEMENT", StringComparison.OrdinalIgnoreCase)
            || functionName.Equals("FINDLASTELEMENT", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadIdentifier(string text, out string identifier)
    {
        identifier = string.Empty;
        var trimmed = text.Trim();
        if (!VariableNamePattern().IsMatch(trimmed))
        {
            return false;
        }

        identifier = trimmed;
        return true;
    }

    private static bool ShouldTreatAsSymbolKey(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !TextHeuristics.IsNumericLike(value)
            && !value.StartsWith('(')
            && value.Any(static ch => !char.IsDigit(ch));
    }

    private static string ExtractVariableName(string expressionText)
    {
        var match = VariableNamePattern().Match(expressionText.Trim());
        return match.Success ? match.Value : string.Empty;
    }

    [GeneratedRegex("""^\s*(?<var>[\p{L}_][\p{L}\p{N}_]*)\s*=\s*(?<expr>.+?)\s*$""", RegexOptions.Compiled)]
    private static partial Regex AssignmentPattern();

    [GeneratedRegex("""^@?"(?<value>(?:[^"\\]|\\.)*)"$""", RegexOptions.Compiled)]
    private static partial Regex QuotedLiteralPattern();

    [GeneratedRegex("""^[\p{L}_][\p{L}\p{N}_]*$""", RegexOptions.Compiled)]
    private static partial Regex VariableNamePattern();

    [GeneratedRegex("""^[A-Za-z_][A-Za-z0-9_]*$""", RegexOptions.Compiled)]
    private static partial Regex IndexVariablePattern();

    [GeneratedRegex("""^\s*SELECTCASE\s+(?<array>[\p{L}_][\p{L}\p{N}_]*)\s*:""", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SelectCaseDimsArrayPattern();

    [GeneratedRegex("""^\s*SELECTCASE\s+(?<namespace>[\p{L}_][\p{L}\p{N}_]*)NAME\s*:""", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SelectCaseCsvNamePattern();

    [GeneratedRegex(@"(?<array>[\p{L}_][\p{L}\p{N}_]*)\s*:[^""'\r\n=<>!]+\s*(?:==|!=|<>)\s*@?""(?<value>(?:[^""]|"""")*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DimsArrayComparisonPattern();

    [GeneratedRegex(@"@?""(?<value>(?:[^""]|"""")*)""", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex QuotedLiteralInLinePattern();

    private readonly record struct AssignmentInfo(string VariableName, string ExpressionText, int ExpressionAbsoluteStart, int LineNumber);

    private readonly record struct ComponentInfo(string Value, int Start, int Length, int End, bool IsExpression)
    {
        public static ComponentInfo Empty => new(string.Empty, 0, 0, 0, false);
    }

    private readonly record struct ExpressionPart(string Text, int AbsoluteStart);

    private readonly record struct LiteralOccurrence(string LiteralValue, int AbsoluteStart, int Length);

    private readonly record struct FunctionCallInfo(string Name, List<FunctionArgumentInfo> Arguments);

    private readonly record struct FunctionArgumentInfo(string Text, int AbsoluteStart);

    private readonly record struct KeyOccurrence(string Value, int AbsoluteStart);

    private readonly record struct ExpressionResolutionResult(
        IReadOnlyCollection<string> Values,
        IReadOnlyCollection<LiteralOccurrence> ExactLiteralOccurrences)
    {
        public static ExpressionResolutionResult Empty => new([], []);

        public ExpressionResolutionResult Union(ExpressionResolutionResult other)
        {
            return new ExpressionResolutionResult(
                Values.Concat(other.Values).Distinct(StringComparer.Ordinal).ToList(),
                ExactLiteralOccurrences.Concat(other.ExactLiteralOccurrences).ToList());
        }
    }
}
