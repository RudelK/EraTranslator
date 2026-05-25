using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class ErbExtractorTests
{
    [Fact]
    public void Extract_HandlesPrintDataAndHtmlMarkupWithoutCapturingTags()
    {
        const string source = """
@TEST
PRINTDATAW
    DATA あいう
    DATAFORM @"<button title='説明'>開始</button>"
    DATALIST
        DATA ほげ
    ENDLIST
ENDDATA
PRINT_TAG = <button title='選択肢'>はい</button>
HTML_PRINT @"<nonbutton title='補足'>本文</nonbutton>"
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erb", source);
        var values = segments.Select(segment => segment.OriginalText).ToArray();

        Assert.Contains("あいう", values);
        Assert.Contains("説明", values);
        Assert.Contains("開始", values);
        Assert.Contains("ほげ", values);
        Assert.Contains("選択肢", values);
        Assert.Contains("はい", values);
        Assert.Contains("補足", values);
        Assert.Contains("本文", values);
        Assert.DoesNotContain(values, value => value.Contains("<button", StringComparison.Ordinal));
        Assert.DoesNotContain(values, value => value.Contains("<nonbutton", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_SkipsQuotedSymbolExpressions()
    {
        const string source = """
PRINTFORMW "ABL:index:従順 * 10 + ABL:index:精神依存 * 5 + EXP:index:愛情経験 + CFLAG:index:依存度"
PRINTFORMW "従順이 높다"
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erb", source);
        var values = segments.Select(segment => segment.OriginalText).ToArray();

        Assert.DoesNotContain("ABL:index:従順 * 10 + ABL:index:精神依存 * 5 + EXP:index:愛情経験 + CFLAG:index:依存度", values);
        Assert.Contains("従順이 높다", values);
    }
}
