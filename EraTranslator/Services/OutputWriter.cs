using System.Text;

namespace EraTranslator.Services;

public sealed class OutputWriter
{
    private static readonly UTF8Encoding Utf8BomEncoding = new(true);
    private static readonly UTF8Encoding Utf8NoBomEncoding = new(false);
    private static readonly UnicodeEncoding Utf16LeBomEncoding = new(false, true, true);
    private static readonly UnicodeEncoding Utf16LeNoBomEncoding = new(false, false, true);
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
        ErbReferenceSessionRefresher.Refresh(session);
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
        CopyGameRootToExportDirectory(session.GameRoot, outputDirectory, cancellationToken);
        var result = new OutputWriteResult();
        var documents = session.Documents.Values.ToList();

        for (var index = 0; index < documents.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = documents[index];
            var translatedMap = BuildTranslationItemMap(session, document.DocumentId, rewritePlan);
            var hasDocumentRewrites = rewritePlan.DocumentReplacements.TryGetValue(document.DocumentId, out var documentReplacements)
                && documentReplacements.Count > 0;
            var josaDocumentReplacements = DocumentFileTypes.IsErbLike(document.FileType)
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
            File.WriteAllText(fullPath, content, ResolveOutputEncoding(document.EncodingInfo));
            result.WrittenFiles.Add(fullPath);
            progress?.Report((((index + 1) / (double)Math.Max(documents.Count, 1)) * 0.95, $"저장 중: {document.RelativePath}"));
        }

        WriteBundledJosaPackage(outputDirectory, result, backupRoot: null);
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
            var translatedMap = BuildTranslationItemMap(session, document.DocumentId, rewritePlan);
            var hasDocumentRewrites = rewritePlan.DocumentReplacements.TryGetValue(document.DocumentId, out var documentReplacements)
                && documentReplacements.Count > 0;
            var josaDocumentReplacements = DocumentFileTypes.IsErbLike(document.FileType)
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
            File.WriteAllText(document.FullPath, content, ResolveOutputEncoding(document.EncodingInfo));
            result.WrittenFiles.Add(document.FullPath);
            progress?.Report((((index + 1) / (double)Math.Max(documents.Count, 1)) * 0.95, $"저장 중: {document.RelativePath}"));
        }

        WriteBundledJosaPackage(session.GameRoot, result, backupRoot);
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
                && rewritePlan.CanWriteItem(item))
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
            ? ApplyCsvTranslations(document, translatedItems, rewritePlan)
            : ApplyErbTranslations(document, translatedItems, rewritePlan, josaDocumentReplacements, packageInfo);
    }

    private string ApplyErbTranslations(
        SourceFileDocument document,
        IReadOnlyDictionary<string, ExtractedTextItem> translatedItems,
        SymbolRewritePlan rewritePlan,
        IReadOnlyList<PlannedTextReplacement> josaDocumentReplacements,
        JosaSupportPackageInfo packageInfo)
    {
        var replacements = new List<PlannedTextReplacement>();
        string? previousTranslatedValue = null;

        foreach (var segment in document.Segments
                     .Where(segment => translatedItems.ContainsKey(segment.SegmentId))
                     .OrderBy(segment => segment.AbsoluteStart))
        {
            var translatedValue = RewriteTranslatedSegmentText(
                translatedItems[segment.SegmentId],
                rewritePlan,
                rewritePlan.RenameMap,
                packageInfo);

            if (!string.IsNullOrWhiteSpace(previousTranslatedValue))
            {
                translatedValue = _josaPatternAnalyzer.RewriteLeadingSplitParticle(previousTranslatedValue, translatedValue);
            }

            replacements.Add(new PlannedTextReplacement(
                segment.AbsoluteStart,
                segment.Length,
                translatedValue));
            previousTranslatedValue = translatedValue;
        }

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

        return buffer;
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
        var josaRewritten = _josaPatternAnalyzer.RewriteText(symbolRewritten, renameMap, packageInfo);
        return DocumentFileTypes.IsErbLike(item.FileType)
            ? TranslationQualityRules.NormalizeErbFunctionArgumentSeparators(josaRewritten.Text)
            : josaRewritten.Text;
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
