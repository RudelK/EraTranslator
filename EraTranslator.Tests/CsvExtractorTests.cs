using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class CsvExtractorTests
{
    [Fact]
    public void Extract_SkipsSemicolonPrefixedFieldValues()
    {
        var extractor = new CsvExtractor();
        var content = "30,,;MILKPOINTで被るので略さない\n31,,通常テキスト";

        var result = extractor.Extract("CSV/base.csv", "CSV/base.csv", content);

        Assert.DoesNotContain(result.segments, segment => segment.OriginalText.StartsWith(';'));
        Assert.Contains(result.segments, segment => segment.OriginalText == "通常テキスト");
        Assert.Equal(CsvDocumentKind.IdFirstTable, result.kind);
    }

    [Fact]
    public void Extract_SkipsAllFieldsInVariableSizeCsv()
    {
        var extractor = new CsvExtractor();
        var content = "STR,100,테스트\nAGI,120,값";

        var result = extractor.Extract("CSV/VariableSize.csv", "CSV/VariableSize.csv", content);

        Assert.Empty(result.segments);
    }

    [Fact]
    public void Extract_IncludesReferenceBearingCharacterSheetKey()
    {
        var extractor = new CsvExtractor();
        var content = "CSTR,あなた呼び方／陥落前,あなた\nフラグ,外見年齢,18";

        var result = extractor.Extract("CSV/Chara3.csv", "CSV/Chara3.csv", content);

        Assert.Contains(result.segments, segment =>
            segment.IsReferenceBearingKey
            && segment.SymbolNamespace == "CSTR"
            && segment.OriginalSymbolKey == "あなた呼び方／陥落前");
        Assert.Contains(result.segments, segment =>
            segment.IsReferenceBearingKey
            && segment.SymbolNamespace == "CFLAG"
            && segment.OriginalSymbolKey == "外見年齢");
    }

    [Fact]
    public void Extract_SkipsNumericOnlyFieldsEvenWhenTheyAreKeys()
    {
        var extractor = new CsvExtractor();
        var content = "10,奴隷候補設定変更チケット,2000000\nフラグ,12345,18";

        var result = extractor.Extract("CSV/Mixed.csv", "CSV/Mixed.csv", content);

        Assert.DoesNotContain(result.segments, segment => segment.OriginalText == "10");
        Assert.DoesNotContain(result.segments, segment => segment.OriginalText == "2000000");
        Assert.DoesNotContain(result.segments, segment => segment.OriginalText == "12345");
        Assert.DoesNotContain(result.segments, segment => segment.OriginalText == "18");
        Assert.Contains(result.segments, segment => segment.OriginalText == "奴隷候補設定変更チケット");
    }

    [Fact]
    public void Extract_CharacterSheetNameLikeFields_PreserveWhitespace()
    {
        var extractor = new CsvExtractor();
        var content = "番号,0\n名前,メイン ヒロイン\n呼び名,マイ レディ\nCSTR,初回 挨拶,おは よう";

        var result = extractor.Extract("CSV/AnyCharacter.csv", "CSV/AnyCharacter.csv", content);

        Assert.Contains(result.segments, segment => segment.OriginalText == "メイン ヒロイン" && segment.PreserveWhitespace);
        Assert.Contains(result.segments, segment => segment.OriginalText == "マイ レディ" && segment.PreserveWhitespace);
        Assert.Contains(result.segments, segment => segment.OriginalText == "初回 挨拶" && segment.PreserveWhitespace);
        Assert.Contains(result.segments, segment => segment.OriginalText == "おは よう" && segment.PreserveWhitespace);
    }
}
