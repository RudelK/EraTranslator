using System.Text;
using EraTranslator.Models;
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

    [Fact]
    public void Scan_DataDirectoryCollectsSupportedFilesWithoutDuplicates()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var dataDir = Path.Combine(tempRoot, "data");
        var nestedCsvDir = Path.Combine(dataDir, "CSV");
        var nestedErbDir = Path.Combine(dataDir, "ERB");
        Directory.CreateDirectory(nestedCsvDir);
        Directory.CreateDirectory(nestedErbDir);
        File.WriteAllText(Path.Combine(nestedCsvDir, "OPTION変数.csv"), "3,妊娠切り替え\r\n", Encoding.UTF8);
        File.WriteAllText(Path.Combine(nestedErbDir, "Test.ERB"), "SIF OPTION変数:妊娠切り替え\r\n", Encoding.UTF8);
        File.WriteAllText(Path.Combine(dataDir, "Ignored.txt"), "무시", Encoding.UTF8);

        try
        {
            var session = new FileScanner().Scan(tempRoot);

            Assert.Equal(2, session.Documents.Count);
            Assert.Contains(session.Documents.Keys, key => key.EndsWith("OPTION変数.csv", StringComparison.Ordinal));
            Assert.Contains(session.Documents.Keys, key => key.EndsWith("Test.ERB", StringComparison.Ordinal));
            Assert.DoesNotContain(session.Documents.Keys, key => key.EndsWith("Ignored.txt", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Scan_AnalyzesJosaPatternsForErbOnly()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var erbDir = Path.Combine(tempRoot, "ERB");
        Directory.CreateDirectory(erbDir);
        File.WriteAllText(Path.Combine(erbDir, "Main.ERB"), "PRINTFORMW %플레이어은%\r\n", Encoding.UTF8);
        File.WriteAllText(Path.Combine(erbDir, "Common.ERH"), "PRINTFORMW %플레이어은%\r\n", Encoding.UTF8);

        try
        {
            var session = new FileScanner().Scan(tempRoot);

            Assert.True(session.Documents["ERB/Main.ERB"].JosaAnalysis.PatternCount > 0);
            Assert.Equal(0, session.Documents["ERB/Common.ERH"].JosaAnalysis.PatternCount);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Scan_ErbDirectoryErdFilesUseCsvExtractionAndNamespaceWithoutDimensionSuffix()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var erbDir = Path.Combine(tempRoot, "ERB");
        Directory.CreateDirectory(erbDir);
        File.WriteAllText(Path.Combine(erbDir, "BATTLE_STATE@2.ERD"), "0,ＨＰ\r\n1,攻撃力\r\n", Encoding.UTF8);
        File.WriteAllText(Path.Combine(erbDir, "Test.ERB"), "SIF BATTLE_STATE:TARGET:ＨＰ > 0\r\n", Encoding.UTF8);

        try
        {
            var session = new FileScanner().Scan(tempRoot);
            var document = session.Documents["ERB/BATTLE_STATE@2.ERD"];
            var item = Assert.Single(session.Items, item => item.OriginalText == "ＨＰ");

            Assert.Equal(DocumentFileTypes.Erd, document.FileType);
            Assert.True(DocumentFileTypes.IsCsvLike(document.FileType));
            Assert.Equal(CsvDocumentKind.IdFirstTable, document.CsvKind);
            Assert.Equal("BATTLE_STATE", item.SymbolNamespace);
            Assert.Equal("ＨＰ", item.OriginalSymbolKey);
            Assert.True(item.IsReferenceBearingKey);
            Assert.True(item.ReferenceImpactCount > 0);
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
