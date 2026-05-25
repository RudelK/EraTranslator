using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class ProviderPlaceholderMarkerTests
{
    [Fact]
    public void DeepLMarker_RoundTripsPlaceholderTokens()
    {
        var placeholders = new[] { "%CALLNAME%", @"\n" };
        var protectedText = "안녕 __PH0__ 테스트 __PH1__";

        var marked = ProviderPlaceholderMarker.MarkForDeepL(protectedText, placeholders);

        Assert.DoesNotContain("__PH0__", marked, StringComparison.Ordinal);
        Assert.Contains("<era-ph idx=\"0\"/>", marked, StringComparison.Ordinal);

        var restored = ProviderPlaceholderMarker.UnmarkFromDeepL(marked, placeholders);
        Assert.Equal(protectedText, restored);
    }

    [Fact]
    public void PapagoMarker_RoundTripsPlaceholderTokens()
    {
        var placeholders = new[] { "%CALLNAME%" };
        var protectedText = "안녕 __PH0__";

        var marked = ProviderPlaceholderMarker.MarkForPapago(protectedText, placeholders);
        Assert.Contains("ERAPHTOKEN0SAFE", marked, StringComparison.Ordinal);

        var restored = ProviderPlaceholderMarker.UnmarkFromPapago(marked, placeholders);
        Assert.Equal(protectedText, restored);
    }
}
