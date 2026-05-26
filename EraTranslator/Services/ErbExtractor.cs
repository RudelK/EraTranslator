using System.Text.RegularExpressions;

namespace EraTranslator.Services;

public sealed partial class ErbExtractor
{
    public List<TextSegment> Extract(string documentId, string content)
    {
        var segments = new List<TextSegment>();
        var lines = content.Split("\n");
        var absoluteOffset = 0;
        var segmentIndex = 0;
        var insideDataBlock = false;

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

            if (insideDataBlock)
            {
                if (EndDataPattern().IsMatch(trimmed))
                {
                    insideDataBlock = false;
                    absoluteOffset += line.Length + 1;
                    continue;
                }

                if (DataListBoundaryPattern().IsMatch(trimmed))
                {
                    absoluteOffset += line.Length + 1;
                    continue;
                }

                if (TryExtractDataLine(normalizedLine))
                {
                    absoluteOffset += line.Length + 1;
                    continue;
                }
            }

            if (PrintDataStartPattern().IsMatch(trimmed))
            {
                insideDataBlock = true;
                absoluteOffset += line.Length + 1;
                continue;
            }

            var htmlContext = HtmlAssignmentPattern().IsMatch(normalizedLine) || HtmlPrintPattern().IsMatch(normalizedLine);
            foreach (Match match in QuotedStringPattern().Matches(normalizedLine))
            {
                var value = match.Groups["content"].Value;
                if (!TextHeuristics.ContainsTranslatableText(value))
                {
                    continue;
                }

                if (htmlContext && LooksLikeHtml(value))
                {
                    ExtractHtmlSegments("html-markup", value, match.Groups["content"].Index);
                    continue;
                }

                AddSegment("quoted-string", match.Groups["content"].Index, value);
            }

            ExtractAssignmentValueIfNeeded(normalizedLine);
            ExtractHtmlTailIfNeeded(normalizedLine);

            var printMatch = PrintCommandPattern().Match(normalizedLine);
            if (!htmlContext && printMatch.Success)
            {
                var tail = printMatch.Groups["tail"].Value;
                var tailOffset = printMatch.Groups["tail"].Index;
                if (!tail.Contains('"'))
                {
                    ExtractPrintTailSegments(tail, tailOffset);
                }
            }

            absoluteOffset += line.Length + 1;

            bool TryExtractDataLine(string sourceLine)
            {
                var dataMatch = DataLinePattern().Match(sourceLine);
                if (!dataMatch.Success)
                {
                    return false;
                }

                var command = dataMatch.Groups["command"].Value.ToLowerInvariant();
                var tail = dataMatch.Groups["tail"].Value;
                var tailOffset = dataMatch.Groups["tail"].Index;
                var extractedQuoted = false;

                foreach (Match match in QuotedStringPattern().Matches(tail))
                {
                    var quotedValue = match.Groups["content"].Value;
                    if (!TextHeuristics.ContainsTranslatableText(quotedValue))
                    {
                        continue;
                    }

                    extractedQuoted = true;
                    if (LooksLikeHtml(quotedValue))
                    {
                        ExtractHtmlSegments($"data-{command}-markup", quotedValue, tailOffset + match.Groups["content"].Index);
                        continue;
                    }

                    AddSegment($"data-{command}", tailOffset + match.Groups["content"].Index, quotedValue);
                }

                if (extractedQuoted || tail.Contains('"'))
                {
                    return true;
                }

                if (LooksLikeHtml(tail))
                {
                    ExtractHtmlSegments($"data-{command}-markup", tail, tailOffset);
                    return true;
                }

                ExtractPrintTailSegments(tail, tailOffset);
                return true;
            }

            void ExtractHtmlTailIfNeeded(string sourceLine)
            {
                var htmlAssignment = HtmlAssignmentPattern().Match(sourceLine);
                if (htmlAssignment.Success)
                {
                    var assignmentTail = htmlAssignment.Groups["tail"].Value;
                    if (!assignmentTail.Contains('"') && LooksLikeHtml(assignmentTail))
                    {
                        ExtractHtmlSegments("html-tail", assignmentTail, htmlAssignment.Groups["tail"].Index);
                    }

                    return;
                }

                var htmlPrint = HtmlPrintPattern().Match(sourceLine);
                if (!htmlPrint.Success)
                {
                    return;
                }

                var printTail = htmlPrint.Groups["tail"].Value;
                if (!printTail.Contains('"') && LooksLikeHtml(printTail))
                {
                    ExtractHtmlSegments("html-print", printTail, htmlPrint.Groups["tail"].Index);
                }
            }

            void ExtractAssignmentValueIfNeeded(string sourceLine)
            {
                var assignmentMatch = AssignmentPattern().Match(sourceLine);
                if (!assignmentMatch.Success)
                {
                    return;
                }

                if (!TryGetAssignmentExpression(
                        assignmentMatch.Groups["tail"].Value,
                        assignmentMatch.Groups["tail"].Index,
                        out var expression,
                        out var expressionLineStart))
                {
                    return;
                }

                if (LooksLikeBareAssignmentText(expression))
                {
                    AddSegment("assignment-value", expressionLineStart, expression);
                    return;
                }

                foreach (var fragment in ExtractAssignmentFragments(expression, expressionLineStart))
                {
                    AddSegment("assignment-fragment", fragment.relativeStart, fragment.value);
                }
            }

            void ExtractHtmlSegments(string type, string markup, int relativeStart)
            {
                foreach (var range in ExtractHtmlTextRanges(markup))
                {
                    AddSegment(type, relativeStart + range.start, range.value);
                }
            }

            void AddSegment(string type, int relativeStart, string value)
            {
                if (!TextHeuristics.ContainsTranslatableText(value)
                    || TextHeuristics.LooksLikeCodeOnly(value)
                    || TextHeuristics.LooksLikeErbSymbolExpression(value))
                {
                    return;
                }

                segments.Add(new TextSegment
                {
                    SegmentId = $"{documentId}:{segmentIndex++}",
                    DocumentId = documentId,
                    SegmentType = type,
                    AbsoluteStart = absoluteOffset + relativeStart,
                    Length = value.Length,
                    LineNumber = lineIndex + 1,
                    OriginalText = value,
                });
            }

            void ExtractPrintTailSegments(string tailValue, int lineOffset)
            {
                if (string.IsNullOrWhiteSpace(tailValue))
                {
                    return;
                }

                var ternaryMatches = InlineConditionalPattern().Matches(tailValue);
                if (ternaryMatches.Count > 0)
                {
                    foreach (Match ternary in ternaryMatches)
                    {
                        var inner = ternary.Groups["inner"].Value;
                        var questionIndex = inner.IndexOf('?');
                        var hashIndex = inner.LastIndexOf('#');
                        if (questionIndex < 0 || hashIndex <= questionIndex)
                        {
                            continue;
                        }

                        var leftRaw = inner[(questionIndex + 1)..hashIndex];
                        var rightRaw = inner[(hashIndex + 1)..];
                        var left = leftRaw.Trim();
                        var right = rightRaw.Trim();

                        if (TextHeuristics.ContainsTranslatableText(left))
                        {
                            var relative = ternary.Groups["inner"].Index + questionIndex + 1 + leftRaw.IndexOf(left, StringComparison.Ordinal);
                            AddSegment("inline-conditional-left", lineOffset + relative, left);
                        }

                        if (TextHeuristics.ContainsTranslatableText(right))
                        {
                            var relative = ternary.Groups["inner"].Index + hashIndex + 1 + rightRaw.IndexOf(right, StringComparison.Ordinal);
                            AddSegment("inline-conditional-right", lineOffset + relative, right);
                        }
                    }

                    return;
                }

                if (TextHeuristics.ContainsTranslatableText(tailValue))
                {
                    AddSegment("print-tail", lineOffset, tailValue);
                }
            }
        }

