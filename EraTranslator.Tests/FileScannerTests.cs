using System.Text;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class FileScannerTests
{
    [Fact]
    public void Scan_StripsLeadingBomFromUtf8BomContent()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var csvDir = Path.Combine(gameRoot, "CSV");
        Directory.CreateDirectory(csvDir);
        var path = Path.Combine(csvDir, "GameBase.csv");
        File.WriteAllText(path, "番号,407,\r\n名前,テスト,\r\n", new UTF8Encoding(true));

        var scanner = new FileScanner();
        var session = scanner.Scan(gameRoot);
        var document = Assert.Single(session.Documents.Values);

        Assert.False(document.OriginalText.StartsWith('\uFEFF'));
        Assert.StartsWith("番号,407,", document.OriginalText, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_ParallelAndSequentialModes_ProduceEquivalentResults()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var csvDir = Path.Combine(tempRoot, "CSV");
        var erbDir = Path.Combine(tempRoot, "ERB");
        Directory.CreateDirectory(csvDir);
        Directory.CreateDirectory(erbDir);
        File.WriteAllText(Path.Combine(csvDir, "Item.csv"), "0,薬草\r\n1,魔石\r\n");
        File.WriteAllText(Path.Combine(csvDir, "GameBase.csv"), "番号,407,\r\n名前,テスト,\r\n", new UTF8Encoding(true));
        File.WriteAllText(
            Path.Combine(erbDir, "Test.ERB"),
            """
PRINTFORMW "こんにちは"
name = 高校生 + "です"
PRINTFORMW GETNUM(CFLAG,"外見年齢")
""",
            new UTF8Encoding(false));

        try
        {
            var sequential = new FileScanner(1).Scan(tempRoot);
            var parallel = new FileScanner(2).Scan(tempRoot);

            Assert.Equal(ProjectSessionSummary(sequential), ProjectSessionSummary(parallel));
            Assert.Equal(ProjectDocumentSummary(sequential), ProjectDocumentSummary(parallel));
            Assert.Equal(ProjectItemSummary(sequential), ProjectItemSummary(parallel));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string ProjectSessionSummary(Models.ScanSession session)
    {
        return string.Join(
            "|",
            session.Metrics.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}:{pair.Value}"));
    }

    private static string ProjectDocumentSummary(Models.ScanSession session)
    {
        return string.Join(
            "\n",
            session.Documents
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair =>
                {
                    var document = pair.Value;
                    var segmentSummary = string.Join(
                        ",",
                        document.Segments.Select(segment => $"{segment.SegmentId}:{segment.SegmentType}:{segment.LineNumber}:{segment.OriginalText}"));
                    var referenceSummary = string.Join(
                        ",",
                        document.SymbolReferences.Select(reference =>
                            $"{reference.Namespace}:{reference.OriginalKey}:{reference.Kind}:{reference.ResolutionKind}:{string.Join("/", reference.CandidateKeys.OrderBy(key => key, StringComparer.Ordinal))}"));
                    var literalSummary = string.Join(
                        ",",
                        document.VariableLiteralOccurrences.Select(occurrence => $"{occurrence.VariableName}:{occurrence.LiteralValue}:{occurrence.LineNumber}"));
                    var warningSummary = string.Join(",", document.ScanWarnings);
                    return string.Join(
                        "|",
                        document.DocumentId,
                        document.FileType,
                        document.CsvKind,
                        document.JosaAnalysis.PatternCount,
                        segmentSummary,
                        referenceSummary,
                        literalSummary,
                        warningSummary);
                }));
    }

    private static string ProjectItemSummary(Models.ScanSession session)
    {
        return string.Join(
            "\n",
            session.Items.Select(item => string.Join(
                "|",
                item.SegmentId,
                item.DocumentId,
                item.FileType,
                item.RelativePath,
                item.SegmentType,
                item.LineNumber,
                item.OriginalText,
                item.SourceKey ?? string.Empty,
                item.FieldIndex?.ToString() ?? string.Empty,
                item.CsvFieldRole,
                item.PreserveWhitespace,
                item.SymbolNamespace ?? string.Empty,
                item.OriginalSymbolKey ?? string.Empty,
                item.ReferenceOriginalSymbolKey ?? string.Empty,
                item.ReferenceImpactCount,
                item.RequiresReferenceRewrite,
                item.ReferenceResolutionStatus,
                item.WarningText)));
    }
}
