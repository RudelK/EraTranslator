namespace EraTranslator.Services;

public sealed class OutputWriteResult
{
    public List<string> WrittenFiles { get; } = [];

    public List<string> BackupFiles { get; } = [];

    public List<string> SkippedFiles { get; } = [];
}
