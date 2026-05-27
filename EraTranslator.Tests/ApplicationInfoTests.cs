using EraTranslator;

namespace EraTranslator.Tests;

public sealed class ApplicationInfoTests
{
    [Fact]
    public void WindowTitle_IncludesInitialVersion()
    {
        Assert.Equal("0.5", ApplicationInfo.Version);
        Assert.Equal("EraTranslator 0.5", ApplicationInfo.WindowTitle);
    }
}
