using System.Diagnostics;
using System.Text;

namespace EraTranslator.Services;

public sealed class OutputWriter
{
    private static readonly UTF8Encoding Utf8BomEncoding = new(true);
    private static readonly UTF8Encoding Utf8NoBomEncoding = new(false);
    private static readonly UnicodeEncoding Utf16LeBomEncoding = new(false, true, true);
    private static readonly UnicodeEncoding Utf16LeNoBomEncoding = new(false, false, true);
    private static readonly string[] ProtectedCodeArgumentFunctionNames =
    [
        "GETCONFIG",
        "VARSIZE",
        "LOADTEXT",
        "SAVETEXT",
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
    private static readonly string[] PaletteLookupFunctionNames = ["BARCOLORSET", "BARCOLORSET_HTML", "カラーパレット", "カラーパレット_透明度込", "カラーパレット_HTML"];
    private readonly SymbolRewritePlanner _rewritePlanner = new();
    private readonly IdentifierRewritePlanner _identifierRewritePlanner = new();
    private readonly InlineSymbolReferenceRewriter _inlineSymbolReferenceRewriter = new();
    private readonly JosaPatternAnalyzer _josaPatternAnalyzer = new();
    private readonly JosaSupportPackageService _josaSupportPackageService = new();

    public OutputWriteResult Save(
        ScanSession session,
        string outputDirectory,
        SaveMode saveMode,
        IProgress<(double value, string detail)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new OutputWriteResult
        {
            StartedAt = DateTimeOffset.Now,
        };
        var totalStopwatch = Stopwatch.StartNew();
        var refreshStopwatch = Stopwatch.StartNew();
        ErbReferenceSessionRefresher.Refresh(session);
        refreshStopwatch.Stop();
        result.RefreshElapsed = refreshStopwatch.Elapsed;

        var rewritePlanStopwatch = Stopwatch.StartNew();
        var rewritePlan = _rewritePlanner.CreatePlan(session);
        var identifierRewritePlan = _identifierRewritePlanner.CreatePlan(session);
        rewritePlanStopwatch.Stop();
        result.RewritePlanElapsed = rewritePlanStopwatch.Elapsed;
        var packageInfo = session.JosaPackageInfo.ErbExists || session.JosaPackageInfo.ErhExists
            ? session.JosaPackageInfo
            : _josaSupportPackageService.InspectProject(session.GameRoot);
        var completed = saveMode switch
        {
            SaveMode.ExportCopy => SaveToExportDirectory(session, outputDirectory, rewritePlan, identifierRewritePlan, packageInfo, progress, cancellationToken, result),
            SaveMode.InPlaceWithBackup => SaveInPlaceWithBackup(session, rewritePlan, identifierRewritePlan, packageInfo, progress, cancellationToken, result),
            _ => throw new NotSupportedException($"지원되지 않는 저장 모드입니다: {saveMode}"),
        };
        totalStopwatch.Stop();
        completed.CompletedAt = DateTimeOffset.Now;
        completed.TotalElapsed = totalStopwatch.Elapsed;
        return completed;
    }

    private OutputWriteResult SaveToExportDirectory(
        ScanSession session,
        string outputDirectory,
        SymbolRewritePlan rewritePlan,
        IdentifierRewritePlan identifierRewritePlan,
        JosaSupportPackageInfo packageInfo,
        IProgress<(double value, string detail)>? progress,
        CancellationToken cancellationToken,
        OutputWriteResult result)
    {
        var copyStopwatch = Stopwatch.StartNew();
        Directory.CreateDirectory(outputDirectory);
        CopyGameRootToExportDirectory(session.GameRoot, outputDirectory, cancellationToken);
        copyStopwatch.Stop();
        result.CopyElapsed = copyStopwatch.Elapsed;

        var documents = session.Documents.Values.ToList();
        var documentWriteStopwatch = Stopwatch.StartNew();

        for (var index = 0; index < documents.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = documents[index];
            var translatedMap = BuildTranslationItemMap(session, document.DocumentId, rewritePlan);
            var hasDocumentRewrites = rewritePlan.DocumentReplacements.TryGetValue(document.DocumentId, out var documentReplacements)
                && documentReplacements.Count > 0;
            var hasIdentifierRewrites = identifierRewritePlan.DocumentReplacements.TryGetValue(document.DocumentId, out var identifierReplacements)
                && identifierReplacements.Count > 0;
            var josaDocumentReplacements = DocumentFileTypes.SupportsJosaRewrite(document.FileType)
                ? _josaPatternAnalyzer.CreateDocumentReplacements(document, rewritePlan.RenameMap, packageInfo)
                : [];
            if (translatedMap.Count == 0 && !hasDocumentRewrites && !hasIdentifierRewrites && josaDocumentReplacements.Count == 0)
            {
                result.SkippedFiles.Add(document.RelativePath);
                progress?.Report((((index + 1) / (double)Math.Max(documents.Count, 1)) * 0.95, $"건너뜀: {document.RelativePath}"));
                continue;
            }

            var content = ApplyTranslations(document, translatedMap, rewritePlan, identifierRewritePlan, josaDocumentReplacements, packageInfo);
            var fullPath = Path.Combine(outputDirectory, document.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content, ResolveOutputEncoding(document.EncodingInfo));
            result.WrittenFiles.Add(fullPath);
            progress?.Report((((index + 1) / (double)Math.Max(documents.Count, 1)) * 0.95, $"저장 중: {document.RelativePath}"));
        }
        documentWriteStopwatch.Stop();
        result.DocumentWriteElapsed = documentWriteStopwatch.Elapsed;

        var packageStopwatch = Stopwatch.StartNew();
        WriteBundledJosaPackage(outputDirectory, result, backupRoot: null);
        packageStopwatch.Stop();
        result.PackageWriteElapsed = packageStopwatch.Elapsed;
        progress?.Report((1.0, result.WrittenFiles.Count == 0 ? "저장할 파일 없음" : $"저장 완료: {result.WrittenFiles.Count}개 파일"));
        return result;
    }

    private static void CopyGameRootToExportDirectory(string gameRoot, string outputDirectory, CancellationToken cancellationToken)
    {
        var normalizedGameRoot = NormalizeDirectoryPath(gameRoot);
        var normalizedOutputRoot = NormalizeDirectoryPath(outputDirectory);
        var backupRoot = Path.Combine(normalizedGameRoot, ".era-translator-backup");
        var backupStateRoot = Path.Combine(normalizedGameRoot, ".era-translator");

        foreach (var sourcePath in Directory.EnumerateFiles(normalizedGameRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsUnderDirectory(sourcePath, normalizedOutputRoot)
                || IsUnderDirectory(sourcePath, backupRoot)
                || IsUnderDirectory(sourcePath, backupStateRoot))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(normalizedGameRoot, sourcePath);
            var destinationPath = Path.Combine(normalizedOutputRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    private OutputWriteResult SaveInPlaceWithBackup(
        ScanSession session,
        SymbolRewritePlan rewritePlan,
        IdentifierRewritePlan identifierRewritePlan,
        JosaSupportPackageInfo packageInfo,
        IProgress<(double value, string detail)>? progress,
        CancellationToken cancellationToken,
        OutputWriteResult result)
    {
        var backupRoot = Path.Combine(session.GameRoot, ".era-translator-backup", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        var documents = session.Documents.Values.ToList();
        var documentWriteStopwatch = Stopwatch.StartNew();
        var backupElapsed = TimeSpan.Zero;

        for (var index = 0; index < documents.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = documents[index];
            var translatedMap = BuildTranslationItemMap(session, document.DocumentId, rewritePlan);
            var hasDocumentRewrites = rewritePlan.DocumentReplacements.TryGetValue(document.DocumentId, out var documentReplacements)
                && documentReplacements.Count > 0;
            var hasIdentifierRewrites = identifierRewritePlan.DocumentReplacements.TryGetValue(document.DocumentId, out var identifierReplacements)
                && identifierReplacements.Count > 0;
            var josaDocumentReplacements = DocumentFileTypes.SupportsJosaRewrite(document.FileType)
                ? _josaPatternAnalyzer.CreateDocumentReplacements(document, rewritePlan.RenameMap, packageInfo)
                : [];
            if (translatedMap.Count == 0 && !hasDocumentRewrites && !hasIdentifierRewrites && josaDocumentReplacements.Count == 0)
            {
                result.SkippedFiles.Add(document.RelativePath);
                progress?.Report((((index + 1) / (double)Math.Max(documents.Count, 1)) * 0.95, $"건너뜀: {document.RelativePath}"));
                continue;
            }

            var backupPath = Path.Combine(backupRoot, document.RelativePath);
            var backupStopwatch = Stopwatch.StartNew();
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            File.Copy(document.FullPath, backupPath, overwrite: false);
            backupStopwatch.Stop();
            backupElapsed += backupStopwatch.Elapsed;
            result.BackupFiles.Add(backupPath);

            var content = ApplyTranslations(document, translatedMap, rewritePlan, identifierRewritePlan, josaDocumentReplacements, packageInfo);
            File.WriteAllText(document.FullPath, content, ResolveOutputEncoding(document.EncodingInfo));
            result.WrittenFiles.Add(document.FullPath);
            progress?.Report((((index + 1) / (double)Math.Max(documents.Count, 1)) * 0.95, $"저장 중: {document.RelativePath}"));
        }
        documentWriteStopwatch.Stop();
        result.BackupElapsed = backupElapsed;
        result.DocumentWriteElapsed = documentWriteStopwatch.Elapsed;

        var packageStopwatch = Stopwatch.StartNew();
        WriteBundledJosaPackage(session.GameRoot, result, backupRoot);
        packageStopwatch.Stop();
        result.PackageWriteElapsed = packageStopwatch.Elapsed;
        progress?.Report((1.0, result.WrittenFiles.Count == 0 ? "저장할 파일 없음" : $"저장 완료: {result.WrittenFiles.Count}개 파일"));
        return result;
    }

    private static Dictionary<string, ExtractedTextItem> BuildTranslationItemMap(
        ScanSession session,
        string documentId,
        SymbolRewritePlan rewritePlan)
    {
        return session.Items
            .Where(item => item.DocumentId == documentId
                && !IdentifierSegmentTypes.IsIdentifier(item.SegmentType)
                && rewritePlan.CanWriteItem(item))
            .ToDictionary(item => item.SegmentId, item => item);
    }

    private string ApplyTranslations(
        SourceFileDocument document,
        IReadOnlyDictionary<string, ExtractedTextItem> translatedItems,
        SymbolRewritePlan rewritePlan,
        IdentifierRewritePlan identifierRewritePlan,
        IReadOnlyList<PlannedTextReplacement> josaDocumentReplacements,
        JosaSupportPackageInfo packageInfo)
    {
        return DocumentFileTypes.IsCsvLike(document.FileType)
            ? ApplyCsvTranslations(document, translatedItems, rewritePlan)
            : ApplyErbTranslations(document, translatedItems, rewritePlan, identifierRewritePlan, josaDocumentReplacements, packageInfo);
    }

    private string ApplyErbTranslations(
        SourceFileDocument document,
        IReadOnlyDictionary<string, ExtractedTextItem> translatedItems,
        SymbolRewritePlan rewritePlan,
        IdentifierRewritePlan identifierRewritePlan,
        IReadOnlyList<PlannedTextReplacement> josaDocumentReplacements,
        JosaSupportPackageInfo packageInfo)
    {
        var replacements = new List<PlannedTextReplacement>();
        var appliedSegmentIds = new HashSet<string>(StringComparer.Ordinal);
        string? previousTranslatedValue = null;
        rewritePlan.DocumentReplacements.TryGetValue(document.DocumentId, out var plannedReplacements);
        plannedReplacements ??= [];
        identifierRewritePlan.DocumentReplacements.TryGetValue(document.DocumentId, out var identifierReplacements);
        identifierReplacements ??= [];

        foreach (var segment in document.Segments
                     .Where(segment => translatedItems.ContainsKey(segment.SegmentId))
                     .OrderBy(segment => segment.AbsoluteStart))
        {
            if (ShouldPreserveOriginalErbSegment(document, segment, plannedReplacements))
            {
                previousTranslatedValue = null;
                continue;
            }

            var translatedValue = RewriteTranslatedSegmentText(
                translatedItems[segment.SegmentId],
                rewritePlan,
                rewritePlan.RenameMap,
                packageInfo);

            if (DocumentFileTypes.SupportsJosaRewrite(document.FileType)
                && !string.IsNullOrWhiteSpace(previousTranslatedValue))
            {
                translatedValue = _josaPatternAnalyzer.RewriteLeadingSplitParticle(previousTranslatedValue, translatedValue);
            }

            replacements.Add(new PlannedTextReplacement(
                segment.AbsoluteStart,
                segment.Length,
                translatedValue));
            appliedSegmentIds.Add(segment.SegmentId);
            previousTranslatedValue = translatedValue;
        }

        if (plannedReplacements.Count > 0)
        {
            foreach (var replacement in plannedReplacements)
            {
                if (document.Segments.Any(segment =>
                        appliedSegmentIds.Contains(segment.SegmentId)
                        && RangesOverlap(segment.AbsoluteStart, segment.Length, replacement.Start, replacement.Length)))
                {
                    continue;
                }

                if (josaDocumentReplacements.Any(josa => RangesOverlap(josa.Start, josa.Length, replacement.Start, replacement.Length)))
                {
                    continue;
                }

                replacements.Add(replacement);
            }
        }

        foreach (var replacement in identifierReplacements)
        {
            if (ShouldSkipIdentifierReplacement(document, replacement, replacements, josaDocumentReplacements))
            {
                continue;
            }

            replacements.Add(new PlannedTextReplacement(
                replacement.Start,
                replacement.Length,
                replacement.Value));
        }

        foreach (var replacement in josaDocumentReplacements)
        {
            if (document.Segments.Any(segment =>
                    appliedSegmentIds.Contains(segment.SegmentId)
                    && RangesOverlap(segment.AbsoluteStart, segment.Length, replacement.Start, replacement.Length)))
            {
                continue;
            }

            replacements.Add(replacement);
        }

        var buffer = document.OriginalText;
        foreach (var replacement in replacements.OrderByDescending(replacement => replacement.Start))
        {
            buffer = buffer.Remove(replacement.Start, replacement.Length)
                .Insert(replacement.Start, replacement.Value);
        }

        return buffer;
    }

    private static bool ShouldPreserveOriginalErbSegment(
        SourceFileDocument document,
        TextSegment segment,
        IReadOnlyList<PlannedTextReplacement> plannedReplacements)
    {
        if (plannedReplacements.Any(replacement =>
                RangeContains(replacement.Start, replacement.Length, segment.AbsoluteStart, segment.Length)))
        {
            return true;
        }

        if (IsCodeFragmentSegment(segment)
            && (IsRangeInsidePercentExpression(document.OriginalText, segment.AbsoluteStart, segment.Length)
                || IsRangeInsideRawStringScriptExpression(document.OriginalText, segment.AbsoluteStart, segment.Length)
                || IsRangeInsideFunctionArgument(document.OriginalText, segment.AbsoluteStart, segment.Length, ProtectedCodeArgumentFunctionNames)
                || IsRangeInsideFunctionArgument(document.OriginalText, segment.AbsoluteStart, segment.Length, PaletteLookupFunctionNames)
                || IsRangeInsideCommandArgument(document.OriginalText, segment.AbsoluteStart, segment.Length, ["LOADTEXT", "SAVETEXT"])
                || IsPaletteCaseLabelLiteral(document.OriginalText, segment)
                || IsQuotedComparisonLiteral(document.OriginalText, segment)))
        {
            return true;
        }

        return false;
    }

    private static bool ShouldSkipIdentifierReplacement(
        SourceFileDocument document,
        PlannedIdentifierReplacement replacement,
        IReadOnlyList<PlannedTextReplacement> existingReplacements,
        IReadOnlyList<PlannedTextReplacement> josaDocumentReplacements)
    {
        if (existingReplacements.Any(existing =>
                RangesOverlap(existing.Start, existing.Length, replacement.Start, replacement.Length))
            || josaDocumentReplacements.Any(josa =>
                RangesOverlap(josa.Start, josa.Length, replacement.Start, replacement.Length)))
        {
            return true;
        }

        if (IsRangeInsideFunctionArgument(document.OriginalText, replacement.Start, replacement.Length, ProtectedCodeArgumentFunctionNames)
            || IsRangeInsideFunctionArgument(document.OriginalText, replacement.Start, replacement.Length, PaletteLookupFunctionNames)
            || IsRangeInsideCommandArgument(document.OriginalText, replacement.Start, replacement.Length, ["LOADTEXT", "SAVETEXT"]))
        {
            return true;
        }

        return false;
    }

    private static bool IsCodeFragmentSegment(TextSegment segment)
    {
        return segment.SegmentType.Contains("quoted", StringComparison.OrdinalIgnoreCase)
            || segment.SegmentType.Contains("assignment", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRangeInsidePercentExpression(string text, int start, int length)
    {
        foreach (var range in EnumerateDelimitedRanges(text, '%', '%'))
        {
            if (RangeContains(range.start, range.length, start, length))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRangeInsideRawStringScriptExpression(string text, int start, int length)
    {
        foreach (var range in EnumerateRawStringScriptExpressionRanges(text))
        {
            if (RangeContains(range.start, range.length, start, length))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPaletteCaseLabelLiteral(string text, TextSegment segment)
    {
        if (!segment.SegmentType.Contains("quoted", StringComparison.OrdinalIgnoreCase)
            || !IsInsidePaletteLookupFunction(text, segment.AbsoluteStart))
        {
            return false;
        }

        var lineStart = text.LastIndexOf('\n', Math.Max(0, segment.AbsoluteStart - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var lineEnd = text.IndexOf('\n', segment.AbsoluteStart);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }

        return IsCaseLabelLine(text[lineStart..lineEnd]);
    }

    private static bool IsInsidePaletteLookupFunction(string text, int start)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, start - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        while (lineStart >= 0)
        {
            var lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0)
            {
                lineEnd = text.Length;
            }

            var line = text[lineStart..lineEnd].TrimStart();
            if (TryReadFunctionName(line, out var functionName))
            {
                return IsPaletteLookupFunction(functionName);
            }

            if (lineStart == 0)
            {
                break;
            }

            var previousEnd = lineStart - 1;
            lineStart = text.LastIndexOf('\n', Math.Max(0, previousEnd - 1));
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
        }

        return false;
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

    private static bool IsRangeInsideCommandArgument(string text, int start, int length, IReadOnlyCollection<string> commandNames)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, start - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var lineEnd = text.IndexOf('\n', start);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }

        var line = text[lineStart..lineEnd];
        var relativeStart = start - lineStart;
        foreach (var commandName in commandNames)
        {
            var commandIndex = line.IndexOf(commandName, StringComparison.OrdinalIgnoreCase);
            if (commandIndex < 0)
            {
                continue;
            }

            var prefix = line[..commandIndex].TrimStart();
            if (prefix.Length > 0 && !prefix.StartsWith(';'))
            {
                continue;
            }

            var afterCommand = commandIndex + commandName.Length;
            if (afterCommand < line.Length && (char.IsLetterOrDigit(line[afterCommand]) || line[afterCommand] == '_'))
            {
                continue;
            }

            if (relativeStart >= afterCommand && relativeStart + length <= line.Length)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRangeInsideFunctionArgument(string text, int start, int length, IReadOnlyCollection<string> functionNames)
    {
        foreach (var functionName in functionNames)
        {
            var searchIndex = 0;
            while ((searchIndex = text.IndexOf(functionName, searchIndex, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var parenIndex = text.IndexOf('(', searchIndex + functionName.Length);
                if (parenIndex < 0)
                {
                    break;
                }

                if (text[(searchIndex + functionName.Length)..parenIndex].Any(static ch => !char.IsWhiteSpace(ch)))
                {
                    searchIndex += functionName.Length;
                    continue;
                }

                var closeIndex = FindMatchingParen(text, parenIndex);
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

    private static bool IsQuotedComparisonLiteral(string text, TextSegment segment)
    {
        if (!segment.SegmentType.Contains("quoted", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var lineStart = text.LastIndexOf('\n', Math.Max(0, segment.AbsoluteStart - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var lineEnd = text.IndexOf('\n', segment.AbsoluteStart);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }

        var prefix = text[lineStart..segment.AbsoluteStart];
        var suffix = text[(segment.AbsoluteStart + segment.Length)..lineEnd];
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

    private static int FindMatchingParen(string text, int openParenIndex)
    {
        var depth = 0;
        var inQuote = false;
        for (var index = openParenIndex; index < text.Length; index++)
        {
            var ch = text[index];
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

    private static IEnumerable<(int start, int length)> EnumerateDelimitedRanges(string text, char open, char close)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != open)
            {
                continue;
            }

            var lineEnd = text.IndexOfAny(['\r', '\n'], index + 1);
            var end = text.IndexOf(close, index + 1);
            if (end < 0 || (lineEnd >= 0 && lineEnd < end))
            {
                continue;
            }

            yield return (index, end - index + 1);
            index = end;
        }
    }

    private static IEnumerable<(int start, int length)> EnumerateRawStringScriptExpressionRanges(string text)
    {
        for (var index = 0; index < text.Length - 1; index++)
        {
            if (text[index] != '@' || text[index + 1] != '"')
            {
                continue;
            }

            var contentStart = index + 2;
            var percentStart = -1;
            var braceStart = -1;
            var percentOpen = false;
            var braceDepth = 0;

            for (var scan = contentStart; scan < text.Length; scan++)
            {
                var ch = text[scan];
                if (ch == '%')
                {
                    if (!percentOpen)
                    {
                        percentStart = scan;
                        percentOpen = true;
                    }
                    else
                    {
                        yield return (percentStart, scan - percentStart + 1);
                        percentStart = -1;
                        percentOpen = false;
                    }

                    continue;
                }

                if (!percentOpen)
                {
                    if (ch == '{')
                    {
                        if (braceDepth == 0)
                        {
                            braceStart = scan;
                        }

                        braceDepth++;
                        continue;
                    }

                    if (ch == '}' && braceDepth > 0)
                    {
                        braceDepth--;
                        if (braceDepth == 0)
                        {
                            yield return (braceStart, scan - braceStart + 1);
                            braceStart = -1;
                        }

                        continue;
                    }
                }

                if (ch != '"' || percentOpen || braceDepth > 0)
                {
                    continue;
                }

                if (scan + 1 < text.Length && text[scan + 1] == '"')
                {
                    scan++;
                    continue;
                }

                index = scan;
                break;
            }
        }
    }

    private static bool RangeContains(int outerStart, int outerLength, int innerStart, int innerLength)
    {
        var outerEnd = outerStart + outerLength;
        var innerEnd = innerStart + innerLength;
        return innerStart >= outerStart && innerEnd <= outerEnd;
    }

    private static string ApplyCsvTranslations(
        SourceFileDocument document,
        IReadOnlyDictionary<string, ExtractedTextItem> translatedItems,
        SymbolRewritePlan rewritePlan)
    {
        var lines = SplitLines(document.OriginalText, document.NewLineSequence);
        var segmentsByLine = document.Segments
            .Where(segment => translatedItems.ContainsKey(segment.SegmentId) && segment.FieldIndex.HasValue)
            .GroupBy(segment => segment.LineNumber)
            .ToDictionary(group => group.Key, group => group.ToList());

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var lineNumber = lineIndex + 1;
            if (!segmentsByLine.TryGetValue(lineNumber, out var lineSegments))
            {
                continue;
            }

            var fields = CsvLineParser.ParseFields(lines[lineIndex]);
            var replacements = lineSegments.ToDictionary(
                segment => segment.FieldIndex!.Value,
                segment => rewritePlan.GetOutputTranslatedText(translatedItems[segment.SegmentId]));
            lines[lineIndex] = CsvLineParser.RebuildLine(fields, replacements);
        }

        return string.Join(document.NewLineSequence, lines);
    }

    private static List<string> SplitLines(string content, string newLineSequence)
    {
        if (string.IsNullOrEmpty(content))
        {
            return [string.Empty];
        }

        return content.Split([newLineSequence], StringSplitOptions.None).ToList();
    }

    private string RewriteTranslatedSegmentText(
        ExtractedTextItem item,
        SymbolRewritePlan rewritePlan,
        IReadOnlyDictionary<(string Namespace, string OriginalKey), string> renameMap,
        JosaSupportPackageInfo packageInfo)
    {
        var symbolRewritten = _inlineSymbolReferenceRewriter.Rewrite(
            rewritePlan.GetOutputTranslatedText(item),
            renameMap,
            rewritePlan.StringLookupRenameMap);
        if (!DocumentFileTypes.SupportsJosaRewrite(item.FileType))
        {
            return symbolRewritten;
        }

        var josaRewritten = _josaPatternAnalyzer.RewriteText(symbolRewritten, renameMap, packageInfo);
        return TranslationQualityRules.NormalizeErbFunctionArgumentSeparators(josaRewritten.Text);
    }

    private void WriteBundledJosaPackage(string rootDirectory, OutputWriteResult result, string? backupRoot)
    {
        var package = _josaSupportPackageService.LoadBundledPackage();
        WritePackageFile(_josaSupportPackageService.GetDefaultErbTargetPath(rootDirectory), package.erbText, result, backupRoot);
        WritePackageFile(_josaSupportPackageService.GetDefaultErhTargetPath(rootDirectory), package.erhText, result, backupRoot);
    }

    private static void WritePackageFile(string targetPath, string content, OutputWriteResult result, string? backupRoot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        if (!string.IsNullOrWhiteSpace(backupRoot) && File.Exists(targetPath))
        {
            var gameRoot = Directory.GetParent(Directory.GetParent(backupRoot)!.FullName)!.FullName;
            var relativePath = Path.GetRelativePath(gameRoot, targetPath);
            var backupPath = Path.Combine(backupRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            if (!File.Exists(backupPath))
            {
                File.Copy(targetPath, backupPath, overwrite: false);
                result.BackupFiles.Add(backupPath);
            }
        }

        File.WriteAllText(targetPath, content, Utf8BomEncoding);
        if (!result.WrittenFiles.Contains(targetPath, StringComparer.OrdinalIgnoreCase))
        {
            result.WrittenFiles.Add(targetPath);
        }
    }

    private static string NormalizeDirectoryPath(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsUnderDirectory(string path, string directoryPath)
    {
        var normalizedPath = NormalizeDirectoryPath(path);
        var normalizedDirectory = NormalizeDirectoryPath(directoryPath);

        if (normalizedPath.Length <= normalizedDirectory.Length)
        {
            return false;
        }

        return normalizedPath.StartsWith(normalizedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static Encoding ResolveOutputEncoding(DetectedEncodingInfo encodingInfo)
    {
        return encodingInfo.Kind switch
        {
            DetectedEncodingKind.Utf8Bom => Utf8BomEncoding,
            DetectedEncodingKind.Utf8 => encodingInfo.HasBom ? Utf8BomEncoding : Utf8NoBomEncoding,
            DetectedEncodingKind.Unicode => encodingInfo.HasBom ? Utf16LeBomEncoding : Utf16LeNoBomEncoding,
            _ => encodingInfo.Encoding,
        };
    }

    private static bool RangesOverlap(int leftStart, int leftLength, int rightStart, int rightLength)
    {
        var leftEnd = leftStart + leftLength;
        var rightEnd = rightStart + rightLength;
        return leftStart < rightEnd && rightStart < leftEnd;
    }
}
