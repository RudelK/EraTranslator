using Microsoft.Data.Sqlite;

namespace EraTranslator.Services;

public interface INaverJapaneseDictionaryStore
{
    bool TryGet(string surface, out NaverJapaneseDictionaryEntry entry);

    void Upsert(NaverJapaneseDictionaryEntry entry);
}

public sealed class NaverJapaneseDictionaryStore(string? baseDirectory = null) : INaverJapaneseDictionaryStore
{
    private readonly object _syncRoot = new();
    private readonly string _databasePath = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "EraTranslator.naver-japanese-dictionary.sqlite");

    public bool TryGet(string surface, out NaverJapaneseDictionaryEntry entry)
    {
        entry = default!;
        var normalizedSurface = Normalize(surface);
        if (string.IsNullOrWhiteSpace(normalizedSurface))
        {
            return false;
        }

        lock (_syncRoot)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT surface, reading_kana, ko_target, source_url, review_required
                FROM naver_dictionary
                WHERE surface = $surface
                LIMIT 1
                """;
            command.Parameters.AddWithValue("$surface", normalizedSurface);

            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return false;
            }

            entry = new NaverJapaneseDictionaryEntry(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4) != 0);
        }

        MarkUsed(normalizedSurface);
        return true;
    }

    public void Upsert(NaverJapaneseDictionaryEntry entry)
    {
        var normalizedSurface = Normalize(entry.Surface);
        var normalizedTarget = Normalize(entry.KoTarget);
        if (string.IsNullOrWhiteSpace(normalizedSurface) || string.IsNullOrWhiteSpace(normalizedTarget))
        {
            return;
        }

        lock (_syncRoot)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO naver_dictionary(
                    surface,
                    reading_kana,
                    ko_target,
                    source_url,
                    created_at_utc,
                    last_used_at_utc,
                    hit_count,
                    review_required)
                VALUES (
                    $surface,
                    $readingKana,
                    $koTarget,
                    $sourceUrl,
                    $now,
                    $now,
                    0,
                    $reviewRequired)
                ON CONFLICT(surface) DO UPDATE SET
                    reading_kana = excluded.reading_kana,
                    ko_target = excluded.ko_target,
                    source_url = excluded.source_url,
                    last_used_at_utc = excluded.last_used_at_utc,
                    review_required = excluded.review_required
                """;
            command.Parameters.AddWithValue("$surface", normalizedSurface);
            command.Parameters.AddWithValue("$readingKana", Normalize(entry.ReadingKana));
            command.Parameters.AddWithValue("$koTarget", normalizedTarget);
            command.Parameters.AddWithValue("$sourceUrl", Normalize(entry.SourceUrl));
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$reviewRequired", entry.ReviewRequired ? 1 : 0);
            command.ExecuteNonQuery();
        }
    }

    private void MarkUsed(string surface)
    {
        lock (_syncRoot)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE naver_dictionary
                SET last_used_at_utc = $now,
                    hit_count = hit_count + 1
                WHERE surface = $surface
                """;
            command.Parameters.AddWithValue("$surface", surface);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    private SqliteConnection OpenConnection()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Pooling = false,
        }.ToString());
        connection.Open();
        EnsureSchema(connection);
        return connection;
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS naver_dictionary (
                surface TEXT PRIMARY KEY NOT NULL,
                reading_kana TEXT NOT NULL,
                ko_target TEXT NOT NULL,
                source_url TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                last_used_at_utc TEXT NOT NULL,
                hit_count INTEGER NOT NULL DEFAULT 0,
                review_required INTEGER NOT NULL DEFAULT 0
            );
            """;
        command.ExecuteNonQuery();
    }

    private static string Normalize(string value)
    {
        return (value ?? string.Empty).Trim();
    }
}

public sealed record NaverJapaneseDictionaryEntry(
    string Surface,
    string ReadingKana,
    string KoTarget,
    string SourceUrl,
    bool ReviewRequired);
