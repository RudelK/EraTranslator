using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class ErbReferenceExtractorTests
{
    [Fact]
    public void Extract_FindsDirectAndIndirectSymbolReferences()
    {
        var extractor = new ErbReferenceExtractor();
        var content = """
IF CFLAG:外見年齢
PRINTFORML %CSTR:あなた呼び方／陥落前%
flagName = "外見年齢"
IF CFLAG:{flagName}
""";

        var result = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "CFLAG"
            && reference.OriginalKey == "外見年齢");
        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "CSTR"
            && reference.OriginalKey == "あなた呼び方／陥落前");
        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.IndirectVariable
            && reference.Namespace == "CFLAG"
            && reference.VariableName == "flagName"
            && reference.ResolutionKind == SymbolReferenceResolutionKind.Resolved
            && reference.CandidateKeys.Contains("外見年齢", StringComparer.Ordinal));
        Assert.Contains(result.variableLiterals, occurrence =>
            occurrence.VariableName == "flagName"
            && occurrence.LiteralValue == "外見年齢"
            && occurrence.IsExactValue);
    }

    [Fact]
    public void Extract_FindsLiteralKeysInVariableIndexedReferences()
    {
        var extractor = new ErbReferenceExtractor();
        const string content = """
PRINTFORMW "ABL:index:従順 * 10 + EXP:index:愛情経験 + CFLAG:index:依存度"
""";

        var result = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "ABL"
            && reference.OriginalKey == "従順");
        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "EXP"
            && reference.OriginalKey == "愛情経験");
        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "CFLAG"
            && reference.OriginalKey == "依存度");
        Assert.DoesNotContain(result.references, reference =>
            string.Equals(reference.OriginalKey, "index:従順", StringComparison.Ordinal)
            || string.Equals(reference.OriginalKey, "index:愛情経験", StringComparison.Ordinal)
            || string.Equals(reference.OriginalKey, "index:依存度", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_FindsAdditionalSupportedNamespacesAndVariableIndexedKeys()
    {
        var extractor = new ErbReferenceExtractor();
        const string content = """
IF MONEY > ITEMPRICE:子宮内避妊結界
RETURNF TALENT:MASTER:警戒 * 5 + CFLAG:targetChara:主人監禁日数
""";

        var result = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "ITEMPRICE"
            && reference.OriginalKey == "子宮内避妊結界");
        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "TALENT"
            && reference.OriginalKey == "警戒");
        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "CFLAG"
            && reference.OriginalKey == "主人監禁日数");
    }

    [Fact]
    public void Extract_FindsLiteralKeysAfterExpressionIndexedReferences()
    {
        var extractor = new ErbReferenceExtractor();
        const string content = """
IF TALENT:(targetChara):脅迫
IF TALENT:GETCHARA(205):失踪
IF MAXBASE:(T):体力 > 1000
""";

        var result = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "TALENT"
            && reference.OriginalKey == "脅迫");
        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "TALENT"
            && reference.OriginalKey == "失踪");
        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "MAXBASE"
            && reference.OriginalKey == "体力");
    }

    [Fact]
    public void Extract_FindsColonBearingAndDecoratedSymbolKeys()
    {
        var extractor = new ErbReferenceExtractor();
        const string content = """
IF ABL:ARG:関心:学業 > 0
IF ABL:関心:課外活動 > 0
SIF TEQUIP:거북등무늬결박(귀갑묶기)
expUp:GETNUM(EXP,"噴乳経験") += 1
""";

        var result = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "ABL"
            && reference.OriginalKey == "関心:学業");
        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "ABL"
            && reference.OriginalKey == "関心:課外活動");
        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "TEQUIP"
            && reference.OriginalKey == "거북등무늬결박(귀갑묶기)");
        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "EXP"
            && reference.OriginalKey == "噴乳経験");
        Assert.DoesNotContain(result.references, reference =>
            string.Equals(reference.OriginalKey, "ARG:関心:学業", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_FindsCustomCsvNamespacesAndVerbatimGetNumKeys()
    {
        var extractor = new ErbReferenceExtractor(new SymbolNamespaceRegistry(["OPTION変数", "フレーバー素質", "プレゼント履歴"]));
        const string content = """
SIF OPTION変数:妊娠切り替え
IF フレーバー素質:ARG:素質表示設定 == 1
IF GETNUM(プレゼント履歴, "花束") >= 0
IF GETNUM(プレゼント履歴, @"ケーキ") >= 0
""";

        var result = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "OPTION変数"
            && reference.OriginalKey == "妊娠切り替え");
        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "フレーバー素質"
            && reference.OriginalKey == "素質表示設定");
        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "プレゼント履歴"
            && reference.OriginalKey == "花束");
        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "プレゼント履歴"
            && reference.OriginalKey == "ケーキ");
    }

    [Fact]
    public void Extract_LeavesDynamicGetNumExpressionAsUnresolvedIndirectReference()
    {
        var extractor = new ErbReferenceExtractor(new SymbolNamespaceRegistry(["知識素質"]));
        const string content = """
CSTR切り分け文字列格納 = "魔物知識"
IF GETNUM(知識素質, CSTR切り分け文字列格納:0) > 0
""";

        var result = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.IndirectVariable
            && reference.Namespace == "知識素質"
            && reference.ResolutionKind == SymbolReferenceResolutionKind.Unresolved
            && reference.ExpressionText == "CSTR切り分け文字列格納:0");
    }

    [Fact]
    public void Extract_FindsNestedNamespaceReferenceInsideParenthesizedIndexExpression()
    {
        var extractor = new ErbReferenceExtractor();
        const string content = """
IF ABL:対象キャラ:(TCVAR:対象キャラ:野外オナニー_部位) > 4
""";

        var result = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "TCVAR"
            && reference.OriginalKey == "野外オナニー_部位");
    }

    [Fact]
    public void Extract_FindsCupNamespaceKeys()
    {
        var extractor = new ErbReferenceExtractor();
        const string content = """
SELECTCASE CUP:TARGET:快Ａ
""";

        var result = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "CUP"
            && reference.OriginalKey == "快Ａ");
    }

    [Fact]
    public void Extract_FindsNowexNamespaceKeys()
    {
        var extractor = new ErbReferenceExtractor();
        const string content = """
IF NOWEX:対象キャラ:Ｃ絶頂
""";

        var result = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "NOWEX"
            && reference.OriginalKey == "Ｃ絶頂");
    }
}
