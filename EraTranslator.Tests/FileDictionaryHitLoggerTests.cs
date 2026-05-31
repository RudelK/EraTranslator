using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class FileDictionaryHitLoggerTests : IDisposable
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
    public void Logger_WritesDictionaryHitEntry()
    {
        Directory.CreateDirectory(_rootPath);
        var logger = new FileDictionaryHitLogger(_rootPath);

        logger.LogHit(new DictionaryHitLogEntry(
            "id-1",
            "CSV/Talent.csv",
            12,
            "快楽",
            "쾌락",
            "사전 번역",
            "surface",
            "快楽",
            "TALENT",
            "TALENT",
            "快楽",
            2,
            false,
            string.Empty,
            "naver",
            true,
            "https://example.test",
            false));

        var content = File.ReadAllText(logger.LogFilePath);

        Assert.Contains("DICTIONARY_HIT", content, StringComparison.Ordinal);
        Assert.Contains("Source: 사전 번역", content, StringComparison.Ordinal);
        Assert.Contains("MatchKind: surface", content, StringComparison.Ordinal);
        Assert.Contains("Original: 快楽", content, StringComparison.Ordinal);
        Assert.Contains("Translated: 쾌락", content, StringComparison.Ordinal);
        Assert.Contains("DictionaryStore: naver", content, StringComparison.Ordinal);
        Assert.Contains("PersistedToNaverDictionary: true", content, StringComparison.Ordinal);
        Assert.Contains("SourceUrl: https://example.test", content, StringComparison.Ordinal);
        Assert.Contains("AffectedItems: 2", content, StringComparison.Ordinal);
    }
}
