using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class FileScannerIntegrationTests
{
    [Fact]
    public void Scan_SyntheticGame_FindsErbAndCsvItems()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "CSV"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "ERB"));
        File.WriteAllText(Path.Combine(tempRoot, "CSV", "Item.csv"), "0,薬草\r\n1,魔石\r\n");
        File.WriteAllText(Path.Combine(tempRoot, "ERB", "Test.ERB"), "PRINTFORMW こんにちは\r\n");
        File.WriteAllText(Path.Combine(tempRoot, "ERB", "Common.ERH"), "PRINTFORMW 共通テキスト\r\n");

        try
        {
            var scanner = new FileScanner();
            var session = scanner.Scan(tempRoot);

            Assert.NotEmpty(session.Documents);
            Assert.NotEmpty(session.Items);
            Assert.True(session.Metrics.GetValueOrDefault("ErbItems") > 0);
            Assert.True(session.Metrics.GetValueOrDefault("CsvItems") > 0);
            Assert.Equal(CsvDocumentKind.IdFirstTable, session.Documents["CSV/Item.csv"].CsvKind);
            Assert.Contains("ERB/Common.ERH", session.Documents.Keys);
            Assert.Equal(DocumentFileTypes.Erh, session.Documents["ERB/Common.ERH"].FileType);
            Assert.Contains(session.Items, item => item.DocumentId == "ERB/Common.ERH" && item.OriginalText == "共通テキスト");
            Assert.DoesNotContain(session.Documents.Values.SelectMany(document => document.ScanWarnings), warning => warning.Contains("전용 추출 규칙", StringComparison.Ordinal));
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
    public void Scan_FindsAssignmentFragmentsInErbFiles()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var erbRoot = Path.Combine(tempRoot, "ERB");
        Directory.CreateDirectory(erbRoot);
        var erbPath = Path.Combine(erbRoot, "Test.ERB");
        File.WriteAllText(erbPath, """
drinkName = 缶ジュース
label = 高校生 + "です"
status = cond ? 学生 # 社会人
""");

        var scanner = new FileScanner();
        var session = scanner.Scan(tempRoot);

        Assert.Contains(session.Items, item => item.SegmentType == "assignment-value" && item.OriginalText == "缶ジュース");
        Assert.Contains(session.Items, item => item.SegmentType == "assignment-fragment" && item.OriginalText == "高校生");
        Assert.Contains(session.Items, item => item.SegmentType == "assignment-fragment" && item.OriginalText == "学生");
        Assert.Contains(session.Items, item => item.SegmentType == "assignment-fragment" && item.OriginalText == "社会人");
    }

    [Fact]
    public void Scan_UsesFunctionRegistryToSkipQuotedCodeRulesButKeepNaturalParentheses()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var erbRoot = Path.Combine(tempRoot, "ERB");
        Directory.CreateDirectory(erbRoot);
        File.WriteAllText(Path.Combine(erbRoot, "Functions.ERB"), """
@IS_UNCONTACTABLE(index)
@IS_NOT_POLICE_RESCUE_TALENT(index)
""");
        File.WriteAllText(Path.Combine(erbRoot, "Test.ERB"), """
abductedCharaCount = COUNT_RULED_CHARAS("!IS_UNCONTACTABLE(index) && TALENT:index:監禁 > 0 && !IS_NOT_POLICE_RESCUE_TALENT(index)")
PRINTFORML [2] - エンディング履歴(エンディング別)
""");

        try
        {
            var scanner = new FileScanner();
            var session = scanner.Scan(tempRoot);

            Assert.DoesNotContain(session.Items, item =>
                item.OriginalText.Contains("IS_UNCONTACTABLE", StringComparison.Ordinal)
                || item.OriginalText.Contains("TALENT:index:監禁", StringComparison.Ordinal));
            Assert.Contains(session.Items, item =>
                item.DocumentId == "ERB/Test.ERB"
                && item.SegmentType == "print-tail"
                && item.OriginalText == "[2] - エンディング履歴(エンディング別)");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
