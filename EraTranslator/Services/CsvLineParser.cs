using System.Text;

namespace EraTranslator.Services;

public static class CsvLineParser
{
    public static List<CsvFieldInfo> ParseFields(string line)
    {
        var result = new List<CsvFieldInfo>();
        var fieldStart = 0;
        var inQuotes = false;

        for (var index = 0; index <= line.Length; index++)
        {
            var atEnd = index == line.Length;
            var ch = atEnd ? ',' : line[index];

            if (!atEnd && ch == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    index++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (atEnd || (ch == ',' && !inQuotes))
            {
                var raw = line[fieldStart..index];
                result.Add(CreateField(result.Count, fieldStart, raw));
                fieldStart = index + 1;
            }
        }

        return result;
    }

    public static string RebuildLine(IReadOnlyList<CsvFieldInfo> fields, IReadOnlyDictionary<int, string> replacements)
    {
        return string.Join(",", fields.Select(field =>
        {
            if (!replacements.TryGetValue(field.FieldIndex, out var replacement))
            {
                return field.RawText;
            }

            var shouldQuote = field.WasQuoted
                || replacement.Contains(',')
                || replacement.Contains('"')
                || replacement.Contains('\r')
                || replacement.Contains('\n')
                || replacement.StartsWith(' ')
                || replacement.EndsWith(' ')
                || replacement.StartsWith('\t')
                || replacement.EndsWith('\t');

            var serialized = shouldQuote
                ? $"\"{replacement.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
                : replacement;

            return $"{field.LeadingTrivia}{serialized}{field.TrailingTrivia}";
        }));
    }

    private static CsvFieldInfo CreateField(int fieldIndex, int rawStart, string raw)
    {
        var leadingTrim = 0;
        while (leadingTrim < raw.Length && (raw[leadingTrim] == ' ' || raw[leadingTrim] == '\t'))
        {
            leadingTrim++;
        }

        var trailingTrim = 0;
        while (trailingTrim < raw.Length - leadingTrim && (raw[raw.Length - 1 - trailingTrim] == ' ' || raw[raw.Length - 1 - trailingTrim] == '\t'))
        {
            trailingTrim++;
        }

        var coreLength = Math.Max(0, raw.Length - leadingTrim - trailingTrim);
        var core = raw.Substring(leadingTrim, coreLength);
        var wasQuoted = core.Length >= 2 && core.StartsWith('"') && core.EndsWith('"');
        var value = wasQuoted
            ? UnescapeQuoted(core[1..^1])
            : core;

        return new CsvFieldInfo
        {
            FieldIndex = fieldIndex,
            RawStart = rawStart,
            RawLength = raw.Length,
            RawText = raw,
            LeadingTrivia = raw[..leadingTrim],
            TrailingTrivia = trailingTrim == 0 ? string.Empty : raw[^trailingTrim..],
            WasQuoted = wasQuoted,
            Value = value,
        };
    }

    private static string UnescapeQuoted(string input)
    {
        var builder = new StringBuilder(input.Length);
        for (var index = 0; index < input.Length; index++)
        {
            if (input[index] == '"' && index + 1 < input.Length && input[index + 1] == '"')
            {
                builder.Append('"');
                index++;
                continue;
            }

            builder.Append(input[index]);
        }

        return builder.ToString();
    }
}
