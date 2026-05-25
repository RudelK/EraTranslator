using System.Text;
using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class OutputWriterTests
{
    [Fact]
    public void ExportCopy_WritesTranslatedCsvWithoutChangingKey()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(gameRoot);

        var document = new SourceFileDocument
        {
            DocumentId = "CSV/GameBase.csv",
            FullPath = Path.Combine(gameRoot, "CSV", "GameBase.csv"),
            RelativePath = Path.Combine("CSV", "GameBase.csv"),
            FileType = "CSV",
            OriginalText = "タイトル,era魔界牧場\r\n作者,polt\r\n",
            EncodingInfo = new DetectedEncodingInfo
            {
                Encoding = new UTF8Encoding(true),
                Name = "UTF-8 BOM",
                Kind = DetectedEncodingKind.Utf8Bom,
                HasBom = true,
            },
            NewLineSequence = "\r\n",
            CsvKind = CsvDocumentKind.KeyValue,
        };

        document.Segments.Add(new TextSegment
        {
            SegmentId = "CSV/GameBase.csv:0",
            DocumentId = document.DocumentId,
            SegmentType = "csv-KeyValue-field-1",
            AbsoluteStart = 4,
            Length = "era魔界牧場".Length,
            LineNumber = 1,
            OriginalText = "era魔界牧場",
            FieldIndex = 1,
            SourceKey = "タイトル",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
        });

        var session = new ScanSession
        {
            GameRoot = gameRoot,
        };
        session.Documents.Add(document.DocumentId, document);
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = "CSV/GameBase.csv:0",
            DocumentId = document.DocumentId,
            FileType = "CSV",
            RelativePath = document.RelativePath,
            EncodingName = "UTF-8 BOM",
            SegmentType = "csv-KeyValue-field-1",
            LineNumber = 1,
            OriginalText = "era魔界牧場",
            SourceKey = "タイトル",
            FieldIndex = 1,
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            TranslatedText = "era마계목장",
            Status = "번역 완료",
            ValidationStatus = "통과",
            WarningText = string.Empty,
        });

        var writer = new OutputWriter();
        var result = writer.Save(session, exportRoot, SaveMode.ExportCopy);

        Assert.Equal(3, result.WrittenFiles.Count);
        var written = File.ReadAllText(Path.Combine(exportRoot, "CSV", "GameBase.csv"), Encoding.UTF8);
        Assert.Contains("タイトル,era마계목장", written, StringComparison.Ordinal);
        Assert.DoesNotContain("타이틀", written, StringComparison.Ordinal);
        Assert.Contains(result.WrittenFiles, path => string.Equals(path, Path.Combine(exportRoot, "ERB", "ZNAME.ERB"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.WrittenFiles, path => string.Equals(path, Path.Combine(exportRoot, "ERB", "ZNAME.ERH"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InPlaceWithBackup_CreatesBackupAndUpdatesOriginal()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var csvDir = Path.Combine(gameRoot, "CSV");
        Directory.CreateDirectory(csvDir);
        var filePath = Path.Combine(csvDir, "GameBase.csv");
        File.WriteAllText(filePath, "タイトル,era魔界牧場\r\n", Encoding.UTF8);

        var document = new SourceFileDocument
        {
            DocumentId = "CSV/GameBase.csv",
            FullPath = filePath,
            RelativePath = Path.Combine("CSV", "GameBase.csv"),
            FileType = "CSV",
            OriginalText = "タイトル,era魔界牧場\r\n",
            EncodingInfo = new DetectedEncodingInfo
            {
                Encoding = Encoding.UTF8,
                Name = "UTF-8",
                Kind = DetectedEncodingKind.Utf8,
                HasBom = false,
            },
            NewLineSequence = "\r\n",
            CsvKind = CsvDocumentKind.KeyValue,
        };

        document.Segments.Add(new TextSegment
        {
            SegmentId = "CSV/GameBase.csv:0",
            DocumentId = document.DocumentId,
            SegmentType = "csv-KeyValue-field-1",
            AbsoluteStart = 4,
            Length = "era魔界牧場".Length,
            LineNumber = 1,
            OriginalText = "era魔界牧場",
            FieldIndex = 1,
            SourceKey = "タイトル",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
        });

        var session = new ScanSession
        {
            GameRoot = gameRoot,
        };
        session.Documents.Add(document.DocumentId, document);
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = "CSV/GameBase.csv:0",
            DocumentId = document.DocumentId,
            FileType = "CSV",
            RelativePath = document.RelativePath,
            EncodingName = "UTF-8",
            SegmentType = "csv-KeyValue-field-1",
            LineNumber = 1,
            OriginalText = "era魔界牧場",
            SourceKey = "タイトル",
            FieldIndex = 1,
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            TranslatedText = "era마계목장",
            Status = "번역 완료",
            ValidationStatus = "통과",
            WarningText = string.Empty,
        });

        var writer = new OutputWriter();
        var result = writer.Save(session, string.Empty, SaveMode.InPlaceWithBackup);

        Assert.Equal(3, result.WrittenFiles.Count);
        Assert.Single(result.BackupFiles);
        Assert.Contains("タイトル,era마계목장", File.ReadAllText(filePath, Encoding.UTF8), StringComparison.Ordinal);
        Assert.Contains("タイトル,era魔界牧場", File.ReadAllText(result.BackupFiles[0], Encoding.UTF8), StringComparison.Ordinal);
    }

    [Fact]
    public void ExportCopy_RewritesDirectAndIndirectErbReferencesAlongsideCsvKeyRename()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(Path.Combine(gameRoot, "CSV"));
        Directory.CreateDirectory(Path.Combine(gameRoot, "ERB"));

        var csvDocument = new SourceFileDocument
        {
            DocumentId = "CSV/Cflag.csv",
            FullPath = Path.Combine(gameRoot, "CSV", "Cflag.csv"),
            RelativePath = Path.Combine("CSV", "Cflag.csv"),
            FileType = "CSV",
            OriginalText = "3179,外見年齢,\r\n",
            EncodingInfo = new DetectedEncodingInfo
            {
                Encoding = Encoding.UTF8,
                Name = "UTF-8",
                Kind = DetectedEncodingKind.Utf8,
                HasBom = false,
            },
            NewLineSequence = "\r\n",
            CsvKind = CsvDocumentKind.IdFirstTable,
        };
        csvDocument.Segments.Add(new TextSegment
        {
            SegmentId = "CSV/Cflag.csv:0",
            DocumentId = csvDocument.DocumentId,
            SegmentType = "csv-IdFirstTable-field-1",
            AbsoluteStart = 5,
            Length = "外見年齢".Length,
            LineNumber = 1,
            OriginalText = "外見年齢",
            FieldIndex = 1,
            SourceKey = "3179",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            SymbolNamespace = "CFLAG",
            OriginalSymbolKey = "外見年齢",
            IsReferenceBearingKey = true,
        });

        var erbText = """
IF CFLAG:外見年齢
flagName = "外見年齢"
IF CFLAG:{flagName}
""".Replace("\n", "\r\n", StringComparison.Ordinal);
        var erbDocument = new SourceFileDocument
        {
            DocumentId = "ERB/Test.ERB",
            FullPath = Path.Combine(gameRoot, "ERB", "Test.ERB"),
            RelativePath = Path.Combine("ERB", "Test.ERB"),
            FileType = "ERB",
            OriginalText = erbText,
            EncodingInfo = new DetectedEncodingInfo
            {
                Encoding = Encoding.UTF8,
                Name = "UTF-8",
                Kind = DetectedEncodingKind.Utf8,
                HasBom = false,
            },
            NewLineSequence = "\r\n",
            CsvKind = CsvDocumentKind.None,
        };

        var referenceExtractor = new ErbReferenceExtractor();
        var extractedReferences = referenceExtractor.Extract(erbDocument.DocumentId, erbText);
        erbDocument.SymbolReferences.AddRange(extractedReferences.references);
        erbDocument.VariableLiteralOccurrences.AddRange(extractedReferences.variableLiterals);

        var session = new ScanSession
        {
            GameRoot = gameRoot,
        };
        session.Documents[csvDocument.DocumentId] = csvDocument;
        session.Documents[erbDocument.DocumentId] = erbDocument;
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = "CSV/Cflag.csv:0",
            DocumentId = csvDocument.DocumentId,
            FileType = "CSV",
            RelativePath = csvDocument.RelativePath,
            EncodingName = "UTF-8",
            SegmentType = "csv-IdFirstTable-field-1",
            LineNumber = 1,
            OriginalText = "外見年齢",
            SourceKey = "3179",
            FieldIndex = 1,
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            SymbolNamespace = "CFLAG",
            OriginalSymbolKey = "外見年齢",
            IsReferenceBearingKey = true,
            TranslatedText = "외견연령",
            Status = "번역 완료",
            ValidationStatus = "통과",
            WarningText = string.Empty,
        });

        new SymbolReferenceAnalyzer().Analyze(session);

        var writer = new OutputWriter();
        var result = writer.Save(session, exportRoot, SaveMode.ExportCopy);

        Assert.Equal(4, result.WrittenFiles.Count);
        var writtenCsv = File.ReadAllText(Path.Combine(exportRoot, "CSV", "Cflag.csv"), Encoding.UTF8);
        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);
        Assert.Contains("3179,외견연령,", writtenCsv, StringComparison.Ordinal);
        Assert.Contains("CFLAG:외견연령", writtenErb, StringComparison.Ordinal);
        Assert.Contains("\"외견연령\"", writtenErb, StringComparison.Ordinal);
        Assert.Contains("CFLAG:{flagName}", writtenErb, StringComparison.Ordinal);
        Assert.Contains(result.WrittenFiles, path => string.Equals(path, Path.Combine(exportRoot, "ERB", "ZNAME.ERB"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.WrittenFiles, path => string.Equals(path, Path.Combine(exportRoot, "ERB", "ZNAME.ERH"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExportCopy_RewritesVariableIndexedInlineReferencesInsideTranslatedSegment()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(Path.Combine(gameRoot, "ERB"));

        const string originalValue = "ステータス式";
        var erbText = $"PRINTFORMW \"{originalValue}\"\r\n";
        var segmentStart = erbText.IndexOf(originalValue, StringComparison.Ordinal);
        var erbDocument = new SourceFileDocument
        {
            DocumentId = "ERB/TestInline.ERB",
            FullPath = Path.Combine(gameRoot, "ERB", "TestInline.ERB"),
            RelativePath = Path.Combine("ERB", "TestInline.ERB"),
            FileType = "ERB",
            OriginalText = erbText,
            EncodingInfo = new DetectedEncodingInfo
            {
                Encoding = Encoding.UTF8,
                Name = "UTF-8",
                Kind = DetectedEncodingKind.Utf8,
                HasBom = false,
            },
            NewLineSequence = "\r\n",
            CsvKind = CsvDocumentKind.None,
        };
        erbDocument.Segments.Add(new TextSegment
        {
            SegmentId = "ERB/TestInline.ERB:0",
            DocumentId = erbDocument.DocumentId,
            SegmentType = "quoted-string",
            AbsoluteStart = segmentStart,
            Length = originalValue.Length,
            LineNumber = 1,
            OriginalText = originalValue,
        });

        var session = new ScanSession
        {
            GameRoot = gameRoot,
        };
        session.Documents[erbDocument.DocumentId] = erbDocument;
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = "ERB/TestInline.ERB:0",
            DocumentId = erbDocument.DocumentId,
            FileType = "ERB",
            RelativePath = erbDocument.RelativePath,
            EncodingName = "UTF-8",
            SegmentType = "quoted-string",
            LineNumber = 1,
            OriginalText = originalValue,
            TranslatedText = "ABL:index:従順 * 10 + EXP:index:愛情経験 + CFLAG:index:依存度",
            Status = "번역 완료",
            ValidationStatus = "통과",
            WarningText = string.Empty,
        });
        session.Items.Add(CreateReferenceBearingItem("CSV/Abl.csv:0", "CSV/Abl.csv", "ABL", "従順", "순종"));
        session.Items.Add(CreateReferenceBearingItem("CSV/Exp.csv:0", "CSV/Exp.csv", "EXP", "愛情経験", "애정경험"));
        session.Items.Add(CreateReferenceBearingItem("CSV/Cflag.csv:0", "CSV/Cflag.csv", "CFLAG", "依存度", "의존도"));

        var writer = new OutputWriter();
        var result = writer.Save(session, exportRoot, SaveMode.ExportCopy);

        Assert.Equal(3, result.WrittenFiles.Count);
        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "TestInline.ERB"), Encoding.UTF8);
        Assert.Contains("ABL:index:순종 * 10 + EXP:index:애정경험 + CFLAG:index:의존도", writtenErb, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportCopy_WritesBundledJosaPackageAndConvertsPostfixPatterns()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(Path.Combine(gameRoot, "ERB"));

        var erbText = """
#INCLUDE "ZNAME.ERH"
PRINTFORMW %CALLNAME:MASTER%(은)는 왔다
PRINTFORMW %플레이어는()% 기다린다
PRINTFORMW %NAME:TARGET%(을)를 본다
""".Replace("\n", "\r\n", StringComparison.Ordinal);
        var erbDocument = new SourceFileDocument
        {
            DocumentId = "ERB/Test.ERB",
            FullPath = Path.Combine(gameRoot, "ERB", "Test.ERB"),
            RelativePath = Path.Combine("ERB", "Test.ERB"),
            FileType = "ERB",
            OriginalText = erbText,
            EncodingInfo = new DetectedEncodingInfo
            {
                Encoding = Encoding.UTF8,
                Name = "UTF-8",
                Kind = DetectedEncodingKind.Utf8,
                HasBom = false,
            },
            NewLineSequence = "\r\n",
            CsvKind = CsvDocumentKind.None,
            JosaAnalysis = new JosaPatternAnalyzer().AnalyzeDocument(erbText, new JosaSupportPackageService().InspectProject(gameRoot)),
        };

        var session = new ScanSession
        {
            GameRoot = gameRoot,
            JosaPackageInfo = new JosaSupportPackageService().InspectProject(gameRoot),
        };
        session.Documents[erbDocument.DocumentId] = erbDocument;

        var writer = new OutputWriter();
        var result = writer.Save(session, exportRoot, SaveMode.ExportCopy);

        Assert.Contains(result.WrittenFiles, path => string.Equals(path, Path.Combine(exportRoot, "ERB", "ZNAME.ERB"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.WrittenFiles, path => string.Equals(path, Path.Combine(exportRoot, "ERB", "ZNAME.ERH"), StringComparison.OrdinalIgnoreCase));

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);
        Assert.Contains("%플레이어는%", writtenErb, StringComparison.Ordinal);
        Assert.Contains("%조사처리(NAME:TARGET,\"을\")%", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("%플레이어는()% ", writtenErb, StringComparison.Ordinal);
    }

    private static ExtractedTextItem CreateReferenceBearingItem(
        string segmentId,
        string documentId,
        string symbolNamespace,
        string originalKey,
        string translatedText)
    {
        return new ExtractedTextItem
        {
            SegmentId = segmentId,
            DocumentId = documentId,
            FileType = "CSV",
            RelativePath = documentId.Replace('/', Path.DirectorySeparatorChar),
            EncodingName = "UTF-8",
            SegmentType = "csv-IdFirstTable-field-1",
            LineNumber = 1,
            OriginalText = originalKey,
            SourceKey = "1",
            FieldIndex = 1,
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            SymbolNamespace = symbolNamespace,
            OriginalSymbolKey = originalKey,
            IsReferenceBearingKey = true,
            TranslatedText = translatedText,
            Status = "번역 완료",
            ValidationStatus = "통과",
            WarningText = string.Empty,
        };
    }

    [Fact]
    public void ExportCopy_InsertsErhIncludeWhenMacroConversionRequiresIt()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(Path.Combine(gameRoot, "ERB"));

        var erbText = """
; header comment

PRINTFORMW %CALLNAME:MASTER%(은)는 왔다
""".Replace("\n", "\r\n", StringComparison.Ordinal);
        var erbDocument = new SourceFileDocument
        {
            DocumentId = "ERB/Test2.ERB",
            FullPath = Path.Combine(gameRoot, "ERB", "Test2.ERB"),
            RelativePath = Path.Combine("ERB", "Test2.ERB"),
            FileType = "ERB",
            OriginalText = erbText,
            EncodingInfo = new DetectedEncodingInfo
            {
                Encoding = Encoding.UTF8,
                Name = "UTF-8",
                Kind = DetectedEncodingKind.Utf8,
                HasBom = false,
            },
            NewLineSequence = "\r\n",
            CsvKind = CsvDocumentKind.None,
            JosaAnalysis = new JosaPatternAnalyzer().AnalyzeDocument(erbText, new JosaSupportPackageInfo()),
        };

        var session = new ScanSession
        {
            GameRoot = gameRoot,
            JosaPackageInfo = new JosaSupportPackageService().InspectProject(gameRoot),
        };
        session.Documents[erbDocument.DocumentId] = erbDocument;

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test2.ERB"), Encoding.UTF8);
        Assert.Contains("#INCLUDE \"ZNAME.ERH\"", writtenErb, StringComparison.Ordinal);
        Assert.Contains("%플레이어는%", writtenErb, StringComparison.Ordinal);
    }
}
