using System.Text;

namespace EraTranslator.Services;

public sealed class TranslationTextExchangeService
{
    private const string Header = "# EraTranslator Text Export v1";
    private const string SegmentPrefix = "<<<ERA_SEGMENT ";
    private const string OriginalMarker = "<<<ERA_ORIGINAL>>>";
    private const string TranslatedMarker = "<<<ERA_TRANSLATED>>>";
    private const string EndMarker = "<<<ERA_END>>>";

    public void Export(string path, IEnumerable<ExtractedTextItem> items)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Header);
        builder.AppendLine();

        foreach (var item in items.OrderBy(item => item.RelativePath).ThenBy(item => item.LineNumber))
        {
            builder.AppendLine($"{SegmentPrefix}{item.SegmentId}>>>");
            builder.AppendLine($"FILE: {item.RelativePath}");
            builder.AppendLine($"LINE: {item.LineNumber}");
            builder.AppendLine($"TYPE: {item.FileType}");
            builder.AppendLine($"STATUS: {item.Status}");
            if (!string.IsNullOrWhiteSpace(item.SourceKey))
            {
                builder.AppendLine($"SOURCE_KEY: {item.SourceKey}");
            }

            builder.AppendLine(OriginalMarker);
            builder.AppendLine(item.OriginalText ?? string.Empty);
            builder.AppendLine(TranslatedMarker);
            builder.AppendLine(item.TranslatedText ?? string.Empty);
            builder.AppendLine(EndMarker);
            builder.AppendLine();
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
    }

    public IReadOnlyList<TranslationTextExchangeEntry> Import(string path)
    {
        var content = File.ReadAllText(path);
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var entries = new List<TranslationTextExchangeEntry>();
        var index = 0;

        if (lines.Length > 0 && lines[0].StartsWith(Header, StringComparison.Ordinal))
        {
            index = 1;
        }

        while (index < lines.Length)
        {
            var line = lines[index].TrimEnd();
            if (!line.StartsWith(SegmentPrefix, StringComparison.Ordinal))
            {
                index++;
                continue;
            }

            var segmentId = line[SegmentPrefix.Length..];
            segmentId = segmentId.EndsWith(">>>", StringComparison.Ordinal)
                ? segmentId[..^3]
                : segmentId;
            index++;

            while (index < lines.Length && !string.Equals(lines[index], OriginalMarker, StringComparison.Ordinal))
            {
                index++;
            }

            if (index >= lines.Length)
            {
                break;
            }

            index++;
            var originalBuilder = new StringBuilder();
            while (index < lines.Length && !string.Equals(lines[index], TranslatedMarker, StringComparison.Ordinal))
            {
                AppendLinePreservingNewlines(originalBuilder, lines[index]);
                index++;
            }

            if (index >= lines.Length)
            {
                break;
            }

            index++;
            var translatedBuilder = new StringBuilder();
            while (index < lines.Length && !string.Equals(lines[index], EndMarker, StringComparison.Ordinal))
            {
                AppendLinePreservingNewlines(translatedBuilder, lines[index]);
                index++;
            }

            entries.Add(new TranslationTextExchangeEntry(
                segmentId.Trim(),
                originalBuilder.ToString(),
                translatedBuilder.ToString()));

            while (index < lines.Length && !string.Equals(lines[index], EndMarker, StringComparison.Ordinal))
            {
                index++;
            }

            if (index < lines.Length)
            {
                index++;
            }
        }

        return entries;
    }

    private static void AppendLinePreservingNewlines(StringBuilder builder, string line)
    {
        if (builder.Length > 0)
        {
            builder.Append('\n');
        }

        builder.Append(line);
    }
}

public sealed record TranslationTextExchangeEntry(
    string SegmentId,
    string OriginalText,
    string TranslatedText);
