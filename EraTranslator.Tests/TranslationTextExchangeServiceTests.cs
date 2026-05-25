using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class TranslationTextExchangeServiceTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    [Fact]
    public void ExportAndImport_RoundTripsEntries()
    {
        Directory.CreateDirectory(_rootPath);
        var path = Path.Combine(_rootPath, "exchange.txt");
        var service = new TranslationTextExchangeService();
        var items = new[]
        {
            new ExtractedTextItem
            {
                SegmentId = "doc:1",
                DocumentId = "doc",
                FileType = "ERB",
                RelativePath = "A.ERB",
                EncodingName = "utf-8",
                SegmentType = "PRINT",
                LineNumber = 10,
                OriginalText = "첫째 줄\n둘째 줄",
                SourceKey = "KEY",
                CsvFieldRole = CsvFieldRole.TranslatableValue,
            },
        };
        items[0].TranslatedText = "번역 첫째\n번역 둘째";

        service.Export(path, items);
        var imported = service.Import(path);

        Assert.Single(imported);
        Assert.Equal("doc:1", imported[0].SegmentId);
        Assert.Equal("첫째 줄\n둘째 줄", imported[0].OriginalText);
        Assert.Equal("번역 첫째\n번역 둘째", imported[0].TranslatedText);
    }
}
