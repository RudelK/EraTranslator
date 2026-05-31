using System.IO.Compression;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;

var options = BuildOptions.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("""
        Usage:
          EraTranslator.DictionaryBuilder --jmdict <JMdict.gz|xml> --jmnedict <JMnedict.gz|xml> --kanjidic2 <kanjidic2.xml.gz|xml> --overrides <ko-overrides.json> --output <bundled-japanese-lexicon.sqlite>

        Notes:
          Raw JMdict/JMnedict/KANJIDIC2 files are build inputs only. Do not copy them into the app release.
        """);
    return 2;
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
if (File.Exists(options.OutputPath))
{
    File.Delete(options.OutputPath);
}

var overrides = KoreanOverrideSet.Load(options.OverridesPath);
await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
{
    DataSource = options.OutputPath,
}.ToString());
await connection.OpenAsync();

CreateSchema(connection);
WriteMetadata(connection, overrides.SnapshotVersion);

using (var transaction = connection.BeginTransaction())
{
    var inserter = new TermInserter(connection, transaction, overrides);
    if (!string.IsNullOrWhiteSpace(options.JMdictPath))
    {
        Console.WriteLine("Importing JMdict...");
        ImportJMdict(options.JMdictPath, inserter);
    }

    if (!string.IsNullOrWhiteSpace(options.JMnedictPath))
    {
        Console.WriteLine("Importing JMnedict...");
        ImportJMnedict(options.JMnedictPath, inserter);
    }

    Console.WriteLine("Adding Korean override seed terms...");
    inserter.InsertOverrideSeedTerms();
    transaction.Commit();
}

using (var transaction = connection.BeginTransaction())
{
    Console.WriteLine("Importing KANJIDIC2...");
    ImportKanjidic2(options.Kanjidic2Path, connection, transaction);
    transaction.Commit();
}

CreateIndexes(connection);
Console.WriteLine($"Wrote {options.OutputPath}");
return 0;

static void CreateSchema(SqliteConnection connection)
{
    using var command = connection.CreateCommand();
    command.CommandText = """
        PRAGMA journal_mode = OFF;
        PRAGMA synchronous = OFF;

        CREATE TABLE metadata (
            key TEXT PRIMARY KEY NOT NULL,
            value TEXT NOT NULL
        );

        CREATE TABLE terms (
            surface TEXT NOT NULL,
            reading_kana TEXT NOT NULL,
            priority INTEGER NOT NULL,
            pos_flags TEXT NOT NULL,
            is_name INTEGER NOT NULL,
            ko_target TEXT NULL,
            source TEXT NOT NULL
        );

        CREATE TABLE kanji_readings (
            kanji_char TEXT PRIMARY KEY NOT NULL,
            korean_h TEXT NOT NULL,
            ja_on TEXT NOT NULL,
            ja_kun TEXT NOT NULL
        );
        """;
    command.ExecuteNonQuery();
}

static void CreateIndexes(SqliteConnection connection)
{
    using var command = connection.CreateCommand();
    command.CommandText = """
        CREATE INDEX idx_terms_surface ON terms(surface);
        CREATE INDEX idx_terms_reading ON terms(reading_kana);
        CREATE INDEX idx_terms_preference ON terms(surface, ko_target, is_name, priority);
        PRAGMA optimize;
        """;
    command.ExecuteNonQuery();
}

