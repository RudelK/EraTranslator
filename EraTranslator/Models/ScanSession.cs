namespace EraTranslator.Models;

public sealed class ScanSession
{
    public required string GameRoot { get; init; }

    public Dictionary<string, SourceFileDocument> Documents { get; } = [];

    public List<ExtractedTextItem> Items { get; } = [];

    public Dictionary<string, int> Metrics { get; } = [];

    public JosaSupportPackageInfo JosaPackageInfo { get; set; } = new();
}
