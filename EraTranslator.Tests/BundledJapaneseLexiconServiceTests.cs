using EraTranslator.Services;
using Microsoft.Data.Sqlite;

namespace EraTranslator.Tests;

public sealed class BundledJapaneseLexiconServiceTests
{
    [Fact]
    public void MissingSnapshot_DisablesLookupsWithoutThrowing()
    {
        var root = CreateTempRoot();
        try
        {
            using var service = new BundledJapaneseLexiconService(root);

            Assert.False(service.TryGetSurfaceEntry("処女", out _));
            Assert.False(service.TryGetReadingEntry("しょじょ", out _));
            Assert.False(service.TryGetKanjiReading('処', out _));
            Assert.Contains("사전 선행 비활성", service.GetSnapshotSummary(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SQLiteSnapshot_ResolvesSurfaceReadingAndKanji()
    {
        var root = CreateTempRoot();
        try
        {
            CreateSnapshot(root);
            using var service = new BundledJapaneseLexiconService(root);

            Assert.True(service.TryGetSurfaceEntry("処女", out var surfaceEntry));
            Assert.Equal("처녀", surfaceEntry.KoTarget);
            Assert.True(service.TryGetReadingEntry("しょじょ", out var readingEntry));
            Assert.Equal("처녀", readingEntry.KoTarget);
            Assert.True(service.TryGetKanjiReading('処', out var kanjiEntry));
            Assert.Equal("처", kanjiEntry.KoreanH);
            Assert.Contains("sqlite-test", service.GetSnapshotSummary(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"EraTranslatorTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Dictionaries"));
        return root;
    }

    private static void CreateSnapshot(string root)
    {
        var path = Path.Combine(root, "Assets", "Dictionaries", "bundled-japanese-lexicon.sqlite");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE metadata (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL);
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
            INSERT INTO metadata(key, value) VALUES ('snapshot_version', 'sqlite-test');
            INSERT INTO terms(surface, reading_kana, priority, pos_flags, is_name, ko_target, source)
            VALUES ('処女', 'しょじょ', 1200, 'noun', 0, '처녀', 'test');
            INSERT INTO kanji_readings(kanji_char, korean_h, ja_on, ja_kun)
            VALUES ('処', '처', 'ショ', '');
            """;
        command.ExecuteNonQuery();
    }
}
