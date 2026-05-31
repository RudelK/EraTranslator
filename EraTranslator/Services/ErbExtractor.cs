using System.Text.RegularExpressions;

namespace EraTranslator.Services;

public sealed partial class ErbExtractor
{
    private static readonly string[] ReservedScriptVariables = ["LOCAL", "LOCALS", "ARG", "ARGS", "RESULT", "RESULTS"];
    private static readonly string[] ProtectedCodeArgumentFunctionNames = ["GETCONFIG", "VARSIZE", "LOADTEXT", "SAVETEXT"];
    private static readonly string[] PaletteLookupFunctionNames = ["BARCOLORSET", "BARCOLORSET_HTML", "カラーパレット", "カラーパレット_透明度込", "カラーパレット_HTML"];
    private readonly Regex _scriptSyntaxTokenPattern;

    public ErbExtractor()
        : this(SymbolNamespaceRegistry.Default)
    {
    }

    public ErbExtractor(SymbolNamespaceRegistry namespaceRegistry)
    {
        _scriptSyntaxTokenPattern = BuildScriptSyntaxTokenPattern(namespaceRegistry);
    }

    public List<TextSegment> Extract(string documentId, string content)
    {
        var segments = new List<TextSegment>();
        var lines = content.Split("\n");
        var absoluteOffset = 0;
        var segmentIndex = 0;
        var insideDataBlock = false;
        var insideDirectiveContinuation = false;
        var directiveContinuationParenDepth = 0;
        var insidePaletteLookupFunction = false;

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            var normalizedLine = line.TrimEnd('\r');
            var trimmed = normalizedLine.TrimStart();
            if (TryReadFunctionName(trimmed, out var functionName))
            {
                insidePaletteLookupFunction = IsPaletteLookupFunction(functionName);
            }

            if (trimmed.StartsWith(';'))
            {
                absoluteOffset += line.Length + 1;
                continue;
            }

            if (insideDirectiveContinuation)
            {
                ExtractDirectiveStrings(normalizedLine, directiveContinuationParenDepth, out directiveContinuationParenDepth);
                insideDirectiveContinuation = ShouldContinueDirective(normalizedLine, directiveContinuationParenDepth);
                absoluteOffset += line.Length + 1;
                continue;
            }

            if (trimmed.StartsWith('#'))
            {
                if (DimDirectivePattern().IsMatch(normalizedLine))
                {
                    ExtractDirectiveStrings(normalizedLine, 0, out directiveContinuationParenDepth);
                    insideDirectiveContinuation = ShouldContinueDirective(normalizedLine, directiveContinuationParenDepth);
                    absoluteOffset += line.Length + 1;
                    continue;
                }

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

            ExtractCharacterSearchArguments(normalizedLine);

            var htmlContext = HtmlAssignmentPattern().IsMatch(normalizedLine) || HtmlPrintPattern().IsMatch(normalizedLine);
            var protectedQuotedRanges = CollectProtectedQuotedRanges(normalizedLine, "html-markup");
            var imageResourceContext = LooksLikeImageResourceContext(normalizedLine);
            var skipQuotedStrings = PrintImageCommandPattern().IsMatch(normalizedLine) || imageResourceContext;
            foreach (Match match in QuotedStringPattern().Matches(normalizedLine))
            {
                if (skipQuotedStrings
                    || protectedQuotedRanges.Any(range => RangesOverlap(range.start, range.end - range.start, match.Index, match.Length))
                    || (insidePaletteLookupFunction && IsCaseLabelLine(normalizedLine))
                    || IsQuotedStringProtectedCodeArgument(normalizedLine, match.Index, match.Length))
                {
                    continue;
                }

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

                if (ContainsScriptSyntaxToken(value)
                    && !ShouldKeepWholeCodeMixedText(value)
                    && TryAddCodeMixedTextSpans("quoted-string-fragment", value, match.Groups["content"].Index))
                {
                    continue;
                }

                AddSegment("quoted-string", match.Groups["content"].Index, value);
            }

            if (!imageResourceContext)
            {
                ExtractAssignmentValueIfNeeded(normalizedLine);
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

            void ExtractDirectiveStrings(string sourceLine, int startingParenDepth, out int endingParenDepth)
            {
                var scanResult = ScanDirectiveQuotedStrings(sourceLine, startingParenDepth);
                endingParenDepth = scanResult.endingParenDepth;
                foreach (var quotedString in scanResult.quotedStrings)
                {
                    var value = quotedString.value;
                    if (!TextHeuristics.ContainsTranslatableText(value))
                    {
                        continue;
                    }

                    AddSegment("directive-string", quotedString.relativeStart, value);
                }
            }

            void ExtractCharacterSearchArguments(string sourceLine)
            {
                foreach (Match match in CharacterSearchArgumentPattern().Matches(sourceLine))
                {
                    var value = match.Groups["name"].Value;
                    if (!TextHeuristics.ContainsTranslatableText(value))
                    {
                        continue;
                    }

                    AddSegment("character-search-name", match.Groups["name"].Index, value);
                }
            }

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

                var protectedTailRanges = CollectProtectedQuotedRanges(tail, $"data-{command}-markup");
                foreach (Match match in QuotedStringPattern().Matches(tail))
                {
                    if (protectedTailRanges.Any(range => RangesOverlap(range.start, range.end - range.start, match.Index, match.Length))
                        || IsQuotedStringProtectedCodeArgument(tail, match.Index, match.Length))
                    {
                        continue;
                    }

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

                    if (ContainsScriptSyntaxToken(quotedValue)
                        && !ShouldKeepWholeCodeMixedText(quotedValue)
                        && TryAddCodeMixedTextSpans($"data-{command}-fragment", quotedValue, tailOffset + match.Groups["content"].Index))
                    {
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
                    if (ContainsScriptSyntaxToken(range.value)
                        && TryAddCodeMixedTextSpans($"{type}-fragment", range.value, relativeStart + range.start))
                    {
                        continue;
                    }

                    AddSegment(type, relativeStart + range.start, range.value);
                }
            }

            List<(int start, int end)> CollectProtectedQuotedRanges(string sourceLine, string htmlSegmentType)
            {
                var ranges = new List<(int start, int end)>();
                foreach (Match match in RawHtmlStringPattern().Matches(sourceLine))
                {
                    if (!match.Success || match.Length == 0)
                    {
                        continue;
                    }

                    ranges.Add((match.Index, match.Index + match.Length));

                    var contentGroup = match.Groups["content"];
                    var markup = contentGroup.Value;
                    if (!TextHeuristics.ContainsTranslatableText(markup))
                    {
                        continue;
                    }

                    ExtractHtmlSegments(htmlSegmentType, markup, contentGroup.Index);
                }

                foreach (var rawString in EnumerateRawStringsWithScriptExpressions(sourceLine))
                {
                    if (ranges.Any(range => RangesOverlap(range.start, range.end - range.start, rawString.start, rawString.end - rawString.start)))
                    {
                        continue;
                    }

                    ranges.Add((rawString.start, rawString.end));

                    var value = sourceLine.Substring(rawString.contentStart, rawString.contentLength);
                    if (!TextHeuristics.ContainsTranslatableText(value))
                    {
                        continue;
                    }

                    if (LooksLikeHtml(value))
                    {
                        ExtractHtmlSegments(htmlSegmentType, value, rawString.contentStart);
                        continue;
                    }

                    if (ContainsScriptSyntaxToken(value)
                        && !ShouldKeepWholeCodeMixedText(value)
                        && TryAddCodeMixedTextSpans("raw-string-fragment", value, rawString.contentStart))
                    {
                        continue;
                    }

                    AddSegment("raw-string", rawString.contentStart, value);
                }

                return ranges;
            }

            void AddSegment(string type, int relativeStart, string value)
            {
                if (!TextHeuristics.ContainsTranslatableText(value)
                    || TextHeuristics.LooksLikeCodeOnly(value)
                    || LooksLikeErbSymbolExpression(value))
                {
                    return;
                }

                var absoluteStart = absoluteOffset + relativeStart;
                if (segments.Any(segment =>
                        segment.AbsoluteStart == absoluteStart
                        && segment.Length == value.Length
                        && string.Equals(segment.OriginalText, value, StringComparison.Ordinal)))
                {
                    return;
                }

                segments.Add(new TextSegment
                {
                    SegmentId = $"{documentId}:{segmentIndex++}",
                    DocumentId = documentId,
                    SegmentType = type,
                    AbsoluteStart = absoluteStart,
                    Length = value.Length,
                    LineNumber = lineIndex + 1,
                    OriginalText = value,
                });
            }

            bool TryAddCodeMixedTextSpans(string type, string value, int relativeStart)
            {
                var spans = ExtractCodeMixedTextSpans(value, relativeStart).ToList();
                foreach (var span in spans)
                {
                    AddSegment(type, span.relativeStart, span.value);
                }

                return spans.Count > 0;
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

                        if (ContainsScriptSyntaxToken(left)
                            && !ShouldKeepWholeCodeMixedText(left)
                            && TryAddCodeMixedTextSpans("inline-conditional-left-fragment", left, lineOffset + ternary.Groups["inner"].Index + questionIndex + 1 + leftRaw.IndexOf(left, StringComparison.Ordinal)))
                        {
                            // Code-mixed condition branches should share stable label translations.
                        }
                        else if (TextHeuristics.ContainsTranslatableText(left))
                        {
                            var relative = ternary.Groups["inner"].Index + questionIndex + 1 + leftRaw.IndexOf(left, StringComparison.Ordinal);
                            AddSegment("inline-conditional-left", lineOffset + relative, left);
                        }

                        if (ContainsScriptSyntaxToken(right)
                            && !ShouldKeepWholeCodeMixedText(right)
                            && TryAddCodeMixedTextSpans("inline-conditional-right-fragment", right, lineOffset + ternary.Groups["inner"].Index + hashIndex + 1 + rightRaw.IndexOf(right, StringComparison.Ordinal)))
                        {
                            // Code-mixed condition branches should share stable label translations.
                        }
                        else if (TextHeuristics.ContainsTranslatableText(right))
                        {
                            var relative = ternary.Groups["inner"].Index + hashIndex + 1 + rightRaw.IndexOf(right, StringComparison.Ordinal);
                            AddSegment("inline-conditional-right", lineOffset + relative, right);
                        }
                    }

                    return;
                }

                if (ContainsScriptSyntaxToken(tailValue)
                    && !ShouldKeepWholeCodeMixedText(tailValue)
                    && TryAddCodeMixedTextSpans("print-tail-fragment", tailValue, lineOffset))
                {
                    return;
                }

                if (TextHeuristics.ContainsTranslatableText(tailValue))
                {
                    AddSegment("print-tail", lineOffset, tailValue);
                }
            }
        }

