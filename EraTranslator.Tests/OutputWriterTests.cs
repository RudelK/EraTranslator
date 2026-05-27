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
    public void ExportCopy_WritesSingleBomForUtf8BomDocument()
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
            OriginalText = "番号,407,\r\n名前,テスト,\r\n",
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
            AbsoluteStart = 13,
            Length = "テスト".Length,
            LineNumber = 2,
            OriginalText = "テスト",
            FieldIndex = 1,
            SourceKey = "名前",
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
            LineNumber = 2,
            OriginalText = "テスト",
            SourceKey = "名前",
            FieldIndex = 1,
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            TranslatedText = "테스트",
            Status = "번역 완료",
            ValidationStatus = "통과",
            WarningText = string.Empty,
        });

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var bytes = File.ReadAllBytes(Path.Combine(exportRoot, "CSV", "GameBase.csv"));
        Assert.True(bytes.Length >= 6);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
        Assert.NotEqual(0xEF, bytes[3]);
    }

    [Fact]
    public void ExportCopy_WritesBundledJosaPackageWithUtf8Bom()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(gameRoot);

        var session = new ScanSession
        {
            GameRoot = gameRoot,
        };

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var bytes = File.ReadAllBytes(Path.Combine(exportRoot, "ERB", "ZNAME.ERH"));
        Assert.True(bytes.Length >= 3);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
    }

    [Fact]
    public void ExportCopy_CopiesUnchangedSupportFilesFromGameRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        var csvDir = Path.Combine(gameRoot, "CSV");
        var supportDir = Path.Combine(gameRoot, "ERB", "汎用組み込み関数");
        Directory.CreateDirectory(csvDir);
        Directory.CreateDirectory(supportDir);

        var gameBasePath = Path.Combine(csvDir, "GameBase.csv");
        var supportPath = Path.Combine(supportDir, "MATHMATICAL_FUNCS.ERB");
        File.WriteAllText(gameBasePath, "タイトル,era魔界牧場\r\n", Encoding.UTF8);
        File.WriteAllText(supportPath, "@GET_PALAM_LV(palamNameStr)\r\n#FUNCTION\r\nRETURNF 0\r\n", Encoding.UTF8);

        var document = new SourceFileDocument
        {
            DocumentId = "CSV/GameBase.csv",
            FullPath = gameBasePath,
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
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        Assert.Equal(
            "@GET_PALAM_LV(palamNameStr)\r\n#FUNCTION\r\nRETURNF 0\r\n",
            File.ReadAllText(Path.Combine(exportRoot, "ERB", "汎用組み込み関数", "MATHMATICAL_FUNCS.ERB"), Encoding.UTF8));
        Assert.Contains("タイトル,era마계목장", File.ReadAllText(Path.Combine(exportRoot, "CSV", "GameBase.csv"), Encoding.UTF8), StringComparison.Ordinal);
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
    public void ExportCopy_RewritesErbReferencesForIdFirstReferenceTablesScannedFromDisk()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        var csvDir = Path.Combine(gameRoot, "CSV");
        var erbDir = Path.Combine(gameRoot, "ERB");
        Directory.CreateDirectory(csvDir);
        Directory.CreateDirectory(erbDir);

        File.WriteAllText(Path.Combine(csvDir, "Talent.csv"), "178,永久発情,;(エロいことを常に求めている)\r\n", Encoding.UTF8);
        File.WriteAllText(Path.Combine(csvDir, "Abl.csv"), "5,Ａ感覚\r\n", Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(erbDir, "Test.ERB"),
            "SIF TALENT:永久発情\r\nIF ABL:Ａ感覚 >= 3\r\n",
            Encoding.UTF8);

        var session = new FileScanner().Scan(gameRoot);
        foreach (var item in session.Items.Where(item => item.SymbolNamespace == "TALENT" && item.OriginalSymbolKey == "永久発情"))
        {
            item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "영구발정");
        }

        foreach (var item in session.Items.Where(item => item.SymbolNamespace == "ABL" && item.OriginalSymbolKey == "Ａ感覚"))
        {
            item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "A감각");
        }

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenTalent = File.ReadAllText(Path.Combine(exportRoot, "CSV", "Talent.csv"), Encoding.UTF8);
        var writtenAbl = File.ReadAllText(Path.Combine(exportRoot, "CSV", "Abl.csv"), Encoding.UTF8);
        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);

        Assert.Contains("178,영구발정,", writtenTalent, StringComparison.Ordinal);
        Assert.Contains("5,A감각", writtenAbl, StringComparison.Ordinal);
        Assert.Contains("TALENT:영구발정", writtenErb, StringComparison.Ordinal);
        Assert.Contains("ABL:A감각 >= 3", writtenErb, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportCopy_RewritesJuelReferencesFromPalamTable()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        var csvDir = Path.Combine(gameRoot, "CSV");
        var erbDir = Path.Combine(gameRoot, "ERB");
        Directory.CreateDirectory(csvDir);
        Directory.CreateDirectory(erbDir);

        File.WriteAllText(Path.Combine(csvDir, "Palam.csv"), "6,欲情\r\n", Encoding.UTF8);
        File.WriteAllText(Path.Combine(erbDir, "Test.ERB"), "SIF JUEL:欲情 < A\r\n", Encoding.UTF8);

        var session = new FileScanner().Scan(gameRoot);
        var palamItem = Assert.Single(
            session.Items,
            item => item.SymbolNamespace == "PALAM" && item.OriginalSymbolKey == "欲情");
        palamItem.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "욕정");

        new SymbolReferenceAnalyzer().Analyze(session);

        Assert.True(palamItem.RequiresReferenceRewrite);
        Assert.Equal(1, palamItem.ReferenceImpactCount);

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenPalam = File.ReadAllText(Path.Combine(exportRoot, "CSV", "Palam.csv"), Encoding.UTF8);
        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);

        Assert.Contains("6,욕정", writtenPalam, StringComparison.Ordinal);
        Assert.Contains("SIF JUEL:욕정 < A", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("JUEL:欲情", writtenErb, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportCopy_ReextractsErbReferencesBeforePlanning()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(Path.Combine(gameRoot, "ERB"));
        Directory.CreateDirectory(Path.Combine(gameRoot, "CSV"));

        File.WriteAllText(Path.Combine(gameRoot, "CSV", "Talent.csv"), "515,既婚\r\n", Encoding.UTF8);
        File.WriteAllText(Path.Combine(gameRoot, "CSV", "Base.csv"), "0,体力\r\n", Encoding.UTF8);

        var erbText = "IF TALENT:targetChara:既婚; comment\r\nCALL PRINT_COLORBAR, BASE:TARGET:体力, MAXBASE:TARGET:体力\r\n";
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

        var session = new ScanSession
        {
            GameRoot = gameRoot,
        };
        session.Documents[erbDocument.DocumentId] = erbDocument;
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = "CSV/Talent.csv:515",
            DocumentId = "CSV/Talent.csv",
            FileType = "CSV",
            RelativePath = Path.Combine("CSV", "Talent.csv"),
            EncodingName = "UTF-8",
            SegmentType = "csv-IdFirstTable-field-1",
            LineNumber = 1,
            OriginalText = "既婚",
            SourceKey = "515",
            FieldIndex = 1,
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            SymbolNamespace = "TALENT",
            OriginalSymbolKey = "既婚",
            IsReferenceBearingKey = true,
            TranslatedText = "기혼",
            Status = "번역 완료",
            ValidationStatus = "통과",
            CanSave = true,
            WarningText = string.Empty,
        });
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = "CSV/Base.csv:0",
            DocumentId = "CSV/Base.csv",
            FileType = "CSV",
            RelativePath = Path.Combine("CSV", "Base.csv"),
            EncodingName = "UTF-8",
            SegmentType = "csv-IdFirstTable-field-1",
            LineNumber = 1,
            OriginalText = "体力",
            SourceKey = "0",
            FieldIndex = 1,
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            SymbolNamespace = "BASE",
            OriginalSymbolKey = "体力",
            IsReferenceBearingKey = true,
            TranslatedText = "체력",
            Status = "번역 완료",
            ValidationStatus = "통과",
            CanSave = true,
            WarningText = string.Empty,
        });

        Assert.Empty(erbDocument.SymbolReferences);

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);
        Assert.Contains("TALENT:targetChara:기혼; comment", writtenErb, StringComparison.Ordinal);
        Assert.Contains("BASE:TARGET:체력", writtenErb, StringComparison.Ordinal);
        Assert.Contains("MAXBASE:TARGET:체력", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("既婚", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("体力", writtenErb, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportCopy_RewritesBothCanonicalAndCurrentReferenceKeysAfterTranslatedRescan()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(Path.Combine(gameRoot, "ERB"));

        var erbText = "SIF TALENT:永久発情\r\nSIF TALENT:영구발정\r\n";
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

        var extractedReferences = new ErbReferenceExtractor().Extract(erbDocument.DocumentId, erbText);
        erbDocument.SymbolReferences.AddRange(extractedReferences.references);
        erbDocument.VariableLiteralOccurrences.AddRange(extractedReferences.variableLiterals);

        var session = new ScanSession
        {
            GameRoot = gameRoot,
        };
        session.Documents[erbDocument.DocumentId] = erbDocument;
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = "CSV/Talent.csv:0",
            DocumentId = "CSV/Talent.csv",
            FileType = "CSV",
            RelativePath = Path.Combine("CSV", "Talent.csv"),
            EncodingName = "UTF-8",
            SegmentType = "csv-IdFirstTable-field-1",
            LineNumber = 1,
            OriginalText = "영구발정",
            SourceKey = "178",
            FieldIndex = 1,
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            SymbolNamespace = "TALENT",
            OriginalSymbolKey = "영구발정",
            IsReferenceBearingKey = true,
            ReferenceOriginalSymbolKey = "永久発情",
            TranslatedText = "상시발정",
            Status = "번역 완료",
            ValidationStatus = "통과",
            CanSave = true,
            WarningText = string.Empty,
        });

        new SymbolReferenceAnalyzer().Analyze(session);

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);
        Assert.Contains("TALENT:상시발정", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("TALENT:永久発情", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("TALENT:영구발정", writtenErb, StringComparison.Ordinal);
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

    [Fact]
    public void ExportCopy_DoesNotDuplicateJosaRewriteWhenTranslationWasPreprocessed()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(Path.Combine(gameRoot, "ERB"));

        var originalValue = "%CALLNAME:娼婦キャラ番号%은 시선을 피했다.";
        var preprocessedValue = "%조사처리(CALLNAME:娼婦キャラ番号,\"는\")% 시선을 피했다.";
        var erbText = $"PRINTFORMW {originalValue}\r\n";
        var valueStart = erbText.IndexOf(originalValue, StringComparison.Ordinal);
        var erbDocument = new SourceFileDocument
        {
            DocumentId = "ERB/JosaPreprocessed.ERB",
            FullPath = Path.Combine(gameRoot, "ERB", "JosaPreprocessed.ERB"),
            RelativePath = Path.Combine("ERB", "JosaPreprocessed.ERB"),
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
            SegmentId = "ERB/JosaPreprocessed.ERB:0",
            DocumentId = erbDocument.DocumentId,
            SegmentType = "print-tail",
            AbsoluteStart = valueStart,
            Length = originalValue.Length,
            LineNumber = 1,
            OriginalText = originalValue,
        });

        var session = new ScanSession
        {
            GameRoot = gameRoot,
            JosaPackageInfo = new JosaSupportPackageService().InspectProject(gameRoot),
        };
        session.Documents[erbDocument.DocumentId] = erbDocument;
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = "ERB/JosaPreprocessed.ERB:0",
            DocumentId = erbDocument.DocumentId,
            FileType = "ERB",
            RelativePath = erbDocument.RelativePath,
            EncodingName = "UTF-8",
            SegmentType = "print-tail",
            LineNumber = 1,
            OriginalText = originalValue,
            TranslatedText = preprocessedValue,
            Status = "번역 완료",
            ValidationStatus = "통과",
            WarningText = string.Empty,
        });

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "JosaPreprocessed.ERB"), Encoding.UTF8);
        Assert.Contains($"PRINTFORMW {preprocessedValue}", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("조사처리(조사처리", writtenErb, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(writtenErb, "%조사처리("));
    }

    [Fact]
    public void Save_ManuallyExcludedItemWritesOriginalText()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(Path.Combine(gameRoot, "ERB"));

        const string originalValue = "こんにちは";
        var erbText = $"PRINTFORMW \"{originalValue}\"\r\n";
        var segmentStart = erbText.IndexOf(originalValue, StringComparison.Ordinal);
        var erbDocument = new SourceFileDocument
        {
            DocumentId = "ERB/Excluded.ERB",
            FullPath = Path.Combine(gameRoot, "ERB", "Excluded.ERB"),
            RelativePath = Path.Combine("ERB", "Excluded.ERB"),
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
            SegmentId = "ERB/Excluded.ERB:0",
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
            SegmentId = "ERB/Excluded.ERB:0",
            DocumentId = erbDocument.DocumentId,
            FileType = "ERB",
            RelativePath = erbDocument.RelativePath,
            EncodingName = "UTF-8",
            SegmentType = "quoted-string",
            LineNumber = 1,
            OriginalText = originalValue,
            TranslatedText = string.Empty,
            Status = "제외됨",
            ValidationStatus = "언어 제외",
            CanSave = true,
            WarningText = string.Empty,
        });

        var writer = new OutputWriter();
        var result = writer.Save(session, exportRoot, SaveMode.ExportCopy);

        Assert.Contains(result.WrittenFiles, path => string.Equals(path, Path.Combine(exportRoot, "ERB", "Excluded.ERB"), StringComparison.OrdinalIgnoreCase));
        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Excluded.ERB"), Encoding.UTF8);
        Assert.Contains($"PRINTFORMW \"{originalValue}\"", writtenErb, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_CollisionRewriteUsesOutputOverrideWithoutMutatingItems()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(Path.Combine(gameRoot, "CSV"));

        var csvText = "1,外見年齢,\r\n2,実年齢,\r\n";
        var csvDocument = new SourceFileDocument
        {
            DocumentId = "CSV/Cflag.csv",
            FullPath = Path.Combine(gameRoot, "CSV", "Cflag.csv"),
            RelativePath = Path.Combine("CSV", "Cflag.csv"),
            FileType = "CSV",
            OriginalText = csvText,
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
            AbsoluteStart = 2,
            Length = "外見年齢".Length,
            LineNumber = 1,
            OriginalText = "外見年齢",
            FieldIndex = 1,
            SourceKey = "1",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            SymbolNamespace = "CFLAG",
            OriginalSymbolKey = "外見年齢",
            IsReferenceBearingKey = true,
        });
        csvDocument.Segments.Add(new TextSegment
        {
            SegmentId = "CSV/Cflag.csv:1",
            DocumentId = csvDocument.DocumentId,
            SegmentType = "csv-IdFirstTable-field-1",
            AbsoluteStart = "1,外見年齢,\r\n2,".Length,
            Length = "実年齢".Length,
            LineNumber = 2,
            OriginalText = "実年齢",
            FieldIndex = 1,
            SourceKey = "2",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            SymbolNamespace = "CFLAG",
            OriginalSymbolKey = "実年齢",
            IsReferenceBearingKey = true,
        });

        var firstItem = CreateReferenceBearingItem("CSV/Cflag.csv:0", csvDocument.DocumentId, "CFLAG", "外見年齢", "연령");
        var secondItem = CreateReferenceBearingItem("CSV/Cflag.csv:1", csvDocument.DocumentId, "CFLAG", "実年齢", "연령");
        var session = new ScanSession
        {
            GameRoot = gameRoot,
        };
        session.Documents[csvDocument.DocumentId] = csvDocument;
        session.Items.Add(firstItem);
        session.Items.Add(secondItem);

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenCsv = File.ReadAllText(Path.Combine(exportRoot, "CSV", "Cflag.csv"), Encoding.UTF8);
        Assert.Contains("1,연령,", writtenCsv, StringComparison.Ordinal);
        Assert.Contains("2,연령__実年齢,", writtenCsv, StringComparison.Ordinal);

        Assert.Equal("연령", firstItem.TranslatedText);
        Assert.Equal("번역 완료", firstItem.Status);
        Assert.Equal("통과", firstItem.ValidationStatus);
        Assert.True(firstItem.CanSave);

        Assert.Equal("연령", secondItem.TranslatedText);
        Assert.Equal("번역 완료", secondItem.Status);
        Assert.Equal("통과", secondItem.ValidationStatus);
        Assert.True(secondItem.CanSave);
    }

    [Fact]
    public void Save_RewritesFullWidthCommaInsideErbFunctionArguments()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(Path.Combine(gameRoot, "ERB"));

        var originalValue = "GET_SP_TRAIN_MEETING_CHARA_NAME(SP_TRAIN_MEETING_CHARA、3)";
        var erbText = $"PRINTFORMW \"{originalValue}\"\r\n";
        var document = new SourceFileDocument
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
        document.Segments.Add(new TextSegment
        {
            SegmentId = "ERB/Test.ERB:0",
            DocumentId = document.DocumentId,
            SegmentType = "quoted-string",
            AbsoluteStart = 12,
            Length = originalValue.Length,
            LineNumber = 1,
            OriginalText = originalValue,
            CsvFieldRole = CsvFieldRole.TranslatableValue,
        });

        var session = new ScanSession
        {
            GameRoot = gameRoot,
        };
        session.Documents[document.DocumentId] = document;
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = "ERB/Test.ERB:0",
            DocumentId = document.DocumentId,
            FileType = "ERB",
            RelativePath = document.RelativePath,
            EncodingName = "UTF-8",
            SegmentType = "quoted-string",
            LineNumber = 1,
            OriginalText = originalValue,
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            TranslatedText = originalValue,
            Status = "번역 완료",
            ValidationStatus = "통과",
            CanSave = true,
            WarningText = string.Empty,
        });

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);
        Assert.Contains("GET_SP_TRAIN_MEETING_CHARA_NAME(SP_TRAIN_MEETING_CHARA,3)", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("GET_SP_TRAIN_MEETING_CHARA_NAME(SP_TRAIN_MEETING_CHARA、3)", writtenErb, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_UnresolvedReferenceBlocksOutputWithoutMutatingItems()
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
            OriginalText = "1,依存度,\r\n",
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
            AbsoluteStart = 2,
            Length = "依存度".Length,
            LineNumber = 1,
            OriginalText = "依存度",
            FieldIndex = 1,
            SourceKey = "1",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            SymbolNamespace = "CFLAG",
            OriginalSymbolKey = "依存度",
            IsReferenceBearingKey = true,
        });

        var erbText = "IF CFLAG:{flagName}\r\n";
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

        var item = CreateReferenceBearingItem("CSV/Cflag.csv:0", csvDocument.DocumentId, "CFLAG", "依存度", "의존도");
        var session = new ScanSession
        {
            GameRoot = gameRoot,
        };
        session.Documents[csvDocument.DocumentId] = csvDocument;
        session.Documents[erbDocument.DocumentId] = erbDocument;
        session.Items.Add(item);

        new SymbolReferenceAnalyzer().Analyze(session);

        var writer = new OutputWriter();
        var result = writer.Save(session, exportRoot, SaveMode.ExportCopy);

        Assert.Contains(csvDocument.RelativePath, result.SkippedFiles);
        Assert.False(File.Exists(Path.Combine(exportRoot, "CSV", "Cflag.csv")));

        Assert.Equal("의존도", item.TranslatedText);
        Assert.Equal("번역 완료", item.Status);
        Assert.Equal("통과", item.ValidationStatus);
        Assert.True(item.CanSave);
    }

    [Fact]
    public void Save_RepeatedOriginalKeyEntriesDoNotTriggerFalseCollisionSuffixes()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(Path.Combine(gameRoot, "CSV"));
        Directory.CreateDirectory(Path.Combine(gameRoot, "ERB"));

        var talentDocument = new SourceFileDocument
        {
            DocumentId = "CSV/Talent.csv",
            FullPath = Path.Combine(gameRoot, "CSV", "Talent.csv"),
            RelativePath = Path.Combine("CSV", "Talent.csv"),
            FileType = "CSV",
            OriginalText = "421,高校生,\r\n",
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
        talentDocument.Segments.Add(new TextSegment
        {
            SegmentId = "CSV/Talent.csv:0",
            DocumentId = talentDocument.DocumentId,
            SegmentType = "csv-IdFirstTable-field-1",
            AbsoluteStart = 4,
            Length = "高校生".Length,
            LineNumber = 1,
            OriginalText = "高校生",
            FieldIndex = 1,
            SourceKey = "421",
            CsvFieldRole = CsvFieldRole.TranslatableValue,
            SymbolNamespace = "TALENT",
            OriginalSymbolKey = "高校生",
            IsReferenceBearingKey = true,
        });

        var charaDocument = new SourceFileDocument
        {
            DocumentId = "CSV/Chara001.csv",
            FullPath = Path.Combine(gameRoot, "CSV", "Chara001.csv"),
            RelativePath = Path.Combine("CSV", "Chara001.csv"),
            FileType = "CSV",
            OriginalText = "素質,高校生\r\n",
            EncodingInfo = new DetectedEncodingInfo
            {
                Encoding = Encoding.UTF8,
                Name = "UTF-8",
                Kind = DetectedEncodingKind.Utf8,
                HasBom = false,
            },
            NewLineSequence = "\r\n",
            CsvKind = CsvDocumentKind.CharacterSheet,
        };
        charaDocument.Segments.Add(new TextSegment
        {
            SegmentId = "CSV/Chara001.csv:0",
            DocumentId = charaDocument.DocumentId,
            SegmentType = "csv-CharacterSheet-field-1",
            AbsoluteStart = 3,
            Length = "高校生".Length,
            LineNumber = 1,
            OriginalText = "高校生",
            FieldIndex = 1,
            SourceKey = "素質/高校生",
            CsvFieldRole = CsvFieldRole.Key,
            SymbolNamespace = "TALENT",
            OriginalSymbolKey = "高校生",
            IsReferenceBearingKey = true,
        });

        var erbText = "IF TALENT:targetChara:高校生\r\n";
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

        var extractedReferences = new ErbReferenceExtractor().Extract(erbDocument.DocumentId, erbText);
        erbDocument.SymbolReferences.AddRange(extractedReferences.references);
        erbDocument.VariableLiteralOccurrences.AddRange(extractedReferences.variableLiterals);

        var session = new ScanSession
        {
            GameRoot = gameRoot,
        };
        session.Documents[talentDocument.DocumentId] = talentDocument;
        session.Documents[charaDocument.DocumentId] = charaDocument;
        session.Documents[erbDocument.DocumentId] = erbDocument;
        session.Items.Add(CreateReferenceBearingItem("CSV/Talent.csv:0", talentDocument.DocumentId, "TALENT", "高校生", "고등학생"));
        session.Items.Add(CreateReferenceBearingItem("CSV/Chara001.csv:0", charaDocument.DocumentId, "TALENT", "高校生", "고등학생"));

        new SymbolReferenceAnalyzer().Analyze(session);

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenTalent = File.ReadAllText(Path.Combine(exportRoot, "CSV", "Talent.csv"), Encoding.UTF8);
        var writtenChara = File.ReadAllText(Path.Combine(exportRoot, "CSV", "Chara001.csv"), Encoding.UTF8);
        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);

        Assert.Contains("421,고등학생,", writtenTalent, StringComparison.Ordinal);
        Assert.Contains("素質,고등학생", writtenChara, StringComparison.Ordinal);
        Assert.Contains("TALENT:targetChara:고등학생", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("__高校生", writtenTalent, StringComparison.Ordinal);
        Assert.DoesNotContain("__高校生", writtenChara, StringComparison.Ordinal);
        Assert.DoesNotContain("__高校生", writtenErb, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_ReferenceBearingCsvKeyUsesNormalizedSymbolKeyWithoutSpaces()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(Path.Combine(gameRoot, "CSV"));
        Directory.CreateDirectory(Path.Combine(gameRoot, "ERB"));

        var charaDocument = new SourceFileDocument
        {
            DocumentId = "CSV/Chara001.csv",
            FullPath = Path.Combine(gameRoot, "CSV", "Chara001.csv"),
            RelativePath = Path.Combine("CSV", "Chara001.csv"),
            FileType = "CSV",
            OriginalText = "CSTR,彼氏姓,テスト\r\n",
            EncodingInfo = new DetectedEncodingInfo
            {
                Encoding = Encoding.UTF8,
                Name = "UTF-8",
                Kind = DetectedEncodingKind.Utf8,
                HasBom = false,
            },
            NewLineSequence = "\r\n",
            CsvKind = CsvDocumentKind.CharacterSheet,
        };
        charaDocument.Segments.Add(new TextSegment
        {
            SegmentId = "CSV/Chara001.csv:0",
            DocumentId = charaDocument.DocumentId,
            SegmentType = "csv-CharacterSheet-field-1",
            AbsoluteStart = "CSTR,".Length,
            Length = "彼氏姓".Length,
            LineNumber = 1,
            OriginalText = "彼氏姓",
            FieldIndex = 1,
            SourceKey = "CSTR/彼氏姓",
            CsvFieldRole = CsvFieldRole.MetaKey,
            PreserveWhitespace = false,
            SymbolNamespace = "CSTR",
            OriginalSymbolKey = "彼氏姓",
            IsReferenceBearingKey = true,
        });

        var erbText = "PRINTFORMW %CSTR:彼氏姓%\r\n";
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
        var extractedReferences = new ErbReferenceExtractor().Extract(erbDocument.DocumentId, erbText);
        erbDocument.SymbolReferences.AddRange(extractedReferences.references);
        erbDocument.VariableLiteralOccurrences.AddRange(extractedReferences.variableLiterals);

        var session = new ScanSession
        {
            GameRoot = gameRoot,
        };
        session.Documents[charaDocument.DocumentId] = charaDocument;
        session.Documents[erbDocument.DocumentId] = erbDocument;
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = "CSV/Chara001.csv:0",
            DocumentId = charaDocument.DocumentId,
            FileType = "CSV",
            RelativePath = charaDocument.RelativePath,
            EncodingName = "UTF-8",
            SegmentType = "csv-CharacterSheet-field-1",
            LineNumber = 1,
            OriginalText = "彼氏姓",
            SourceKey = "CSTR/彼氏姓",
            FieldIndex = 1,
            CsvFieldRole = CsvFieldRole.MetaKey,
            PreserveWhitespace = false,
            SymbolNamespace = "CSTR",
            OriginalSymbolKey = "彼氏姓",
            IsReferenceBearingKey = true,
            TranslatedText = "남자친구 성씨",
            Status = "번역 완료",
            ValidationStatus = "통과",
            WarningText = string.Empty,
        });

        new SymbolReferenceAnalyzer().Analyze(session);

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenCsv = File.ReadAllText(Path.Combine(exportRoot, "CSV", "Chara001.csv"), Encoding.UTF8);
        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);
        Assert.Contains("CSTR,남자친구성씨,テスト", writtenCsv, StringComparison.Ordinal);
        Assert.Contains("%CSTR:남자친구성씨%", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("남자친구 성씨", writtenCsv, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportCopy_RewritesLeadingParticleOnFollowingSegmentUsingJosaOnlyFunction()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(Path.Combine(gameRoot, "ERB"));

        var firstLineValue = "%CALLNAME:supportChara%";
        var secondLineValue = "는 시선을 피했다.";
        var erbText = $"PRINTFORM {firstLineValue}\r\nPRINTFORM {secondLineValue}\r\n";
        var firstStart = erbText.IndexOf(firstLineValue, StringComparison.Ordinal);
        var secondStart = erbText.IndexOf(secondLineValue, StringComparison.Ordinal);
        var erbDocument = new SourceFileDocument
        {
            DocumentId = "ERB/SplitJosa.ERB",
            FullPath = Path.Combine(gameRoot, "ERB", "SplitJosa.ERB"),
            RelativePath = Path.Combine("ERB", "SplitJosa.ERB"),
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
            SegmentId = "ERB/SplitJosa.ERB:0",
            DocumentId = erbDocument.DocumentId,
            SegmentType = "print-tail",
            AbsoluteStart = firstStart,
            Length = firstLineValue.Length,
            LineNumber = 1,
            OriginalText = firstLineValue,
        });
        erbDocument.Segments.Add(new TextSegment
        {
            SegmentId = "ERB/SplitJosa.ERB:1",
            DocumentId = erbDocument.DocumentId,
            SegmentType = "print-tail",
            AbsoluteStart = secondStart,
            Length = secondLineValue.Length,
            LineNumber = 2,
            OriginalText = secondLineValue,
        });

        var session = new ScanSession
        {
            GameRoot = gameRoot,
            JosaPackageInfo = new JosaSupportPackageService().InspectProject(gameRoot),
        };
        session.Documents[erbDocument.DocumentId] = erbDocument;
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = "ERB/SplitJosa.ERB:0",
            DocumentId = erbDocument.DocumentId,
            FileType = "ERB",
            RelativePath = erbDocument.RelativePath,
            EncodingName = "UTF-8",
            SegmentType = "print-tail",
            LineNumber = 1,
            OriginalText = firstLineValue,
            TranslatedText = firstLineValue,
            Status = "번역 완료",
            ValidationStatus = "통과",
            WarningText = string.Empty,
        });
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = "ERB/SplitJosa.ERB:1",
            DocumentId = erbDocument.DocumentId,
            FileType = "ERB",
            RelativePath = erbDocument.RelativePath,
            EncodingName = "UTF-8",
            SegmentType = "print-tail",
            LineNumber = 2,
            OriginalText = secondLineValue,
            TranslatedText = secondLineValue,
            Status = "번역 완료",
            ValidationStatus = "통과",
            WarningText = string.Empty,
        });

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "SplitJosa.ERB"), Encoding.UTF8);
        Assert.Contains("PRINTFORM %CALLNAME:supportChara%", writtenErb, StringComparison.Ordinal);
        Assert.Contains("PRINTFORM %조사만처리(CALLNAME:supportChara,\"는\")% 시선을 피했다.", writtenErb, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportCopy_RewritesLeadingSlashAndParentheticalParticleVariantsOnFollowingSegment()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(Path.Combine(gameRoot, "ERB"));

        var firstLineValue = "%NAME:(friendList:index)%";
        var secondLineValue = "은/는 이미 이쪽의 수중에 있다....";
        var erbText = $"PRINTFORM {firstLineValue}\r\nPRINTFORMW {secondLineValue}\r\n";
        var firstStart = erbText.IndexOf(firstLineValue, StringComparison.Ordinal);
        var secondStart = erbText.IndexOf(secondLineValue, StringComparison.Ordinal);
        var erbDocument = new SourceFileDocument
        {
            DocumentId = "ERB/SplitJosaSlash.ERB",
            FullPath = Path.Combine(gameRoot, "ERB", "SplitJosaSlash.ERB"),
            RelativePath = Path.Combine("ERB", "SplitJosaSlash.ERB"),
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
            SegmentId = "ERB/SplitJosaSlash.ERB:0",
            DocumentId = erbDocument.DocumentId,
            SegmentType = "print-tail",
            AbsoluteStart = firstStart,
            Length = firstLineValue.Length,
            LineNumber = 1,
            OriginalText = firstLineValue,
        });
        erbDocument.Segments.Add(new TextSegment
        {
            SegmentId = "ERB/SplitJosaSlash.ERB:1",
            DocumentId = erbDocument.DocumentId,
            SegmentType = "print-tail",
            AbsoluteStart = secondStart,
            Length = secondLineValue.Length,
            LineNumber = 2,
            OriginalText = secondLineValue,
        });

        var session = new ScanSession
        {
            GameRoot = gameRoot,
            JosaPackageInfo = new JosaSupportPackageService().InspectProject(gameRoot),
        };
        session.Documents[erbDocument.DocumentId] = erbDocument;
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = "ERB/SplitJosaSlash.ERB:0",
            DocumentId = erbDocument.DocumentId,
            FileType = "ERB",
            RelativePath = erbDocument.RelativePath,
            EncodingName = "UTF-8",
            SegmentType = "print-tail",
            LineNumber = 1,
            OriginalText = firstLineValue,
            TranslatedText = firstLineValue,
            Status = "번역 완료",
            ValidationStatus = "통과",
            WarningText = string.Empty,
        });
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = "ERB/SplitJosaSlash.ERB:1",
            DocumentId = erbDocument.DocumentId,
            FileType = "ERB",
            RelativePath = erbDocument.RelativePath,
            EncodingName = "UTF-8",
            SegmentType = "print-tail",
            LineNumber = 2,
            OriginalText = secondLineValue,
            TranslatedText = "는(은) 이미 이쪽의 수중에 있다....",
            Status = "번역 완료",
            ValidationStatus = "통과",
            WarningText = string.Empty,
        });

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "SplitJosaSlash.ERB"), Encoding.UTF8);
        Assert.Contains("PRINTFORM %NAME:(friendList:index)%", writtenErb, StringComparison.Ordinal);
        Assert.Contains("PRINTFORMW %조사만처리(NAME:(friendList:index),\"는\")% 이미 이쪽의 수중에 있다....", writtenErb, StringComparison.Ordinal);
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
    public void ExportCopy_DoesNotInsertPerFileErhIncludeWhenJosaConversionRequiresPackage()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(Path.Combine(gameRoot, "ERB"));

        var erbText = """
; header comment

@TEST2
#FUNCTION
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
        Assert.DoesNotContain("#INCLUDE \"ZNAME.ERH\"", writtenErb, StringComparison.Ordinal);
        Assert.Contains("%플레이어는%", writtenErb, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(exportRoot, "ERB", "ZNAME.ERB")));
        Assert.True(File.Exists(Path.Combine(exportRoot, "ERB", "ZNAME.ERH")));
    }

    [Fact]
    public void ExportCopy_RewritesExpressionIndexedErbReferences()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        var csvDir = Path.Combine(gameRoot, "CSV");
        var erbDir = Path.Combine(gameRoot, "ERB");
        Directory.CreateDirectory(csvDir);
        Directory.CreateDirectory(erbDir);

        File.WriteAllText(Path.Combine(csvDir, "Talent.csv"), "307,交際\r\n610,失踪\r\n", Encoding.UTF8);
        File.WriteAllText(Path.Combine(csvDir, "Base.csv"), "0,体力\r\n", Encoding.UTF8);
        File.WriteAllText(Path.Combine(csvDir, "Item.csv"), "92,子宮内避妊結界,2000\r\n", Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(erbDir, "Test.ERB"),
            "IF TALENT:(targetChara):交際\r\nIF TALENT:GETCHARA(205):失踪\r\nIF MAXBASE:(T):体力 > 1000\r\nIF MONEY > ITEMPRICE:子宮内避妊結界\r\n",
            Encoding.UTF8);

        var session = new FileScanner().Scan(gameRoot);
        foreach (var item in session.Items.Where(item => item.SymbolNamespace == "TALENT" && item.OriginalSymbolKey == "交際"))
        {
            item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "교제");
        }

        foreach (var item in session.Items.Where(item => item.SymbolNamespace == "TALENT" && item.OriginalSymbolKey == "失踪"))
        {
            item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "실종");
        }

        foreach (var item in session.Items.Where(item => item.SymbolNamespace == "BASE" && item.OriginalSymbolKey == "体力"))
        {
            item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "체력");
        }

        foreach (var item in session.Items.Where(item => item.SymbolNamespace == "ITEM" && item.OriginalSymbolKey == "子宮内避妊結界"))
        {
            item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "자궁내피임결계");
        }

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);
        Assert.Contains("TALENT:(targetChara):교제", writtenErb, StringComparison.Ordinal);
        Assert.Contains("TALENT:GETCHARA(205):실종", writtenErb, StringComparison.Ordinal);
        Assert.Contains("MAXBASE:(T):체력 > 1000", writtenErb, StringComparison.Ordinal);
        Assert.Contains("ITEMPRICE:자궁내피임결계", writtenErb, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportCopy_UsesSafeReferencesForColonAndDecoratedSymbolKeys()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        var csvDir = Path.Combine(gameRoot, "CSV");
        var erbDir = Path.Combine(gameRoot, "ERB");
        Directory.CreateDirectory(csvDir);
        Directory.CreateDirectory(erbDir);

        File.WriteAllText(Path.Combine(csvDir, "Abl.csv"), "30,関心:学業\r\n", Encoding.UTF8);
        File.WriteAllText(Path.Combine(csvDir, "Tequip.csv"), "42,亀甲縛り,\r\n", Encoding.UTF8);
        File.WriteAllText(Path.Combine(csvDir, "Exp.csv"), "54,噴乳経験\r\n", Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(erbDir, "Test.ERB"),
            "IF ABL:ARG:関心:学業 > 0\r\nSIF TEQUIP:亀甲縛り\r\nexpUp:GETNUM(EXP,\"噴乳経験\") += 1\r\n",
            Encoding.UTF8);

        var session = new FileScanner().Scan(gameRoot);
        foreach (var item in session.Items.Where(item => item.SymbolNamespace == "ABL" && item.OriginalSymbolKey == "関心:学業"))
        {
            item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "관심:학업");
        }

        foreach (var item in session.Items.Where(item => item.SymbolNamespace == "TEQUIP" && item.OriginalSymbolKey == "亀甲縛り"))
        {
            item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "거북등무늬결박(귀갑묶기)");
        }

        foreach (var item in session.Items.Where(item => item.SymbolNamespace == "EXP" && item.OriginalSymbolKey == "噴乳経験"))
        {
            item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "분유 경험");
        }

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);
        Assert.Contains("ABL:ARG:30 > 0", writtenErb, StringComparison.Ordinal);
        Assert.Contains("TEQUIP:42", writtenErb, StringComparison.Ordinal);
        Assert.Contains("GETNUM(EXP,\"분유경험\")", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("관심:학업", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("귀갑묶기", writtenErb, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
