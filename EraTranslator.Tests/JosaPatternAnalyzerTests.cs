using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class JosaPatternAnalyzerTests
{
    private static readonly JosaSupportPackageInfo PackageInfo = new()
    {
        ErbExists = true,
        ErhExists = true,
        HasFunctionSignatures = true,
        HasMacroDefines = true,
        HasErhIncludeLinkage = true,
        SupportsLBatchimRoroException = true,
        SupportsImplicitYiFallback = true,
        SupportsParticlePassThrough = true,
        SupportsMacroDefines = true,
        SupportedParticles = ["는", "이", "을", "과", "으로", "랑", "며", "고", "라", "다", "였", "여", "야", "이나", "이면", "의", "에게"],
    };

    [Theory]
    [InlineData("%CALLNAME:MASTER%(은)는", "%플레이어는%")]
    [InlineData("%CALLNAME:MASTER%은(는)", "%플레이어는%")]
    [InlineData("%CALLNAME:MASTER%는(은)", "%플레이어는%")]
    [InlineData("%CALLNAME:MASTER%(는)은", "%플레이어는%")]
    [InlineData("%CALLNAME:MASTER%은/는", "%플레이어는%")]
    [InlineData("%CALLNAME:TARGET%(이)가", "%타겟가%")]
    [InlineData("%CALLNAME:TARGET%이(가)", "%타겟가%")]
    [InlineData("%CALLNAME:TARGET%가(이)", "%타겟가%")]
    [InlineData("%CALLNAME:TARGET%(가)이", "%타겟가%")]
    [InlineData("%CALLNAME:TARGET%이/가", "%타겟가%")]
    [InlineData("%CALLNAME:PLAYER%(을)를", "%조교자를%")]
    [InlineData("%CALLNAME:PLAYER%을(를)", "%조교자를%")]
    [InlineData("%CALLNAME:PLAYER%를(을)", "%조교자를%")]
    [InlineData("%CALLNAME:PLAYER%(를)을", "%조교자를%")]
    [InlineData("%CALLNAME:PLAYER%을/를", "%조교자를%")]
    [InlineData("%CALLNAME:ARG%(은)는", "%ARG는%")]
    [InlineData("%CALLNAME:娼婦キャラ番号%은", "%조사처리(CALLNAME:娼婦キャラ番号,\"는\")%")]
    [InlineData("%CALLNAME:娼婦キャラ番号%은/는", "%조사처리(CALLNAME:娼婦キャラ番号,\"는\")%")]
    [InlineData("%CALLNAME:娼婦キャラ番号%(는)은", "%조사처리(CALLNAME:娼婦キャラ番号,\"는\")%")]
    [InlineData("%CALLNAME:娼婦キャラ番号%는(은)", "%조사처리(CALLNAME:娼婦キャラ番号,\"는\")%")]
    [InlineData("%CALLNAME:LCOUNT%는", "%조사처리(CALLNAME:LCOUNT,\"는\")%")]
    [InlineData("%NAME:ARG%의", "%NAME:ARG%의")]
    [InlineData("%플레이어는()%","%플레이어는%")]
    [InlineData("%NAME:TARGET%(을)를", "%조사처리(NAME:TARGET,\"을\")%")]
    [InlineData("%NAME:TARGET%을(를)", "%조사처리(NAME:TARGET,\"을\")%")]
    [InlineData("%조사선택(CALLNAME:MASTER,\"는\")%", "%플레이어는%")]
    [InlineData("%조사만선택(CALLNAME:MASTER,\"는\")%", "%조사만처리(CALLNAME:MASTER,\"는\")%")]
    [InlineData("%CALLNAME:MASTER%겠", "%조사처리(CALLNAME:MASTER,\"겠\")%")]
    [InlineData("%~%(은)는", "%조사처리(~,\"는\")%")]
    [InlineData("%~%은(는)", "%조사처리(~,\"는\")%")]
    [InlineData("%~%의", "%~%의")]
    [InlineData("%조사처리(CALLNAME:MASTER,\"의\")%", "%플레이어의%")]
    [InlineData("%조사처리(CALLNAME:(LOCAL:11),\"과\")%", "%조사처리(CALLNAME:(LOCAL:11),\"과\")%")]
    public void RewriteText_RewritesToLatestMacroOrGenericForm(string source, string expected)
    {
        var analyzer = new JosaPatternAnalyzer();

        var result = analyzer.RewriteText(source, new Dictionary<(string Namespace, string OriginalKey), string>(), PackageInfo);

        Assert.Equal(expected, result.Text);
    }

    [Theory]
    [InlineData("사과은 맛있다.", "사과는 맛있다.")]
    [InlineData("서울는 조용하다.", "서울은 조용하다.")]
    [InlineData("사람와 함께 간다.", "사람과 함께 간다.")]
    [InlineData("길으로 갔다.", "길로 갔다.")]
    [InlineData("사과이다.", "사과다.")]
    [InlineData("레벨2은 높다.", "레벨2는 높다.")]
    [InlineData("레벨3는 높다.", "레벨3은 높다.")]
    [InlineData("영문ABC은 유지", "영문ABC은 유지")]
    [InlineData("%CALLNAME:MASTER%는 사과은 좋아한다.", "%플레이어는% 사과는 좋아한다.")]
    [InlineData("집의 불빛", "집의 불빛")]
    [InlineData("친구에게 말했다", "친구에게 말했다")]
    public void RewriteText_RewritesLiteralParticlesInGeneralKoreanSentences(string source, string expected)
    {
        var analyzer = new JosaPatternAnalyzer();

        var result = analyzer.RewriteText(source, new Dictionary<(string Namespace, string OriginalKey), string>(), PackageInfo);

        Assert.Equal(expected, result.Text);
    }

    [Fact]
    public void AnalyzeDocument_TracksMacroAndLegacyUsage()
    {
        var analyzer = new JosaPatternAnalyzer();
        const string text = """
PRINTFORMW %플레이어는()% 움직였다
PRINTFORMW %조사처리(NAME:TARGET,"를")% 본다
PRINTFORMW %CALLNAME:MASTER%(은)는 왔다
""";

        var analysis = analyzer.AnalyzeDocument(text, PackageInfo);

        Assert.Equal(3, analysis.PatternCount);
        Assert.True(analysis.RequiresErh);
        Assert.Equal("혼합", analysis.SyntaxType);
    }

    [Theory]
    [InlineData("%CALLNAME:supportChara%", "는 시선을 피했다.", "%조사만처리(CALLNAME:supportChara,\"는\")% 시선을 피했다.")]
    [InlineData("%NAME:(friendList:index)%", "은/는 이미 이쪽의 수중에 있다....", "%조사만처리(NAME:(friendList:index),\"는\")% 이미 이쪽의 수중에 있다....")]
    [InlineData("%NAME:(friendList:index)%", "은(는) 이미 이쪽의 수중에 있다....", "%조사만처리(NAME:(friendList:index),\"는\")% 이미 이쪽의 수중에 있다....")]
    [InlineData("%NAME:(friendList:index)%", "는(은) 이미 이쪽의 수중에 있다....", "%조사만처리(NAME:(friendList:index),\"는\")% 이미 이쪽의 수중에 있다....")]
    [InlineData("%NAME:(friendList:index)%", "(는)은 이미 이쪽의 수중에 있다....", "%조사만처리(NAME:(friendList:index),\"는\")% 이미 이쪽의 수중에 있다....")]
    [InlineData("%NAME:ARG%", "의 눈빛이다.", "의 눈빛이다.")]
    [InlineData("%CALLNAME:MASTER%", "에 가까이 갔다.", "에 가까이 갔다.")]
    [InlineData("사과", "은 맛있다.", "는 맛있다.")]
    [InlineData("길", "으로 갔다.", "로 갔다.")]
    public void RewriteLeadingSplitParticle_RewritesSupportedLeadingParticlesOnly(string previousText, string currentText, string expected)
    {
        var analyzer = new JosaPatternAnalyzer();

        var rewritten = analyzer.RewriteLeadingSplitParticle(previousText, currentText);

        Assert.Equal(expected, rewritten);
    }
}
