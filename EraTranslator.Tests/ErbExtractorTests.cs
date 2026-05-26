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

    [Fact]
    public void Extract_FindsBareJapaneseAssignmentValuesWithoutCapturingCodeAssignments()
    {
        const string source = """
drinkName = 缶ジュース
bagName = 部活の鞄 ; inline comment
friendRule = IS_FRIEND({targetChara},index) && !TALENT:index:監禁
answer = TALENT:targetChara:高校生 * 3
PRINTFORMW drinkName
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erb", source);
        var values = segments.Select(segment => segment.OriginalText).ToArray();

        Assert.Contains("缶ジュース", values);
        Assert.Contains("部活の鞄", values);
        Assert.DoesNotContain(values, value => string.Equals(value, "IS_FRIEND({targetChara},index) && !TALENT:index:監禁", StringComparison.Ordinal));
        Assert.DoesNotContain(values, value => string.Equals(value, "TALENT:targetChara:高校生 * 3", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_FindsTopLevelAssignmentFragmentsWithoutCapturingWholeExpressions()
    {
        const string source = """
label = 高校生 + "です"
subject = prefix + 学生
status = cond ? 学生 # 社会人
wrapped = (生徒)
value = GET_NAME(index)
numeric = CFLAG:targetChara:主人監禁日数
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erb", source);
        var values = segments.Select(segment => segment.OriginalText).ToArray();

        Assert.Contains("高校生", values);
        Assert.Contains("です", values);
        Assert.Contains("学生", values);
        Assert.Contains("社会人", values);
        Assert.Contains("生徒", values);
        Assert.Contains(segments, segment => segment.OriginalText == "高校生" && segment.SegmentType == "assignment-fragment");
        Assert.Contains(segments, segment => segment.OriginalText == "学生" && segment.SegmentType == "assignment-fragment");
        Assert.Contains(segments, segment => segment.OriginalText == "社会人" && segment.SegmentType == "assignment-fragment");
        Assert.Contains(segments, segment => segment.OriginalText == "生徒" && segment.SegmentType == "assignment-fragment");
        Assert.DoesNotContain(values, value => string.Equals(value, "prefix + 学生", StringComparison.Ordinal));
        Assert.DoesNotContain(values, value => string.Equals(value, "cond ? 学生 # 社会人", StringComparison.Ordinal));
        Assert.DoesNotContain(values, value => string.Equals(value, "GET_NAME(index)", StringComparison.Ordinal));
        Assert.DoesNotContain(values, value => string.Equals(value, "CFLAG:targetChara:主人監禁日数", StringComparison.Ordinal));
    }
}
