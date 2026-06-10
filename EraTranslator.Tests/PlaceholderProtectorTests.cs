using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class PlaceholderProtectorTests
{
    [Fact]
    public void ProtectAndRestore_RoundTripsCoreTokens()
    {
        var protector = new PlaceholderProtector();
        var original = @"%CALLNAME%가 {BASE:체력,3}\% 회복했다.\n<FLAG> \/";

        var protectedText = protector.Protect(original);

        Assert.Contains("__PH0__", protectedText.Text, StringComparison.Ordinal);
        Assert.True(protector.HasAllTokens(protectedText.Text, protectedText.Placeholders, out _));

        var restored = protector.Restore(protectedText.Text, protectedText.Placeholders);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void ProtectAndRestore_RoundTripsFullWidthSpecialCharacters()
    {
        var protector = new PlaceholderProtector();
        var original = "／【테스트】＜값＞「문장」（괄호）『겹문장』％";

        var protectedText = protector.Protect(original);

        Assert.Contains("＜", protectedText.Placeholders);
        Assert.Contains("＞", protectedText.Placeholders);
        Assert.Contains("（", protectedText.Placeholders);
        Assert.Contains("）", protectedText.Placeholders);
        Assert.Contains("『", protectedText.Placeholders);
        Assert.Contains("』", protectedText.Placeholders);
        Assert.True(protector.HasAllTokens(protectedText.Text, protectedText.Placeholders, out _));

        var restored = protector.Restore(protectedText.Text, protectedText.Placeholders);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void Protect_UsesCustomFullWidthSpecialCharacters()
    {
        var protector = new PlaceholderProtector("（）");
        var original = "「문장」（괄호）";

        var protectedText = protector.Protect(original);

        Assert.DoesNotContain("「", protectedText.Placeholders);
        Assert.DoesNotContain("」", protectedText.Placeholders);
        Assert.Contains("（", protectedText.Placeholders);
        Assert.Contains("）", protectedText.Placeholders);
    }

    [Fact]
    public void Protect_AllowsEmptyFullWidthSpecialCharacterList()
    {
        var protector = new PlaceholderProtector(string.Empty);
        var original = "「문장」（괄호）";

        var protectedText = protector.Protect(original);

        Assert.Equal(original, protectedText.Text);
        Assert.Empty(protectedText.Placeholders);
    }

    [Fact]
    public void Protect_DoesNotTokenizeFullWidthSpaces()
    {
        var protector = new PlaceholderProtector();
        var original = "앞　　중간　끝";

        var protectedText = protector.Protect(original);

        Assert.Equal(original, protectedText.Text);
        Assert.Empty(protectedText.Placeholders);
        Assert.True(protector.HasAllTokens(protectedText.Text, protectedText.Placeholders, out _));

        var restored = protector.Restore(protectedText.Text, protectedText.Placeholders);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void Protect_PreservesErbCodeReferencesInsideTranslatableQuotedText()
    {
        var protector = new PlaceholderProtector();
        var original = "売春中;WORK_TYPE_TEXT:(CFLAG:index:労役種類) == `売春`";

        var protectedText = protector.Protect(original);

        Assert.Equal("売春中;__PH0__ == `売春`", protectedText.Text);
        Assert.Equal(["WORK_TYPE_TEXT:(CFLAG:index:労役種類)"], protectedText.Placeholders);
        Assert.True(protector.HasAllTokens(protectedText.Text, protectedText.Placeholders, out _));

        var restored = protector.Restore("매춘 중;__PH0__ == `매춘`", protectedText.Placeholders);
        Assert.Equal("매춘 중;WORK_TYPE_TEXT:(CFLAG:index:労役種類) == `매춘`", restored);
    }

    [Fact]
    public void Protect_PreservesStandaloneErbSymbolReferences()
    {
        var protector = new PlaceholderProtector();
        var original = "현재 값은 CFLAG:index:労役種類 입니다";

        var protectedText = protector.Protect(original);

        Assert.Equal("현재 값은 __PH0__ 입니다", protectedText.Text);
        Assert.Equal(["CFLAG:index:労役種類"], protectedText.Placeholders);
    }

    [Fact]
    public void HasAllTokens_FailsWhenPlaceholderOrderIsBroken()
    {
        var protector = new PlaceholderProtector();
        var protectedText = protector.Protect("%CALLNAME%와 %MASTERNAME%");

        var isValid = protector.HasAllTokens("__PH1__ 와 __PH0__", protectedText.Placeholders, out var error);

        Assert.False(isValid);
        Assert.Contains("손상", error, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeTokenCandidates_RepairsNearMissPlaceholderTokens()
    {
        var protector = new PlaceholderProtector();
        var placeholders = new[] { "「", "　", "」" };

        var normalized = protector.NormalizeTokenCandidates("__PH0__……그런 얼굴 하지 마.__PH1__도와달라고 할 생각은 없어.__PH2_", placeholders);

        Assert.Equal("__PH0__……그런 얼굴 하지 마.__PH1__도와달라고 할 생각은 없어.__PH2__", normalized);
        Assert.True(protector.HasAllTokens(normalized, placeholders, out _));
    }

    [Fact]
    public void Protect_PreservesHtmlEntities()
    {
        var protector = new PlaceholderProtector();
        var original = "A&amp;B &#123; &#xABCD; 설명";

        var protectedText = protector.Protect(original);

        Assert.Equal("A__PH0__B __PH1__ __PH2__ 설명", protectedText.Text);
        Assert.Equal(["&amp;", "&#123;", "&#xABCD;"], protectedText.Placeholders);
    }
}
