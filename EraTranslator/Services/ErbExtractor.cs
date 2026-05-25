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
}
