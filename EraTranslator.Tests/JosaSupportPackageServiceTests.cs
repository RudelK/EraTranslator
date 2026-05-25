using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class JosaSupportPackageServiceTests
{
    [Fact]
    public void InspectProject_DetectsLatestPackageAndErhInclude()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(tempRoot, "game");
        var erbDir = Path.Combine(gameRoot, "ERB");
        Directory.CreateDirectory(erbDir);

        var service = new JosaSupportPackageService();
        var bundled = service.LoadBundledPackage();
        File.WriteAllText(Path.Combine(erbDir, "ZNAME.ERB"), bundled.erbText);
        File.WriteAllText(Path.Combine(erbDir, "ZNAME.ERH"), bundled.erhText);
        File.WriteAllText(Path.Combine(erbDir, "Main.ERB"), "#INCLUDE \"ZNAME.ERH\"\r\nPRINTFORMW %플레이어는%");

        var info = service.InspectProject(gameRoot);

        Assert.True(info.ErbExists);
        Assert.True(info.ErhExists);
        Assert.True(info.HasFunctionSignatures);
        Assert.True(info.HasMacroDefines);
        Assert.True(info.HasErhIncludeLinkage);
        Assert.Equal("최신 ZNAME 패키지 호환", info.CompatibilityStatus);
    }
}
