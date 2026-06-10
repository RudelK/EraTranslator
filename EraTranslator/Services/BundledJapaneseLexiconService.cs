using Microsoft.Data.Sqlite;

namespace EraTranslator.Services;

public interface IBundledJapaneseLexiconService
{
    bool TryGetSurfaceEntry(string term, out BundledJapaneseLexiconEntry entry);

    bool TryGetReadingEntry(string term, out BundledJapaneseLexiconEntry entry);

    bool TryGetKanjiReading(char kanji, out BundledKanjiReadingEntry entry);

    BundledJapaneseLexiconGlossaryLookupResult FindGlossaryCandidates(
        IReadOnlyList<string> originals,
        int minTermLength,
        int maxTermLength,
        int maxCandidates);

    string GetSnapshotSummary();

    string GetAttributionText();

    string NoticeFilePath { get; }
}

public sealed class BundledJapaneseLexiconService(string? baseDirectory = null) : IBundledJapaneseLexiconService, IDisposable
{
    private readonly Lazy<SnapshotState> _snapshot = new(() => SnapshotState.Load(baseDirectory ?? AppContext.BaseDirectory));

    public string NoticeFilePath => Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "Assets", "Dictionaries", "EDRDG-NOTICE.txt");

    public bool TryGetSurfaceEntry(string term, out BundledJapaneseLexiconEntry entry)
    {
        return _snapshot.Value.TryGetSurfaceEntry(Normalize(term), out entry);
    }

    public bool TryGetReadingEntry(string term, out BundledJapaneseLexiconEntry entry)
    {
        return _snapshot.Value.TryGetReadingEntry(Normalize(term), out entry);
    }

    public bool TryGetKanjiReading(char kanji, out BundledKanjiReadingEntry entry)
    {
        return _snapshot.Value.TryGetKanjiReading(kanji, out entry);
    }

    public BundledJapaneseLexiconGlossaryLookupResult FindGlossaryCandidates(
        IReadOnlyList<string> originals,
        int minTermLength,
        int maxTermLength,
        int maxCandidates)
    {
        var snapshot = _snapshot.Value;
        if (!snapshot.IsAvailable || originals.Count == 0)
        {
            return BundledJapaneseLexiconGlossaryLookupResult.Empty;
        }

        var minimumLength = Math.Clamp(minTermLength, 1, 20);
        var maximumLength = Math.Clamp(maxTermLength, minimumLength, 40);
        var candidateLimit = Math.Clamp(maxCandidates, 1, 1000);
        var surfaceCandidates = BuildSurfaceCandidates(originals, minimumLength, maximumLength);
        if (surfaceCandidates.Count == 0)
        {
            return BundledJapaneseLexiconGlossaryLookupResult.Empty;
        }

        return snapshot.FindGlossaryCandidates(surfaceCandidates, candidateLimit);
    }

    public string GetSnapshotSummary()
    {
        var snapshot = _snapshot.Value;
        if (!snapshot.IsAvailable)
        {
            return string.IsNullOrWhiteSpace(snapshot.LoadError)
                ? "내장 사전 스냅샷 없음: 사전 선행 비활성"
                : $"내장 사전 스냅샷 로드 실패: {snapshot.LoadError}";
        }

        return $"내장 사전 스냅샷: {snapshot.TermVersion} / 용어 {snapshot.TermCount:N0}개 / 한자 {snapshot.KanjiCount:N0}개";
    }

    public string GetAttributionText()
    {
        var snapshot = _snapshot.Value;
        return $"내장 공개 사전 스냅샷은 JMdict, JMnedict, KANJIDIC2 기반의 축약 자산과 앱용 보조 용어집으로 구성됩니다.{Environment.NewLine}"
            + $"버전: {snapshot.TermVersion}{Environment.NewLine}"
            + "원본/라이선스: https://www.edrdg.org/jmdict/j_jmdict.html / https://www.edrdg.org/edrdg/licence.html / https://www.edrdg.org/kanjidic/kanjidic2_ov_legacy.html";
    }

    public void Dispose()
    {
        if (_snapshot.IsValueCreated)
        {
            _snapshot.Value.Dispose();
        }
    }

    private static string Normalize(string value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static IReadOnlyList<string> BuildSurfaceCandidates(
        IReadOnlyList<string> originals,
        int minTermLength,
        int maxTermLength)
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var original in originals)
        {
            if (string.IsNullOrWhiteSpace(original))
            {
                continue;
            }

            foreach (var run in EnumerateJapaneseRuns(original))
            {
                for (var start = 0; start < run.Length; start++)
                {
                    var maxLength = Math.Min(maxTermLength, run.Length - start);
                    for (var length = minTermLength; length <= maxLength; length++)
                    {
                        var candidate = run.Substring(start, length);
                        candidates.Add(candidate);
                        AddDeinflectedCandidates(candidates, candidate, minTermLength, maxTermLength);
                    }
                }
            }
        }

        return candidates.ToList();
    }

    private static IEnumerable<string> EnumerateJapaneseRuns(string value)
    {
        var start = -1;
        for (var index = 0; index < value.Length; index++)
        {
            if (IsJapaneseTermChar(value[index]))
            {
                if (start < 0)
                {
                    start = index;
                }

                continue;
            }

            if (start >= 0)
            {
                yield return value[start..index];
                start = -1;
            }
        }

        if (start >= 0)
        {
            yield return value[start..];
        }
    }

    private static void AddDeinflectedCandidates(
        ISet<string> candidates,
        string value,
        int minTermLength,
        int maxTermLength)
    {
        AddDeinflectedCandidate(candidates, value, "した", "する", minTermLength, maxTermLength);
        AddDeinflectedCandidate(candidates, value, "した", "す", minTermLength, maxTermLength);
        AddDeinflectedCandidate(candidates, value, "して", "する", minTermLength, maxTermLength);
        AddDeinflectedCandidate(candidates, value, "して", "す", minTermLength, maxTermLength);
        AddDeinflectedCandidate(candidates, value, "しない", "する", minTermLength, maxTermLength);
        AddDeinflectedCandidate(candidates, value, "します", "する", minTermLength, maxTermLength);
        AddDeinflectedCandidate(candidates, value, "された", "する", minTermLength, maxTermLength);
        AddDeinflectedCandidate(candidates, value, "された", "す", minTermLength, maxTermLength);
        AddDeinflectedCandidate(candidates, value, "される", "する", minTermLength, maxTermLength);
        AddDeinflectedCandidate(candidates, value, "される", "す", minTermLength, maxTermLength);
        AddDeinflectedCandidate(candidates, value, "されて", "する", minTermLength, maxTermLength);
        AddDeinflectedCandidate(candidates, value, "されて", "す", minTermLength, maxTermLength);
        AddDeinflectedCandidate(candidates, value, "ない", "る", minTermLength, maxTermLength);
        AddDeinflectedCandidate(candidates, value, "なかった", "る", minTermLength, maxTermLength);
        AddDeinflectedCandidate(candidates, value, "ます", "る", minTermLength, maxTermLength);
        AddDeinflectedCandidate(candidates, value, "ました", "る", minTermLength, maxTermLength);
        AddDeinflectedCandidate(candidates, value, "ません", "る", minTermLength, maxTermLength);
    }

    private static void AddDeinflectedCandidate(
        ISet<string> candidates,
        string value,
        string suffix,
        string replacement,
        int minTermLength,
        int maxTermLength)
    {
        if (!value.EndsWith(suffix, StringComparison.Ordinal) || value.Length <= suffix.Length)
        {
            return;
        }

        var candidate = string.Concat(value.AsSpan(0, value.Length - suffix.Length), replacement);
        if (candidate.Length >= minTermLength && candidate.Length <= maxTermLength)
        {
            candidates.Add(candidate);
        }
    }

    private static bool IsJapaneseTermChar(char value)
    {
        return value is >= '\u3040' and <= '\u30ff'
            or >= '\u3400' and <= '\u9fff'
            or >= '\uf900' and <= '\ufaff'
            or '々'
            or '〆'
            or 'ヶ'
            or 'ー'
            or '・';
    }

    private sealed class SnapshotState : IDisposable
    {
        private readonly object _syncRoot = new();
        private readonly SqliteConnection? _connection;

        private SnapshotState(
            string termVersion,
            long termCount,
            long kanjiCount,
            SqliteConnection? connection,
            string loadError)
        {
            TermVersion = termVersion;
            TermCount = termCount;
            KanjiCount = kanjiCount;
            _connection = connection;
            LoadError = loadError;
        }

        public string TermVersion { get; }

        public long TermCount { get; }

        public long KanjiCount { get; }

        public string LoadError { get; }

        public bool IsAvailable => _connection is not null;

        public static SnapshotState Load(string rootDirectory)
        {
            var snapshotPath = Path.Combine(rootDirectory, "Assets", "Dictionaries", "bundled-japanese-lexicon.sqlite");
            if (!File.Exists(snapshotPath))
            {
                return Missing(string.Empty);
            }

            try
            {
                var connectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = snapshotPath,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false,
                }.ToString();
                var connection = new SqliteConnection(connectionString);
                connection.Open();

                return new SnapshotState(
                    ReadMetadata(connection, "snapshot_version", "unknown"),
                    ReadCount(connection, "terms"),
                    ReadCount(connection, "kanji_readings"),
                    connection,
                    string.Empty);
            }
            catch (Exception ex) when (ex is IOException or SqliteException or InvalidOperationException)
            {
                return Missing(ex.Message);
            }
        }

        public bool TryGetSurfaceEntry(string term, out BundledJapaneseLexiconEntry entry)
        {
            return TryGetTerm("surface", term, out entry);
        }

        public bool TryGetReadingEntry(string term, out BundledJapaneseLexiconEntry entry)
        {
            return TryGetTerm("reading_kana", term, out entry);
        }

        public bool TryGetKanjiReading(char kanji, out BundledKanjiReadingEntry entry)
        {
            entry = default!;
            if (_connection is null)
            {
                return false;
            }

            lock (_syncRoot)
            {
                using var command = _connection.CreateCommand();
                command.CommandText = """
                    SELECT kanji_char, korean_h, ja_on, ja_kun
                    FROM kanji_readings
                    WHERE kanji_char = $kanji
                    LIMIT 1
                    """;
                command.Parameters.AddWithValue("$kanji", kanji.ToString());

                using var reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    return false;
                }

                entry = new BundledKanjiReadingEntry(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3));
                return true;
            }
        }

        public BundledJapaneseLexiconGlossaryLookupResult FindGlossaryCandidates(
            IReadOnlyList<string> surfaceCandidates,
            int maxCandidates)
        {
            if (_connection is null || surfaceCandidates.Count == 0)
            {
                return BundledJapaneseLexiconGlossaryLookupResult.Empty;
            }

            const int ChunkSize = 400;
            var bestBySurface = new Dictionary<string, BundledJapaneseLexiconGlossaryEntry>(StringComparer.Ordinal);
            var dbHitCount = 0;
            lock (_syncRoot)
            {
                for (var offset = 0; offset < surfaceCandidates.Count; offset += ChunkSize)
                {
                    var chunk = surfaceCandidates.Skip(offset).Take(ChunkSize).ToList();
                    using var command = _connection.CreateCommand();
                    var parameters = new List<string>(chunk.Count);
                    for (var index = 0; index < chunk.Count; index++)
                    {
                        var parameterName = $"$surface_{index}";
                        parameters.Add(parameterName);
                        command.Parameters.AddWithValue(parameterName, chunk[index]);
                    }

                    command.CommandText = $"""
                        SELECT surface, reading_kana, priority, pos_flags, is_name, ko_target
                        FROM terms
                        WHERE surface IN ({string.Join(", ", parameters)})
                          AND ko_target IS NOT NULL
                          AND TRIM(ko_target) <> ''
                        ORDER BY surface ASC, is_name ASC, priority DESC, rowid ASC
                        """;
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        dbHitCount++;
                        var surface = reader.GetString(0);
                        if (bestBySurface.ContainsKey(surface))
                        {
                            continue;
                        }

                        bestBySurface[surface] = new BundledJapaneseLexiconGlossaryEntry(
                            surface,
                            reader.GetString(1),
                            reader.GetInt32(2),
                            reader.GetString(3),
                            reader.GetInt64(4) != 0,
                            reader.GetString(5));
                    }
                }
            }

            var entries = bestBySurface.Values
                .OrderByDescending(static entry => entry.Surface.Length)
                .ThenBy(static entry => entry.IsName)
                .ThenByDescending(static entry => entry.Priority)
                .ThenBy(static entry => entry.Surface, StringComparer.Ordinal)
                .Take(maxCandidates)
                .ToList();

            return new BundledJapaneseLexiconGlossaryLookupResult(
                entries,
                surfaceCandidates.Count,
                dbHitCount);
        }

        private static SnapshotState Missing(string reason)
        {
            return new SnapshotState("unavailable", 0, 0, null, reason);
        }

        public void Dispose()
        {
            _connection?.Dispose();
        }

        private bool TryGetTerm(string columnName, string term, out BundledJapaneseLexiconEntry entry)
        {
            entry = default!;
            if (_connection is null)
            {
                return false;
            }

            lock (_syncRoot)
            {
                using var command = _connection.CreateCommand();
                command.CommandText = $"""
                    SELECT surface, reading_kana, priority, pos_flags, is_name, ko_target
                    FROM terms
                    WHERE {columnName} = $term
                    ORDER BY
                        CASE WHEN ko_target IS NULL OR TRIM(ko_target) = '' THEN 0 ELSE 1 END DESC,
                        is_name ASC,
                        priority DESC,
                        rowid ASC
                    LIMIT 1
                    """;
                command.Parameters.AddWithValue("$term", Normalize(term));

                using var reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    return false;
                }

                entry = new BundledJapaneseLexiconEntry(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetInt64(4) != 0,
                    reader.IsDBNull(5) ? null : reader.GetString(5));
                return true;
            }
        }

        private static string ReadMetadata(SqliteConnection connection, string key, string fallback)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM metadata WHERE key = $key LIMIT 1";
            command.Parameters.AddWithValue("$key", key);
            return command.ExecuteScalar() as string ?? fallback;
        }

        private static long ReadCount(SqliteConnection connection, string tableName)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {tableName}";
            return (long)(command.ExecuteScalar() ?? 0L);
        }
    }
}

public sealed record BundledJapaneseLexiconEntry(
    string Surface,
    string ReadingKana,
    int Priority,
    string PosFlags,
    bool IsName,
    string? KoTarget);

public sealed record BundledKanjiReadingEntry(
    string Kanji,
    string KoreanH,
    string JaOn,
    string JaKun);

public sealed record BundledJapaneseLexiconGlossaryEntry(
    string Surface,
    string ReadingKana,
    int Priority,
    string PosFlags,
    bool IsName,
    string KoTarget);

public sealed record BundledJapaneseLexiconGlossaryLookupResult(
    IReadOnlyList<BundledJapaneseLexiconGlossaryEntry> Entries,
    int SurfaceCandidateCount,
    int DbHitCount)
{
    public static BundledJapaneseLexiconGlossaryLookupResult Empty { get; } = new([], 0, 0);
}
