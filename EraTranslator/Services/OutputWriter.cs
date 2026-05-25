namespace EraTranslator.Services;

public sealed class OutputWriter
{
    private readonly SymbolRewritePlanner _rewritePlanner = new();
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
        var rewritePlan = _rewritePlanner.CreatePlan(session);
        var packageInfo = session.JosaPackageInfo.ErbExists || session.JosaPackageInfo.ErhExists
            ? session.JosaPackageInfo
            : _josaSupportPackageService.InspectProject(session.GameRoot);
        return saveMode switch
        {
            SaveMode.ExportCopy => SaveToExportDirectory(session, outputDirectory, rewritePlan, packageInfo, progress, cancellationToken),
            SaveMode.InPlaceWithBackup => SaveInPlaceWithBackup(session, rewritePlan, packageInfo, progress, cancellationToken),
            _ => throw new NotSupportedException($"지원되지 않는 저장 모드입니다: {saveMode}"),
        };
    }

    private OutputWriteResult SaveToExportDirectory(
        ScanSession session,
        string outputDirectory,
        SymbolRewritePlan rewritePlan,
        JosaSupportPackageInfo packageInfo,
        IProgress<(double value, string detail)>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var result = new OutputWriteResult();
        var documents = session.Documents.Values.ToList();

        for (var index = 0; index < documents.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = documents[index];
            var translatedMap = BuildTranslationItemMap(session, document.DocumentId);
            var hasDocumentRewrites = rewritePlan.DocumentReplacements.TryGetValue(document.DocumentId, out var documentReplacements)
                && documentReplacements.Count > 0;
            var josaDocumentReplacements = document.FileType == "ERB"
                ? _josaPatternAnalyzer.CreateDocumentReplacements(document, rewritePlan.RenameMap, packageInfo)
                : [];
            if (translatedMap.Count == 0 && !hasDocumentRewrites && josaDocumentReplacements.Count == 0)
            {
                result.SkippedFiles.Add(document.RelativePath);
                progress?.Report((((index + 1) / (double)Math.Max(documents.Count, 1)) * 0.95, $"건너뜀: {document.RelativePath}"));
                continue;
            }

            var content = ApplyTranslations(document, translatedMap, rewritePlan, josaDocumentReplacements, packageInfo);
            var fullPath = Path.Combine(outputDirectory, document.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content, document.EncodingInfo.Encoding);
            result.WrittenFiles.Add(fullPath);
            progress?.Report((((index + 1) / (double)Math.Max(documents.Count, 1)) * 0.95, $"저장 중: {document.RelativePath}"));
        }

        WriteBundledJosaPackage(outputDirectory, result, backupRoot: null);
        progress?.Report((1.0, result.WrittenFiles.Count == 0 ? "저장할 파일 없음" : $"저장 완료: {result.WrittenFiles.Count}개 파일"));
        return result;
    }

    private OutputWriteResult SaveInPlaceWithBackup(
        ScanSession session,
        SymbolRewritePlan rewritePlan,
        JosaSupportPackageInfo packageInfo,
        IProgress<(double value, string detail)>? progress,
        CancellationToken cancellationToken)
    {
        var result = new OutputWriteResult();
        var backupRoot = Path.Combine(session.GameRoot, ".era-translator-backup", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        var documents = session.Documents.Values.ToList();

        for (var index = 0; index < documents.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = documents[index];
            var translatedMap = BuildTranslationItemMap(session, document.DocumentId);
            var hasDocumentRewrites = rewritePlan.DocumentReplacements.TryGetValue(document.DocumentId, out var documentReplacements)
                && documentReplacements.Count > 0;
            var josaDocumentReplacements = document.FileType == "ERB"
                ? _josaPatternAnalyzer.CreateDocumentReplacements(document, rewritePlan.RenameMap, packageInfo)
                : [];
            if (translatedMap.Count == 0 && !hasDocumentRewrites && josaDocumentReplacements.Count == 0)
            {
                result.SkippedFiles.Add(document.RelativePath);
                progress?.Report((((index + 1) / (double)Math.Max(documents.Count, 1)) * 0.95, $"건너뜀: {document.RelativePath}"));
                continue;
            }

            var backupPath = Path.Combine(backupRoot, document.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            File.Copy(document.FullPath, backupPath, overwrite: false);
            result.BackupFiles.Add(backupPath);

            var content = ApplyTranslations(document, translatedMap, rewritePlan, josaDocumentReplacements, packageInfo);
            File.WriteAllText(document.FullPath, content, document.EncodingInfo.Encoding);
            result.WrittenFiles.Add(document.FullPath);
            progress?.Report((((index + 1) / (double)Math.Max(documents.Count, 1)) * 0.95, $"저장 중: {document.RelativePath}"));
        }

        WriteBundledJosaPackage(session.GameRoot, result, backupRoot);
        progress?.Report((1.0, result.WrittenFiles.Count == 0 ? "저장할 파일 없음" : $"저장 완료: {result.WrittenFiles.Count}개 파일"));
        return result;
    }

    private static Dictionary<string, ExtractedTextItem> BuildTranslationItemMap(ScanSession session, string documentId)
    {
        return session.Items
            .Where(item => item.DocumentId == documentId
                && !string.IsNullOrWhiteSpace(item.TranslatedText)
                && item.CanSave
                && item.ValidationStatus == "통과")
            .ToDictionary(item => item.SegmentId, item => item);
    }

    private string ApplyTranslations(
        SourceFileDocument document,
        IReadOnlyDictionary<string, ExtractedTextItem> translatedItems,
        SymbolRewritePlan rewritePlan,
        IReadOnlyList<PlannedTextReplacement> josaDocumentReplacements,
        JosaSupportPackageInfo packageInfo)
    {
        return document.FileType == "CSV"
            ? ApplyCsvTranslations(document, translatedItems)
            : ApplyErbTranslations(document, translatedItems, rewritePlan, josaDocumentReplacements, packageInfo);
    }

    private string ApplyErbTranslations(
        SourceFileDocument document,
        IReadOnlyDictionary<string, ExtractedTextItem> translatedItems,
        SymbolRewritePlan rewritePlan,
        IReadOnlyList<PlannedTextReplacement> josaDocumentReplacements,
        JosaSupportPackageInfo packageInfo)
    {
        var replacements = document.Segments
            .Where(segment => translatedItems.ContainsKey(segment.SegmentId))
            .Select(segment => new PlannedTextReplacement(
                segment.AbsoluteStart,
                segment.Length,
                RewriteTranslatedSegmentText(translatedItems[segment.SegmentId], rewritePlan.RenameMap, packageInfo)))
            .ToList();

        if (rewritePlan.DocumentReplacements.TryGetValue(document.DocumentId, out var plannedReplacements))
        {
            foreach (var replacement in plannedReplacements)
            {
                if (document.Segments.Any(segment =>
                        translatedItems.ContainsKey(segment.SegmentId)
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

        foreach (var replacement in josaDocumentReplacements)
        {
            if (document.Segments.Any(segment =>
                    translatedItems.ContainsKey(segment.SegmentId)
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

        return EnsureErhIncludeIfNeeded(buffer, document.NewLineSequence);
    }

    private static string ApplyCsvTranslations(SourceFileDocument document, IReadOnlyDictionary<string, ExtractedTextItem> translatedItems)
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
                segment => translatedItems[segment.SegmentId].TranslatedText);
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
        IReadOnlyDictionary<(string Namespace, string OriginalKey), string> renameMap,
        JosaSupportPackageInfo packageInfo)
    {
        var symbolRewritten = _inlineSymbolReferenceRewriter.Rewrite(item.TranslatedText, renameMap);
        var josaRewritten = _josaPatternAnalyzer.RewriteText(symbolRewritten, renameMap, packageInfo);
        return josaRewritten.Text;
    }

    private string EnsureErhIncludeIfNeeded(string content, string newLineSequence)
    {
        if (!_josaSupportPackageService.HasErhInclude(content) && ContainsMacroJosaUsage(content))
        {
            var lines = SplitLines(content, newLineSequence);
            var insertIndex = FindIncludeInsertionLine(lines);
            lines.Insert(insertIndex, "#INCLUDE \"ZNAME.ERH\"");
            return string.Join(newLineSequence, lines);
        }

        return content;
    }

    private static int FindIncludeInsertionLine(IReadOnlyList<string> lines)
    {
        var index = 0;
        while (index < lines.Count)
        {
            var trimmed = lines[index].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
            {
                index++;
                continue;
            }

            break;
        }

        return index;
    }

    private static bool ContainsMacroJosaUsage(string content)
    {
        var analysis = new JosaPatternAnalyzer().AnalyzeDocument(content, new JosaSupportPackageInfo());
        return analysis.MacroPatternCount > 0;
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

        File.WriteAllText(targetPath, content);
        if (!result.WrittenFiles.Contains(targetPath, StringComparer.OrdinalIgnoreCase))
        {
            result.WrittenFiles.Add(targetPath);
        }
    }

    private static bool RangesOverlap(int leftStart, int leftLength, int rightStart, int rightLength)
    {
        var leftEnd = leftStart + leftLength;
        var rightEnd = rightStart + rightLength;
        return leftStart < rightEnd && rightStart < leftEnd;
    }
}
