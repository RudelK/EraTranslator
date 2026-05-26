using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class FileScannerIntegrationTests
{
    [Fact]
    public void Scan_SampleGame_FindsErbAndCsvItems()
    {
        var samplePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "sample", "era魔界牧場1.050"));
        Assert.True(Directory.Exists(samplePath), $"샘플 경로를 찾지 못했습니다: {samplePath}");

        var scanner = new FileScanner();
        var session = scanner.Scan(samplePath);

        Assert.NotEmpty(session.Documents);
        Assert.NotEmpty(session.Items);
        Assert.True(session.Metrics.GetValueOrDefault("ErbItems") > 0);
        Assert.True(session.Metrics.GetValueOrDefault("CsvItems") > 0);
        Assert.Equal(CsvDocumentKind.IdFirstTable, session.Documents["CSV/Item.csv"].CsvKind);
        Assert.DoesNotContain(session.Documents.Values.SelectMany(document => document.ScanWarnings), warning => warning.Contains("전용 추출 규칙", StringComparison.Ordinal));
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
}
