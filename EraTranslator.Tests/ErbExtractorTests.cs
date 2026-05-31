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
