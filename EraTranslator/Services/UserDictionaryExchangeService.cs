using System.Text;
using System.Text.RegularExpressions;
using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed partial class UserDictionaryExchangeService
{
    private const string Header = "# EraTranslator User Dictionary v1";
    private static readonly UTF8Encoding Utf8BomEncoding = new(true);

    public void Export(string path, IEnumerable<UserDictionaryEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Header);
        foreach (var entry in NormalizeEntries(entries))
        {
            builder.Append(Escape(entry.Source));
            builder.Append('\t');
            builder.Append(Escape(entry.Target));
            builder.Append('\t');
            builder.Append(FormatApplyMode(entry.ApplyMode));
            builder.Append('\t');
            builder.Append(entry.IsEnabled ? "사용" : "미사용");
            builder.AppendLine();
        }

        File.WriteAllText(path, builder.ToString(), Utf8BomEncoding);
    }

    public UserDictionaryExchangeImportResult Import(string path)
    {
        var content = File.ReadAllText(path);
        var extension = Path.GetExtension(path);
        if (extension.Equals(".simplesrs", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".srs", StringComparison.OrdinalIgnoreCase))
        {
            return ImportSimpleSrs(content);
        }

        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var firstLine = normalized.Split('\n').FirstOrDefault() ?? string.Empty;
        return firstLine.StartsWith(Header, StringComparison.Ordinal)
            || extension.Equals(".etdict", StringComparison.OrdinalIgnoreCase)
            ? ImportEraDictionary(normalized)
            : ImportSimpleSrs(content);
    }

    private static UserDictionaryExchangeImportResult ImportEraDictionary(string content)
    {
        var entries = new List<UserDictionaryEntry>();
        var skipped = 0;
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)
                || line.StartsWith(Header, StringComparison.Ordinal)
                || string.Equals(line, "원문\t번역문\t치환여부\t사용여부", StringComparison.Ordinal))
            {
                continue;
            }

            var fields = line.Split('\t');
            if (fields.Length != 4
                || !TryParseApplyMode(fields[2], out var applyMode)
                || !TryParseEnabled(fields[3], out var isEnabled))
            {
                skipped++;
                continue;
            }

            var source = Unescape(fields[0]).Trim();
            var target = Unescape(fields[1]).Trim();
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
            {
                skipped++;
                continue;
            }

            entries.Add(new UserDictionaryEntry
            {
                IsEnabled = isEnabled,
                Source = source,
                Target = target,
                ApplyMode = applyMode,
            });
        }

        return new UserDictionaryExchangeImportResult(entries, skipped, []);
    }

    private static UserDictionaryExchangeImportResult ImportSimpleSrs(string content)
    {
        var lines = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(static line => line.Trim())
            .Select(static line => line.Length == 0
                || line.StartsWith(';')
                || LooksLikeSimpleSrsDirective(line)
                    ? new SimpleSrsLine(string.Empty, IsBoundary: true)
                    : new SimpleSrsLine(line, IsBoundary: false))
            .ToList();
        var contentLines = lines
            .Where(static line => !line.IsBoundary)
            .Select(static line => line.Text)
            .ToList();
        var quotedLineCount = contentLines.Count(line => TryParseQuotedLine(line, out _));
        var plainLineCount = contentLines.Count - quotedLineCount;
        return plainLineCount > quotedLineCount
            ? ImportPlainSimpleSrs(lines)
            : ImportQuotedSimpleSrs(lines);
    }

    private static UserDictionaryExchangeImportResult ImportPlainSimpleSrs(IReadOnlyList<SimpleSrsLine> lines)
    {
        var entries = new List<UserDictionaryEntry>();
        var skipped = 0;
        string? pendingSource = null;

        foreach (var parsedLine in lines)
        {
            if (parsedLine.IsBoundary)
            {
                if (pendingSource is not null)
                {
                    pendingSource = null;
                    skipped++;
                }

                continue;
            }

            var line = parsedLine.Text;
            if (LooksLikeBrokenQuotedLine(line))
            {
                if (pendingSource is not null)
                {
                    pendingSource = null;
                    skipped++;
                }

                skipped++;
                continue;
            }

            var value = TryParseQuotedLine(line, out var quotedValue)
                ? quotedValue
                : line;
            if (pendingSource is null)
            {
                pendingSource = value;
                continue;
            }

            AddSimpleSrsEntry(entries, pendingSource, value, ref skipped);
            pendingSource = null;
        }

        if (pendingSource is not null)
        {
            skipped++;
        }

        return new UserDictionaryExchangeImportResult(entries, skipped, []);
    }

    private static UserDictionaryExchangeImportResult ImportQuotedSimpleSrs(IReadOnlyList<SimpleSrsLine> lines)
    {
        var entries = new List<UserDictionaryEntry>();
        var skipped = 0;
        string? pendingSource = null;

        foreach (var parsedLine in lines)
        {
            if (parsedLine.IsBoundary)
            {
                if (pendingSource is not null)
                {
                    pendingSource = null;
                    skipped++;
                }

                continue;
            }

            var line = parsedLine.Text;
            if (!TryParseQuotedLine(line, out var value))
            {
                if (pendingSource is not null)
                {
                    pendingSource = null;
                    skipped++;
                }

                skipped++;
                continue;
            }

            if (pendingSource is null)
            {
                pendingSource = value;
                continue;
            }

            AddSimpleSrsEntry(entries, pendingSource, value, ref skipped);
            pendingSource = null;
        }

        if (pendingSource is not null)
        {
            skipped++;
        }

        return new UserDictionaryExchangeImportResult(entries, skipped, []);
    }

    private static void AddSimpleSrsEntry(
        ICollection<UserDictionaryEntry> entries,
        string rawSource,
        string rawTarget,
        ref int skipped)
    {
        var source = rawSource.Trim();
        var target = rawTarget.Trim();
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
        {
            skipped++;
            return;
        }

        entries.Add(new UserDictionaryEntry
        {
            IsEnabled = true,
            Source = source,
            Target = target,
            ApplyMode = UserDictionaryApplyMode.Prompting,
        });
    }

    private static bool LooksLikeSimpleSrsDirective(string line)
    {
        return line.StartsWith("[-", StringComparison.Ordinal)
            && line.EndsWith(']');
    }

    private static IEnumerable<UserDictionaryEntry> NormalizeEntries(IEnumerable<UserDictionaryEntry> entries)
    {
        foreach (var entry in entries)
        {
            var source = (entry.Source ?? string.Empty).Trim();
            var target = (entry.Target ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            yield return new UserDictionaryEntry
            {
                IsEnabled = entry.IsEnabled,
                Source = source,
                Target = target,
                ApplyMode = entry.ApplyMode,
            };
        }
    }

    private static string Escape(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static string Unescape(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (ch != '\\' || index + 1 >= value.Length)
            {
                builder.Append(ch);
                continue;
            }

            var escaped = value[++index];
            builder.Append(escaped switch
            {
                't' => '\t',
                'n' => '\n',
                'r' => '\r',
                '\\' => '\\',
                _ => escaped,
            });
        }

        return builder.ToString();
    }

    private static string FormatApplyMode(UserDictionaryApplyMode applyMode)
    {
        return applyMode == UserDictionaryApplyMode.Prompting ? "프롬프팅" : "치환";
    }

    private static bool TryParseApplyMode(string value, out UserDictionaryApplyMode applyMode)
    {
        var normalized = value.Trim();
        if (string.Equals(normalized, "치환", StringComparison.Ordinal))
        {
            applyMode = UserDictionaryApplyMode.Replace;
            return true;
        }

        if (string.Equals(normalized, "프롬프팅", StringComparison.Ordinal))
        {
            applyMode = UserDictionaryApplyMode.Prompting;
            return true;
        }

        applyMode = UserDictionaryApplyMode.Replace;
        return false;
    }

    private static bool TryParseEnabled(string value, out bool isEnabled)
    {
        var normalized = value.Trim();
        if (string.Equals(normalized, "사용", StringComparison.Ordinal))
        {
            isEnabled = true;
            return true;
        }

        if (string.Equals(normalized, "미사용", StringComparison.Ordinal))
        {
            isEnabled = false;
            return true;
        }

        isEnabled = false;
        return false;
    }

    private static bool TryParseQuotedLine(string line, out string value)
    {
        var match = QuotedLinePattern().Match(line);
        if (!match.Success)
        {
            value = string.Empty;
            return false;
        }

        value = Regex.Unescape(match.Groups["value"].Value);
        return true;
    }

    private static bool LooksLikeBrokenQuotedLine(string line)
    {
        var trimmed = line.Trim();
        return (trimmed.StartsWith('"') || trimmed.EndsWith('"'))
            && !TryParseQuotedLine(trimmed, out _);
    }

    [GeneratedRegex("""^\s*"(?<value>(?:[^"\\]|\\.)*)"\s*$""", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex QuotedLinePattern();

    private readonly record struct SimpleSrsLine(string Text, bool IsBoundary);
}

public sealed record UserDictionaryExchangeImportResult(
    IReadOnlyList<UserDictionaryEntry> Entries,
    int Skipped,
    IReadOnlyList<string> Warnings);
