using System.Text;

namespace EraTranslator.Services;

public sealed class FilePerformanceDebugLogger(string? baseDirectory = null)
{
    private static readonly object FileLock = new();
    private static long _sequence;

    public string LogFilePath { get; } = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "EraTranslator.performance.log");

    public void Log(string category, string message, IReadOnlyDictionary<string, string>? fields = null)
    {
        var sequence = Interlocked.Increment(ref _sequence);
        var builder = new StringBuilder();
        builder.AppendLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] #{sequence} {category}");
        builder.AppendLine(string.IsNullOrWhiteSpace(message) ? "(empty)" : message);
        if (fields is not null && fields.Count > 0)
        {
            foreach (var pair in fields)
            {
                builder.AppendLine($"  {pair.Key}: {pair.Value}");
            }
        }

        builder.AppendLine(new string('-', 80));

        lock (FileLock)
        {
            File.AppendAllText(LogFilePath, builder.ToString(), Encoding.UTF8);
        }
    }
}
