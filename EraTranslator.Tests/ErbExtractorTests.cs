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

    [Fact]
    public void Extract_FindsStableTextSpansInsideCodeMixedExpressions()
    {
        const string source = """
labelA = GET_NAME(index) + HP回復
labelB = LOVEポイント + LOCAL:1
labelC = 高校生2 + CALLNAME:TARGET
labelD = TALENT:targetChara:高校生 * 3
labelE = GETNUM(CFLAG:targetChara:魔力回復)
labelF = ABC_ONLY
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erb", source);
        var values = segments.Select(segment => segment.OriginalText).ToArray();

        Assert.Contains("HP回復", values);
        Assert.Contains("LOVEポイント", values);
        Assert.Contains("高校生2", values);
        Assert.DoesNotContain("高校生", values);
        Assert.DoesNotContain("魔力回復", values);
        Assert.DoesNotContain("CALLNAME:TARGET", values);
        Assert.DoesNotContain("LOCAL:1", values);
        Assert.DoesNotContain("ABC_ONLY", values);
    }

    [Fact]
    public void Extract_FindsTextSpansInCodeMixedPrintAndQuotedStrings()
    {
        const string source = """
PRINTFORMW %CALLNAME:TARGET%の高校生
PRINTFORMW 高校生%CALLNAME:TARGET%
PRINTFORMW "%CALLNAME:MASTER%魔力回復"
PRINTFORMW 普通の文章です
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erb", source);
        var values = segments.Select(segment => segment.OriginalText).ToArray();

        Assert.Contains("高校生", values);
        Assert.Contains("魔力回復", values);
        Assert.Contains("普通の文章です", values);
        Assert.DoesNotContain("%CALLNAME:TARGET%の高校生", values);
        Assert.DoesNotContain("の", values);
        Assert.Contains(segments, segment => segment.OriginalText == "高校生" && segment.SegmentType == "print-tail-fragment");
        Assert.Contains(segments, segment => segment.OriginalText == "魔力回復" && segment.SegmentType == "quoted-string-fragment");
        Assert.Contains(segments, segment => segment.OriginalText == "普通の文章です" && segment.SegmentType == "print-tail");
    }

    [Fact]
    public void Extract_FindsTrailingCurrencyUnitAfterCodePlaceholder()
    {
        const string source = """
PRINTFORM 入学金{admissionCost}円
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erb", source);
        var values = segments.Select(segment => segment.OriginalText).ToArray();

        Assert.Contains("入学金", values);
        Assert.Contains("円", values);
        Assert.Contains(segments, segment => segment.OriginalText == "円" && segment.SegmentType == "print-tail-fragment");
    }

    [Fact]
    public void Extract_SkipsPlaceholderAndNumericModifierOnlyPrintTail()
    {
        const string source = """
PRINTFORML %TALENTNAME:7% ＋100％
PRINTFORML %TALENTNAME:7%は上昇した
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erb", source);
        var values = segments.Select(segment => segment.OriginalText).ToArray();

        Assert.DoesNotContain(values, value => string.Equals(value, "%TALENTNAME:7% ＋100％", StringComparison.Ordinal));
        Assert.Contains(values, value => string.Equals(value, "%TALENTNAME:7%は上昇した", StringComparison.Ordinal));
        Assert.DoesNotContain(values, value => string.Equals(value, "上昇した", StringComparison.Ordinal));
        Assert.DoesNotContain(values, value => string.Equals(value, "は", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_KeepsCodeMixedDialogueAsWholeSentence()
    {
        const string source = """
PRINTDATAW
DATAFORM %CALLNAME:prostitutionLady%「……あ、ぁ……」（怖い、怖い……誰か、助けて……お姉ちゃん……っ）
ENDDATA
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erb", source);
        var values = segments.Select(segment => segment.OriginalText).ToArray();

        Assert.Contains("%CALLNAME:prostitutionLady%「……あ、ぁ……」（怖い、怖い……誰か、助けて……お姉ちゃん……っ）", values);
        Assert.DoesNotContain("怖い", values);
        Assert.DoesNotContain("誰か", values);
        Assert.DoesNotContain("助けて", values);
        Assert.DoesNotContain("お姉ちゃん", values);
    }

    [Fact]
    public void Extract_FindsQuotedStringsInDimDirectives_WithoutCapturingNumericDimValues()
    {
        const string source = """
#DIMS CONST PROSTITUTION_HAIR_COLOR_LIST = "栗毛","黒髪","灰髪"
#DIM CONST PROSTITUTION_HAIR_STYLE_COUNT = 40
#DIM SAVEDATA PROSTITUTION_CUSTOMER_MIN_VALUE = 0
#DIMS CONST PROSTITUTION_SEX_LIST = "男","女","集団","ふたなり","その他"
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erh", source);
        var values = segments.Select(segment => segment.OriginalText).ToArray();

        Assert.Contains("栗毛", values);
        Assert.Contains("黒髪", values);
        Assert.Contains("灰髪", values);
        Assert.Contains("男", values);
        Assert.Contains("女", values);
        Assert.Contains("集団", values);
        Assert.Contains("ふたなり", values);
        Assert.Contains("その他", values);
        Assert.All(segments, segment => Assert.Equal("directive-string", segment.SegmentType));
        Assert.DoesNotContain(values, value => string.Equals(value, "40", StringComparison.Ordinal));
        Assert.DoesNotContain(values, value => string.Equals(value, "0", StringComparison.Ordinal));
    }
}
