namespace EraTranslator.Services;

public sealed class OutputWriteResult
{
    public List<string> WrittenFiles { get; } = [];

    public List<string> BackupFiles { get; } = [];

    public List<string> SkippedFiles { get; } = [];

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset CompletedAt { get; set; }

    public TimeSpan TotalElapsed { get; set; }

    public TimeSpan RefreshElapsed { get; set; }

    public TimeSpan RewritePlanElapsed { get; set; }

    public TimeSpan CopyElapsed { get; set; }

    public TimeSpan BackupElapsed { get; set; }

    public TimeSpan DocumentWriteElapsed { get; set; }

    public TimeSpan PackageWriteElapsed { get; set; }
}
