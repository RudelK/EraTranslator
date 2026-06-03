using System.Text;
using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class OutputWriterTests
{
    [Fact]
    public void ExportCopy_RewritesTranslatedErbIdentifiersWithoutSpaces()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var erbDir = Path.Combine(gameRoot, "ERB");
        var exportRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(erbDir);
        File.WriteAllText(
            Path.Combine(erbDir, "Test.ERB"),
            """
@キャラ検索(ARGS)
#FUNCTION
#DIM キーボード選択コマンドID
キーボード選択コマンドID = 1
CALL キャラ検索("[グラツィア大公家の小公女]セレナ", キーボード選択コマンドID)
SIF EXIST画像FILE(@"%CSTR:(キャラ検索("[グラツィア大公家の小公女]セレナ")):画像フォルダ%/ダンジョン用_野盗ボス")
IF ABL:対象キャラ:(TCVAR:対象キャラ:野外オナニー_部位) > 4
""",
            Encoding.UTF8);

        try
        {
            var session = new FileScanner().Scan(gameRoot);
            var functionItem = Assert.Single(session.Items, item =>
                item.SegmentType == IdentifierSegmentTypes.Function
                && item.OriginalText == "キャラ検索");
            functionItem.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "캐릭터 검색");
            var variableItem = Assert.Single(session.Items, item =>
                item.SegmentType == IdentifierSegmentTypes.Variable
                && item.OriginalText == "キーボード選択コマンドID");
            variableItem.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "키보드 선택 커맨드 ID");

            var writer = new OutputWriter();
            writer.Save(session, exportRoot, SaveMode.ExportCopy);

            var written = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);
            Assert.Contains("@캐릭터검색(ARGS)", written, StringComparison.Ordinal);
            Assert.Contains("CALL 캐릭터검색", written, StringComparison.Ordinal);
            Assert.Contains("CSTR:(캐릭터검색(", written, StringComparison.Ordinal);
            Assert.Contains("#DIM 키보드선택커맨드ID", written, StringComparison.Ordinal);
            Assert.Contains("키보드선택커맨드ID = 1", written, StringComparison.Ordinal);
            Assert.Contains("ダンジョン用_野盗ボス", written, StringComparison.Ordinal);
            Assert.Contains("TCVAR:対象キャラ:野外オナニー_部位", written, StringComparison.Ordinal);
            Assert.DoesNotContain("캐릭터 검색", written, StringComparison.Ordinal);
            Assert.DoesNotContain("키보드 선택 커맨드 ID", written, StringComparison.Ordinal);
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
    public void ExportCopy_RewritesErbIdentifiersInDimInitializersCaseStatementsAndFunctionArguments()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var erbDir = Path.Combine(gameRoot, "ERB");
        var exportRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(erbDir);
        File.WriteAllText(
            Path.Combine(erbDir, "Test.ERB"),
            """
#DIM CONST 売春一括指示_FALSE = 0
#DIM  CONST 売春一括指示_ループ順_避妊方法, 4 = 売春一括指示_FALSE, 売春一括指示_生
#DIM SAVEDATA 売春一括指示_OPTION = 売春一括指示_FALSE
@壁尻部屋_彼初回口上, 彼Label
@SUCCESSION_CHARA(選択キャラ)
#DIMS 彼Label
#DIM 選択キャラ,1
SELECTCASE 売春一括指示_ループ順_避妊方法
CASE 売春一括指示_FALSE, 売春一括指示_生
[IF 売春一括指示]
""",
            Encoding.UTF8);

        ScanSession session;
        try
        {
            session = new FileScanner().Scan(gameRoot);
            CompleteIdentifier("売春一括指示_FALSE", "매춘일괄지시_FALSE");
            CompleteIdentifier("売春一括指示_生", "매춘일괄지시_생");
            CompleteIdentifier("売春一括指示_ループ順_避妊方法", "매춘일괄지시_루프순_피임방법");
            CompleteIdentifier("売春一括指示_OPTION", "매춘일괄지시_OPTION");
            CompleteIdentifier("売春一括指示", "매춘일괄지시");
            CompleteIdentifier("彼Label", "그Label");
            CompleteIdentifier("選択キャラ", "선택캐릭터");

            var writer = new OutputWriter();
            writer.Save(session, exportRoot, SaveMode.ExportCopy);

            var written = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);
            Assert.Contains("#DIM CONST 매춘일괄지시_FALSE = 0", written, StringComparison.Ordinal);
            Assert.Contains("#DIM  CONST 매춘일괄지시_루프순_피임방법, 4 = 매춘일괄지시_FALSE, 매춘일괄지시_생", written, StringComparison.Ordinal);
            Assert.Contains("#DIM SAVEDATA 매춘일괄지시_OPTION = 매춘일괄지시_FALSE", written, StringComparison.Ordinal);
            Assert.Contains("@壁尻部屋_彼初回口上, 그Label", written, StringComparison.Ordinal);
            Assert.Contains("@SUCCESSION_CHARA(선택캐릭터)", written, StringComparison.Ordinal);
            Assert.Contains("#DIMS 그Label", written, StringComparison.Ordinal);
            Assert.Contains("#DIM 선택캐릭터,1", written, StringComparison.Ordinal);
            Assert.Contains("SELECTCASE 매춘일괄지시_루프순_피임방법", written, StringComparison.Ordinal);
            Assert.Contains("CASE 매춘일괄지시_FALSE, 매춘일괄지시_생", written, StringComparison.Ordinal);
            Assert.Contains("[IF 매춘일괄지시]", written, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        void CompleteIdentifier(string originalText, string translatedText)
        {
            var item = Assert.Single(session.Items, item =>
                item.SegmentType == IdentifierSegmentTypes.Variable
                && item.OriginalText == originalText);
            item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, translatedText);
        }
    }

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
    public void ExportCopy_RewritesCsvKeyListEntriesAsSymbolReferences()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        var csvDir = Path.Combine(gameRoot, "CSV");
        var erbDir = Path.Combine(gameRoot, "ERB");
        Directory.CreateDirectory(csvDir);
        Directory.CreateDirectory(erbDir);

        File.WriteAllText(Path.Combine(csvDir, "Talent.csv"), "1,気骨\r\n2,反抗的\r\n3,気丈\r\n", Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(erbDir, "Test.ERB"),
            """
RETURNF CALC_CHARA_SINGLE_DATA("TALENT",targetChara,"気骨*3,反抗的")
RETURNF GET_NONEXISTABLE_TALENT_BYNAME("気骨*3,反抗的",charaIndex,charaNo)
RETURNF CALC_CHARA_MULTIPLE_DATA_BASE(answer,"TALENT",targetChara,"気骨*3,反抗的*11,気丈*12",10,1000)
RETURNF CALC_CHARA_SINGLE_DATA("TALENT",targetChara,"気骨派")
""".Replace("\n", "\r\n", StringComparison.Ordinal),
            Encoding.UTF8);

        var session = new FileScanner().Scan(gameRoot);
        foreach (var item in session.Items.Where(item => item.SymbolNamespace == "TALENT" && item.OriginalSymbolKey == "気骨"))
        {
            item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "기개");
        }

        foreach (var item in session.Items.Where(item => item.SymbolNamespace == "TALENT" && item.OriginalSymbolKey == "反抗的"))
        {
            item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "반항적");
        }

        foreach (var item in session.Items.Where(item => item.SymbolNamespace == "TALENT" && item.OriginalSymbolKey == "気丈"))
        {
            item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "강인함");
        }

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);
        Assert.Contains("\"기개*3,반항적\"", writtenErb, StringComparison.Ordinal);
        Assert.Contains("\"기개*3,반항적*11,강인함*12\"", writtenErb, StringComparison.Ordinal);
        Assert.Contains("\"気骨派\"", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain(" 기개", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain(" 반항적", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain(" 강인함", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("\"기개派\"", writtenErb, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportCopy_RewritesDimsLookupArrayDefinitionsAndWrapperArgumentsConsistently()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        var erbDir = Path.Combine(gameRoot, "ERB");
        Directory.CreateDirectory(erbDir);

        File.WriteAllText(
            Path.Combine(erbDir, "Prostitution.ERH"),
            """
#DIMS CONST CUSTOMER_VALUES_ARRAY="対応娼婦","プレイ傾向"
""".Replace("\n", "\r\n", StringComparison.Ordinal),
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(erbDir, "Test.ERB"),
            """
@GET_CUSTOMER_VALUEINDEX_FROM_VALUENAME(valueName)
#FUNCTION
valueIndex = FINDELEMENT(CUSTOMER_VALUES_ARRAY,valueName,,,1)
@GET_PROSTITUTION_CUSTOMER_VALUE(targetCustomerIndex,valueName)
#FUNCTION
RETURNF CUSTOMER:targetCustomerIndex:GET_CUSTOMER_VALUEINDEX_FROM_VALUENAME(valueName)
@SET_PROSTITUTION_CUSTOMER_VALUE(targetCustomerIndex,valueName,value)
#FUNCTION
valueIndex = GET_CUSTOMER_VALUEINDEX_FROM_VALUENAME(valueName)
answer = GET_PROSTITUTION_CUSTOMER_VALUE(customerIndex,"プレイ傾向")
CALLF SET_PROSTITUTION_CUSTOMER_VALUE(customerIndex,"対応娼婦",0)
""".Replace("\n", "\r\n", StringComparison.Ordinal),
            Encoding.UTF8);

        ScanSession session;
        try
        {
            session = new FileScanner().Scan(gameRoot);
            CompleteDimsKey("対応娼婦", "대응 창부");
            CompleteDimsKey("プレイ傾向", "플레이 경향");

            var writer = new OutputWriter();
            writer.Save(session, exportRoot, SaveMode.ExportCopy);

            var writtenErh = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Prostitution.ERH"), Encoding.UTF8);
            var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);

            Assert.Contains("\"대응 창부\",\"플레이 경향\"", writtenErh, StringComparison.Ordinal);
            Assert.Contains("GET_PROSTITUTION_CUSTOMER_VALUE(customerIndex,\"플레이 경향\")", writtenErb, StringComparison.Ordinal);
            Assert.Contains("SET_PROSTITUTION_CUSTOMER_VALUE(customerIndex,\"대응 창부\",0)", writtenErb, StringComparison.Ordinal);
            Assert.DoesNotContain("プレイ傾向", writtenErb, StringComparison.Ordinal);
            Assert.DoesNotContain("対応娼婦", writtenErb, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        void CompleteDimsKey(string originalText, string translatedText)
        {
            foreach (var item in session.Items.Where(item =>
                         item.SymbolNamespace == "DIMS:CUSTOMER_VALUES_ARRAY"
                         && item.OriginalSymbolKey == originalText))
            {
                item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, translatedText);
            }
        }
    }

    [Fact]
    public void ExportCopy_RewritesDimsLookupSelectCaseAndDirectFindElementConsistently()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        var erbDir = Path.Combine(gameRoot, "ERB");
        Directory.CreateDirectory(erbDir);

        File.WriteAllText(
            Path.Combine(erbDir, "Prostitution.ERH"),
            """
#DIMS CONST PROSTITUTION_SEX_LIST="男","女","ふたなり"
""".Replace("\n", "\r\n", StringComparison.Ordinal),
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(erbDir, "Test.ERB"),
            """
SELECTCASE PROSTITUTION_SEX_LIST:customerSex
    CASE "男"
        RETURNF "偉そうな"
    CASE "女","ふたなり"
        RETURNF "高慢な"
ENDSELECT
value = FINDELEMENT(PROSTITUTION_SEX_LIST,"女")
""".Replace("\n", "\r\n", StringComparison.Ordinal),
            Encoding.UTF8);

        ScanSession session;
        try
        {
            session = new FileScanner().Scan(gameRoot);
            CompleteDimsKey("男", "남");
            CompleteDimsKey("女", "여");
            CompleteDimsKey("ふたなり", "후타나리");

            var writer = new OutputWriter();
            writer.Save(session, exportRoot, SaveMode.ExportCopy);

            var writtenErh = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Prostitution.ERH"), Encoding.UTF8);
            var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);

            Assert.Contains("\"남\",\"여\",\"후타나리\"", writtenErh, StringComparison.Ordinal);
            Assert.Contains("CASE \"남\"", writtenErb, StringComparison.Ordinal);
            Assert.Contains("CASE \"여\",\"후타나리\"", writtenErb, StringComparison.Ordinal);
            Assert.Contains("FINDELEMENT(PROSTITUTION_SEX_LIST,\"여\")", writtenErb, StringComparison.Ordinal);
            Assert.DoesNotContain("\"女\"", writtenErb, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        void CompleteDimsKey(string originalText, string translatedText)
        {
            foreach (var item in session.Items.Where(item =>
                         item.SymbolNamespace == "DIMS:PROSTITUTION_SEX_LIST"
                         && item.OriginalSymbolKey == originalText))
            {
                item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, translatedText);
            }
        }
    }

    [Fact]
    public void ExportCopy_RewritesCsvNameSelectCaseLabelsByExactCsvKey()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        var csvDir = Path.Combine(gameRoot, "CSV");
        var erbDir = Path.Combine(gameRoot, "ERB");
        Directory.CreateDirectory(csvDir);
        Directory.CreateDirectory(erbDir);

        File.WriteAllText(
            Path.Combine(csvDir, "Talent.csv"),
            """
280,回復早い
290,オトコ
323,寄生
324,浄化
""".Replace("\n", "\r\n", StringComparison.Ordinal),
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(erbDir, "PrintState.ERB"),
            """
SELECTCASE TALENTNAME:index
    CASE "寄生","浄化","オトコ","オトコっぽい"
        isLevelSatisfied = 0
ENDSELECT
""".Replace("\n", "\r\n", StringComparison.Ordinal),
            Encoding.UTF8);

        ScanSession session;
        try
        {
            session = new FileScanner().Scan(gameRoot);
            CompleteTalentKey("オトコ", "남자");
            CompleteTalentKey("寄生", "기생");
            CompleteTalentKey("浄化", "정화");

            var writer = new OutputWriter();
            writer.Save(session, exportRoot, SaveMode.ExportCopy);

            var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "PrintState.ERB"), Encoding.UTF8);

            Assert.Contains("CASE \"기생\",\"정화\",\"남자\",\"オトコっぽい\"", writtenErb, StringComparison.Ordinal);
            Assert.DoesNotContain("\"オトコ\",", writtenErb, StringComparison.Ordinal);
            Assert.Contains("\"オトコっぽい\"", writtenErb, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        void CompleteTalentKey(string originalText, string translatedText)
        {
            foreach (var item in session.Items.Where(item =>
                         item.SymbolNamespace == "TALENT"
                         && item.OriginalSymbolKey == originalText))
            {
                item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, translatedText);
            }
        }
    }

    [Fact]
    public void ExportCopy_DoesNotLetPercentProtectionLeakAcrossLines()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        var erbDir = Path.Combine(gameRoot, "ERB");
        Directory.CreateDirectory(erbDir);
        File.WriteAllText(
            Path.Combine(erbDir, "PrintState.ERB"),
            """
PRINTFORM %UNFINISHED
CALL PRINT_IN_CLIENT_WIDTH("性格:")
""".Replace("\n", "\r\n", StringComparison.Ordinal),
            Encoding.UTF8);

        try
        {
            var session = new FileScanner().Scan(gameRoot);
            var item = Assert.Single(session.Items, item => item.OriginalText == "性格:");
            item.ApplyTranslationState("검수 필요", "통과", string.Empty, true, "성격:");

            new OutputWriter().Save(session, exportRoot, SaveMode.ExportCopy);

            var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "PrintState.ERB"), Encoding.UTF8);
            Assert.Contains("CALL PRINT_IN_CLIENT_WIDTH(\"성격:\")", writtenErb, StringComparison.Ordinal);
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
    public void ExportCopy_DoesNotApplyJosaRewriteToErhSegments()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        var erbDir = Path.Combine(gameRoot, "ERB");
        Directory.CreateDirectory(erbDir);

        var originalValue = "사과은";
        var erhText = $"PRINTFORMW \"{originalValue}\"\r\n";
        var valueStart = erhText.IndexOf(originalValue, StringComparison.Ordinal);
        var erhDocument = new SourceFileDocument
        {
            DocumentId = "ERB/Common.ERH",
            FullPath = Path.Combine(erbDir, "Common.ERH"),
            RelativePath = Path.Combine("ERB", "Common.ERH"),
            FileType = "ERH",
            OriginalText = erhText,
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
        erhDocument.Segments.Add(new TextSegment
        {
            SegmentId = "ERB/Common.ERH:0",
            DocumentId = erhDocument.DocumentId,
            SegmentType = "quoted-string",
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
        session.Documents[erhDocument.DocumentId] = erhDocument;
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = "ERB/Common.ERH:0",
            DocumentId = erhDocument.DocumentId,
            FileType = "ERH",
            RelativePath = erhDocument.RelativePath,
            EncodingName = "UTF-8",
            SegmentType = "quoted-string",
            LineNumber = 1,
            OriginalText = originalValue,
            TranslatedText = originalValue,
            Status = "번역 완료",
            ValidationStatus = "통과",
            WarningText = string.Empty,
        });

        var writer = new OutputWriter();
        var result = writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErh = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Common.ERH"), Encoding.UTF8);
        Assert.Contains("PRINTFORMW \"사과은\"", writtenErh, StringComparison.Ordinal);
        Assert.True(result.CompletedAt >= result.StartedAt);
        Assert.True(result.TotalElapsed >= TimeSpan.Zero);
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
    public void Save_UnresolvedReferenceKeepsCsvOutputAndLeavesErbUntouched()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(Path.Combine(gameRoot, "CSV"));
        Directory.CreateDirectory(Path.Combine(gameRoot, "ERB"));
        File.WriteAllText(Path.Combine(gameRoot, "CSV", "Cflag.csv"), "1,依存度,\r\n", Encoding.UTF8);
        File.WriteAllText(Path.Combine(gameRoot, "ERB", "Test.ERB"), "IF CFLAG:{flagName}\r\n", Encoding.UTF8);

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

        var writtenCsv = File.ReadAllText(Path.Combine(exportRoot, "CSV", "Cflag.csv"), Encoding.UTF8);
        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);
        Assert.DoesNotContain(csvDocument.RelativePath, result.SkippedFiles);
        Assert.Contains("1,의존도,", writtenCsv, StringComparison.Ordinal);
        Assert.Equal("IF CFLAG:{flagName}\r\n", writtenErb);
        Assert.Contains(erbDocument.RelativePath, result.SkippedFiles);

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
    public void SymbolRewritePlanner_MarksCustomNamespaceWithUnresolvedDynamicReferenceAsReviewOnly()
    {
        var csvItem = CreateReferenceBearingItem("CSV/OPTION変数.csv:0", "CSV/OPTION変数.csv", "OPTION変数", "妊娠切り替え", "임신전환");
        var session = new ScanSession
        {
            GameRoot = "D:\\dummy",
        };
        session.Items.Add(csvItem);
        session.Documents["ERB/Test.ERB"] = new SourceFileDocument
        {
            DocumentId = "ERB/Test.ERB",
            FullPath = "D:\\dummy\\ERB\\Test.ERB",
            RelativePath = Path.Combine("ERB", "Test.ERB"),
            FileType = "ERB",
            OriginalText = "IF GETNUM(OPTION変数, keyName) > 0\r\n",
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

        var registry = new SymbolNamespaceRegistry(["OPTION変数"]);
        var extractedReferences = new ErbReferenceExtractor(registry).Extract("ERB/Test.ERB", "IF GETNUM(OPTION変数, keyName) > 0\r\n");
        session.Documents["ERB/Test.ERB"].SymbolReferences.AddRange(extractedReferences.references);
        session.Documents["ERB/Test.ERB"].VariableLiteralOccurrences.AddRange(extractedReferences.variableLiterals);

        var plan = new SymbolRewritePlanner().CreatePlan(session);
        var itemOverride = plan.GetOverride(csvItem.SegmentId);

        Assert.Equal("임신전환", itemOverride.TranslatedText);
        Assert.True(itemOverride.CanSave);
        Assert.Equal("통과", itemOverride.ValidationStatus);
        Assert.Equal("검수 필요", itemOverride.Status);
        Assert.Contains("동적 OPTION変数 참조", itemOverride.TranslationError, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportCopy_RewritesCustomCsvNamespaceReferences()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(Path.Combine(gameRoot, "CSV"));
        Directory.CreateDirectory(Path.Combine(gameRoot, "ERB"));

        File.WriteAllText(Path.Combine(gameRoot, "CSV", "OPTION変数.csv"), "3,妊娠切り替え\r\n", Encoding.UTF8);
        File.WriteAllText(Path.Combine(gameRoot, "ERB", "Test.ERB"), "SIF OPTION変数:妊娠切り替え\r\n", Encoding.UTF8);

        var session = new FileScanner().Scan(gameRoot);
        var optionItem = Assert.Single(
            session.Items,
            item => item.SymbolNamespace == "OPTION変数" && item.OriginalSymbolKey == "妊娠切り替え");
        optionItem.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "임신전환");

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenCsv = File.ReadAllText(Path.Combine(exportRoot, "CSV", "OPTION変数.csv"), Encoding.UTF8);
        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);
        Assert.Contains("3,임신전환", writtenCsv, StringComparison.Ordinal);
        Assert.Contains("OPTION変数:임신전환", writtenErb, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportCopy_RewritesErdCsvLikeNamespaceReferences()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        var erbDir = Path.Combine(gameRoot, "ERB");
        Directory.CreateDirectory(erbDir);

        File.WriteAllText(Path.Combine(erbDir, "BATTLE_STATE@2.ERD"), "0,ＨＰ\r\n1,攻撃力\r\n", Encoding.UTF8);
        File.WriteAllText(Path.Combine(erbDir, "Test.ERB"), "SIF BATTLE_STATE:TARGET:ＨＰ > 0\r\n", Encoding.UTF8);

        var session = new FileScanner().Scan(gameRoot);
        var stateItem = Assert.Single(
            session.Items,
            item => item.SymbolNamespace == "BATTLE_STATE" && item.OriginalSymbolKey == "ＨＰ");
        stateItem.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "체력");

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErd = File.ReadAllText(Path.Combine(exportRoot, "ERB", "BATTLE_STATE@2.ERD"), Encoding.UTF8);
        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);
        Assert.Contains("0,체력", writtenErd, StringComparison.Ordinal);
        Assert.Contains("BATTLE_STATE:TARGET:체력", writtenErb, StringComparison.Ordinal);
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

    [Fact]
    public void ExportCopy_RewritesLiteralKoreanParticlesInsideTranslatedSegments()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(Path.Combine(gameRoot, "ERB"));

        const string originalValue = "適当";
        var erbText = $"PRINTFORMW {originalValue}\r\n";
        var valueStart = erbText.IndexOf(originalValue, StringComparison.Ordinal);
        var erbDocument = new SourceFileDocument
        {
            DocumentId = "ERB/LiteralJosa.ERB",
            FullPath = Path.Combine(gameRoot, "ERB", "LiteralJosa.ERB"),
            RelativePath = Path.Combine("ERB", "LiteralJosa.ERB"),
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
            SegmentId = "ERB/LiteralJosa.ERB:0",
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
            SegmentId = "ERB/LiteralJosa.ERB:0",
            DocumentId = erbDocument.DocumentId,
            FileType = "ERB",
            RelativePath = erbDocument.RelativePath,
            EncodingName = "UTF-8",
            SegmentType = "print-tail",
            LineNumber = 1,
            OriginalText = originalValue,
            TranslatedText = "%CALLNAME:MASTER%는 사과은 좋아하고 길으로 간다.",
            Status = "번역 완료",
            ValidationStatus = "통과",
            WarningText = string.Empty,
        });

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "LiteralJosa.ERB"), Encoding.UTF8);
        Assert.Contains("%조사처리(CALLNAME:MASTER,\"는\")% 사과는 좋아하고 길로 간다.", writtenErb, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportCopy_RewritesLeadingLiteralParticleOnFollowingSegment()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(Path.Combine(gameRoot, "ERB"));

        var firstLineValue = "사과";
        var secondLineValue = "은 맛있다.";
        var erbText = $"PRINTFORM {firstLineValue}\r\nPRINTFORM {secondLineValue}\r\n";
        var firstStart = erbText.IndexOf(firstLineValue, StringComparison.Ordinal);
        var secondStart = erbText.IndexOf(secondLineValue, StringComparison.Ordinal);
        var erbDocument = new SourceFileDocument
        {
            DocumentId = "ERB/SplitLiteralJosa.ERB",
            FullPath = Path.Combine(gameRoot, "ERB", "SplitLiteralJosa.ERB"),
            RelativePath = Path.Combine("ERB", "SplitLiteralJosa.ERB"),
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
            SegmentId = "ERB/SplitLiteralJosa.ERB:0",
            DocumentId = erbDocument.DocumentId,
            SegmentType = "print-tail",
            AbsoluteStart = firstStart,
            Length = firstLineValue.Length,
            LineNumber = 1,
            OriginalText = firstLineValue,
        });
        erbDocument.Segments.Add(new TextSegment
        {
            SegmentId = "ERB/SplitLiteralJosa.ERB:1",
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
            SegmentId = "ERB/SplitLiteralJosa.ERB:0",
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
            SegmentId = "ERB/SplitLiteralJosa.ERB:1",
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

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "SplitLiteralJosa.ERB"), Encoding.UTF8);
        Assert.Contains("PRINTFORM 사과", writtenErb, StringComparison.Ordinal);
        Assert.Contains("PRINTFORM 는 맛있다.", writtenErb, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportCopy_ReplacesOnlyExtractedCodeMixedTextSpan()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        var erbDir = Path.Combine(gameRoot, "ERB");
        Directory.CreateDirectory(erbDir);
        File.WriteAllText(
            Path.Combine(erbDir, "Mixed.ERB"),
            "PRINTFORMW %CALLNAME:TARGET%の高校生\r\n",
            Encoding.UTF8);

        var session = new FileScanner().Scan(gameRoot);
        var item = Assert.Single(session.Items, item => item.OriginalText == "高校生");
        item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "고등학생");

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Mixed.ERB"), Encoding.UTF8);
        Assert.Contains("PRINTFORMW %CALLNAME:TARGET%の고등학생", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("PRINTFORMW 고등학생", writtenErb, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportCopy_PreservesRawHtmlFunctionAndImageKeysWhileReplacingVisibleText()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        var erbDir = Path.Combine(gameRoot, "ERB");
        Directory.CreateDirectory(erbDir);
        File.WriteAllText(
            Path.Combine(erbDir, "HtmlMixed.ERB"),
            "LOCALS += @\"<font color='#%カラーパレット_HTML(\"黄\")%'>特濃</font><img src='えっちハート'>\"\r\n",
            Encoding.UTF8);

        var session = new FileScanner().Scan(gameRoot);
        var item = Assert.Single(session.Items, item => item.OriginalText == "特濃");
        item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "진한");

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "HtmlMixed.ERB"), Encoding.UTF8);
        Assert.Contains("%カラーパレット_HTML(\"黄\")%", writtenErb, StringComparison.Ordinal);
        Assert.Contains("<img src='えっちハート'>", writtenErb, StringComparison.Ordinal);
        Assert.Contains(">진한</font>", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("컬러 팔레트_HTML", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("img src='진한'", writtenErb, StringComparison.Ordinal);
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
        Assert.Contains("%조사처리(CALLNAME:MASTER,\"는\")%", writtenErb, StringComparison.Ordinal);
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
        File.WriteAllText(Path.Combine(csvDir, "Item.csv"), "64,APTX5000ハーフリング,40000\r\n92,子宮内避妊結界,2000\r\n", Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(erbDir, "Test.ERB"),
            "IF TALENT:(targetChara):交際\r\nIF TALENT:GETCHARA(205):失踪\r\nIF MAXBASE:(T):体力 > 1000\r\nIF MONEY > ITEMPRICE:子宮内避妊結界\r\nITEMSALES:APTX5000ハーフリング = 1\r\n",
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

        foreach (var item in session.Items.Where(item => item.SymbolNamespace == "ITEM" && item.OriginalSymbolKey == "APTX5000ハーフリング"))
        {
            item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "APTX5000하프링");
        }

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);
        Assert.Contains("TALENT:(targetChara):교제", writtenErb, StringComparison.Ordinal);
        Assert.Contains("TALENT:GETCHARA(205):실종", writtenErb, StringComparison.Ordinal);
        Assert.Contains("MAXBASE:(T):체력 > 1000", writtenErb, StringComparison.Ordinal);
        Assert.Contains("ITEMPRICE:자궁내피임결계", writtenErb, StringComparison.Ordinal);
        Assert.Contains("ITEMSALES:APTX5000하프링 = 1", writtenErb, StringComparison.Ordinal);
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

    [Fact]
    public void ExportCopy_RewritesNowexReferencesUsingExCsvAliases()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        var csvDir = Path.Combine(gameRoot, "CSV");
        var erbDir = Path.Combine(gameRoot, "ERB");
        Directory.CreateDirectory(csvDir);
        Directory.CreateDirectory(erbDir);

        File.WriteAllText(Path.Combine(csvDir, "Ex.csv"), "0,Ｃ絶頂\r\n", Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(erbDir, "Test.ERB"),
            "IF NOWEX:TARGET:Ｃ絶頂\r\n",
            Encoding.UTF8);

        var session = new FileScanner().Scan(gameRoot);
        foreach (var item in session.Items.Where(item => item.SymbolNamespace == "EX" && item.OriginalSymbolKey == "Ｃ絶頂"))
        {
            item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "C절정");
        }

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);
        Assert.Contains("IF NOWEX:TARGET:C절정", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("IF NOWEX:TARGET:Ｃ絶頂", writtenErb, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportCopy_RewritesCupReferencesUsingSourceCsvAliases()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        var csvDir = Path.Combine(gameRoot, "CSV");
        var erbDir = Path.Combine(gameRoot, "ERB");
        Directory.CreateDirectory(csvDir);
        Directory.CreateDirectory(erbDir);

        File.WriteAllText(Path.Combine(csvDir, "Source.csv"), "0,快Ｃ\r\n", Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(erbDir, "Test.ERB"),
            "SELECTCASE CUP:TARGET:快Ｃ\r\n",
            Encoding.UTF8);

        var session = new FileScanner().Scan(gameRoot);
        foreach (var item in session.Items.Where(item => item.SymbolNamespace == "SOURCE" && item.OriginalSymbolKey == "快Ｃ"))
        {
            item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "쾌C");
        }

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);
        Assert.Contains("SELECTCASE CUP:TARGET:쾌C", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECTCASE CUP:TARGET:快Ｃ", writtenErb, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportCopy_RewritesCupReferencesUsingPalamCsvAliases()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        var csvDir = Path.Combine(gameRoot, "CSV");
        var erbDir = Path.Combine(gameRoot, "ERB");
        Directory.CreateDirectory(csvDir);
        Directory.CreateDirectory(erbDir);

        File.WriteAllText(Path.Combine(csvDir, "Palam.csv"), "0,快Ｃ\r\n", Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(erbDir, "Test.ERB"),
            "IF CUP:ARG:快Ｃ > 0\r\n",
            Encoding.UTF8);

        var session = new FileScanner().Scan(gameRoot);
        foreach (var item in session.Items.Where(item => item.SymbolNamespace == "PALAM" && item.OriginalSymbolKey == "快Ｃ"))
        {
            item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "쾌C");
        }

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);
        Assert.Contains("IF CUP:ARG:쾌C > 0", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("IF CUP:ARG:快Ｃ > 0", writtenErb, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportCopy_RewritesCdownReferencesUsingPalamCsvAliases()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        var csvDir = Path.Combine(gameRoot, "CSV");
        var erbDir = Path.Combine(gameRoot, "ERB");
        Directory.CreateDirectory(csvDir);
        Directory.CreateDirectory(erbDir);

        File.WriteAllText(Path.Combine(csvDir, "Palam.csv"), "0,快Ｃ\r\n", Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(erbDir, "Test.ERB"),
            "IF CDOWN:ARG:快Ｃ > 0\r\n",
            Encoding.UTF8);

        var session = new FileScanner().Scan(gameRoot);
        foreach (var item in session.Items.Where(item => item.SymbolNamespace == "PALAM" && item.OriginalSymbolKey == "快Ｃ"))
        {
            item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "쾌C");
        }

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);
        Assert.Contains("IF CDOWN:ARG:쾌C > 0", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("IF CDOWN:ARG:快Ｃ > 0", writtenErb, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportCopy_RewritesDownbaseReferencesUsingBaseCsvAliases()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        var csvDir = Path.Combine(gameRoot, "CSV");
        var erbDir = Path.Combine(gameRoot, "ERB");
        Directory.CreateDirectory(csvDir);
        Directory.CreateDirectory(erbDir);

        File.WriteAllText(Path.Combine(csvDir, "Base.csv"), "0,体力\r\n", Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(erbDir, "Test.ERB"),
            "IF DOWNBASE:TARGET:体力 > 0\r\n",
            Encoding.UTF8);

        var session = new FileScanner().Scan(gameRoot);
        foreach (var item in session.Items.Where(item => item.SymbolNamespace == "BASE" && item.OriginalSymbolKey == "体力"))
        {
            item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "체력");
        }

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);
        Assert.Contains("IF DOWNBASE:TARGET:체력 > 0", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("IF DOWNBASE:TARGET:体力 > 0", writtenErb, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportCopy_RewritesStrReferencesUsingStrnameCsvAliases()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        var csvDir = Path.Combine(gameRoot, "CSV");
        var erbDir = Path.Combine(gameRoot, "ERB");
        Directory.CreateDirectory(csvDir);
        Directory.CreateDirectory(erbDir);

        File.WriteAllText(Path.Combine(csvDir, "strname.csv"), "2,セーブコメント保存\r\n", Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(erbDir, "Test.ERB"),
            "IF STR:セーブコメント保存 != \"\"\r\n",
            Encoding.UTF8);

        var session = new FileScanner().Scan(gameRoot);
        foreach (var item in session.Items.Where(item => item.SymbolNamespace == "STRNAME" && item.OriginalSymbolKey == "セーブコメント保存"))
        {
            item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "세이브코멘트보존");
        }

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);
        Assert.Contains("IF STR:세이브코멘트보존 != \"\"", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("STR:セーブコメント保存", writtenErb, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportCopy_LetsSymbolRewriteWinOverStaleQuotedStringTranslation()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        var csvDir = Path.Combine(gameRoot, "CSV");
        var erbDir = Path.Combine(gameRoot, "ERB");
        Directory.CreateDirectory(csvDir);
        Directory.CreateDirectory(erbDir);

        File.WriteAllText(Path.Combine(csvDir, "CFLAG.csv"), "328,招待不可フラグ\r\n", Encoding.UTF8);
        var erbText = "SIF CFLAG:GETCHARA(136):招待不可フラグ == CSVCFLAG(136, GETNUM(CFLAG, \"招待不可フラグ\"))\r\n";
        File.WriteAllText(Path.Combine(erbDir, "Test.ERB"), erbText, Encoding.UTF8);

        var session = new FileScanner().Scan(gameRoot);
        foreach (var item in session.Items.Where(item => item.SymbolNamespace == "CFLAG" && item.OriginalSymbolKey == "招待不可フラグ"))
        {
            item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "초대불가플래그");
        }

        var document = session.Documents.Values.Single(document => document.RelativePath.EndsWith("Test.ERB", StringComparison.Ordinal));
        var staleStart = document.OriginalText.LastIndexOf("招待不可フラグ", StringComparison.Ordinal);
        document.Segments.Add(new TextSegment
        {
            SegmentId = $"{document.DocumentId}:stale-getnum",
            DocumentId = document.DocumentId,
            SegmentType = "quoted-string",
            AbsoluteStart = staleStart,
            Length = "招待不可フラグ".Length,
            LineNumber = 1,
            OriginalText = "招待不可フラグ",
        });
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = $"{document.DocumentId}:stale-getnum",
            DocumentId = document.DocumentId,
            FileType = "ERB",
            RelativePath = document.RelativePath,
            EncodingName = "UTF-8",
            SegmentType = "quoted-string",
            LineNumber = 1,
            OriginalText = "招待不可フラグ",
            TranslatedText = "초대 불가 플래그",
            Status = "번역 완료",
            ValidationStatus = "통과",
            WarningText = string.Empty,
        });

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);
        Assert.Contains("CFLAG:GETCHARA(136):초대불가플래그", writtenErb, StringComparison.Ordinal);
        Assert.Contains("GETNUM(CFLAG, \"초대불가플래그\")", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("초대 불가 플래그", writtenErb, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportCopy_IgnoresStaleTranslationsInsideRawStringScriptExpressions()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        var erbDir = Path.Combine(gameRoot, "ERB");
        Directory.CreateDirectory(erbDir);

        var erbText = "SIF STRCOUNT(削除番号, @\"_{DT_CELL_GET(\"ミルクデータベース\", ミルク番号, \"id\")}_\")\r\n";
        File.WriteAllText(Path.Combine(erbDir, "Test.ERB"), erbText, Encoding.UTF8);

        var session = new FileScanner().Scan(gameRoot);
        var document = session.Documents.Values.Single(document => document.RelativePath.EndsWith("Test.ERB", StringComparison.Ordinal));
        var staleText = ", ミルク番号, ";
        var staleStart = document.OriginalText.IndexOf(staleText, StringComparison.Ordinal);
        document.Segments.Add(new TextSegment
        {
            SegmentId = $"{document.DocumentId}:stale-raw",
            DocumentId = document.DocumentId,
            SegmentType = "quoted-string",
            AbsoluteStart = staleStart,
            Length = staleText.Length,
            LineNumber = 1,
            OriginalText = staleText,
        });
        session.Items.Add(new ExtractedTextItem
        {
            SegmentId = $"{document.DocumentId}:stale-raw",
            DocumentId = document.DocumentId,
            FileType = "ERB",
            RelativePath = document.RelativePath,
            EncodingName = "UTF-8",
            SegmentType = "quoted-string",
            LineNumber = 1,
            OriginalText = staleText,
            TranslatedText = ", 밀크 번호,",
            Status = "번역 완료",
            ValidationStatus = "통과",
            WarningText = string.Empty,
        });

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);
        Assert.Contains(staleText, writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("밀크 번호", writtenErb, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportCopy_IgnoresStaleLoadTextAndSaveTextPathTranslations()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        var erbDir = Path.Combine(gameRoot, "ERB");
        Directory.CreateDirectory(erbDir);

        var erbText = "LOADTEXT \"dat/人物DT_XML.txt\"\r\nSAVETEXT RESULTS:0, \"dat/素材DT_schema.txt\"\r\n";
        File.WriteAllText(Path.Combine(erbDir, "Test.ERB"), erbText, Encoding.UTF8);

        var session = new FileScanner().Scan(gameRoot);
        var document = session.Documents.Values.Single(document => document.RelativePath.EndsWith("Test.ERB", StringComparison.Ordinal));
        AddStaleItem(session, document, "dat/人物DT_XML.txt", "dat/인물 DT_XML.txt", "loadtext");
        AddStaleItem(session, document, "dat/素材DT_schema.txt", "dat/소재 DT_schema.txt", "savetext");

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Test.ERB"), Encoding.UTF8);
        Assert.Contains("LOADTEXT \"dat/人物DT_XML.txt\"", writtenErb, StringComparison.Ordinal);
        Assert.Contains("SAVETEXT RESULTS:0, \"dat/素材DT_schema.txt\"", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("dat/인물", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("dat/소재", writtenErb, StringComparison.Ordinal);

        static void AddStaleItem(ScanSession session, SourceFileDocument document, string original, string translated, string suffix)
        {
            var start = document.OriginalText.IndexOf(original, StringComparison.Ordinal);
            var segmentId = $"{document.DocumentId}:stale-{suffix}";
            document.Segments.Add(new TextSegment
            {
                SegmentId = segmentId,
                DocumentId = document.DocumentId,
                SegmentType = "quoted-string",
                AbsoluteStart = start,
                Length = original.Length,
                LineNumber = document.OriginalText[..start].Count(static ch => ch == '\n') + 1,
                OriginalText = original,
            });
            session.Items.Add(new ExtractedTextItem
            {
                SegmentId = segmentId,
                DocumentId = document.DocumentId,
                FileType = "ERB",
                RelativePath = document.RelativePath,
                EncodingName = "UTF-8",
                SegmentType = "quoted-string",
                LineNumber = document.OriginalText[..start].Count(static ch => ch == '\n') + 1,
                OriginalText = original,
                TranslatedText = translated,
                Status = "번역 완료",
                ValidationStatus = "통과",
                WarningText = string.Empty,
            });
        }
    }

    [Fact]
    public void ExportCopy_IgnoresStalePaletteLookupKeyTranslations()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var exportRoot = Path.Combine(tempRoot, "out");
        var erbDir = Path.Combine(gameRoot, "ERB");
        Directory.CreateDirectory(erbDir);

        var erbText = """
@カラーパレット(ARGS)
#FUNCTION
SELECTCASE ARGS
    CASE "真っ赤"
        RETURNF 0xFF3030
ENDSELECT

SETCOLOR カラーパレット("青緑")
PRINTFORMW "普通の文章です"
""";
        File.WriteAllText(Path.Combine(erbDir, "Palette.ERB"), erbText, Encoding.UTF8);

        var session = new FileScanner().Scan(gameRoot);
        var document = session.Documents.Values.Single(document => document.RelativePath.EndsWith("Palette.ERB", StringComparison.Ordinal));
        AddStaleItem(session, document, "真っ赤", "빨강", "case");
        AddStaleItem(session, document, "青緑", "청록색", "call");

        var item = Assert.Single(session.Items, item => item.OriginalText == "普通の文章です");
        item.ApplyTranslationState("번역 완료", "통과", string.Empty, true, "평범한 문장입니다");

        var writer = new OutputWriter();
        writer.Save(session, exportRoot, SaveMode.ExportCopy);

        var writtenErb = File.ReadAllText(Path.Combine(exportRoot, "ERB", "Palette.ERB"), Encoding.UTF8);
        Assert.Contains("CASE \"真っ赤\"", writtenErb, StringComparison.Ordinal);
        Assert.Contains("SETCOLOR カラーパレット(\"青緑\")", writtenErb, StringComparison.Ordinal);
        Assert.Contains("PRINTFORMW \"평범한 문장입니다\"", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("빨강", writtenErb, StringComparison.Ordinal);
        Assert.DoesNotContain("청록색", writtenErb, StringComparison.Ordinal);

        static void AddStaleItem(ScanSession session, SourceFileDocument document, string original, string translated, string suffix)
        {
            var start = document.OriginalText.IndexOf(original, StringComparison.Ordinal);
            var segmentId = $"{document.DocumentId}:stale-palette-{suffix}";
            document.Segments.Add(new TextSegment
            {
                SegmentId = segmentId,
                DocumentId = document.DocumentId,
                SegmentType = "quoted-string",
                AbsoluteStart = start,
                Length = original.Length,
                LineNumber = document.OriginalText[..start].Count(static ch => ch == '\n') + 1,
                OriginalText = original,
            });
            session.Items.Add(new ExtractedTextItem
            {
                SegmentId = segmentId,
                DocumentId = document.DocumentId,
                FileType = "ERB",
                RelativePath = document.RelativePath,
                EncodingName = "UTF-8",
                SegmentType = "quoted-string",
                LineNumber = document.OriginalText[..start].Count(static ch => ch == '\n') + 1,
                OriginalText = original,
                TranslatedText = translated,
                Status = "번역 완료",
                ValidationStatus = "통과",
                WarningText = string.Empty,
            });
        }
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
