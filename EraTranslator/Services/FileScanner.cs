using System.Text;

namespace EraTranslator.Services;

public sealed class FileScanner
{
    private static readonly string[] ErbExtensions = [".erb", ".era"];
    private static readonly string[] CsvExtensions = [".csv", ".cvs"];
    private readonly EncodingDetector _encodingDetector = new();
    private readonly ErbExtractor _erbExtractor = new();
    private readonly ErbReferenceExtractor _erbReferenceExtractor = new();
    private readonly CsvExtractor _csvExtractor = new();
    private readonly SymbolReferenceAnalyzer _symbolReferenceAnalyzer = new();
    private readonly JosaSupportPackageService _josaSupportPackageService = new();
    private readonly JosaPatternAnalyzer _josaPatternAnalyzer = new();

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
        var translatableErbSegments = 0;
        var translatableCsvSegments = 0;
        var warningCount = 0;
        var josaPatternCount = 0;
        var files = EnumerateTargetFiles(gameRoot).ToList();

        for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = files[fileIndex];
            var bytes = File.ReadAllBytes(path);
            var encodingInfo = _encodingDetector.Detect(bytes);
            var content = encodingInfo.Encoding.GetString(bytes);
            var relativePath = Path.GetRelativePath(gameRoot, path);
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var fileType = CsvExtensions.Contains(extension) ? "CSV" : "ERB";
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
            if (fileType == "CSV")
            {
                var csvResult = _csvExtractor.Extract(documentId, relativePath, content);
                document.CsvKind = csvResult.kind;
                document.ScanWarnings.AddRange(csvResult.warnings);
                segments = csvResult.segments;
                translatableCsvSegments += segments.Count;
            }
            else
            {
                document.CsvKind = CsvDocumentKind.None;
                document.ScanWarnings.AddRange(FindErbWarnings(content));
                segments = _erbExtractor.Extract(documentId, content);
                var referenceResult = _erbReferenceExtractor.Extract(documentId, content);
                document.SymbolReferences.AddRange(referenceResult.references);
                document.VariableLiteralOccurrences.AddRange(referenceResult.variableLiterals);
                document.JosaAnalysis = _josaPatternAnalyzer.AnalyzeDocument(content, session.JosaPackageInfo);
                josaPatternCount += document.JosaAnalysis.PatternCount;
                translatableErbSegments += segments.Count;
            }

            document.Segments.AddRange(segments);
            session.Documents.Add(documentId, document);
            warningCount += document.ScanWarnings.Count;

            foreach (var segment in segments)
            {
                session.Items.Add(new ExtractedTextItem
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
                    SymbolNamespace = segment.SymbolNamespace,
                    OriginalSymbolKey = segment.OriginalSymbolKey,
                    IsReferenceBearingKey = segment.IsReferenceBearingKey,
                    WarningText = string.Join(" | ", document.ScanWarnings),
                });
            }

            progress?.Report((((fileIndex + 1) / (double)Math.Max(files.Count, 1)) * 0.95, $"스캔 중: {relativePath}"));
        }

        session.Metrics["Documents"] = session.Documents.Count;
        session.Metrics["Items"] = session.Items.Count;
        session.Metrics["ErbItems"] = translatableErbSegments;
        session.Metrics["CsvItems"] = translatableCsvSegments;
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
        var files = EnumerateTargetFiles(gameRoot).ToList();

        for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = files[fileIndex];
            var bytes = File.ReadAllBytes(path);
            var encodingInfo = _encodingDetector.Detect(bytes);
            var relativePath = Path.GetRelativePath(gameRoot, path);
            if (!encodingInfo.CanConvertToUtf8Bom)
            {
                progress?.Report((((fileIndex + 1) / (double)Math.Max(files.Count, 1)) * 0.95, $"건너뜀: {relativePath}"));
                continue;
            }

            var text = encodingInfo.Encoding.GetString(bytes);
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
                var name = Path.GetFileName(path);
                return name.Equals("ERB", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("CSV", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("CVS", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        foreach (var directory in directories)
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(file).ToLowerInvariant();
                if (ErbExtensions.Contains(extension) || CsvExtensions.Contains(extension))
                {
                    yield return file;
                }
            }
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
}
