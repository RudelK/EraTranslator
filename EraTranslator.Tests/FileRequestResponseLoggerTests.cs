using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class FileRequestResponseLoggerTests : IDisposable
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
    public void Logger_WritesSequentialRequestAndResponseEntries()
    {
        Directory.CreateDirectory(_rootPath);
        var logger = new FileRequestResponseLogger(_rootPath);

        logger.LogRequest("OpenAI", "http://localhost/request", "{\"x\":1}");
        logger.LogResponse("OpenAI", "http://localhost/request", 200, "{\"ok\":true}");

        var content = File.ReadAllText(logger.LogFilePath);

        Assert.Contains("REQUEST", content, StringComparison.Ordinal);
        Assert.Contains("RESPONSE", content, StringComparison.Ordinal);
        Assert.Contains("Provider: OpenAI", content, StringComparison.Ordinal);
        Assert.Contains("Status: 200", content, StringComparison.Ordinal);
        Assert.True(content.IndexOf("REQUEST", StringComparison.Ordinal) < content.IndexOf("RESPONSE", StringComparison.Ordinal));
    }
}
