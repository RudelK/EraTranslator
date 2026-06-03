using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class TranslationQualityRulesTests
{
    [Fact]
    public void NormalizeProtectedCharacterSpacing_RemovesSpacesAroundProtectedFullWidthCharacters()
    {
        var normalized = TranslationQualityRules.NormalizeProtectedCharacterSpacing("초기 장비 색상 ／ 머리 「 테스트 」 、 기본");

        Assert.Equal("초기 장비 색상／머리「테스트」、기본", normalized);
    }

    [Fact]
    public void RequiresLengthReview_ReturnsTrueWhenTextsDifferByMoreThanOnePointFiveTimes()
    {
        Assert.True(TranslationQualityRules.RequiresLengthReview("股間札", "가랑이 표식 또는 사타구니 표식"));
        Assert.False(TranslationQualityRules.RequiresLengthReview("もちもち", "모찌모찌"));
    }

    [Fact]
    public void RequiresLengthReview_ReturnsTrueWhenTranslationIsShorterThanSource()
    {
        Assert.True(TranslationQualityRules.RequiresLengthReview("おはようございます", "안녕"));
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
    public void GetReviewReason_ReturnsAsciiNoiseReason()
    {
        var reason = TranslationQualityRules.GetReviewReason("あなたと違って忙しいの。", "나는 당신과 다르게 바빠요 much");

        Assert.Equal("영어 또는 로마자 잡음이 섞여 있어 검토가 필요합니다.", reason);
    }

    [Fact]
    public void GetHardFailureReason_ReturnsFailureForBlankTranslation()
    {
        var reason = TranslationQualityRules.GetHardFailureReason("   ", "ja", "ko");

        Assert.NotNull(reason);
        Assert.Equal("빈 번역문", reason.Value.ValidationStatus);
    }

    [Fact]
    public void GetHardFailureReason_ReturnsFailureForJapaneseLeakInJaToKo()
    {
        var reason = TranslationQualityRules.GetHardFailureReason("쾌락快楽", "ja", "ko");

        Assert.NotNull(reason);
        Assert.Equal("대상 언어 불일치", reason.Value.ValidationStatus);
    }

    [Fact]
    public void GetHardFailureReason_IgnoresJapaneseInsideErbCodeReferences()
    {
        var reason = TranslationQualityRules.GetHardFailureReason(
            "매춘 중;WORK_TYPE_TEXT:(CFLAG:index:労役種類) == `매춘`",
            "ja",
            "ko",
            "売春中;WORK_TYPE_TEXT:(CFLAG:index:労役種類) == `売春`");

        Assert.Null(reason);
    }

    [Fact]
    public void GetHardFailureReason_StillRejectsUntranslatedBacktickLiteralAroundErbCode()
    {
        var reason = TranslationQualityRules.GetHardFailureReason(
            "매춘 중;WORK_TYPE_TEXT:(CFLAG:index:労役種類) == `売春`",
            "ja",
            "ko",
            "売春中;WORK_TYPE_TEXT:(CFLAG:index:労役種類) == `売春`");

        Assert.NotNull(reason);
        Assert.Equal("대상 언어 불일치", reason.Value.ValidationStatus);
    }

    [Fact]
    public void GetHardFailureReason_AllowsDecorativeJapaneseProlongedSoundMarks()
    {
        var reason = TranslationQualityRules.GetHardFailureReason(
            "%CSVCALLNAME(targettedLady)%짱 찾았다ーー!",
            "ja",
            "ko",
            "%CSVCALLNAME(targettedLady)%ちゃんみつけたーー！");

        Assert.Null(reason);
    }

    [Fact]
    public void GetHardFailureReason_RejectsUntranslatedNaturalParentheticalText()
    {
        var reason = TranslationQualityRules.GetHardFailureReason(
            "[2] - 엔딩이력(エンディング別)",
            "ja",
            "ko",
            "[2] - エンディング履歴(エンディング別)");

        Assert.NotNull(reason);
        Assert.Equal("대상 언어 불일치", reason.Value.ValidationStatus);
    }

    [Fact]
    public void GetHardFailureReason_ReturnsFailureForUnchangedKanjiOnlyTranslationInJaToKo()
    {
        var reason = TranslationQualityRules.GetHardFailureReason("交渉術", "ja", "ko", "交渉術");

        Assert.NotNull(reason);
        Assert.Equal("대상 언어 불일치", reason.Value.ValidationStatus);
    }

    [Fact]
    public void NormalizeTranslatedText_RemovesSpacesForCsv()
    {
        var normalized = TranslationQualityRules.NormalizeTranslatedText("CSV", "초기 장비 색상 ／ 머리");

        Assert.Equal("초기장비색상／머리", normalized);
    }

    [Fact]
    public void NormalizeTranslatedText_PreservesSpacesForCharacterSheetNameLikeFields()
    {
        var normalized = TranslationQualityRules.NormalizeTranslatedText("CSV", "메인 히로인", preserveWhitespace: true);

        Assert.Equal("메인 히로인", normalized);
    }

    [Fact]
    public void NormalizeTranslatedText_ConvertsCsvAsciiCommaToFullWidthComma()
    {
        var normalized = TranslationQualityRules.NormalizeTranslatedText("CSV", "기본 장비, 머리 장비");

        Assert.Equal("기본장비、머리장비", normalized);
    }

    [Fact]
    public void NormalizeTranslatedText_RemovesSpacesAroundFullWidthCharactersEvenWhenWhitespaceIsPreserved()
    {
        var normalized = TranslationQualityRules.NormalizeTranslatedText("CSV", "메인 히로인 、 호출 명", preserveWhitespace: true);

        Assert.Equal("메인 히로인、호출 명", normalized);
    }

    [Fact]
    public void NormalizeTranslatedText_RewritesFullWidthCommaInsideErbFunctionArguments()
    {
        var normalized = TranslationQualityRules.NormalizeTranslatedText(
            "ERB",
            "GET_SP_TRAIN_MEETING_CHARA_NAME(SP_TRAIN_MEETING_CHARA、3)");

        Assert.Equal("GET_SP_TRAIN_MEETING_CHARA_NAME(SP_TRAIN_MEETING_CHARA,3)", normalized);
    }

    [Fact]
    public void NormalizeTranslatedText_RewritesFullWidthCommaInsideErbBraceExpressions()
    {
        var normalized = TranslationQualityRules.NormalizeTranslatedText(
            "ERB",
            "{needPoint、5、RIGHT}");

        Assert.Equal("{needPoint,5,RIGHT}", normalized);
    }

    [Fact]
    public void NormalizeTranslatedText_DoesNotRewriteNaturalParentheticalComma()
    {
        var normalized = TranslationQualityRules.NormalizeTranslatedText(
            "ERB",
            "상태(좋음、나쁨)");

        Assert.Equal("상태(좋음、나쁨)", normalized);
    }

    [Fact]
    public void NormalizeTranslatedText_DoesNotRewriteNaturalBraceComma()
    {
        var normalized = TranslationQualityRules.NormalizeTranslatedText(
            "ERB",
            "{좋음、나쁨}");

        Assert.Equal("{좋음、나쁨}", normalized);
    }
}
