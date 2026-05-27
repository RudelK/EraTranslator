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
}
