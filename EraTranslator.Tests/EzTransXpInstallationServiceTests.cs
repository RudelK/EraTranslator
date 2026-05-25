using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class EzTransXpInstallationServiceTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    [Fact]
    public void Detect_ReturnsAvailableForPortableInstallationLayout()
    {
        var installPath = CreateInstallationLayout(includeDat: true, includeEngine: true, enhancedEngine: true);
        var service = new EzTransXpInstallationService();

        var result = service.Detect(installPath);

        Assert.True(result.IsAvailable);
        Assert.Equal(installPath, result.InstallationPath);
        Assert.Equal(Path.Combine(installPath, "Dat"), result.DatPath);
        Assert.Equal(Path.Combine(installPath, "J2KEngineH.dll"), result.EnginePath);
        Assert.True(result.UsesEnhancedEngine);
    }

    [Fact]
    public void Detect_ReturnsUnavailableWhenDatFolderMissing()
    {
        var installPath = CreateInstallationLayout(includeDat: false, includeEngine: true, enhancedEngine: false);
        var service = new EzTransXpInstallationService();

        var result = service.Detect(installPath);

        Assert.False(result.IsAvailable);
        Assert.Contains("Dat", result.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void Detect_ReturnsUnavailableWhenEngineMissing()
    {
        var installPath = CreateInstallationLayout(includeDat: true, includeEngine: false, enhancedEngine: false);
        var service = new EzTransXpInstallationService();

        var result = service.Detect(installPath);

        Assert.False(result.IsAvailable);
        Assert.Contains("엔진 DLL", result.StatusText, StringComparison.Ordinal);
    }

    private string CreateInstallationLayout(bool includeDat, bool includeEngine, bool enhancedEngine)
    {
        var installPath = Path.Combine(_rootPath, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installPath);

        if (includeDat)
        {
            Directory.CreateDirectory(Path.Combine(installPath, "Dat"));
        }

        if (includeEngine)
        {
            var engineName = enhancedEngine ? "J2KEngineH.dll" : "J2KEngine.dll";
            File.WriteAllText(Path.Combine(installPath, engineName), "stub");
        }

        return installPath;
    }
}
