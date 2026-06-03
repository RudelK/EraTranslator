using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class ErbIdentifierExtractorTests
{
    [Fact]
    public void Extract_CollectsFunctionAndVariableIdentifiersButSkipsSymbolNamespaces()
    {
        const string content = """
@キャラ検索(ARGS)
#FUNCTION
#DIM キーボード選択コマンドID
キーボード選択コマンドID = 1
LOCALS = 二つ名を入力してください
WSTR:(K++) = %CALLNAME:TARGET%は浅くチンポを咥えしゃぶりながら竿の根元をぎゅっと揉むようにしごいて
CALL キャラ検索("[グラツィア大公家の小公女]セレナ", キーボード選択コマンドID)
IF ABL:対象キャラ:(TCVAR:対象キャラ:野外オナニー_部位) > 4
PRINTFORMW %CSTR:(キャラ検索("[グラツィア大公家の小公女]セレナ")):画像フォルダ%
PRINTFORMW 二つ名を入力してください。
""";

        var extractor = new ErbIdentifierExtractor();
        var occurrences = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(occurrences, occurrence =>
            occurrence.Kind == ErbIdentifierKind.Function
            && occurrence.Role == ErbIdentifierRole.Definition
            && occurrence.OriginalName == "キャラ検索");
        Assert.Contains(occurrences, occurrence =>
            occurrence.Kind == ErbIdentifierKind.Function
            && occurrence.Role == ErbIdentifierRole.Call
            && occurrence.OriginalName == "キャラ検索");
        Assert.Contains(occurrences, occurrence =>
            occurrence.Kind == ErbIdentifierKind.Variable
            && occurrence.Role == ErbIdentifierRole.Declaration
            && occurrence.OriginalName == "キーボード選択コマンドID");
        Assert.Contains(occurrences, occurrence =>
            occurrence.Kind == ErbIdentifierKind.Variable
            && occurrence.OriginalName == "キーボード選択コマンドID");
        Assert.DoesNotContain(occurrences, occurrence => occurrence.OriginalName == "野外オナニー_部位");
        Assert.DoesNotContain(occurrences, occurrence => occurrence.OriginalName == "対象キャラ");
        Assert.DoesNotContain(occurrences, occurrence => occurrence.OriginalName == "二つ名を入力してください");
        Assert.DoesNotContain(occurrences, occurrence => occurrence.OriginalName.Contains("浅くチンポ", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_SkipsInlineCommentsInCodeStatements()
    {
        const string content = """
SIF R:21 < 50	;200000$なら50％の確率で当選
CALL キャラ検索("セミ;コロン", 対象ID) ;コメント関数(当選)
""";

        var extractor = new ErbIdentifierExtractor();
        var occurrences = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(occurrences, occurrence => occurrence.OriginalName == "キャラ検索");
        Assert.Contains(occurrences, occurrence => occurrence.OriginalName == "対象ID");
        Assert.DoesNotContain(occurrences, occurrence => occurrence.OriginalName.Contains("なら", StringComparison.Ordinal));
        Assert.DoesNotContain(occurrences, occurrence => occurrence.OriginalName.Contains("確率", StringComparison.Ordinal));
        Assert.DoesNotContain(occurrences, occurrence => occurrence.OriginalName.Contains("当選", StringComparison.Ordinal));
        Assert.DoesNotContain(occurrences, occurrence => occurrence.OriginalName == "コメント関数");
    }

    [Fact]
    public void Extract_SkipsSingleHiraganaInflectionTokens()
    {
        const string content = """
#DIM 衣装番号
IF 条件変数 && 選択状態 && 衣装番号 > 0 && た && した
""";

        var extractor = new ErbIdentifierExtractor();
        var occurrences = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(occurrences, occurrence => occurrence.OriginalName == "条件変数");
        Assert.Contains(occurrences, occurrence => occurrence.OriginalName == "選択状態");
        Assert.Contains(occurrences, occurrence => occurrence.OriginalName == "衣装番号");
        Assert.DoesNotContain(occurrences, occurrence => occurrence.OriginalName == "た");
        Assert.DoesNotContain(occurrences, occurrence => occurrence.OriginalName == "した");
    }

    [Fact]
    public void Extract_CollectsDimInitializersSelectCaseAndFunctionDefinitionArguments()
    {
        const string content = """
#DIM CONST 売春一括指示_FALSE = 0
#DIM  CONST 売春一括指示_ループ順_避妊方法, 4 = 売春一括指示_生, 売春一括指示_コンドーム
#DIM SAVEDATA 売春一括指示_OPTION = 売春一括指示_FALSE
@壁尻部屋_彼初回口上, 彼Label
@SUCCESSION_CHARA(選択キャラ)
#DIMS 彼Label
#DIM 選択キャラ,1
SELECTCASE 売春一括指示_避妊方法
CASE 売春一括指示_避妊結界, 売春一括指示_生
[IF 売春一括指示]
""";

        var extractor = new ErbIdentifierExtractor();
        var occurrences = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(occurrences, occurrence =>
            occurrence.OriginalName == "売春一括指示_ループ順_避妊方法"
            && occurrence.Role == ErbIdentifierRole.Declaration);
        Assert.Contains(occurrences, occurrence =>
            occurrence.OriginalName == "売春一括指示_FALSE"
            && occurrence.Role == ErbIdentifierRole.Reference);
        Assert.Contains(occurrences, occurrence =>
            occurrence.OriginalName == "売春一括指示_生"
            && occurrence.Role == ErbIdentifierRole.Reference);
        Assert.Contains(occurrences, occurrence =>
            occurrence.OriginalName == "売春一括指示_コンドーム"
            && occurrence.Role == ErbIdentifierRole.Reference);
        Assert.Contains(occurrences, occurrence =>
            occurrence.OriginalName == "売春一括指示_避妊方法"
            && occurrence.Role == ErbIdentifierRole.Reference);
        Assert.Contains(occurrences, occurrence =>
            occurrence.OriginalName == "売春一括指示_避妊結界"
            && occurrence.Role == ErbIdentifierRole.Reference);
        Assert.Contains(occurrences, occurrence =>
            occurrence.OriginalName == "売春一括指示"
            && occurrence.Role == ErbIdentifierRole.Reference);
        Assert.Contains(occurrences, occurrence =>
            occurrence.OriginalName == "彼Label"
            && occurrence.Role == ErbIdentifierRole.Declaration);
        Assert.Contains(occurrences, occurrence =>
            occurrence.OriginalName == "選択キャラ"
            && occurrence.Role == ErbIdentifierRole.Declaration);
    }
}
