using System.Diagnostics;
using System.Text;

namespace EraTranslator.Services;

public sealed class FileScanner
{
    private static readonly string[] ErbExtensions = [".erb", ".era", ".erh"];
    private static readonly string[] CsvExtensions = [".csv", ".cvs", ".erd"];
    private static readonly string[] SupportedScanDirectoryNames = ["ERB", "CSV", "CVS", "DATA"];
    private static readonly string[] ExcludedScanDirectoryNames = [".era-translator-backup", ".era-translator"];
    private readonly int? _maxDegreeOfParallelismOverride;
    private readonly SymbolReferenceAnalyzer _symbolReferenceAnalyzer = new();
    private readonly JosaSupportPackageService _josaSupportPackageService = new();

    public FileScanner()
    {
    }

    internal FileScanner(int? maxDegreeOfParallelismOverride)
    {
        _maxDegreeOfParallelismOverride = maxDegreeOfParallelismOverride;
    }

    public ScanSession Scan(
        string gameRoot,
        IProgress<(double value, string detail)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var session = new ScanSession
        {
            GameRoot = gameRoot,
            JosaPackageInfo = _josaSupportPackageService.InspectProject(gameRoot),
        };
        var targetFiles = EnumerateTargetFiles(gameRoot).ToList();
        var namespaceRegistry = SymbolNamespaceRegistry.CreateFromRelativePaths(
            targetFiles.Select(path => Path.GetRelativePath(gameRoot, path)));
        var functionRegistry = BuildFunctionRegistry(targetFiles, cancellationToken);
        var dimsLookupRegistry = BuildDimsLookupRegistry(targetFiles, cancellationToken);
        var files = targetFiles
            .Select((path, index) => new ScanTargetFile(index, path))
            .ToList();
        var results = new ScannedFileResult[files.Count];
        var translatableErbSegments = 0;
        var translatableCsvSegments = 0;
        var warningCount = 0;
        var josaPatternCount = 0;
        var progressReporter = new ScanProgressReporter(files.Count, progress);

        Parallel.ForEach(
            files,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = GetMaxDegreeOfParallelism(),
            },
            targetFile =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = ScanFile(gameRoot, targetFile.Path, namespaceRegistry, functionRegistry, dimsLookupRegistry, session.JosaPackageInfo, cancellationToken);
                results[targetFile.Index] = result;
                Interlocked.Add(ref translatableErbSegments, result.ErbItemCount);
                Interlocked.Add(ref translatableCsvSegments, result.CsvItemCount);
                Interlocked.Add(ref warningCount, result.WarningCount);
                Interlocked.Add(ref josaPatternCount, result.JosaPatternCount);
                progressReporter.Report(targetFile.Index + 1, result.Document.RelativePath);
            });

        foreach (var result in results)
        {
            if (result is null)
            {
                continue;
            }

            session.Documents.Add(result.Document.DocumentId, result.Document);
            session.Items.AddRange(result.Items);
        }

        var identifierItems = BuildIdentifierItems(session);
        session.Items.AddRange(identifierItems);

        session.Metrics["Documents"] = session.Documents.Count;
        session.Metrics["Items"] = session.Items.Count;
        session.Metrics["ErbItems"] = translatableErbSegments;
        session.Metrics["CsvItems"] = translatableCsvSegments;
        session.Metrics["IdentifierItems"] = identifierItems.Count;
        session.Metrics["Warnings"] = warningCount;
        session.Metrics["JosaPatterns"] = josaPatternCount;
        _symbolReferenceAnalyzer.Analyze(session);
        progress?.Report((1.0, $"스캔 완료: 문서 {session.Documents.Count}개"));

        return session;
    }

    public IReadOnlyList<string> ConvertEncodingsToUtf8Bom(
        string gameRoot,
        IProgress<(double value, string detail)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var converted = new List<string>();
        var utf8Bom = new UTF8Encoding(true);
        var encodingDetector = new EncodingDetector();
        var files = EnumerateTargetFiles(gameRoot).ToList();

        for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = files[fileIndex];
            var bytes = File.ReadAllBytes(path);
            var encodingInfo = encodingDetector.Detect(bytes);
            var relativePath = Path.GetRelativePath(gameRoot, path);
            if (!encodingInfo.CanConvertToUtf8Bom)
            {
                progress?.Report((((fileIndex + 1) / (double)Math.Max(files.Count, 1)) * 0.95, $"건너뜀: {relativePath}"));
                continue;
            }

            var text = DecodeContent(bytes, encodingInfo);
            File.WriteAllText(path, text, utf8Bom);
            converted.Add(relativePath);
            progress?.Report((((fileIndex + 1) / (double)Math.Max(files.Count, 1)) * 0.95, $"변환 중: {relativePath}"));
        }

        progress?.Report((1.0, converted.Count == 0 ? "변환 대상 없음" : $"변환 완료: {converted.Count}개 파일"));
        return converted;
    }

    private static IEnumerable<string> EnumerateTargetFiles(string gameRoot)
    {
        var directories = Directory.EnumerateDirectories(gameRoot, "*", SearchOption.AllDirectories)
            .Where(path =>
            {
                if (IsExcludedScanPath(gameRoot, path))
                {
                    return false;
                }

                var name = Path.GetFileName(path);
                return SupportedScanDirectoryNames.Contains(name, StringComparer.OrdinalIgnoreCase);
            })
            .Concat(SupportedScanDirectoryNames.Contains(Path.GetFileName(gameRoot), StringComparer.OrdinalIgnoreCase)
                ? [gameRoot]
                : [])
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var yieldedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in directories)
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (IsExcludedScanPath(gameRoot, file))
                {
                    continue;
                }

                var fullPath = Path.GetFullPath(file);
                var extension = Path.GetExtension(file).ToLowerInvariant();
                if ((ErbExtensions.Contains(extension) || CsvExtensions.Contains(extension))
                    && yieldedFiles.Add(fullPath))
                {
                    yield return fullPath;
                }
            }
        }
    }

    private static bool IsExcludedScanPath(string gameRoot, string path)
    {
        var fullGameRoot = Path.GetFullPath(gameRoot);
        var fullPath = Path.GetFullPath(path);
        var relativePath = Path.GetRelativePath(fullGameRoot, fullPath);
        var segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => ExcludedScanDirectoryNames.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }

    private int GetMaxDegreeOfParallelism()
    {
        return _maxDegreeOfParallelismOverride ?? Math.Max(1, Environment.ProcessorCount - 2);
    }

    private static ErbCodeFunctionRegistry BuildFunctionRegistry(
        IReadOnlyCollection<string> targetFiles,
        CancellationToken cancellationToken)
    {
        var encodingDetector = new EncodingDetector();
        var contents = new List<string>();
        foreach (var path in targetFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (!ErbExtensions.Contains(extension))
            {
                continue;
            }

            var bytes = File.ReadAllBytes(path);
            var encodingInfo = encodingDetector.Detect(bytes);
            contents.Add(DecodeContent(bytes, encodingInfo));
        }

        return ErbCodeFunctionRegistry.BuildFromDocuments(contents);
    }

    private static ErbDimsLookupRegistry BuildDimsLookupRegistry(
        IReadOnlyCollection<string> targetFiles,
        CancellationToken cancellationToken)
    {
        var encodingDetector = new EncodingDetector();
        var contents = new List<string>();
        foreach (var path in targetFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (!ErbExtensions.Contains(extension))
            {
                continue;
            }

            var bytes = File.ReadAllBytes(path);
            var encodingInfo = encodingDetector.Detect(bytes);
            contents.Add(DecodeContent(bytes, encodingInfo));
        }

        return ErbDimsLookupRegistry.BuildFromDocuments(contents);
    }

    private static ScannedFileResult ScanFile(
        string gameRoot,
        string path,
        SymbolNamespaceRegistry namespaceRegistry,
        ErbCodeFunctionRegistry functionRegistry,
        ErbDimsLookupRegistry dimsLookupRegistry,
        JosaSupportPackageInfo packageInfo,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var encodingDetector = new EncodingDetector();
        var erbExtractor = new ErbExtractor(namespaceRegistry, functionRegistry, dimsLookupRegistry);
        var erbReferenceExtractor = new ErbReferenceExtractor(namespaceRegistry, dimsLookupRegistry);
        var erbIdentifierExtractor = new ErbIdentifierExtractor(namespaceRegistry);
        var csvExtractor = new CsvExtractor(namespaceRegistry);
        var josaPatternAnalyzer = new JosaPatternAnalyzer();

        var bytes = File.ReadAllBytes(path);
        cancellationToken.ThrowIfCancellationRequested();

        var encodingInfo = encodingDetector.Detect(bytes);
        var content = DecodeContent(bytes, encodingInfo);
        var relativePath = Path.GetRelativePath(gameRoot, path);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var fileType = CsvExtensions.Contains(extension)
            ? extension.Equals(".erd", StringComparison.OrdinalIgnoreCase)
                ? DocumentFileTypes.Erd
                : DocumentFileTypes.Csv
            : extension.Equals(".erh", StringComparison.OrdinalIgnoreCase)
                ? DocumentFileTypes.Erh
                : DocumentFileTypes.Erb;
        var documentId = relativePath.Replace(Path.DirectorySeparatorChar, '/');
        var newLine = DetectNewLine(content);

        var document = new SourceFileDocument
        {
            DocumentId = documentId,
            FullPath = path,
            RelativePath = relativePath,
            FileType = fileType,
            OriginalText = content,
            EncodingInfo = encodingInfo,
            NewLineSequence = newLine,
        };

        List<TextSegment> segments;
        var csvItemCount = 0;
        var erbItemCount = 0;
        if (DocumentFileTypes.IsCsvLike(fileType))
        {
            var csvResult = csvExtractor.Extract(documentId, relativePath, content);
            document.CsvKind = csvResult.kind;
            document.ScanWarnings.AddRange(csvResult.warnings);
            segments = csvResult.segments;
            csvItemCount = segments.Count;
        }
        else
        {
            document.CsvKind = CsvDocumentKind.None;
            document.ScanWarnings.AddRange(FindErbWarnings(content));
            segments = erbExtractor.Extract(documentId, content);
            var referenceResult = erbReferenceExtractor.Extract(documentId, content);
            document.SymbolReferences.AddRange(referenceResult.references);
            document.VariableLiteralOccurrences.AddRange(referenceResult.variableLiterals);
            document.IdentifierOccurrences.AddRange(erbIdentifierExtractor.Extract(documentId, content));
            if (DocumentFileTypes.SupportsJosaRewrite(fileType))
            {
                document.JosaAnalysis = josaPatternAnalyzer.AnalyzeDocument(content, packageInfo);
            }

            erbItemCount = segments.Count;
        }

        document.Segments.AddRange(segments);
        var warningText = string.Join(" | ", document.ScanWarnings);
        var items = segments.Select(segment => new ExtractedTextItem
        {
            SegmentId = segment.SegmentId,
            DocumentId = documentId,
            FileType = fileType,
            RelativePath = relativePath,
            EncodingName = encodingInfo.Name,
            SegmentType = segment.SegmentType,
            LineNumber = segment.LineNumber,
            OriginalText = segment.OriginalText,
            SourceKey = segment.SourceKey,
            FieldIndex = segment.FieldIndex,
            CsvFieldRole = segment.CsvFieldRole,
            PreserveWhitespace = segment.PreserveWhitespace,
            SymbolNamespace = segment.SymbolNamespace,
            OriginalSymbolKey = segment.OriginalSymbolKey,
            IsReferenceBearingKey = segment.IsReferenceBearingKey,
            ReferenceOriginalSymbolKey = segment.OriginalSymbolKey,
            WarningText = warningText,
        }).ToList();

        return new ScannedFileResult(
            document,
            items,
            erbItemCount,
            csvItemCount,
            document.ScanWarnings.Count,
            document.JosaAnalysis.PatternCount);
    }

    private static List<ExtractedTextItem> BuildIdentifierItems(ScanSession session)
    {
        var items = new List<ExtractedTextItem>();
        var occurrences = session.Documents.Values
            .Where(document => DocumentFileTypes.IsErbLike(document.FileType))
            .SelectMany(document => document.IdentifierOccurrences.Select(occurrence => (document, occurrence)))
            .OrderBy(pair => pair.occurrence.Kind)
            .ThenBy(pair => pair.occurrence.OriginalName, StringComparer.Ordinal)
            .ThenBy(pair => pair.document.RelativePath, StringComparer.Ordinal)
            .ThenBy(pair => pair.occurrence.AbsoluteStart)
            .GroupBy(pair => (pair.occurrence.Kind, pair.occurrence.OriginalName), pair => pair, IdentifierOccurrenceKeyComparer.Instance);

        foreach (var group in occurrences)
        {
            var first = group.First();
            var document = first.document;
            var occurrence = first.occurrence;
            var segmentType = IdentifierSegmentTypes.ForKind(occurrence.Kind);
            var segmentId = $"identifier:{(occurrence.Kind == EraTranslator.Models.ErbIdentifierKind.Function ? "function" : "variable")}:{occurrence.OriginalName}";
            var segment = new TextSegment
            {
                SegmentId = segmentId,
                DocumentId = document.DocumentId,
                SegmentType = segmentType,
                AbsoluteStart = occurrence.AbsoluteStart,
                Length = occurrence.Length,
                LineNumber = occurrence.LineNumber,
                OriginalText = occurrence.OriginalName,
                SourceKey = IdentifierSegmentTypes.SourceKeyFor(occurrence.Kind, occurrence.OriginalName),
            };
            document.Segments.Add(segment);
            items.Add(new ExtractedTextItem
            {
                SegmentId = segment.SegmentId,
                DocumentId = document.DocumentId,
                FileType = document.FileType,
                RelativePath = document.RelativePath,
                EncodingName = document.EncodingInfo.Name,
                SegmentType = segment.SegmentType,
                LineNumber = segment.LineNumber,
                OriginalText = segment.OriginalText,
                SourceKey = segment.SourceKey,
                PreserveWhitespace = false,
                WarningText = string.Join(" | ", document.ScanWarnings),
            });
        }

        return items;
    }

    private sealed class IdentifierOccurrenceKeyComparer : IEqualityComparer<(EraTranslator.Models.ErbIdentifierKind Kind, string OriginalName)>
    {
        public static IdentifierOccurrenceKeyComparer Instance { get; } = new();

        public bool Equals(
            (EraTranslator.Models.ErbIdentifierKind Kind, string OriginalName) x,
            (EraTranslator.Models.ErbIdentifierKind Kind, string OriginalName) y)
        {
            return x.Kind == y.Kind && string.Equals(x.OriginalName, y.OriginalName, StringComparison.Ordinal);
        }

        public int GetHashCode((EraTranslator.Models.ErbIdentifierKind Kind, string OriginalName) obj)
        {
            return HashCode.Combine(obj.Kind, StringComparer.Ordinal.GetHashCode(obj.OriginalName));
        }
    }

    private static string DetectNewLine(string content)
    {
        var crlf = content.IndexOf("\r\n", StringComparison.Ordinal);
        if (crlf >= 0)
        {
            return "\r\n";
        }

        return content.Contains('\n') ? "\n" : Environment.NewLine;
    }

    private static string DecodeContent(byte[] bytes, DetectedEncodingInfo encodingInfo)
    {
        var content = encodingInfo.Encoding.GetString(bytes);
        if (encodingInfo.HasBom && content.Length > 0 && content[0] == '\uFEFF')
        {
            return content[1..];
        }

        return content;
    }

    private static IEnumerable<string> FindErbWarnings(string content)
    {
        if (content.Contains("PRINTDATA", StringComparison.OrdinalIgnoreCase)
            && !content.Contains("ENDDATA", StringComparison.OrdinalIgnoreCase))
        {
            yield return "PRINTDATA 블록 종료를 찾지 못했습니다. 추출 결과를 확인하세요.";
        }

        if (content.Contains("DATALIST", StringComparison.OrdinalIgnoreCase)
            && !content.Contains("ENDLIST", StringComparison.OrdinalIgnoreCase))
        {
            yield return "DATALIST 블록 종료를 찾지 못했습니다. 추출 결과를 확인하세요.";
        }
    }

    private sealed record ScanTargetFile(int Index, string Path);

    private sealed record ScannedFileResult(
        SourceFileDocument Document,
        List<ExtractedTextItem> Items,
        int ErbItemCount,
        int CsvItemCount,
        int WarningCount,
        int JosaPatternCount);

    private sealed class ScanProgressReporter
    {
        private readonly object _gate = new();
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly IProgress<(double value, string detail)>? _progress;
        private readonly int _totalFiles;
        private int _lastReportedCompletedCount;
        private long _lastReportedMilliseconds;

        public ScanProgressReporter(int totalFiles, IProgress<(double value, string detail)>? progress)
        {
            _totalFiles = Math.Max(totalFiles, 1);
            _progress = progress;
        }

        public void Report(int completedCount, string relativePath)
        {
            if (_progress is null)
            {
                return;
            }

            lock (_gate)
            {
                var elapsedMilliseconds = _stopwatch.ElapsedMilliseconds;
                var shouldReport = completedCount >= _totalFiles
                    || completedCount - _lastReportedCompletedCount >= 32
                    || elapsedMilliseconds - _lastReportedMilliseconds >= 100;
                if (!shouldReport)
                {
                    return;
                }

                _lastReportedCompletedCount = completedCount;
                _lastReportedMilliseconds = elapsedMilliseconds;
                var progressValue = (completedCount / (double)_totalFiles) * 0.92;
                _progress.Report((progressValue, $"스캔 중: {relativePath}"));
            }
        }
    }
}