        return segments;

        static bool ShouldContinueDirective(string sourceLine, int endingParenDepth)
        {
            if (endingParenDepth > 0)
            {
                return true;
            }

            var trimmed = sourceLine.TrimEnd();
            return trimmed.EndsWith('=') || trimmed.EndsWith(',');
        }

        static (List<(int relativeStart, string value)> quotedStrings, int endingParenDepth) ScanDirectiveQuotedStrings(
            string sourceLine,
            int startingParenDepth)
        {
            var quotedStrings = new List<(int relativeStart, string value)>();
            var parenDepth = startingParenDepth;
            var inQuote = false;
            var contentStart = -1;

            for (var index = 0; index < sourceLine.Length; index++)
            {
                var ch = sourceLine[index];
                if (ch == '"' && (index == 0 || sourceLine[index - 1] != '\\'))
                {
                    if (inQuote)
                    {
                        if (parenDepth == 0 && contentStart >= 0 && index >= contentStart)
                        {
                            quotedStrings.Add((contentStart, sourceLine[contentStart..index]));
                        }

                        inQuote = false;
                        contentStart = -1;
                    }
                    else
                    {
                        inQuote = true;
                        contentStart = index + 1;
                    }

                    continue;
                }

                if (inQuote)
                {
                    continue;
                }

                if (ch == '(')
                {
                    parenDepth++;
                }
                else if (ch == ')' && parenDepth > 0)
                {
                    parenDepth--;
                }
            }

            return (quotedStrings, parenDepth);
        }
    }

    private static bool LooksLikeHtml(string value)
    {
        return HtmlTagPattern().IsMatch(value);
    }

    private static bool LooksLikeImageResourceContext(string sourceLine)
    {
        if (string.IsNullOrWhiteSpace(sourceLine))
        {
            return false;
        }

        return sourceLine.Contains("EXIST画像FILE(", StringComparison.Ordinal)
            || sourceLine.Contains("ENUMFILES(", StringComparison.OrdinalIgnoreCase)
            || sourceLine.Contains("顔グラ表示用文字列関数(", StringComparison.Ordinal)
            || sourceLine.Contains("任意顔グラ表示用文字列関数(", StringComparison.Ordinal)
            || sourceLine.Contains("GCREATE_拡張子(", StringComparison.Ordinal)
            || sourceLine.Contains("GCREATE_拡張子F(", StringComparison.Ordinal)
            || sourceLine.Contains("IMAGEPATH_", StringComparison.Ordinal)
            || sourceLine.Contains(":画像フォルダ%/", StringComparison.Ordinal)
            || sourceLine.Contains("\"ダンジョン用_", StringComparison.Ordinal)
            || sourceLine.Contains("/ダンジョン用_", StringComparison.Ordinal);
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

    private bool LooksLikeBareAssignmentText(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !TextHeuristics.ContainsTranslatableText(value)
            || TextHeuristics.LooksLikeCodeOnly(value)
            || LooksLikeErbSymbolExpression(value)
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

    private List<(int relativeStart, string value)> ExtractAssignmentFragments(string expression, int expressionLineStart)
    {
        var fragments = new List<(int relativeStart, string value)>();
        CollectAssignmentFragments(expression, expressionLineStart, fragments);
        return fragments
            .Distinct()
            .ToList();
    }

    private void CollectAssignmentFragments(
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

            return;
        }

        if (ContainsScriptSyntaxToken(trimmed) && ShouldKeepWholeCodeMixedText(trimmed))
        {
            fragments.Add((trimmedStart, trimmed));
            return;
        }

        foreach (var span in ExtractCodeMixedTextSpans(trimmed, trimmedStart))
        {
            fragments.Add(span);
        }
    }

    private IEnumerable<(int relativeStart, string value)> ExtractCodeMixedTextSpans(string value, int relativeStart)
    {
        var protectedRanges = CollectScriptSyntaxRanges(value);
        var spans = new List<(int relativeStart, string value)>();
        var index = 0;

        while (index < value.Length)
        {
            if (IsInsideRange(index, protectedRanges) || !IsTextSpanCharacter(value[index]))
            {
                index++;
                continue;
            }

            var start = index;
            while (index < value.Length && !IsInsideRange(index, protectedRanges) && IsTextSpanCharacter(value[index]))
            {
                index++;
            }

            AddMeaningfulSpan(value, relativeStart, start, index, spans);
        }

        return spans
            .Distinct()
            .ToList();
    }

    private void AddMeaningfulSpan(
        string source,
        int relativeStart,
        int start,
        int end,
        ICollection<(int relativeStart, string value)> spans)
    {
        var spanStart = start;
        var spanEnd = end;
        while (spanStart < spanEnd && IsTrimmableSpanEdge(source[spanStart]))
        {
            spanStart++;
        }

        while (spanEnd > spanStart && IsTrimmableSpanEdge(source[spanEnd - 1]))
        {
            spanEnd--;
        }

        if (spanEnd <= spanStart)
        {
            return;
        }

        if (CanTrimLeadingJapaneseParticle(source, spanStart, spanEnd))
        {
            spanStart++;
        }

        var span = source[spanStart..spanEnd];
        if (!IsMeaningfulTextSpan(span))
        {
            return;
        }

        spans.Add((relativeStart + spanStart, span));
    }

    private bool ContainsScriptSyntaxToken(string value)
    {
        return _scriptSyntaxTokenPattern.IsMatch(value);
    }

    private bool ShouldKeepWholeCodeMixedText(string value)
    {
        if (!ContainsScriptSyntaxToken(value))
        {
            return false;
        }

        var visibleText = _scriptSyntaxTokenPattern.Replace(value, string.Empty).Trim();
        if (!TextHeuristics.ContainsTranslatableText(visibleText))
        {
            return false;
        }

        return CodeMixedSentenceMarkerPattern().IsMatch(visibleText)
            || CodeMixedPredicateEndingPattern().IsMatch(visibleText);
    }

    private List<(int start, int end)> CollectScriptSyntaxRanges(string value)
    {
        var ranges = new List<(int start, int end)>();
        foreach (Match match in _scriptSyntaxTokenPattern.Matches(value))
        {
            if (match.Length == 0)
            {
                continue;
            }

            var range = (match.Index, match.Index + match.Length);
            if (ranges.Any(existing => RangesOverlap(existing.start, existing.end - existing.start, range.Item1, range.Item2 - range.Item1)))
            {
                continue;
            }

            ranges.Add(range);
        }

        return ranges
            .OrderBy(static range => range.start)
            .ToList();
    }

    private static bool IsInsideRange(int index, IReadOnlyList<(int start, int end)> ranges)
    {
        return ranges.Any(range => index >= range.start && index < range.end);
    }

    private static bool IsTextSpanCharacter(char ch)
    {
        return IsJapaneseTextCharacter(ch)
            || ch is >= 'A' and <= 'Z'
            || ch is >= 'a' and <= 'z'
            || ch is >= '0' and <= '9'
            || ch is >= '\uAC00' and <= '\uD7A3'
            || ch is 'ー' or '々' or '〆' or 'ヶ';
    }

    private static bool IsJapaneseTextCharacter(char ch)
    {
        return ch is >= '\u3040' and <= '\u30FF'
            or >= '\u31F0' and <= '\u31FF'
            or >= '\u4E00' and <= '\u9FFF';
    }

    private bool IsMeaningfulTextSpan(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length > 0
            && trimmed.Any(IsJapaneseTextCharacter)
            && !IsStandaloneJapaneseParticle(trimmed)
            && !TextHeuristics.LooksLikeCodeOnly(trimmed)
            && !LooksLikeErbSymbolExpression(trimmed)
            && !TextHeuristics.IsNumericLike(trimmed);
    }

    private static bool IsTrimmableSpanEdge(char ch)
    {
        return char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch);
    }

    private static bool CanTrimLeadingJapaneseParticle(string source, int start, int end)
    {
        return end - start > 1
            && IsStandaloneJapaneseParticle(source[start].ToString())
            && !IsHiragana(source[start + 1]);
    }

    private static bool IsStandaloneJapaneseParticle(string value)
    {
        return value is "の" or "は" or "を" or "が" or "に" or "へ" or "と" or "で" or "も" or "や" or "から" or "まで";
    }

    private static bool IsHiragana(char ch)
    {
        return ch is >= '\u3040' and <= '\u309F';
    }

    private static bool RangesOverlap(int leftStart, int leftLength, int rightStart, int rightLength)
    {
        var leftEnd = leftStart + leftLength;
        var rightEnd = rightStart + rightLength;
        return leftStart < rightEnd && rightStart < leftEnd;
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

    private bool LooksLikeErbSymbolExpression(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var sanitized = _scriptSyntaxTokenPattern.Replace(value, string.Empty);
        if (string.Equals(sanitized, value, StringComparison.Ordinal))
        {
            return false;
        }

        sanitized = Regex.Replace(sanitized, @"(%[^%\r\n]+%|\{[^{}\r\n]+\}|<[^\r\n<>]+>)", string.Empty, RegexOptions.CultureInvariant);
        sanitized = Regex.Replace(sanitized, @"[A-Za-z_][A-Za-z0-9_]*", string.Empty, RegexOptions.CultureInvariant);
        sanitized = new string(sanitized.Where(static character =>
                !char.IsWhiteSpace(character)
                && !char.IsDigit(character)
                && !char.IsPunctuation(character)
                && !char.IsSymbol(character))
            .ToArray());
        return sanitized.Length == 0;
    }

    private static Regex BuildScriptSyntaxTokenPattern(SymbolNamespaceRegistry namespaceRegistry)
    {
        var namespacePattern = BuildNamespaceAlternation(namespaceRegistry);
        var headPattern = string.IsNullOrWhiteSpace(namespacePattern)
            ? string.Join("|", ReservedScriptVariables)
            : $"{namespacePattern}|{string.Join("|", ReservedScriptVariables)}";
        var pattern =
            $@"(%[^%\r\n]+%|\{{[^{{}}\r\n]+\}}|<[^\r\n<>]+>|(?<![\p{{L}}\p{{N}}_])(?:{headPattern}):(?:\{{[^{{}}\r\n]+\}}|[\p{{L}}_][\p{{L}}\p{{N}}_]*:[^\s,\)\(\]\[\+\-\*\/<>=!&|%""']+|[^\s,\)\(\]\[\+\-\*\/<>=!&|%""']+)|(?<![\p{{L}}\p{{N}}_])[\p{{L}}_][\p{{L}}\p{{N}}_]*\s*\([^()\r\n]*\)|(?<![\p{{L}}\p{{N}}_])[\p{{L}}_][\p{{L}}\p{{N}}_]*(?::[^\s,\)\(\]\[\+\-\*\/<>=!&|%""']+)+)";
        return new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private static string BuildNamespaceAlternation(SymbolNamespaceRegistry namespaceRegistry)
    {
        return string.Join("|", namespaceRegistry.OrderedNamespaces.Select(Regex.Escape));
    }

    private static bool IsQuotedStringProtectedCodeArgument(string sourceLine, int quoteStart, int quoteLength)
    {
        return IsRangeInsidePercentExpression(sourceLine, quoteStart, quoteLength)
            || IsRangeInsideFunctionArgument(sourceLine, quoteStart, quoteLength, ProtectedCodeArgumentFunctionNames)
            || IsRangeInsideFunctionArgument(sourceLine, quoteStart, quoteLength, PaletteLookupFunctionNames)
            || IsRangeInsideCommandArgument(sourceLine, quoteStart, quoteLength, ["LOADTEXT", "SAVETEXT"])
            || IsQuotedComparisonLiteral(sourceLine, quoteStart, quoteLength);
    }

    private static bool IsCaseLabelLine(string sourceLine)
    {
        var trimmed = sourceLine.TrimStart();
        return trimmed.Length > "CASE".Length
            && trimmed.StartsWith("CASE", StringComparison.OrdinalIgnoreCase)
            && char.IsWhiteSpace(trimmed["CASE".Length]);
    }

    private static bool TryReadFunctionName(string trimmedLine, out string functionName)
    {
        functionName = string.Empty;
        if (trimmedLine.Length < 2 || trimmedLine[0] != '@')
        {
            return false;
        }

        var index = 1;
        if (index >= trimmedLine.Length || (!char.IsLetter(trimmedLine[index]) && trimmedLine[index] != '_'))
        {
            return false;
        }

        var start = index;
        while (index < trimmedLine.Length && (char.IsLetterOrDigit(trimmedLine[index]) || trimmedLine[index] == '_'))
        {
            index++;
        }

        functionName = trimmedLine[start..index];
        return true;
    }

    private static bool IsPaletteLookupFunction(string functionName)
    {
        return PaletteLookupFunctionNames.Any(name => string.Equals(name, functionName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRangeInsidePercentExpression(string value, int start, int length)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '%')
            {
                continue;
            }

            var end = value.IndexOf('%', index + 1);
            if (end < 0)
            {
                return false;
            }

            if (RangeContains(index, end - index + 1, start, length))
            {
                return true;
            }

            index = end;
        }

        return false;
    }

    private static bool IsRangeInsideCommandArgument(string value, int start, int length, IReadOnlyCollection<string> commandNames)
    {
        foreach (var commandName in commandNames)
        {
            var commandIndex = value.IndexOf(commandName, StringComparison.OrdinalIgnoreCase);
            if (commandIndex < 0)
            {
                continue;
            }

            var prefix = value[..commandIndex].TrimStart();
            if (prefix.Length > 0 && !prefix.StartsWith(';'))
            {
                continue;
            }

            var afterCommand = commandIndex + commandName.Length;
            if (afterCommand < value.Length && (char.IsLetterOrDigit(value[afterCommand]) || value[afterCommand] == '_'))
            {
                continue;
            }

            if (start >= afterCommand && start + length <= value.Length)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRangeInsideFunctionArgument(string value, int start, int length, IReadOnlyCollection<string> functionNames)
    {
        foreach (var functionName in functionNames)
        {
            var searchIndex = 0;
            while ((searchIndex = value.IndexOf(functionName, searchIndex, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var parenIndex = value.IndexOf('(', searchIndex + functionName.Length);
                if (parenIndex < 0)
                {
                    break;
                }

                if (value[(searchIndex + functionName.Length)..parenIndex].Any(static ch => !char.IsWhiteSpace(ch)))
                {
                    searchIndex += functionName.Length;
                    continue;
                }

                var closeIndex = FindMatchingParen(value, parenIndex);
                if (closeIndex > parenIndex
                    && RangeContains(parenIndex + 1, closeIndex - parenIndex - 1, start, length))
                {
                    return true;
                }

                searchIndex = parenIndex + 1;
            }
        }

        return false;
    }

    private static bool IsQuotedComparisonLiteral(string value, int quoteStart, int quoteLength)
    {
        var prefix = value[..quoteStart];
        var suffixStart = Math.Min(value.Length, quoteStart + quoteLength);
        var suffix = value[suffixStart..];
        return EndsWithComparisonOperator(prefix) || StartsWithComparisonOperator(suffix);
    }

    private static bool EndsWithComparisonOperator(string value)
    {
        var trimmed = value.TrimEnd();
        return trimmed.EndsWith("==", StringComparison.Ordinal)
            || trimmed.EndsWith("!=", StringComparison.Ordinal)
            || trimmed.EndsWith("<>", StringComparison.Ordinal)
            || trimmed.EndsWith(">=", StringComparison.Ordinal)
            || trimmed.EndsWith("<=", StringComparison.Ordinal);
    }

    private static bool StartsWithComparisonOperator(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.StartsWith("==", StringComparison.Ordinal)
            || trimmed.StartsWith("!=", StringComparison.Ordinal)
            || trimmed.StartsWith("<>", StringComparison.Ordinal)
            || trimmed.StartsWith(">=", StringComparison.Ordinal)
            || trimmed.StartsWith("<=", StringComparison.Ordinal);
    }

    private static int FindMatchingParen(string value, int openParenIndex)
    {
        var depth = 0;
        var inQuote = false;
        for (var index = openParenIndex; index < value.Length; index++)
        {
            var ch = value[index];
            if (ch == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (inQuote)
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
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static bool RangeContains(int outerStart, int outerLength, int innerStart, int innerLength)
    {
        var outerEnd = outerStart + outerLength;
        var innerEnd = innerStart + innerLength;
        return innerStart >= outerStart && innerEnd <= outerEnd;
    }

    private static IEnumerable<(int start, int end, int contentStart, int contentLength)> EnumerateRawStringsWithScriptExpressions(string sourceLine)
    {
        for (var index = 0; index < sourceLine.Length - 1; index++)
        {
            if (sourceLine[index] != '@' || sourceLine[index + 1] != '"')
            {
                continue;
            }

            var contentStart = index + 2;
            var insidePercentExpression = false;
            var braceDepth = 0;
            var containsScriptExpression = false;

            for (var scan = contentStart; scan < sourceLine.Length; scan++)
            {
                var ch = sourceLine[scan];
                if (ch == '%')
                {
                    insidePercentExpression = !insidePercentExpression;
                    containsScriptExpression = true;
                    continue;
                }

                if (!insidePercentExpression)
                {
                    if (ch == '{')
                    {
                        braceDepth++;
                        containsScriptExpression = true;
                        continue;
                    }

                    if (ch == '}' && braceDepth > 0)
                    {
                        braceDepth--;
                        continue;
                    }
                }

                if (ch != '"' || insidePercentExpression || braceDepth > 0)
                {
                    continue;
                }

                if (scan + 1 < sourceLine.Length && sourceLine[scan + 1] == '"')
                {
                    scan++;
                    continue;
                }

                if (containsScriptExpression)
                {
                    yield return (index, scan + 1, contentStart, scan - contentStart);
                }

                index = scan;
                break;
            }
        }
    }

    [GeneratedRegex(@"@?""(?<content>(?:[^""]|"""")*)""", RegexOptions.Compiled)]
    private static partial Regex QuotedStringPattern();

    [GeneratedRegex(@"@""(?<content><.*>)""(?=\s*(?:,|\+|$))", RegexOptions.Compiled)]
    private static partial Regex RawHtmlStringPattern();

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

    [GeneratedRegex(@"<[/!\p{L}A-Za-z][^>]*>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagPattern();

    [GeneratedRegex(@"^\s*(?<var>[\p{L}_][\p{L}\p{N}_]*)\s*=\s*(?<tail>.+?)\s*$", RegexOptions.Compiled)]
    private static partial Regex AssignmentPattern();

    [GeneratedRegex(@"[(){}\[\]=<>!&|+\-*/%:,\\@#?]", RegexOptions.Compiled)]
    private static partial Regex BareAssignmentCodeSyntaxPattern();

    [GeneratedRegex(@"^\s*#DIMS?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex DimDirectivePattern();

    [GeneratedRegex(@"^\s*PRINT_IMG\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex PrintImageCommandPattern();

    [GeneratedRegex(@"キャラ検索\s*\(\s*""(?<name>(?:[^""]|"""")*)""\s*\)", RegexOptions.Compiled)]
    private static partial Regex CharacterSearchArgumentPattern();

    [GeneratedRegex(@"[「」『』（）()、。！？!?…‥]", RegexOptions.Compiled)]
    private static partial Regex CodeMixedSentenceMarkerPattern();

    [GeneratedRegex(@"[\u3040-\u309F].*(?:した|している|してる|だった|です|ます|ない|いる|ある|った|いた|えた|れた|た)$", RegexOptions.Compiled)]
    private static partial Regex CodeMixedPredicateEndingPattern();
}
