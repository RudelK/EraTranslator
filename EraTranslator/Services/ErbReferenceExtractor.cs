using System.Text.RegularExpressions;
using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed partial class ErbReferenceExtractor
{
    private static readonly string[] SupportedNamespaces =
    [
        "CALLNAME",
        "CFLAG",
        "TFLAG",
        "FLAG",
        "CSTR",
        "STR",
        "ITEM",
        "BASE",
        "ABL",
        "PALAM",
        "EXP",
        "MARK",
        "TALENT",
        "SOURCE",
        "JUEL",
        "TEQUIP",
        "NOWEX",
        "EX",
        "SAVESTR",
    ];

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

    private static List<ErbSymbolReference> ExtractReferences(
        string documentId,
        IReadOnlyList<string> lines,
        IReadOnlyDictionary<string, HashSet<string>> resolvedVariables)
    {
        var results = new List<ErbSymbolReference>();
        var absoluteOffset = 0;

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

            for (var index = 0; index < normalizedLine.Length; index++)
            {
                foreach (var symbolNamespace in SupportedNamespaces)
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

                    var firstComponent = ReadComponent(normalizedLine, cursor);
                    if (firstComponent.Length == 0)
                    {
                        continue;
                    }

                    cursor = firstComponent.End;
                    SkipWhitespace(normalizedLine, ref cursor);

                    ComponentInfo target = firstComponent;
                    if (!target.IsExpression && cursor < normalizedLine.Length && normalizedLine[cursor] == ':')
                    {
                        cursor++;
                        SkipWhitespace(normalizedLine, ref cursor);
                        var secondComponent = ReadComponent(normalizedLine, cursor);
                        if (secondComponent.Length > 0)
                        {
                            target = secondComponent;
                        }
                    }

                    if (target.Length == 0)
                    {
                        continue;
                    }

                    if (target.IsExpression)
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
                            LineNumber = lineIndex + 1,
                            CandidateKeys = candidateKeys,
                        });

                        index = target.End - 1;
                        continue;
                    }

                    if (!ShouldTreatAsSymbolKey(target.Value))
                    {
                        index = target.End - 1;
                        continue;
                    }

                    results.Add(new ErbSymbolReference
                    {
                        DocumentId = documentId,
                        Namespace = symbolNamespace,
                        Kind = ErbSymbolReferenceKind.DirectLiteral,
                        ResolutionKind = SymbolReferenceResolutionKind.Direct,
                        OriginalKey = target.Value,
                        AbsoluteStart = absoluteOffset + target.Start,
                        Length = target.Length,
                        LineNumber = lineIndex + 1,
                        CandidateKeys = [target.Value],
                    });

                    index = target.End - 1;
                }
            }

            absoluteOffset += line.Length + 1;
        }

        return results;
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

        if (!line.AsSpan(index, symbolNamespace.Length).Equals(symbolNamespace, StringComparison.Ordinal))
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
            var endBrace = line.IndexOf('}', start + 1);
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
        while (end < line.Length && !IsReferenceDelimiter(line[end]))
        {
            end++;
        }

        var rawValue = line[start..end].Trim();
        if (rawValue.Length == 0)
        {
            return ComponentInfo.Empty;
        }

        var valueStart = line.IndexOf(rawValue, start, StringComparison.Ordinal);
        return new ComponentInfo(rawValue, valueStart, rawValue.Length, end, false);
    }

    private static bool IsReferenceDelimiter(char ch)
    {
        return char.IsWhiteSpace(ch)
            || ch is ',' or ')' or '(' or ']' or '[' or '+' or '-' or '*' or '/' or '<' or '>' or '=' or '!' or '&' or '|' or '%' or '"' or '\'';
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

    [GeneratedRegex("""^\s*(?<var>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<expr>.+?)\s*$""", RegexOptions.Compiled)]
    private static partial Regex AssignmentPattern();

    [GeneratedRegex("""^@?"(?<value>(?:[^"\\]|\\.)*)"$""", RegexOptions.Compiled)]
    private static partial Regex QuotedLiteralPattern();

    [GeneratedRegex("""^[A-Za-z_][A-Za-z0-9_]*$""", RegexOptions.Compiled)]
    private static partial Regex VariableNamePattern();

    private readonly record struct AssignmentInfo(string VariableName, string ExpressionText, int ExpressionAbsoluteStart, int LineNumber);

    private readonly record struct ComponentInfo(string Value, int Start, int Length, int End, bool IsExpression)
    {
        public static ComponentInfo Empty => new(string.Empty, 0, 0, 0, false);
    }

    private readonly record struct ExpressionPart(string Text, int AbsoluteStart);

    private readonly record struct LiteralOccurrence(string LiteralValue, int AbsoluteStart, int Length);

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
