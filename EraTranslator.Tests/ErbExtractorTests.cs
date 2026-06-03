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
    public void Extract_KeepsNaturalParenthesizedPrintTailWhole()
    {
        const string source = """
PRINTFORML [2] - エンディング履歴(エンディング別)
PRINTFORML [3] - エンディング履歴(達成順)
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erb", source);

        Assert.Contains(segments, segment =>
            segment.OriginalText == "[2] - エンディング履歴(エンディング別)"
            && segment.SegmentType == "print-tail");
        Assert.Contains(segments, segment =>
            segment.OriginalText == "[3] - エンディング履歴(達成順)"
            && segment.SegmentType == "print-tail");
        Assert.DoesNotContain(segments, segment =>
            segment.OriginalText == "エンディング履歴"
            || segment.OriginalText == "エンディング別"
            || segment.OriginalText == "達成順");
    }

    [Fact]
    public void Extract_KeepsPrintTailTextAroundInlineCodeAndNaturalParentheses()
    {
        const string source = """
PRINTFORML %MASTER_LAYER_GET_NAME(layer)%{years}%MASTER_LAYER_GET_YEARS_COUNT_TEXT(layer)%\@ addYears == 0 ? # 、留年{addYears}%MASTER_LAYER_GET_EXTRA_YEARS_COUNT_TEXT(layer)%\@の{TALENT:MASTER:年齢}歳で設定しました
PRINTFORML 能力値にあと【{extraPoint}】のポイントを割り振って下さい(能力最小1,最大8)
PRINTL [1]いいえ(16歳・普通体型の男子高校生として設定されます)
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erb", source);

        Assert.Contains(segments, segment =>
            segment.OriginalText == "、留年{addYears}%MASTER_LAYER_GET_EXTRA_YEARS_COUNT_TEXT(layer)%"
            && segment.SegmentType == "inline-conditional-right");
        Assert.Contains(segments, segment =>
            segment.OriginalText == "の{TALENT:MASTER:年齢}歳で設定しました"
            && segment.SegmentType == "print-tail");
        Assert.Contains(segments, segment =>
            segment.OriginalText == "能力値にあと【{extraPoint}】のポイントを割り振って下さい(能力最小1,最大8)"
            && segment.SegmentType == "print-tail");
        Assert.Contains(segments, segment =>
            segment.OriginalText == "[1]いいえ(16歳・普通体型の男子高校生として設定されます)"
            && segment.SegmentType == "print-tail");
    }

    [Fact]
    public void Extract_KeepsPrintTailDisplayLabelsWithPlaceholdersAndRateSymbols()
    {
        const string source = """
PRINTFORML 所持金:{MONEY}円
PRINTFORML 客の所持金による修正:×50％
PRINTFORML ……？:×50％
PRINTFORML 　　所持金：{MONEY}円　　残り時間：{FLAG:残調教時間}分
PRINTFORM \@ GET_CHARA_GO_SCHOOL_INDEX(ASSI) > 0 ?[801]%GET_SCHOOL_NAME(GET_CHARA_GO_SCHOOL_INDEX(ASSI)) + "所属",24,LEFT% # 無所属     　　　　　　　　　 \@
PRINTPLAINFORM 従順:LV{ABL:0} 欲望:LV{ABL:1} 技巧:LV{ABL:2}    
PRINTFORML TALENT:MASTER:気骨
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erb", source);

        Assert.Contains(segments, segment =>
            segment.OriginalText == "所持金:{MONEY}円"
            && segment.SegmentType == "print-tail");
        Assert.Contains(segments, segment =>
            segment.OriginalText == "客の所持金による修正:×50％"
            && segment.SegmentType == "print-tail");
        Assert.Contains(segments, segment =>
            segment.OriginalText == "……？:×50％"
            && segment.SegmentType == "print-tail");
        Assert.Contains(segments, segment =>
            segment.OriginalText == "所持金：{MONEY}円"
            && segment.SegmentType == "print-tail");
        Assert.Contains(segments, segment =>
            segment.OriginalText == "残り時間：{FLAG:残調教時間}分"
            && segment.SegmentType == "print-tail");
        Assert.DoesNotContain(segments, segment =>
            segment.OriginalText == "所持金：{MONEY}円　　残り時間：{FLAG:残調教時間}分");
        Assert.Contains(segments, segment =>
            segment.OriginalText == "所属"
            && segment.SegmentType == "inline-conditional-left-fragment");
        Assert.Contains(segments, segment =>
            segment.OriginalText == "無所属"
            && segment.SegmentType == "inline-conditional-right");
        Assert.Contains(segments, segment =>
            segment.OriginalText == "従順:LV{ABL:0}"
            && segment.SegmentType == "print-tail");
        Assert.Contains(segments, segment =>
            segment.OriginalText == "欲望:LV{ABL:1}"
            && segment.SegmentType == "print-tail");
        Assert.Contains(segments, segment =>
            segment.OriginalText == "技巧:LV{ABL:2}"
            && segment.SegmentType == "print-tail");
        Assert.DoesNotContain(segments, segment =>
            segment.OriginalText == "TALENT:MASTER:気骨");
    }

    [Fact]
    public void Extract_FindsNaturalTextInsideAngleBracketPrintTail()
    {
        const string source = """
PRINT <愛液>
PRINT <br>
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erb", source);

        Assert.Contains(segments, segment =>
            segment.OriginalText == "愛液"
            && segment.SegmentType == "print-tail-fragment");
        Assert.DoesNotContain(segments, segment => segment.OriginalText == "br");
    }

    [Fact]
    public void Extract_SkipsQuotedRuleStringsWithRegisteredFunctionCalls()
    {
        const string source = """
abductedCharaCount = COUNT_RULED_CHARAS("!IS_UNCONTACTABLE(index) && TALENT:index:監禁 > 0 && !IS_NOT_POLICE_RESCUE_TALENT(index)")
PRINTFORML [2] - エンディング履歴(エンディング別)
""";

        var functionRegistry = ErbCodeFunctionRegistry.FromNames(
            ["COUNT_RULED_CHARAS", "IS_UNCONTACTABLE", "IS_NOT_POLICE_RESCUE_TALENT"]);
        var extractor = new ErbExtractor(SymbolNamespaceRegistry.Default, functionRegistry);
        var segments = extractor.Extract("test.erb", source);

        Assert.DoesNotContain(segments, segment =>
            segment.OriginalText.Contains("IS_UNCONTACTABLE", StringComparison.Ordinal)
            || segment.OriginalText.Contains("TALENT:index:監禁", StringComparison.Ordinal));
        Assert.Contains(segments, segment =>
            segment.OriginalText == "[2] - エンディング履歴(エンディング別)"
            && segment.SegmentType == "print-tail");
    }

    [Fact]
    public void Extract_SkipsQuotedErbExpressionsWithSymbolReferences()
    {
        const string source = """
answer = "MAX(EXP:index:レズ経験,EXP:index:ゲイ経験)"
PRINTL [1]いいえ(16歳・普通体型の男子高校生として設定されます)
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erb", source);

        Assert.DoesNotContain(segments, segment =>
            string.Equals(segment.OriginalText, "MAX(EXP:index:レズ経験,EXP:index:ゲイ経験)", StringComparison.Ordinal));
        Assert.Contains(segments, segment =>
            segment.OriginalText == "[1]いいえ(16歳・普通体型の男子高校生として設定されます)"
            && segment.SegmentType == "print-tail");
    }

    [Fact]
    public void Extract_DoesNotFreeTranslateCsvKeyListsInsideCalculationFunctions()
    {
        const string source = """
RETURNF CALC_CHARA_SINGLE_DATA("TALENT",targetChara,"気骨*3,反抗的")
RETURNF CALC_CHARA_MULTIPLE_DATA_BASE(answer,"TALENT",targetChara,"臆病*9,反抗的*11,気丈*12",10,1000)
RETURNF GET_NONEXISTABLE_TALENT_BYNAME("気骨*3,反抗的",charaIndex,charaNo)
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erb", source);

        Assert.DoesNotContain(segments, segment => segment.OriginalText.Contains("気骨", StringComparison.Ordinal));
        Assert.DoesNotContain(segments, segment => segment.OriginalText.Contains("反抗的", StringComparison.Ordinal));
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
    public void Extract_KeepsInflectionsInsideNaturalTextSegments()
    {
        const string source = """
PRINTFORMW 服を着た
PRINTDATAW
DATAFORM 靴まで履いた
ENDDATA
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erb", source);
        var values = segments.Select(segment => segment.OriginalText).ToArray();

        Assert.Contains("服を着た", values);
        Assert.Contains("靴まで履いた", values);
        Assert.DoesNotContain("た", values);
        Assert.DoesNotContain("まで", values);
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

    [Fact]
    public void Extract_SkipsDirectiveStringsInsideFunctionCallArguments()
    {
        const string source = """
#DIM CONST デバッグ用_差分要素数MAX = VARSIZE("デバッグ用_差分名定義")
#DIMS SAVEDATA キャラ一覧フィルタ_EXキャラ, VARSIZE("キャラ一覧フィルタ")
#DIMS CONST PROSTITUTION_HAIR_COLOR_LIST = "栗毛","黒髪","灰髪"
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erh", source);
        var values = segments.Select(segment => segment.OriginalText).ToArray();

        Assert.DoesNotContain("デバッグ用_差分名定義", values);
        Assert.DoesNotContain("キャラ一覧フィルタ", values);
        Assert.Contains("栗毛", values);
        Assert.Contains("黒髪", values);
        Assert.Contains("灰髪", values);
    }

    [Fact]
    public void Extract_TreatsDimsLookupArrayKeysAsReferenceBearingItems()
    {
        const string source = """
#DIMS CONST CUSTOMER_VALUES_ARRAY="対応娼婦","プレイ傾向"
@GET_CUSTOMER_VALUEINDEX_FROM_VALUENAME(valueName)
#FUNCTION
valueIndex = FINDELEMENT(CUSTOMER_VALUES_ARRAY,valueName,,,1)
@GET_PROSTITUTION_CUSTOMER_VALUE(targetCustomerIndex,valueName)
#FUNCTION
RETURNF CUSTOMER:targetCustomerIndex:GET_CUSTOMER_VALUEINDEX_FROM_VALUENAME(valueName)
answer = GET_PROSTITUTION_CUSTOMER_VALUE(customerIndex,"プレイ傾向")
""";

        var registry = ErbDimsLookupRegistry.BuildFromDocuments([source]);
        var extractor = new ErbExtractor(SymbolNamespaceRegistry.Default, ErbCodeFunctionRegistry.Empty, registry);
        var segments = extractor.Extract("test.erh", source);

        Assert.Contains(segments, segment =>
            segment.SegmentType == "erb-dims-lookup-key"
            && segment.SymbolNamespace == "DIMS:CUSTOMER_VALUES_ARRAY"
            && segment.OriginalSymbolKey == "プレイ傾向");
        Assert.DoesNotContain(segments, segment =>
            segment.SegmentType == "quoted-string"
            && segment.OriginalText == "プレイ傾向");

        var nestedSource = """
answer = PROSTITUTION_CUSTOMER_PLAYTYPE_VALUE_AFFECT(GET_PROSTITUTION_CUSTOMER_VALUE(customerIndex,"プレイ傾向"),prostitutionLady)
""";
        var nestedSegments = extractor.Extract("test.erb", nestedSource);
        Assert.DoesNotContain(nestedSegments, segment =>
            segment.SegmentType == "quoted-string"
            && segment.OriginalText == "プレイ傾向");
    }

    [Fact]
    public void Extract_TreatsSplitStringLookupArrayKeyFieldAsCsvReference()
    {
        const string source = """
@GET_CHARA_STATUS_TEXT(targetChara)
#FUNCTIONS
#DIMS STATUS_TALENT="ロボ娘,5","プリンセス,5,お姫様"
#DIMS talentParts,3
itemCount = SPLIT_STRING(STATUS_TALENT:index,",",talentParts)
IF TALENT:targetChara:GETNUM(TALENT,talentParts:0) && TOINT(talentParts:1) > 0
    answer = \@ itemCount >= 2 ? %talentParts:2% # %talentParts:0% \@
ENDIF
""";

        var registry = ErbDimsLookupRegistry.BuildFromDocuments([source]);
        var extractor = new ErbExtractor(SymbolNamespaceRegistry.Default, ErbCodeFunctionRegistry.Empty, registry);

        var segments = extractor.Extract("test.erb", source);

        Assert.Contains(segments, segment =>
            segment.SegmentType == "erb-split-lookup-key"
            && segment.OriginalText == "ロボ娘"
            && segment.SymbolNamespace == "TALENT"
            && segment.OriginalSymbolKey == "ロボ娘");
        Assert.Contains(segments, segment =>
            segment.SegmentType == "erb-split-lookup-key"
            && segment.OriginalText == "プリンセス"
            && segment.SymbolNamespace == "TALENT"
            && segment.OriginalSymbolKey == "プリンセス");
        Assert.Contains(segments, segment =>
            segment.SegmentType == "directive-string"
            && segment.OriginalText == "お姫様");
        Assert.DoesNotContain(segments, segment =>
            string.Equals(segment.OriginalText, "ロボ娘,5", StringComparison.Ordinal)
            || string.Equals(segment.OriginalText, "プリンセス,5,お姫様", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_SkipsDimsLookupSelectCaseLabels()
    {
        const string source = """
#DIMS CONST PROSTITUTION_SEX_LIST="男","女","ふたなり"
SELECTCASE PROSTITUTION_SEX_LIST:customerSex
    CASE "男"
        RETURNF "偉そうな"
    CASE "女","ふたなり"
        RETURNF "高慢な"
ENDSELECT
""";

        var registry = ErbDimsLookupRegistry.BuildFromDocuments([source]);
        var extractor = new ErbExtractor(SymbolNamespaceRegistry.Default, ErbCodeFunctionRegistry.Empty, registry);

        var segments = extractor.Extract("test.erb", source);

        Assert.DoesNotContain(segments, segment =>
            segment.SegmentType == "quoted-string"
            && (segment.OriginalText == "男"
                || segment.OriginalText == "女"
                || segment.OriginalText == "ふたなり"));
        Assert.Contains(segments, segment =>
            segment.SegmentType == "quoted-string"
            && segment.OriginalText == "偉そうな");
    }

    [Fact]
    public void Extract_SkipsCsvNameSelectCaseLabels()
    {
        const string source = """
SELECTCASE TALENTNAME:index
    CASE "寄生","浄化","オトコ"
        RETURNF "表示文"
ENDSELECT
""";

        var extractor = new ErbExtractor();

        var segments = extractor.Extract("test.erb", source);

        Assert.DoesNotContain(segments, segment =>
            segment.SegmentType == "quoted-string"
            && (segment.OriginalText == "寄生"
                || segment.OriginalText == "浄化"
                || segment.OriginalText == "オトコ"));
        Assert.Contains(segments, segment =>
            segment.SegmentType == "quoted-string"
            && segment.OriginalText == "表示文");
    }

    [Fact]
    public void Extract_IncludesStandaloneColorWordsWhenCaseIsNotKnownLookup()
    {
        const string source = """
SELECTCASE colorText
    CASE "黒","金","銀"
        answer = %colorText%髪
    CASE "栗","緑","灰"
        answer = %colorText%色の髪
    CASE "赤","青","白"
        answer = %colorText%い髪
    CASE "ピンク","紫","オレンジ","水色"
        answer = %colorText%の髪
ENDSELECT
""";

        var extractor = new ErbExtractor();

        var segments = extractor.Extract("test.erb", source);
        var values = segments.Select(segment => segment.OriginalText).ToArray();

        Assert.Contains("黒", values);
        Assert.Contains("金", values);
        Assert.Contains("銀", values);
        Assert.Contains("緑", values);
        Assert.Contains("灰", values);
        Assert.Contains("赤", values);
        Assert.Contains("青", values);
        Assert.Contains("白", values);
        Assert.Contains("紫", values);
        Assert.Contains("水色", values);
        Assert.Contains("栗", values);
        Assert.Contains("ピンク", values);
        Assert.Contains("オレンジ", values);
    }

    [Fact]
    public void Extract_SkipsDirectiveStringsInsideMultilineDirectiveFunctionArguments()
    {
        const string source = """
#DIMS CONST デバッグ用_キャラ固有衣装名定義 = 
    "25カリオストロ:デフォルト_SU,黒魔導娘服,おしゃれ着_大人",
    "1132マリー:デフォルト_PC"
#DIM CONST デバッグ用_キャラ固有差分要素数MAX = MAX(
        VARSIZE("デバッグ用_キャラ固有衣装名定義"), 
        VARSIZE("デバッグ用_キャラ固有差分名定義")
    )
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erh", source);
        var values = segments.Select(segment => segment.OriginalText).ToArray();

        Assert.Contains("25カリオストロ:デフォルト_SU,黒魔導娘服,おしゃれ着_大人", values);
        Assert.Contains("1132マリー:デフォルト_PC", values);
        Assert.DoesNotContain("デバッグ用_キャラ固有衣装名定義", values);
        Assert.DoesNotContain("デバッグ用_キャラ固有差分名定義", values);
    }

    [Fact]
    public void Extract_SkipsPrintImgResourceNames()
    {
        const string source = """
PRINT_IMG "タイトルロゴ", 5330, 1520
PRINTFORMW "普通の文章です"
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erb", source);
        var values = segments.Select(segment => segment.OriginalText).ToArray();

        Assert.DoesNotContain("タイトルロゴ", values);
        Assert.Contains("普通の文章です", values);
    }

    [Fact]
    public void Extract_HandlesRawHtmlStringsWithInnerQuotesWithoutCapturingFunctionOrImageKeys()
    {
        const string source = """
CALL 履歴データベース登録(CFLAG:TARGET:人物番号, @"<font color='#%カラーパレット_HTML("赤ピンク")%'>%CALLNAME:寝取らせ_主導キャラ%に押さえつけられながら%CALLNAME:PLAYER%に無理やり犯される、忘れられない屈辱の思い出が刻まれてしまった</font><img src='えっちハート'>","うふふ")
LOCALS += @"<font color='#%カラーパレット_HTML("黄")%'>特濃</font>"
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erb", source);
        var values = segments.Select(segment => segment.OriginalText).ToArray();

        Assert.Contains("忘れられない屈辱の思い出が刻まれてしまった", values);
        Assert.Contains("特濃", values);
        Assert.DoesNotContain(values, value => value.Contains("カラーパレット_HTML", StringComparison.Ordinal));
        Assert.DoesNotContain("赤ピンク", values);
        Assert.DoesNotContain("黄", values);
        Assert.DoesNotContain("えっちハート", values);
        Assert.DoesNotContain(values, value => value.Contains("%CALLNAME:", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_OnlyKeepsCharacterSearchNamesInsideImageResourceExpressions()
    {
        const string source = """
SIF EXIST画像FILE(@"%CSTR:(キャラ検索("[グラツィア大公家の小公女]セレナ")):画像フォルダ%/ダンジョン用_野盗ボス")
    敵BATTLE_STATE_STR:ARG:ボス画像 = %CSTR:(キャラ検索("[グラツィア大公家の小公女]セレナ")):画像フォルダ%/ダンジョン用_野盗ボス
CALLF 任意顔グラ表示用文字列関数(キャラ検索("[グラツィア大公家の小公女]セレナ"),"ダンジョン用_野盗ボス",124,,,"怒りマーク")
対象キャラ = キャラ検索("[グラツィア大公家の小公女]セレナ")
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erb", source);
        var values = segments.Select(segment => segment.OriginalText).ToArray();

        Assert.Contains("[グラツィア大公家の小公女]セレナ", values);
        Assert.DoesNotContain("キャラ検索", values);
        Assert.DoesNotContain("画像フォルダ", values);
        Assert.DoesNotContain("ダンジョン用_野盗ボス", values);
        Assert.DoesNotContain("怒りマーク", values);
    }

    [Fact]
    public void Extract_ProtectsPercentExpressionsInsideRawStrings()
    {
        const string source = """
ELSEIF SPRITECREATED(@"%DT_CELL_GETS("戦闘効果データベース", バフデバフ番号,"バフ・デバフフラグ")%_%DT_CELL_GETS("戦闘効果データベース", バフデバフ番号,"対象能力")%")
CALL 履歴データベース登録(CFLAG:PLAYER:人物番号, @"初めて%CALLNAME:TARGET%とデートをした","日常")
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erb", source);
        var values = segments.Select(segment => segment.OriginalText).ToArray();

        Assert.Contains("初めて%CALLNAME:TARGET%とデートをした", values);
        Assert.Contains("日常", values);
        Assert.DoesNotContain("戦闘効果データベース", values);
        Assert.DoesNotContain("バフデバフ番号", values);
        Assert.DoesNotContain("バフ・デバフフラグ", values);
        Assert.DoesNotContain("対象能力", values);
    }

    [Fact]
    public void Extract_ProtectsCodeArgumentsInsidePercentAndBraceExpressions()
    {
        const string source = """
アイコン名 = %DT_CELL_GETS("戦闘効果データベース", バフ・デバフ番号, "対象能力")%
SIF STRCOUNT(削除番号, @"_{DT_CELL_GET("ミルクデータベース", ミルク番号, "id")}_")
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erb", source);
        var values = segments.Select(segment => segment.OriginalText).ToArray();

        Assert.DoesNotContain("戦闘効果データベース", values);
        Assert.DoesNotContain("バフ・デバフ番号", values);
        Assert.DoesNotContain("対象能力", values);
        Assert.DoesNotContain("ミルクデータベース", values);
        Assert.DoesNotContain("ミルク番号", values);
    }

    [Fact]
    public void Extract_SkipsConfigNamesAndComparisonModeConstants()
    {
        const string source = """
FOR 連番, 0, (Y幅 / GETCONFIG("一行の高さ")) + 1
IF 処理モード == "写真用文字列_プレイヤー"
PRINTFORMW "普通の文章です"
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erb", source);
        var values = segments.Select(segment => segment.OriginalText).ToArray();

        Assert.DoesNotContain("一行の高さ", values);
        Assert.DoesNotContain("写真用文字列_プレイヤー", values);
        Assert.Contains("普通の文章です", values);
    }

    [Fact]
    public void Extract_SkipsPaletteLookupKeys()
    {
        const string source = """
@カラーパレット(ARGS)
#FUNCTION
SELECTCASE ARGS
    CASE "真っ赤"
        RETURNF 0xFF3030
ENDSELECT

SETCOLOR カラーパレット("青緑")
SETCOLOR BARCOLORSET("火属性")
PRINTFORMW "普通の文章です"
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erb", source);
        var values = segments.Select(segment => segment.OriginalText).ToArray();

        Assert.DoesNotContain("真っ赤", values);
        Assert.DoesNotContain("青緑", values);
        Assert.DoesNotContain("火属性", values);
        Assert.Contains("普通の文章です", values);
    }

    [Fact]
    public void Extract_SkipsLoadTextAndSaveTextPathArguments()
    {
        const string source = """
LOADTEXT "dat/人物DT_XML.txt"
SAVETEXT RESULTS:0, "dat/人物DT_XML.txt"
LOCALS '= LOADTEXT(@"sav/%RESULTS%")
PRINTFORML "普通の文章です"
""";

        var extractor = new ErbExtractor();
        var segments = extractor.Extract("test.erb", source);
        var values = segments.Select(segment => segment.OriginalText).ToArray();

        Assert.DoesNotContain("dat/人物DT_XML.txt", values);
        Assert.DoesNotContain(@"sav/%RESULTS%", values);
        Assert.Contains("普通の文章です", values);
    }
}
