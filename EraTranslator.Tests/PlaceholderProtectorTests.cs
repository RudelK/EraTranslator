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
        var original = "／【테스트】＜값＞「문장」％";

        var protectedText = protector.Protect(original);

        Assert.True(protectedText.Placeholders.Count >= 7);
        Assert.True(protector.HasAllTokens(protectedText.Text, protectedText.Placeholders, out _));

        var restored = protector.Restore(protectedText.Text, protectedText.Placeholders);
        Assert.Equal(original, restored);
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
}
