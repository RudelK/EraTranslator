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
ITEMSALES:APTX5000ハーフリング = 1
RETURNF TALENT:MASTER:警戒 * 5 + CFLAG:targetChara:主人監禁日数
""";

        var result = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "ITEMPRICE"
            && reference.OriginalKey == "子宮内避妊結界");
        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "ITEMSALES"
            && reference.OriginalKey == "APTX5000ハーフリング");
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
    public void Extract_FindsCsvKeyListReferencesInsideCalculationFunctions()
    {
        var extractor = new ErbReferenceExtractor();
        const string content = """
RETURNF CALC_CHARA_SINGLE_DATA("TALENT",targetChara,"気骨*3,反抗的")
RETURNF CALC_CHARA_SINGLE_DATA_RULED("TALENT",targetChara,"素直,RAND:3==0|気骨*2,RAND:2==0")
RETURNF CALC_CHARA_MULTIPLE_DATA("TALENT",targetChara,"臆病*9,反抗的*11,気丈*12",10,1000)
RETURNF CALC_CHARA_MULTIPLE_DATA_BASE(answer,"TALENT",targetChara,"臆病*9,反抗的*11,気丈*12",10,1000)
RETURNF GET_NONEXISTABLE_TALENT_BYNAME("気骨*3,反抗的",charaIndex,charaNo)
RETURNF CALC_CHARA_RANGED_DATA("ABL",targetChara,"戦闘能力","1,0|3,2")
""";

        var result = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "TALENT"
            && reference.OriginalKey == "気骨");
        Assert.Contains(result.references, reference =>
            reference.Namespace == "TALENT"
            && reference.OriginalKey == "反抗的");
        Assert.Contains(result.references, reference =>
            reference.Namespace == "TALENT"
            && reference.OriginalKey == "素直");
        Assert.Contains(result.references, reference =>
            reference.Namespace == "TALENT"
            && reference.OriginalKey == "気丈");
        Assert.Contains(result.references, reference =>
            reference.Namespace == "ABL"
            && reference.OriginalKey == "戦闘能力");
        Assert.DoesNotContain(result.references, reference => reference.OriginalKey.Contains("RAND", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_FindsCsvKeyReferencesInAdjacentNamespaceAndKeyArguments()
    {
        var extractor = new ErbReferenceExtractor();
        const string content = """
CALL DISPLAY_FALLEN_PARTS(charaIndex,"EXP","絶頂経験",50)
CALL DISPLAY_FALLEN_PARTS(charaIndex,"ABL","欲望",4)
""";

        var result = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "EXP"
            && reference.OriginalKey == "絶頂経験");
        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "ABL"
            && reference.OriginalKey == "欲望");
    }

    [Fact]
    public void Extract_FindsDimsLookupWrapperReferences()
    {
        const string definitions = """
#DIMS CONST CUSTOMER_VALUES_ARRAY="対応娼婦","プレイ傾向"
@GET_CUSTOMER_VALUEINDEX_FROM_VALUENAME(valueName)
#FUNCTION
valueIndex = FINDELEMENT(CUSTOMER_VALUES_ARRAY,valueName,,,1)
@GET_PROSTITUTION_CUSTOMER_VALUE(targetCustomerIndex,valueName)
#FUNCTION
RETURNF CUSTOMER:targetCustomerIndex:GET_CUSTOMER_VALUEINDEX_FROM_VALUENAME(valueName)
@SET_PROSTITUTION_CUSTOMER_VALUE(targetCustomerIndex,valueName,value)
#FUNCTION
valueIndex = GET_CUSTOMER_VALUEINDEX_FROM_VALUENAME(valueName)
""";
        const string content = """
answer = PROSTITUTION_CUSTOMER_PLAYTYPE_VALUE_AFFECT(GET_PROSTITUTION_CUSTOMER_VALUE(customerIndex,"プレイ傾向"),prostitutionLady)
CALLF SET_PROSTITUTION_CUSTOMER_VALUE(customerIndex,"対応娼婦",0)
""";
        var registry = ErbDimsLookupRegistry.BuildFromDocuments([definitions, content]);
        var extractor = new ErbReferenceExtractor(SymbolNamespaceRegistry.Default, registry);

        var result = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "DIMS:CUSTOMER_VALUES_ARRAY"
            && reference.OriginalKey == "プレイ傾向");
        Assert.Contains(result.references, reference =>
            reference.Namespace == "DIMS:CUSTOMER_VALUES_ARRAY"
            && reference.OriginalKey == "対応娼婦");
    }

    [Fact]
    public void Extract_DimsLookupRegistry_AllowsDuplicateFunctionDefinitions()
    {
        const string content = """
#DIMS CONST CUSTOMER_VALUES_ARRAY="対応娼婦","プレイ傾向"
@EVENTTRAIN
#FUNCTION
RETURNF 0
@EVENTTRAIN
#FUNCTION
RETURNF 1
@GET_CUSTOMER_VALUEINDEX_FROM_VALUENAME(valueName)
#FUNCTION
valueIndex = FINDELEMENT(CUSTOMER_VALUES_ARRAY,valueName,,,1)
@GET_PROSTITUTION_CUSTOMER_VALUE(targetCustomerIndex,valueName)
#FUNCTION
RETURNF CUSTOMER:targetCustomerIndex:GET_CUSTOMER_VALUEINDEX_FROM_VALUENAME(valueName)
answer = GET_PROSTITUTION_CUSTOMER_VALUE(customerIndex,"プレイ傾向")
""";

        var registry = ErbDimsLookupRegistry.BuildFromDocuments([content]);
        var extractor = new ErbReferenceExtractor(SymbolNamespaceRegistry.Default, registry);

        var result = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(result.references, reference =>
            reference.Namespace == "DIMS:CUSTOMER_VALUES_ARRAY"
            && reference.OriginalKey == "プレイ傾向");
    }

    [Fact]
    public void Extract_FindsDirectDimsLookupFunctionLiteralReferences()
    {
        const string content = """
