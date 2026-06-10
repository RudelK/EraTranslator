using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class ErbIdentifierExtractor
{
    private static readonly HashSet<string> ReservedIdentifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "ABS",
        "ARGS",
        "BARCOLORSET",
        "BARCOLORSET_HTML",
        "CALL",
        "CALLF",
        "CALLFORM",
        "CALLFORMF",
        "CALLNAME",
        "CASE",
        "CFLAG",
        "CONTINUE",
        "DATA",
        "DATAFORM",
        "DATALIST",
        "DIM",
        "DIMS",
        "ELSE",
        "ELSEIF",
        "ENDIF",
        "ENDDATA",
        "ENDFUNCTION",
        "EXIST画像FILE",
        "FOR",
        "GETCONFIG",
        "GETNUM",
        "IF",
        "INPUT",
        "INPUTS",
        "LOCAL",
        "LOCALS",
        "LOADTEXT",
        "MASTER",
        "NEXT",
        "PLAYER",
        "PRINT",
        "PRINTFORM",
        "PRINTFORML",
        "PRINTFORMW",
        "PRINTL",
        "PRINTW",
        "RESULT",
        "RESULTS",
        "RETURN",
        "RETURNF",
        "SAVETEXT",
        "SELECTCASE",
        "SETCOLOR",
        "SIF",
        "TARGET",
        "THROW",
        "TO",
        "TRYCALLFORM",
        "VARSIZE",
        "WHILE",
        "カラーパレット",
        "カラーパレット_HTML",
        "カラーパレット_透明度込",
    };

    private static readonly HashSet<string> DimModifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "CHARADATA",
        "CONST",
        "DYNAMIC",
        "GLOBAL",
        "REF",
        "SAVEDATA",
        "STATIC",
    };

    private readonly SymbolNamespaceRegistry _namespaceRegistry;

    public ErbIdentifierExtractor()
        : this(SymbolNamespaceRegistry.Default)
    {
    }

    public ErbIdentifierExtractor(SymbolNamespaceRegistry namespaceRegistry)
    {
        _namespaceRegistry = namespaceRegistry;
    }

    public List<ErbIdentifierOccurrence> Extract(string documentId, string content)
    {
        var results = new List<ErbIdentifierOccurrence>();
        var seen = new HashSet<(ErbIdentifierKind Kind, int Start, int Length)>();
        var lines = content.Split('\n');
        var absoluteOffset = 0;

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            var sourceLine = line.TrimEnd('\r');
            var lineNumber = lineIndex + 1;
            if (ErbSyntaxCatalog.TryNormalizeSpecialCommentLine(sourceLine, out var specialCommentCodeLine))
            {
                sourceLine = specialCommentCodeLine;
            }

            var logicalLineLength = line.Length + 1;
            while (ErbSyntaxCatalog.HasOpenBraceContinuation(sourceLine) && lineIndex + 1 < lines.Length)
            {
                lineIndex++;
                var continuationLine = lines[lineIndex];
                var continuationSourceLine = continuationLine.TrimEnd('\r');
                continuationSourceLine = ErbSyntaxCatalog.NormalizeContinuationLine(continuationSourceLine);

                sourceLine += "\n" + continuationSourceLine;
                logicalLineLength += continuationLine.Length + 1;
            }

            var trimmed = sourceLine.TrimStart();
            if (trimmed.StartsWith(';'))
            {
                absoluteOffset += logicalLineLength;
                continue;
            }

            var codeLineLength = FindCodeLineLength(sourceLine);
            var codeLine = sourceLine[..codeLineLength];
            var protectedRanges = CollectQuotedRanges(codeLine);
            AddFunctionDefinition(documentId, codeLine, absoluteOffset, lineNumber, protectedRanges, results, seen);
            AddFunctionDefinitionArguments(documentId, codeLine, absoluteOffset, lineNumber, protectedRanges, results, seen);
            AddDimDeclaration(documentId, codeLine, absoluteOffset, lineNumber, protectedRanges, results, seen);
            AddAssignmentTarget(documentId, codeLine, absoluteOffset, lineNumber, protectedRanges, results, seen);
            AddCallCommandTarget(documentId, codeLine, absoluteOffset, lineNumber, protectedRanges, results, seen);
            if (!IsPrintTextLine(codeLine))
            {
                AddFunctionCallTokens(documentId, codeLine, absoluteOffset, lineNumber, protectedRanges, results, seen);
                AddVariableReferenceTokensInCodeRanges(documentId, codeLine, absoluteOffset, lineNumber, protectedRanges, results, seen);
            }

            foreach (var percentRange in EnumeratePercentExpressionRanges(codeLine))
            {
                var nestedProtectedRanges = CollectQuotedRanges(codeLine[percentRange.start..(percentRange.start + percentRange.length)])
                    .Select(range => (start: range.start + percentRange.start, end: range.end + percentRange.start))
                    .ToList();
                AddFunctionCallTokens(documentId, codeLine, absoluteOffset, lineNumber, nestedProtectedRanges, results, seen, percentRange);
                AddVariableReferenceTokens(documentId, codeLine, absoluteOffset, lineNumber, nestedProtectedRanges, results, seen, percentRange);
            }

            foreach (var braceRange in EnumerateBraceExpressionRanges(codeLine))
            {
                AddVariableReferenceTokens(documentId, codeLine, absoluteOffset, lineNumber, protectedRanges, results, seen, braceRange);
            }

            absoluteOffset += logicalLineLength;
        }

        return results
            .OrderBy(occurrence => occurrence.AbsoluteStart)
            .ThenBy(occurrence => occurrence.Kind)
            .ToList();
    }

    private void AddFunctionDefinition(
        string documentId,
        string line,
        int absoluteOffset,
        int lineNumber,
        IReadOnlyList<(int start, int end)> protectedRanges,
        List<ErbIdentifierOccurrence> results,
        HashSet<(ErbIdentifierKind Kind, int Start, int Length)> seen)
    {
        var trimmedStart = 0;
        while (trimmedStart < line.Length && char.IsWhiteSpace(line[trimmedStart]))
        {
            trimmedStart++;
        }

        if (trimmedStart >= line.Length || line[trimmedStart] != '@')
        {
            return;
        }

        var tokenStart = trimmedStart + 1;
        var tokenEnd = ReadIdentifierEnd(line, tokenStart);
        AddOccurrence(
            documentId,
            ErbIdentifierKind.Function,
            ErbIdentifierRole.Definition,
            line,
            tokenStart,
            tokenEnd,
            absoluteOffset,
            lineNumber,
            protectedRanges,
            results,
            seen);
    }

    private void AddDimDeclaration(
        string documentId,
        string line,
        int absoluteOffset,
        int lineNumber,
        IReadOnlyList<(int start, int end)> protectedRanges,
        List<ErbIdentifierOccurrence> results,
        HashSet<(ErbIdentifierKind Kind, int Start, int Length)> seen)
    {
        var commandStart = FirstNonWhitespaceIndex(line);
        if (commandStart < 0 || line[commandStart] != '#')
        {
            return;
        }

        var commandEnd = ReadIdentifierEnd(line, commandStart + 1);
        var command = line[(commandStart + 1)..commandEnd];
        if (!string.Equals(command, "DIM", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(command, "DIMS", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var tokenStart = commandEnd;
        while (tokenStart < line.Length && (char.IsWhiteSpace(line[tokenStart]) || line[tokenStart] == ','))
        {
            tokenStart++;
        }

        while (tokenStart < line.Length)
        {
            var modifierEnd = ReadIdentifierEnd(line, tokenStart);
            if (modifierEnd <= tokenStart || !DimModifiers.Contains(line[tokenStart..modifierEnd]))
            {
                break;
            }

            tokenStart = modifierEnd;
            while (tokenStart < line.Length && (char.IsWhiteSpace(line[tokenStart]) || line[tokenStart] == ','))
            {
                tokenStart++;
            }
        }

        var tokenEnd = ReadIdentifierEnd(line, tokenStart);
        AddOccurrence(
            documentId,
            ErbIdentifierKind.Variable,
            ErbIdentifierRole.Declaration,
            line,
            tokenStart,
            tokenEnd,
            absoluteOffset,
            lineNumber,
            protectedRanges,
            results,
            seen);

        var assignmentIndex = FindAssignmentOperator(line, protectedRanges);
        if (assignmentIndex < 0)
        {
            return;
        }

        var referenceStart = assignmentIndex + 1;
        while (referenceStart < line.Length && char.IsWhiteSpace(line[referenceStart]))
        {
            referenceStart++;
        }

        AddVariableReferenceTokens(
            documentId,
            line,
            absoluteOffset,
            lineNumber,
            protectedRanges,
            results,
            seen,
            (referenceStart, line.Length - referenceStart));
    }

    private void AddFunctionDefinitionArguments(
        string documentId,
        string line,
        int absoluteOffset,
        int lineNumber,
        IReadOnlyList<(int start, int end)> protectedRanges,
        List<ErbIdentifierOccurrence> results,
        HashSet<(ErbIdentifierKind Kind, int Start, int Length)> seen)
    {
        var functionStart = FirstNonWhitespaceIndex(line);
        if (functionStart < 0 || line[functionStart] != '@')
        {
            return;
        }

        var functionEnd = ReadIdentifierEnd(line, functionStart + 1);
        var cursor = functionEnd;
        while (cursor < line.Length && char.IsWhiteSpace(line[cursor]))
        {
            cursor++;
        }

        if (cursor >= line.Length)
        {
            return;
        }

        if (line[cursor] == '(')
        {
            var closeIndex = FindMatchingParen(line, cursor, protectedRanges);
            var argumentStart = cursor + 1;
            var argumentEnd = closeIndex > argumentStart ? closeIndex : line.Length;
            AddVariableReferenceTokens(
                documentId,
                line,
                absoluteOffset,
                lineNumber,
                protectedRanges,
                results,
                seen,
                (argumentStart, argumentEnd - argumentStart),
                ErbIdentifierRole.Declaration);
            return;
        }

        if (line[cursor] != ',')
        {
            return;
        }

        var commaArgumentStart = cursor + 1;
        AddVariableReferenceTokens(
            documentId,
            line,
            absoluteOffset,
            lineNumber,
            protectedRanges,
            results,
            seen,
            (commaArgumentStart, line.Length - commaArgumentStart),
            ErbIdentifierRole.Declaration);
    }

    private void AddAssignmentTarget(
        string documentId,
        string line,
        int absoluteOffset,
        int lineNumber,
        IReadOnlyList<(int start, int end)> protectedRanges,
        List<ErbIdentifierOccurrence> results,
        HashSet<(ErbIdentifierKind Kind, int Start, int Length)> seen)
    {
        var assignmentIndex = FindAssignmentOperator(line, protectedRanges);
        if (assignmentIndex < 0)
        {
            return;
        }

        var tokenEnd = assignmentIndex > 0 && line[assignmentIndex - 1] == '\''
            ? assignmentIndex - 1
            : assignmentIndex;
        while (tokenEnd > 0 && char.IsWhiteSpace(line[tokenEnd - 1]))
        {
            tokenEnd--;
        }

        var tokenStart = tokenEnd - 1;
        while (tokenStart >= 0 && IsIdentifierCharacter(line[tokenStart]))
        {
            tokenStart--;
        }

        tokenStart++;
        AddOccurrence(
            documentId,
            ErbIdentifierKind.Variable,
            ErbIdentifierRole.Assignment,
            line,
            tokenStart,
            tokenEnd,
            absoluteOffset,
            lineNumber,
            protectedRanges,
            results,
            seen);
    }

    private void AddCallCommandTarget(
        string documentId,
        string line,
        int absoluteOffset,
        int lineNumber,
        IReadOnlyList<(int start, int end)> protectedRanges,
        List<ErbIdentifierOccurrence> results,
        HashSet<(ErbIdentifierKind Kind, int Start, int Length)> seen)
    {
        var commandStart = FirstNonWhitespaceIndex(line);
        if (commandStart < 0 || IsProtected(commandStart, protectedRanges))
        {
            return;
        }

        var commandEnd = ReadIdentifierEnd(line, commandStart);
        var command = line[commandStart..commandEnd];
        if (!IsCallCommand(command))
        {
            return;
        }

        var tokenStart = commandEnd;
        while (tokenStart < line.Length && (char.IsWhiteSpace(line[tokenStart]) || line[tokenStart] == ','))
        {
            tokenStart++;
        }

        var tokenEnd = ReadIdentifierEnd(line, tokenStart);
        AddOccurrence(
            documentId,
            ErbIdentifierKind.Function,
            ErbIdentifierRole.Call,
            line,
            tokenStart,
            tokenEnd,
            absoluteOffset,
            lineNumber,
            protectedRanges,
            results,
            seen);
    }

    private void AddFunctionCallTokens(
        string documentId,
        string line,
        int absoluteOffset,
        int lineNumber,
        IReadOnlyList<(int start, int end)> protectedRanges,
        List<ErbIdentifierOccurrence> results,
        HashSet<(ErbIdentifierKind Kind, int Start, int Length)> seen,
        (int start, int length)? scanRange = null)
    {
        var start = scanRange?.start ?? 0;
        var end = scanRange is null ? line.Length : scanRange.Value.start + scanRange.Value.length;
        for (var index = start; index < end; index++)
        {
            if (line[index] != '(')
            {
                continue;
            }

            var tokenEnd = index;
            while (tokenEnd > start && char.IsWhiteSpace(line[tokenEnd - 1]))
            {
                tokenEnd--;
            }

            var tokenStart = tokenEnd - 1;
            while (tokenStart >= start && IsIdentifierCharacter(line[tokenStart]))
            {
                tokenStart--;
            }

            tokenStart++;
            AddOccurrence(
                documentId,
                ErbIdentifierKind.Function,
                ErbIdentifierRole.Call,
                line,
                tokenStart,
                tokenEnd,
                absoluteOffset,
                lineNumber,
                protectedRanges,
                results,
                seen);
        }
    }

    private void AddVariableReferenceTokens(
        string documentId,
        string line,
        int absoluteOffset,
        int lineNumber,
        IReadOnlyList<(int start, int end)> protectedRanges,
        List<ErbIdentifierOccurrence> results,
        HashSet<(ErbIdentifierKind Kind, int Start, int Length)> seen,
        (int start, int length)? scanRange = null,
        ErbIdentifierRole role = ErbIdentifierRole.Reference)
    {
        var start = scanRange?.start ?? 0;
        var end = scanRange is null ? line.Length : scanRange.Value.start + scanRange.Value.length;
        var index = start;
        while (index < end)
        {
            if (!IsIdentifierCharacter(line[index]))
            {
                index++;
                continue;
            }

            var tokenStart = index;
            var tokenEnd = ReadIdentifierEnd(line, tokenStart);
            AddOccurrence(
                documentId,
                ErbIdentifierKind.Variable,
                role,
                line,
                tokenStart,
                Math.Min(tokenEnd, end),
                absoluteOffset,
                lineNumber,
                protectedRanges,
                results,
                seen);
            index = tokenEnd;
        }
    }

    private void AddVariableReferenceTokensInCodeRanges(
        string documentId,
        string line,
        int absoluteOffset,
        int lineNumber,
        IReadOnlyList<(int start, int end)> protectedRanges,
        List<ErbIdentifierOccurrence> results,
        HashSet<(ErbIdentifierKind Kind, int Start, int Length)> seen)
    {
        var codeRange = GetCodeRange(line, protectedRanges);
        if (codeRange is null)
        {
            return;
        }

        AddVariableReferenceTokens(documentId, line, absoluteOffset, lineNumber, protectedRanges, results, seen, codeRange.Value);
    }

    private void AddOccurrence(
        string documentId,
        ErbIdentifierKind kind,
        ErbIdentifierRole role,
        string line,
        int tokenStart,
        int tokenEnd,
        int absoluteOffset,
        int lineNumber,
        IReadOnlyList<(int start, int end)> protectedRanges,
        List<ErbIdentifierOccurrence> results,
        HashSet<(ErbIdentifierKind Kind, int Start, int Length)> seen)
    {
        if (tokenStart < 0 || tokenEnd <= tokenStart || tokenEnd > line.Length)
        {
            return;
        }

        var name = line[tokenStart..tokenEnd];
        if (!ShouldTranslateIdentifier(name)
            || IsProtected(tokenStart, tokenEnd, protectedRanges)
            || IsNamespaceAdjacent(line, tokenStart, tokenEnd)
            || IsPropertyLikeAccess(line, tokenStart, tokenEnd)
            || (kind == ErbIdentifierKind.Variable && IsFunctionCallToken(line, tokenEnd))
            || (kind == ErbIdentifierKind.Variable && role == ErbIdentifierRole.Reference && IsCommandAtLineStart(line, tokenStart, tokenEnd))
            || !seen.Add((kind, absoluteOffset + tokenStart, tokenEnd - tokenStart)))
        {
            return;
        }

        results.Add(new ErbIdentifierOccurrence
        {
            DocumentId = documentId,
            Kind = kind,
            Role = role,
            OriginalName = name,
            AbsoluteStart = absoluteOffset + tokenStart,
            Length = tokenEnd - tokenStart,
            LineNumber = lineNumber,
        });
    }

    private bool ShouldTranslateIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || ReservedIdentifiers.Contains(value)
            || _namespaceRegistry.TryResolveNamespace(value, out _)
            || TextHeuristics.IsNumericLike(value)
            || LooksLikeJapaneseParticleOrInflectionToken(value)
            || !TextHeuristics.ContainsTranslatableText(value))
        {
            return false;
        }

        return true;
    }

    private static bool LooksLikeJapaneseParticleOrInflectionToken(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed.Length == 1 && IsHiragana(trimmed[0]))
        {
            return true;
        }

        return trimmed is "から" or "まで" or "より" or "なら" or "です" or "ます" or "した" or "して" or "する";
    }

    private static bool IsHiragana(char character)
    {
        return character is >= '\u3040' and <= '\u309F';
    }

    private static int FindAssignmentOperator(string line, IReadOnlyList<(int start, int end)> protectedRanges)
    {
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] != '=' || IsProtected(index, protectedRanges))
            {
                continue;
            }

            if ((index > 0 && line[index - 1] is '<' or '>' or '!' or '=')
                || (index + 1 < line.Length && line[index + 1] == '='))
            {
                continue;
            }

            return index;
        }

        return -1;
    }

    private static IReadOnlyList<(int start, int end)> CollectQuotedRanges(string line)
    {
        var ranges = new List<(int start, int end)>();
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] != '"')
            {
                continue;
            }

            var rawStart = index > 0 && line[index - 1] == '@' ? index - 1 : index;
            index++;
            while (index < line.Length)
            {
                if (line[index] == '"')
                {
                    index++;
                    break;
                }

                index++;
            }

            ranges.Add((rawStart, index));
        }

        return ranges;
    }

    private static IEnumerable<(int start, int length)> EnumeratePercentExpressionRanges(string line)
    {
        var start = -1;
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] != '%')
            {
                continue;
            }

            if (start < 0)
            {
                start = index;
                continue;
            }

            if (index > start + 1)
            {
                yield return (start, index - start + 1);
            }

            start = -1;
        }
    }

    private static IEnumerable<(int start, int length)> EnumerateBraceExpressionRanges(string line)
    {
        var depth = 0;
        var start = -1;
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] == '{')
            {
                if (depth == 0)
                {
                    start = index;
                }

                depth++;
                continue;
            }

            if (line[index] != '}' || depth == 0)
            {
                continue;
            }

            depth--;
            if (depth == 0 && start >= 0 && index > start + 1)
            {
                yield return (start, index - start + 1);
                start = -1;
            }
        }
    }

    private static (int start, int length)? GetCodeRange(string line, IReadOnlyList<(int start, int end)> protectedRanges)
    {
        var first = FirstNonWhitespaceIndex(line);
        if (first < 0 || line[first] is ';' or '@' or '#')
        {
            return null;
        }

        if (line[first] == '[')
        {
            var bracketEnd = line.IndexOf(']', first + 1);
            if (bracketEnd > first + 1)
            {
                return (first + 1, bracketEnd - first - 1);
            }
        }

        var commandEnd = ReadIdentifierEnd(line, first);
        var command = line[first..commandEnd];
        if (IsPrintLikeCommand(command) || string.Equals(command, "DATAFORM", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (IsCodeStatementCommand(command))
        {
            return (commandEnd, line.Length - commandEnd);
        }

        var assignmentIndex = FindAssignmentOperator(line, protectedRanges);
        if (assignmentIndex >= 0)
        {
            var rhsStart = assignmentIndex + 1;
            while (rhsStart < line.Length && char.IsWhiteSpace(line[rhsStart]))
            {
                rhsStart++;
            }

            if (LooksLikeBareTextAssignmentValue(line[rhsStart..]))
            {
                return null;
            }

            return LooksLikeCodeExpression(line[rhsStart..])
                ? (rhsStart, line.Length - rhsStart)
                : null;
        }

        return null;
    }

    private static int FindCodeLineLength(string line)
    {
        var inQuote = false;
        for (var index = 0; index < line.Length; index++)
        {
            var ch = line[index];
            if (ch == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && ch == ';')
            {
                return index;
            }
        }

        return line.Length;
    }

    private static int FindMatchingParen(string line, int openParenIndex, IReadOnlyList<(int start, int end)> protectedRanges)
    {
        var depth = 0;
        for (var index = openParenIndex; index < line.Length; index++)
        {
            if (IsProtected(index, protectedRanges))
            {
                continue;
            }

            if (line[index] == '(')
            {
                depth++;
                continue;
            }

            if (line[index] != ')' || depth == 0)
            {
                continue;
            }

            depth--;
            if (depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static int FirstNonWhitespaceIndex(string line)
    {
        for (var index = 0; index < line.Length; index++)
        {
            if (!char.IsWhiteSpace(line[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static int ReadIdentifierEnd(string line, int start)
    {
        var index = start;
        while (index < line.Length && IsIdentifierCharacter(line[index]))
        {
            index++;
        }

        return index;
    }

    private static bool IsIdentifierCharacter(char character)
    {
        return char.IsLetterOrDigit(character)
            || character == '_'
            || character == '＿';
    }

    private static bool IsProtected(int index, IReadOnlyList<(int start, int end)> protectedRanges)
    {
        return protectedRanges.Any(range => index >= range.start && index < range.end);
    }

    private static bool IsProtected(int start, int end, IReadOnlyList<(int start, int end)> protectedRanges)
    {
        return protectedRanges.Any(range => start < range.end && end > range.start);
    }

    private static bool IsNamespaceAdjacent(string line, int tokenStart, int tokenEnd)
    {
        var previous = tokenStart > 0 ? line[tokenStart - 1] : '\0';
        var next = tokenEnd < line.Length ? line[tokenEnd] : '\0';
        return previous == ':' || next == ':';
    }

    private static bool IsPropertyLikeAccess(string line, int tokenStart, int tokenEnd)
    {
        var previous = tokenStart > 0 ? line[tokenStart - 1] : '\0';
        var next = tokenEnd < line.Length ? line[tokenEnd] : '\0';
        return previous == '.' || next == '.';
    }

    private static bool IsFunctionCallToken(string line, int tokenEnd)
    {
        var cursor = tokenEnd;
        while (cursor < line.Length && char.IsWhiteSpace(line[cursor]))
        {
            cursor++;
        }

        return cursor < line.Length && line[cursor] == '(';
    }

    private static bool IsCommandAtLineStart(string line, int tokenStart, int tokenEnd)
    {
        if (FirstNonWhitespaceIndex(line) != tokenStart)
        {
            return false;
        }

        return ReservedIdentifiers.Contains(line[tokenStart..tokenEnd]);
    }

    private static bool IsCallCommand(string value)
    {
        return value.Equals("CALL", StringComparison.OrdinalIgnoreCase)
            || value.Equals("CALLF", StringComparison.OrdinalIgnoreCase)
            || value.Equals("CALLFORM", StringComparison.OrdinalIgnoreCase)
            || value.Equals("CALLFORMF", StringComparison.OrdinalIgnoreCase)
            || value.Equals("TRYCALLFORM", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeCodeExpression(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is ':' or '(' or ')' or '{' or '}' or '[' or ']'
                or '+' or '-' or '*' or '/' or '<' or '>' or '=' or '!' or '&' or '|'
                or '%' or ',')
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeBareTextAssignmentValue(string value)
    {
        var normalized = RemoveInlineExpressions(value).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (!TextHeuristics.ContainsTranslatableText(normalized))
        {
            return false;
        }

        if (normalized.Any(static character => char.IsWhiteSpace(character)))
        {
            return true;
        }

        if (normalized.Any(static character => character is '、' or '。' or '…' or '・' or '「' or '」' or '『' or '』'
            or '！' or '？' or '～' or 'ー' or '─' or 'っ' or 'ッ'))
        {
            return true;
        }

        return normalized.Contains('は')
            || normalized.Contains('が')
            || normalized.Contains('を')
            || normalized.Contains('に')
            || normalized.Contains('で')
            || normalized.Contains('と')
            || normalized.Contains('の')
            || normalized.Contains('へ')
            || normalized.Contains("から", StringComparison.Ordinal)
            || normalized.Contains("まで", StringComparison.Ordinal);
    }

    private static string RemoveInlineExpressions(string value)
    {
        var buffer = value;
        foreach (var range in EnumeratePercentExpressionRanges(value).OrderByDescending(range => range.start))
        {
            buffer = buffer.Remove(range.start, range.length);
        }

        foreach (var range in EnumerateBraceExpressionRanges(buffer).OrderByDescending(range => range.start))
        {
            buffer = buffer.Remove(range.start, range.length);
        }

        return buffer;
    }

    private static bool IsCodeStatementCommand(string value)
    {
        return value.Equals("CASE", StringComparison.OrdinalIgnoreCase)
            || value.Equals("IF", StringComparison.OrdinalIgnoreCase)
            || value.Equals("SIF", StringComparison.OrdinalIgnoreCase)
            || value.Equals("ELSEIF", StringComparison.OrdinalIgnoreCase)
            || value.Equals("WHILE", StringComparison.OrdinalIgnoreCase)
            || value.Equals("RETURN", StringComparison.OrdinalIgnoreCase)
            || value.Equals("RETURNF", StringComparison.OrdinalIgnoreCase)
            || value.Equals("SELECTCASE", StringComparison.OrdinalIgnoreCase)
            || value.Equals("SETCOLOR", StringComparison.OrdinalIgnoreCase)
            || value.Equals("DRAWLINE", StringComparison.OrdinalIgnoreCase)
            || value.Equals("CALL", StringComparison.OrdinalIgnoreCase)
            || value.Equals("CALLF", StringComparison.OrdinalIgnoreCase)
            || value.Equals("CALLFORM", StringComparison.OrdinalIgnoreCase)
            || value.Equals("CALLFORMF", StringComparison.OrdinalIgnoreCase)
            || value.Equals("TRYCALLFORM", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPrintLikeCommand(string value)
    {
        return value.StartsWith("PRINT", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("HTML_PRINT", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPrintTextLine(string value)
    {
        var trimmed = value.TrimStart();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var tokenEnd = 0;
        while (tokenEnd < trimmed.Length && !char.IsWhiteSpace(trimmed[tokenEnd]))
        {
            tokenEnd++;
        }

        return tokenEnd < trimmed.Length && IsPrintLikeCommand(trimmed[..tokenEnd]);
    }
}
