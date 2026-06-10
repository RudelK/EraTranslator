using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class TextHeuristicsTests
{
    [Theory]
    [InlineData("ITEMPRICE:子宮内避妊結界")]
    [InlineData("TALENT:MASTER:警戒")]
    [InlineData("CFLAG:targetChara:主人監禁日数")]
    [InlineData("TALENT:MASTER:警戒*5 + CFLAG:targetChara:主人監禁日数")]
    public void LooksLikeErbSymbolExpression_RecognizesSupportedExpressions(string value)
    {
        Assert.True(TextHeuristics.LooksLikeErbSymbolExpression(value));
    }

    [Theory]
    [InlineData("画像.png")]
    [InlineData("ボス戦.ogg")]
    [InlineData("font.ttf")]
    [InlineData("ui/button.webp")]
    public void LooksLikeResourcePathLiteral_RecognizesSlashlessEmueraResources(string value)
    {
        Assert.True(TextHeuristics.LooksLikeResourcePathLiteral(value));
    }
}
