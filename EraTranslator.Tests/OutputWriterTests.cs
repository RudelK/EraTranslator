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
