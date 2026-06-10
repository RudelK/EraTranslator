using System.Text;
using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class UserDictionaryExchangeServiceTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    [Fact]
    public void ExportAndImport_RoundTripsEtdictTsvFields()
    {
        Directory.CreateDirectory(_rootPath);
        var path = Path.Combine(_rootPath, "dictionary.etdict");
        var service = new UserDictionaryExchangeService();

        service.Export(
            path,
            [
                new UserDictionaryEntry
                {
                    IsEnabled = true,
                    Source = "勇者\t一行目\n二行目\\",
                    Target = "용사\t첫째\n둘째\\",
                    ApplyMode = UserDictionaryApplyMode.Prompting,
                },
                new UserDictionaryEntry
                {
                    IsEnabled = false,
                    Source = "魔王",
                    Target = "마왕",
                    ApplyMode = UserDictionaryApplyMode.Replace,
                },
            ]);

        var text = File.ReadAllText(path, Encoding.UTF8);
        Assert.StartsWith("# EraTranslator User Dictionary v1", text, StringComparison.Ordinal);
        Assert.Contains("勇者\\t一行目\\n二行目\\\\\t용사\\t첫째\\n둘째\\\\\t프롬프팅\t사용", text, StringComparison.Ordinal);
        Assert.Contains("魔王\t마왕\t치환\t미사용", text, StringComparison.Ordinal);

        var imported = service.Import(path);

        Assert.Equal(2, imported.Entries.Count);
        Assert.Equal(0, imported.Skipped);
        Assert.Equal("勇者\t一行目\n二行目\\", imported.Entries[0].Source);
        Assert.Equal("용사\t첫째\n둘째\\", imported.Entries[0].Target);
        Assert.Equal(UserDictionaryApplyMode.Prompting, imported.Entries[0].ApplyMode);
        Assert.True(imported.Entries[0].IsEnabled);
        Assert.False(imported.Entries[1].IsEnabled);
    }

    [Fact]
    public void Import_SkipsInvalidEtdictRows()
    {
        Directory.CreateDirectory(_rootPath);
        var path = Path.Combine(_rootPath, "dictionary.etdict");
        File.WriteAllText(
            path,
            """
# EraTranslator User Dictionary v1
勇者	용사	치환	사용
깨진줄
魔王	마왕	Unknown	사용
空		치환	사용
""",
            new UTF8Encoding(true));

        var imported = new UserDictionaryExchangeService().Import(path);

        Assert.Single(imported.Entries);
        Assert.Equal("勇者", imported.Entries[0].Source);
        Assert.Equal(3, imported.Skipped);
    }

    [Fact]
    public void ImportSimpleSrs_ReadsQuotedPairsAndSkipsNotes()
    {
        Directory.CreateDirectory(_rootPath);
        var path = Path.Combine(_rootPath, "extra.simplesrs");
        File.WriteAllText(
            path,
            """
; comment

"強化剤"
"강화제"
"伏字"
복자 설명 메모
"零"
"영"
""",
            new UTF8Encoding(true));

        var imported = new UserDictionaryExchangeService().Import(path);

        Assert.Equal(2, imported.Entries.Count);
        Assert.Contains(imported.Entries, entry =>
            entry.Source == "強化剤"
            && entry.Target == "강화제"
            && entry.ApplyMode == UserDictionaryApplyMode.Prompting
            && entry.IsEnabled);
        Assert.Contains(imported.Entries, entry =>
            entry.Source == "零"
            && entry.Target == "영");
        Assert.Equal(2, imported.Skipped);
    }

    [Fact]
    public void ImportSimpleSrs_ReadsPlainLinePairsAndSkipsDirectives()
    {
        Directory.CreateDirectory(_rootPath);
        var path = Path.Combine(_rootPath, "plain.simplesrs");
        File.WriteAllText(
            path,
            """
[-TRIM-][-SORT-]
; comment
時間帯:昼
시간대:낮
TRAIN:無し
TRAIN:없음
""",
            new UTF8Encoding(true));

        var imported = new UserDictionaryExchangeService().Import(path);

        Assert.Equal(2, imported.Entries.Count);
        Assert.Contains(imported.Entries, entry =>
            entry.Source == "時間帯:昼"
            && entry.Target == "시간대:낮"
            && entry.ApplyMode == UserDictionaryApplyMode.Prompting);
        Assert.Contains(imported.Entries, entry =>
            entry.Source == "TRAIN:無し"
            && entry.Target == "TRAIN:없음");
        Assert.Equal(0, imported.Skipped);
    }

    [Fact]
    public void ImportSimpleSrs_PlainModeSkipsBrokenQuotedLinesWithoutShiftingPairs()
    {
        Directory.CreateDirectory(_rootPath);
        var path = Path.Combine(_rootPath, "mixed.simplesrs");
        File.WriteAllText(
            path,
            """
[-TRIM-][-SORT-]
悩み_解決0
"悩み_解決
"고민_해결
"モラル_UPPER"
"모럴_UPPER"
""",
            new UTF8Encoding(true));

        var imported = new UserDictionaryExchangeService().Import(path);

        Assert.Single(imported.Entries);
        Assert.Equal("モラル_UPPER", imported.Entries[0].Source);
        Assert.Equal("모럴_UPPER", imported.Entries[0].Target);
        Assert.DoesNotContain(imported.Entries, entry =>
            entry.Source == "悩み_解決0"
            || entry.Source == "\"고민_해결");
        Assert.Equal(3, imported.Skipped);
    }

    [Fact]
    public void ImportSimpleSrs_PlainModeTreatsCommentsAndBlankLinesAsPairBoundaries()
    {
        Directory.CreateDirectory(_rootPath);
        var path = Path.Combine(_rootPath, "boundary.simplesrs");
        File.WriteAllText(
            path,
            """
時間帯:昼
시간대:낮
孤立行

; next section uses quoted entries
"モラル_UPPER"
"모럴_UPPER"
""",
            new UTF8Encoding(true));

        var imported = new UserDictionaryExchangeService().Import(path);

        Assert.Equal(2, imported.Entries.Count);
        Assert.Contains(imported.Entries, entry =>
            entry.Source == "時間帯:昼"
            && entry.Target == "시간대:낮");
        Assert.Contains(imported.Entries, entry =>
            entry.Source == "モラル_UPPER"
            && entry.Target == "모럴_UPPER");
        Assert.DoesNotContain(imported.Entries, entry => entry.Target == "モラル_UPPER");
        Assert.Equal(1, imported.Skipped);
    }
}