static void WriteMetadata(SqliteConnection connection, string snapshotVersion)
{
    using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO metadata(key, value) VALUES
            ('snapshot_version', $snapshotVersion),
            ('generated_at_utc', $generatedAtUtc),
            ('sources', 'JMdict/JMnedict/KANJIDIC2 + EraTranslator ko-overrides')
        """;
    command.Parameters.AddWithValue("$snapshotVersion", snapshotVersion);
    command.Parameters.AddWithValue("$generatedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
    command.ExecuteNonQuery();
}

static void ImportJMdict(string path, TermInserter inserter)
{
    foreach (var entry in ReadEntryElements(path))
    {
        var surfaces = entry.Elements("k_ele")
            .Elements("keb")
            .Select(static element => element.Value.Trim())
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var readings = entry.Elements("r_ele")
            .Elements("reb")
            .Select(static element => element.Value.Trim())
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var forms = surfaces.Count > 0 ? surfaces : readings;
        var priority = ResolvePriority(entry.Descendants().Where(static element => element.Name.LocalName is "ke_pri" or "re_pri").Select(static element => element.Value));
        var posFlags = string.Join('|', entry.Elements("sense").Elements("pos").Select(static element => element.Value.Trim()).Where(static value => value.Length > 0).Distinct(StringComparer.Ordinal));

        foreach (var surface in forms)
        {
            foreach (var reading in readings.DefaultIfEmpty(string.Empty))
            {
                inserter.Insert(surface, reading, priority, posFlags, isName: false, source: "JMdict");
            }
        }
    }
}

static void ImportJMnedict(string path, TermInserter inserter)
{
    foreach (var entry in ReadEntryElements(path))
    {
        var surfaces = entry.Elements("k_ele")
            .Elements("keb")
            .Select(static element => element.Value.Trim())
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var readings = entry.Elements("r_ele")
            .Elements("reb")
            .Select(static element => element.Value.Trim())
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var forms = surfaces.Count > 0 ? surfaces : readings;
        var priority = ResolvePriority(entry.Descendants().Where(static element => element.Name.LocalName is "ke_pri" or "re_pri").Select(static element => element.Value));
        var nameTypes = string.Join('|', entry.Descendants("name_type").Select(static element => element.Value.Trim()).Where(static value => value.Length > 0).Distinct(StringComparer.Ordinal));

        foreach (var surface in forms)
        {
            foreach (var reading in readings.DefaultIfEmpty(string.Empty))
            {
                inserter.Insert(surface, reading, priority, nameTypes, isName: true, source: "JMnedict");
            }
        }
    }
}

static void ImportKanjidic2(string path, SqliteConnection connection, SqliteTransaction transaction)
{
    using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = """
        INSERT OR REPLACE INTO kanji_readings(kanji_char, korean_h, ja_on, ja_kun)
        VALUES ($kanji, $koreanH, $jaOn, $jaKun)
        """;
    var kanjiParameter = command.Parameters.Add("$kanji", SqliteType.Text);
    var koreanParameter = command.Parameters.Add("$koreanH", SqliteType.Text);
    var jaOnParameter = command.Parameters.Add("$jaOn", SqliteType.Text);
    var jaKunParameter = command.Parameters.Add("$jaKun", SqliteType.Text);

    foreach (var character in ReadCharacterElements(path))
    {
        var literal = character.Element("literal")?.Value.Trim() ?? string.Empty;
        if (literal.Length != 1)
        {
            continue;
        }

        var readings = character.Descendants("reading").ToList();
        var koreanH = readings.FirstOrDefault(static element => string.Equals((string?)element.Attribute("r_type"), "korean_h", StringComparison.Ordinal))?.Value.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(koreanH))
        {
            continue;
        }

        kanjiParameter.Value = literal;
        koreanParameter.Value = koreanH;
        jaOnParameter.Value = string.Join('|', readings.Where(static element => string.Equals((string?)element.Attribute("r_type"), "ja_on", StringComparison.Ordinal)).Select(static element => element.Value.Trim()).Where(static value => value.Length > 0).Distinct(StringComparer.Ordinal));
        jaKunParameter.Value = string.Join('|', readings.Where(static element => string.Equals((string?)element.Attribute("r_type"), "ja_kun", StringComparison.Ordinal)).Select(static element => element.Value.Trim()).Where(static value => value.Length > 0).Distinct(StringComparer.Ordinal));
        command.ExecuteNonQuery();
    }
}

static IEnumerable<XElement> ReadEntryElements(string path)
{
    using var stream = OpenPossiblyGzip(path);
    using var reader = XmlReader.Create(stream, XmlSettings());
    while (reader.ReadToFollowing("entry"))
    {
        if (XNode.ReadFrom(reader) is XElement element)
        {
            yield return element;
        }
    }
}

static IEnumerable<XElement> ReadCharacterElements(string path)
{
    using var stream = OpenPossiblyGzip(path);
    using var reader = XmlReader.Create(stream, XmlSettings());
    while (reader.ReadToFollowing("character"))
    {
        if (XNode.ReadFrom(reader) is XElement element)
        {
            yield return element;
        }
    }
}

static XmlReaderSettings XmlSettings()
{
    return new XmlReaderSettings
    {
        DtdProcessing = DtdProcessing.Parse,
        XmlResolver = null,
        IgnoreWhitespace = true,
        MaxCharactersFromEntities = 1024L * 1024L * 256L,
    };
}

static Stream OpenPossiblyGzip(string path)
{
    var fileStream = File.OpenRead(path);
    return path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
        ? new GZipStream(fileStream, CompressionMode.Decompress)
        : fileStream;
}

static int ResolvePriority(IEnumerable<string> priorityTags)
{
    var score = 0;
    foreach (var priorityTag in priorityTags)
    {
        var tag = priorityTag.Trim();
        score = Math.Max(score, tag switch
        {
            "news1" or "ichi1" => 500,
            "spec1" or "gai1" => 400,
            "news2" or "ichi2" => 300,
            "spec2" or "gai2" => 250,
            _ when tag.StartsWith("nf", StringComparison.Ordinal) && int.TryParse(tag[2..], out var nf) => Math.Max(1, 200 - nf),
            _ => 100,
        });
    }

    return score;
}

sealed class TermInserter
{
    private readonly SqliteCommand _command;
    private readonly KoreanOverrideSet _overrides;
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    public TermInserter(SqliteConnection connection, SqliteTransaction transaction, KoreanOverrideSet overrides)
    {
        _overrides = overrides;
        _command = connection.CreateCommand();
        _command.Transaction = transaction;
        _command.CommandText = """
            INSERT INTO terms(surface, reading_kana, priority, pos_flags, is_name, ko_target, source)
            VALUES ($surface, $readingKana, $priority, $posFlags, $isName, $koTarget, $source)
            """;
        _command.Parameters.Add("$surface", SqliteType.Text);
        _command.Parameters.Add("$readingKana", SqliteType.Text);
        _command.Parameters.Add("$priority", SqliteType.Integer);
        _command.Parameters.Add("$posFlags", SqliteType.Text);
        _command.Parameters.Add("$isName", SqliteType.Integer);
        _command.Parameters.Add("$koTarget", SqliteType.Text);
        _command.Parameters.Add("$source", SqliteType.Text);
    }

    public void Insert(string surface, string readingKana, int priority, string posFlags, bool isName, string source)
    {
        if (string.IsNullOrWhiteSpace(surface))
        {
            return;
        }

        var normalizedSurface = surface.Trim();
        var normalizedReading = readingKana.Trim();
        var koTarget = _overrides.ResolveKoTarget(normalizedSurface, normalizedReading);
        InsertCore(
            normalizedSurface,
            normalizedReading,
            Math.Max(priority, koTarget is null ? 0 : 1000),
            string.IsNullOrWhiteSpace(posFlags) ? string.Empty : posFlags.Trim(),
            isName,
            koTarget,
            source);
    }

    public void InsertOverrideSeedTerms()
    {
        foreach (var entry in _overrides.Entries)
        {
            InsertCore(entry.Surface, entry.ReadingKana, entry.Priority, entry.PosFlags, isName: false, entry.KoTarget, "ko-overrides");
        }
    }

    private void InsertCore(string surface, string readingKana, int priority, string posFlags, bool isName, string? koTarget, string source)
    {
        var key = string.Join('\u001f', surface, readingKana, koTarget ?? string.Empty, isName ? "1" : "0", source);
        if (!_seen.Add(key))
        {
            return;
        }

        _command.Parameters["$surface"].Value = surface;
        _command.Parameters["$readingKana"].Value = readingKana;
        _command.Parameters["$priority"].Value = priority;
        _command.Parameters["$posFlags"].Value = posFlags;
        _command.Parameters["$isName"].Value = isName ? 1 : 0;
        _command.Parameters["$koTarget"].Value = string.IsNullOrWhiteSpace(koTarget) ? DBNull.Value : koTarget;
        _command.Parameters["$source"].Value = source;
        _command.ExecuteNonQuery();
    }
}

sealed class KoreanOverrideSet
{
    private readonly Dictionary<string, KoreanOverrideEntry> _bySurface;
    private readonly Dictionary<string, KoreanOverrideEntry> _byReading;

    private KoreanOverrideSet(string snapshotVersion, IReadOnlyList<KoreanOverrideEntry> entries)
    {
        SnapshotVersion = snapshotVersion;
        Entries = entries;
        _bySurface = entries
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.Surface))
            .GroupBy(static entry => entry.Surface, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.OrderByDescending(static entry => entry.Priority).First(), StringComparer.Ordinal);
        _byReading = entries
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.ReadingKana))
            .GroupBy(static entry => entry.ReadingKana, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.OrderByDescending(static entry => entry.Priority).First(), StringComparer.Ordinal);
    }

    public string SnapshotVersion { get; }

    public IReadOnlyList<KoreanOverrideEntry> Entries { get; }

    public static KoreanOverrideSet Load(string path)
    {
        var document = JsonSerializer.Deserialize<KoreanOverrideDocument>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new KoreanOverrideDocument();

        return new KoreanOverrideSet(
            string.IsNullOrWhiteSpace(document.SnapshotVersion) ? "jmdict-large" : document.SnapshotVersion,
            document.Entries
                .Where(static entry => !string.IsNullOrWhiteSpace(entry.Surface) && !string.IsNullOrWhiteSpace(entry.KoTarget))
                .ToList());
    }

    public string? ResolveKoTarget(string surface, string readingKana)
    {
        if (_bySurface.TryGetValue(surface, out var surfaceEntry))
        {
            return surfaceEntry.KoTarget;
        }

        return _byReading.TryGetValue(readingKana, out var readingEntry)
            ? readingEntry.KoTarget
            : null;
    }
}

sealed class KoreanOverrideDocument
{
    public string SnapshotVersion { get; init; } = string.Empty;

    public List<KoreanOverrideEntry> Entries { get; init; } = [];
}

sealed record KoreanOverrideEntry(
    string Surface,
    string ReadingKana,
    int Priority,
    string PosFlags,
    string KoTarget);

sealed record BuildOptions(
    string JMdictPath,
    string JMnedictPath,
    string Kanjidic2Path,
    string OverridesPath,
    string OutputPath)
{
    public static BuildOptions? Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length - 1; i += 2)
        {
            values[args[i]] = args[i + 1];
        }

        return values.TryGetValue("--jmdict", out var jmdict)
            && values.TryGetValue("--jmnedict", out var jmnedict)
            && values.TryGetValue("--kanjidic2", out var kanjidic2)
            && values.TryGetValue("--overrides", out var overrides)
            && values.TryGetValue("--output", out var output)
            ? new BuildOptions(jmdict, jmnedict, kanjidic2, overrides, output)
            : null;
    }
}