        return segments;
    }

    private static bool LooksLikeHtml(string value)
    {
        return value.Contains('<') && value.Contains('>');
    }

    private static bool TryGetAssignmentExpression(
        string rawTail,
        int tailLineStart,
        out string expression,
        out int expressionLineStart)
    {
        var commentless = StripInlineComment(rawTail);
        var trimmed = commentless.Trim();
        if (trimmed.Length == 0)
        {
            expression = string.Empty;
            expressionLineStart = 0;
            return false;
        }

        var offset = commentless.IndexOf(trimmed, StringComparison.Ordinal);
        if (offset < 0)
        {
            expression = string.Empty;
            expressionLineStart = 0;
            return false;
        }

        expression = trimmed;
        expressionLineStart = tailLineStart + offset;
        return true;
    }

    private static bool LooksLikeBareAssignmentText(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !TextHeuristics.ContainsTranslatableText(value)
            || TextHeuristics.LooksLikeCodeOnly(value)
            || TextHeuristics.LooksLikeErbSymbolExpression(value)
            || TextHeuristics.IsNumericLike(value))
        {
            return false;
        }

        return !BareAssignmentCodeSyntaxPattern().IsMatch(value);
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

    private static List<(int relativeStart, string value)> ExtractAssignmentFragments(string expression, int expressionLineStart)
    {
        var fragments = new List<(int relativeStart, string value)>();
        CollectAssignmentFragments(expression, expressionLineStart, fragments);
        return fragments
            .Distinct()
            .ToList();
    }

    private static void CollectAssignmentFragments(
        string expression,
        int expressionLineStart,
        ICollection<(int relativeStart, string value)> fragments)
    {
        var trimmed = expression.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        var trimOffset = expression.IndexOf(trimmed, StringComparison.Ordinal);
        var trimmedStart = expressionLineStart + Math.Max(trimOffset, 0);

        if (LooksLikeBareAssignmentText(trimmed))
        {
            fragments.Add((trimmedStart, trimmed));
            return;
        }

        if (TryUnwrapOuterParentheses(trimmed, out var inner, out var innerOffset))
        {
            CollectAssignmentFragments(inner, trimmedStart + innerOffset, fragments);
            return;
        }

        var ternary = SplitTopLevelTernary(trimmed);
        if (ternary is not null)
        {
            CollectAssignmentFragments(ternary.Value.left, trimmedStart + ternary.Value.leftOffset, fragments);
            CollectAssignmentFragments(ternary.Value.right, trimmedStart + ternary.Value.rightOffset, fragments);
            return;
        }

        var parts = SplitTopLevel(trimmed, '+');
        if (parts.Count > 1)
        {
            foreach (var part in parts)
            {
                CollectAssignmentFragments(part.text, trimmedStart + part.offset, fragments);
            }
        }
    }

    private static bool TryUnwrapOuterParentheses(string value, out string inner, out int innerOffset)
    {
        inner = string.Empty;
        innerOffset = 0;
        if (value.Length < 2 || value[0] != '(' || value[^1] != ')')
        {
            return false;
        }

        var quote = false;
        var depth = 0;
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

            if (ch == '(')
            {
                depth++;
            }
            else if (ch == ')')
            {
                depth--;
                if (depth == 0 && index != value.Length - 1)
                {
                    return false;
                }
            }
        }

        if (depth != 0)
        {
            return false;
        }

        inner = value[1..^1];
        innerOffset = 1;
        return true;
    }

    private static (string left, int leftOffset, string right, int rightOffset)? SplitTopLevelTernary(string expression)
    {
        var quote = false;
        var parenDepth = 0;
        var braceDepth = 0;
        var bracketDepth = 0;
        var questionIndex = -1;

        for (var index = 0; index < expression.Length; index++)
        {
            var ch = expression[index];
            UpdateParserState(ch, ref quote, ref parenDepth, ref braceDepth, ref bracketDepth);
            if (quote || parenDepth > 0 || braceDepth > 0 || bracketDepth > 0)
            {
                continue;
            }

            if (questionIndex < 0 && ch == '?')
            {
                questionIndex = index;
                continue;
            }

            if (questionIndex >= 0 && ch == '#')
            {
                var leftRaw = expression[(questionIndex + 1)..index];
                var rightRaw = expression[(index + 1)..];
                var left = leftRaw.Trim();
                var right = rightRaw.Trim();
                if (left.Length == 0 || right.Length == 0)
                {
                    return null;
                }

                return (
                    left,
                    questionIndex + 1 + leftRaw.IndexOf(left, StringComparison.Ordinal),
                    right,
                    index + 1 + rightRaw.IndexOf(right, StringComparison.Ordinal));
            }
        }

        return null;
    }

    private static List<(string text, int offset)> SplitTopLevel(string expression, char separator)
    {
        var parts = new List<(string text, int offset)>();
        var quote = false;
        var parenDepth = 0;
        var braceDepth = 0;
        var bracketDepth = 0;
        var start = 0;

        for (var index = 0; index < expression.Length; index++)
        {
            var ch = expression[index];
            UpdateParserState(ch, ref quote, ref parenDepth, ref braceDepth, ref bracketDepth);
            if (quote || parenDepth > 0 || braceDepth > 0 || bracketDepth > 0)
            {
                continue;
            }

            if (ch != separator)
            {
                continue;
            }

            AddPart(start, index);
            start = index + 1;
        }

        AddPart(start, expression.Length);
        return parts;

        void AddPart(int rawStart, int rawEnd)
        {
            var raw = expression[rawStart..rawEnd];
            var text = raw.Trim();
            if (text.Length == 0)
            {
                return;
            }

            parts.Add((text, rawStart + raw.IndexOf(text, StringComparison.Ordinal)));
        }
    }

    private static void UpdateParserState(
        char ch,
        ref bool quote,
        ref int parenDepth,
        ref int braceDepth,
        ref int bracketDepth)
    {
        if (ch == '"')
        {
            quote = !quote;
            return;
        }

        if (quote)
        {
            return;
        }

        switch (ch)
        {
            case '(':
                parenDepth++;
                break;
            case ')':
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
        }
    }

    private static IEnumerable<(int start, string value)> ExtractHtmlTextRanges(string markup)
    {
        var ranges = new List<(int start, string value)>();
        var textStart = -1;
        var insideTag = false;
        char? quotedAttribute = null;

        for (var index = 0; index < markup.Length; index++)
        {
            var ch = markup[index];
            if (insideTag)
            {
                if (quotedAttribute.HasValue)
                {
                    if (ch == quotedAttribute.Value)
                    {
                        quotedAttribute = null;
                    }

                    continue;
                }

                if (ch is '\'' or '"')
                {
                    quotedAttribute = ch;
                    continue;
                }

                if (ch == '>')
                {
                    insideTag = false;
                }

                continue;
            }

            if (ch == '<')
            {
                FlushText(index);
                insideTag = true;
                continue;
            }

            if (textStart < 0)
            {
                textStart = index;
            }
        }

        FlushText(markup.Length);

        foreach (Match match in HtmlTitleAttributePattern().Matches(markup))
        {
            var value = match.Groups["content"].Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                ranges.Add((match.Groups["content"].Index, value));
            }
        }

        return ranges;

        void FlushText(int endExclusive)
        {
            if (textStart < 0 || endExclusive <= textStart)
            {
                textStart = -1;
                return;
            }

            var raw = markup[textStart..endExclusive];
            var trimmed = raw.Trim();
            if (trimmed.Length == 0)
            {
                textStart = -1;
                return;
            }

            var offset = raw.IndexOf(trimmed, StringComparison.Ordinal);
            ranges.Add((textStart + offset, trimmed));
            textStart = -1;
        }
    }

    [GeneratedRegex(@"@?""(?<content>(?:[^""]|"""")*)""", RegexOptions.Compiled)]
    private static partial Regex QuotedStringPattern();

    [GeneratedRegex(@"^\s*PRINT[A-Z_]*\s+(?<tail>.+)$", RegexOptions.Compiled)]
    private static partial Regex PrintCommandPattern();

    [GeneratedRegex(@"\\@(?<inner>.*?)\\@", RegexOptions.Compiled)]
    private static partial Regex InlineConditionalPattern();

    [GeneratedRegex(@"^\s*(?:PRINTDATA[A-Z_]*|STRDATA)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex PrintDataStartPattern();

    [GeneratedRegex(@"^\s*ENDDATA\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex EndDataPattern();

    [GeneratedRegex(@"^\s*(?:DATALIST|ENDLIST)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex DataListBoundaryPattern();

    [GeneratedRegex(@"^\s*(?<command>DATAFORM|DATA)\s+(?<tail>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex DataLinePattern();

    [GeneratedRegex(@"^\s*PRINT_TAG\s*(?:\+?=)\s*(?<tail>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex HtmlAssignmentPattern();

    [GeneratedRegex(@"^\s*HTML_PRINT\s+(?<tail>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex HtmlPrintPattern();

    [GeneratedRegex(@"\btitle\s*=\s*(['""])(?<content>.*?)\1", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex HtmlTitleAttributePattern();

    [GeneratedRegex(@"^\s*(?<var>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<tail>.+?)\s*$", RegexOptions.Compiled)]
    private static partial Regex AssignmentPattern();

    [GeneratedRegex(@"[(){}\[\]=<>!&|+\-*/%:,\\@#?]", RegexOptions.Compiled)]
    private static partial Regex BareAssignmentCodeSyntaxPattern();
}
