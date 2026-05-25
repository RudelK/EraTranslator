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
}
