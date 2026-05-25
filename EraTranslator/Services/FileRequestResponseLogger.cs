using System.Text;

namespace EraTranslator.Services;

public sealed class FileRequestResponseLogger(string? baseDirectory = null) : IRequestResponseLogger
{
    private static readonly object FileLock = new();
    private static long _sequence;

    public string LogFilePath { get; } = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "EraTranslator.request-response.log");

    public void LogRequest(string providerName, string endpoint, string content, IReadOnlyDictionary<string, string>? headers = null)
    {
        WriteEntry("REQUEST", providerName, endpoint, null, content, headers);
    }

    public void LogResponse(string providerName, string endpoint, int statusCode, string content, IReadOnlyDictionary<string, string>? headers = null)
    {
        WriteEntry("RESPONSE", providerName, endpoint, statusCode, content, headers);
    }

    public void LogError(string providerName, string endpoint, string message)
    {
        WriteEntry("ERROR", providerName, endpoint, null, message, null);
    }

    private void WriteEntry(
        string kind,
        string providerName,
        string endpoint,
        int? statusCode,
        string content,
        IReadOnlyDictionary<string, string>? headers)
    {
        var sequence = Interlocked.Increment(ref _sequence);
        var builder = new StringBuilder();
        builder.AppendLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] #{sequence} {kind}");
        builder.AppendLine($"Provider: {providerName}");
        builder.AppendLine($"Endpoint: {endpoint}");
        if (statusCode.HasValue)
        {
            builder.AppendLine($"Status: {statusCode.Value}");
        }

        if (headers is not null && headers.Count > 0)
        {
            builder.AppendLine("Headers:");
            foreach (var pair in headers)
            {
                builder.AppendLine($"  {pair.Key}: {pair.Value}");
            }
        }

        builder.AppendLine("Content:");
        builder.AppendLine(string.IsNullOrWhiteSpace(content) ? "(empty)" : content);
        builder.AppendLine(new string('-', 80));

        lock (FileLock)
        {
            File.AppendAllText(LogFilePath, builder.ToString(), Encoding.UTF8);
        }
    }
}
