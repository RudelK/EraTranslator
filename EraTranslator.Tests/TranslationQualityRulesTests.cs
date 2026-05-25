using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class TranslationQualityRulesTests
{
    [Fact]
    public void NormalizeProtectedCharacterSpacing_RemovesSpacesAroundProtectedFullWidthCharacters()
    {
        var normalized = TranslationQualityRules.NormalizeProtectedCharacterSpacing("초기 장비 색상 ／ 머리 「 테스트 」");

        Assert.Equal("초기 장비 색상／머리「테스트」", normalized);
    }

    [Fact]
    public void RequiresLengthReview_ReturnsTrueWhenTextsDifferByMoreThanOnePointFiveTimes()
    {
        Assert.True(TranslationQualityRules.RequiresLengthReview("股間札", "가랑이 표식 또는 사타구니 표식"));
        Assert.False(TranslationQualityRules.RequiresLengthReview("もちもち", "모찌모찌"));
    }

    [Fact]
    public void GetReviewReason_ReturnsAlternativeCandidateReason()
    {
        var reason = TranslationQualityRules.GetReviewReason("デフォ子", "데포자/기본 캐릭터");

        Assert.Equal("대체 후보가 함께 출력되어 검토가 필요합니다.", reason);
    }

    [Fact]
    public void GetReviewReason_ReturnsExplanationParenthesesReason()
    {
        var reason = TranslationQualityRules.GetReviewReason("パイパン", "파이판(무모/제모 상태)");

        Assert.Equal("대체 후보가 함께 출력되어 검토가 필요합니다.", reason);
    }

    [Fact]
    public void NormalizeTranslatedText_RemovesSpacesForCsv()
    {
        var normalized = TranslationQualityRules.NormalizeTranslatedText("CSV", "초기 장비 색상 ／ 머리");

        Assert.Equal("초기장비색상／머리", normalized);
    }
}