#DIMS CONST PROSTITUTION_SEX_LIST="男","女","ふたなり"
RETURNF FINDELEMENT(PROSTITUTION_SEX_LIST,"女")
""";

        var registry = ErbDimsLookupRegistry.BuildFromDocuments([content]);
        var extractor = new ErbReferenceExtractor(SymbolNamespaceRegistry.Default, registry);

        var result = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "DIMS:PROSTITUTION_SEX_LIST"
            && reference.OriginalKey == "女");
    }

    [Fact]
    public void Extract_FindsDimsLookupSelectCaseLiteralReferences()
    {
        const string content = """
#DIMS CONST PROSTITUTION_SEX_LIST="男","女","ふたなり"
SELECTCASE PROSTITUTION_SEX_LIST:customerSex
    CASE "男"
        RETURNF "偉そうな"
    CASE "女","ふたなり"
        RETURNF "高慢な"
ENDSELECT
""";

        var registry = ErbDimsLookupRegistry.BuildFromDocuments([content]);
        var extractor = new ErbReferenceExtractor(SymbolNamespaceRegistry.Default, registry);

        var result = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(result.references, reference =>
            reference.Namespace == "DIMS:PROSTITUTION_SEX_LIST"
            && reference.OriginalKey == "男");
        Assert.Contains(result.references, reference =>
            reference.Namespace == "DIMS:PROSTITUTION_SEX_LIST"
            && reference.OriginalKey == "女");
        Assert.Contains(result.references, reference =>
            reference.Namespace == "DIMS:PROSTITUTION_SEX_LIST"
            && reference.OriginalKey == "ふたなり");
    }

    [Fact]
    public void Extract_FindsCsvNameSelectCaseLiteralReferences()
    {
        const string content = """
SELECTCASE TALENTNAME:index
    CASE "寄生","浄化","オトコ"
        isLevelSatisfied = 0
ENDSELECT
""";

        var extractor = new ErbReferenceExtractor();

        var result = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "TALENT"
            && reference.OriginalKey == "寄生");
        Assert.Contains(result.references, reference =>
            reference.Namespace == "TALENT"
            && reference.OriginalKey == "浄化");
        Assert.Contains(result.references, reference =>
            reference.Namespace == "TALENT"
            && reference.OriginalKey == "オトコ");
    }

    [Fact]
    public void Extract_CsvNameSelectCaseUsesWholeCaseLiteralOnly()
    {
        const string content = """
SELECTCASE TALENTNAME:index
    CASE "オトコっぽい"
        RETURNF 1
ENDSELECT
""";

        var extractor = new ErbReferenceExtractor();

        var result = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(result.references, reference =>
            reference.Namespace == "TALENT"
            && reference.OriginalKey == "オトコっぽい");
        Assert.DoesNotContain(result.references, reference =>
            reference.Namespace == "TALENT"
            && reference.OriginalKey == "オトコ");
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

    [Fact]
    public void Extract_FindsGetNumCommandFormAndErdDimensionNamespaces()
    {
        var extractor = new ErbReferenceExtractor(new SymbolNamespaceRegistry(["Foo"]));
        const string content = """
keyName = "外見年齢"
GETNUM CFLAG, "依存度"
GETNUM CFLAG, keyName
GETNUM Foo@1, "バー"
""";

        var result = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "CFLAG"
            && reference.OriginalKey == "依存度");
        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.IndirectVariable
            && reference.Namespace == "CFLAG"
            && reference.VariableName == "keyName"
            && reference.ResolutionKind == SymbolReferenceResolutionKind.Resolved
            && reference.CandidateKeys.Contains("外見年齢", StringComparer.Ordinal));
        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "FOO"
            && reference.OriginalKey == "バー");
    }

    [Fact]
    public void Extract_ReadsReferencesInsideEmueraSpecialCommentCodeLines()
    {
        var extractor = new ErbReferenceExtractor();
        const string content = """
;^;IF CFLAG:外見年齢
;!;GETNUM TALENT, "警戒"
; IF CFLAG:コメント
""";

        var result = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "CFLAG"
            && reference.OriginalKey == "外見年齢");
        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "TALENT"
            && reference.OriginalKey == "警戒");
        Assert.DoesNotContain(result.references, reference =>
            reference.Namespace == "CFLAG"
            && reference.OriginalKey == "コメント");
    }

    [Fact]
    public void Extract_FindsReferencesInsideBraceContinuationLinesWithOriginalOffsets()
    {
        var extractor = new ErbReferenceExtractor();
        const string content = """
IF {
    CFLAG:外見年齢
}
""";

        var result = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "CFLAG"
            && reference.OriginalKey == "外見年齢"
            && reference.AbsoluteStart == content.IndexOf("外見年齢", StringComparison.Ordinal));
    }
}
