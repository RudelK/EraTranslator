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
    [InlineData("%CALLNAME:TARGET%(이)가", "%타겟가%")]
    [InlineData("%CALLNAME:PLAYER%(을)를", "%조교자를%")]
    [InlineData("%CALLNAME:ARG%(은)는", "%ARG는%")]
    [InlineData("%플레이어는()%","%플레이어는%")]
    [InlineData("%NAME:TARGET%(을)를", "%조사처리(NAME:TARGET,\"을\")%")]
    [InlineData("%조사처리(CALLNAME:MASTER,\"의\")%", "%플레이어의%")]
    [InlineData("%조사처리(CALLNAME:(LOCAL:11),\"과\")%", "%조사처리(CALLNAME:(LOCAL:11),\"과\")%")]
    public void RewriteText_RewritesToLatestMacroOrGenericForm(string source, string expected)
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
}
