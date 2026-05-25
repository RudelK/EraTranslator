using Microsoft.Win32;

namespace EraTranslator.Services;

public sealed class EzTransXpInstallationService
{
    public EzTransXpInstallationInfo Detect(string? configuredInstallationPath = null)
    {
        var resolvedPath = ResolveInstallationPath(configuredInstallationPath);
        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            return EzTransXpInstallationInfo.Unavailable("EzTransXP 설치 경로를 찾지 못했습니다.");
        }

        try
        {
            resolvedPath = Path.GetFullPath(resolvedPath);
        }
        catch
        {
            return EzTransXpInstallationInfo.Unavailable("EzTransXP 설치 경로 형식이 올바르지 않습니다.");
        }

        if (!Directory.Exists(resolvedPath))
        {
            return EzTransXpInstallationInfo.Unavailable("EzTransXP 설치 폴더가 존재하지 않습니다.", resolvedPath);
        }

        var datPath = Path.Combine(resolvedPath, "Dat");
        if (!Directory.Exists(datPath))
        {
            return EzTransXpInstallationInfo.Unavailable("EzTransXP Dat 폴더를 찾지 못했습니다.", resolvedPath);
        }

        var enhancedEnginePath = Path.Combine(resolvedPath, "J2KEngineH.dll");
        var standardEnginePath = Path.Combine(resolvedPath, "J2KEngine.dll");
        var enginePath = File.Exists(enhancedEnginePath)
            ? enhancedEnginePath
            : File.Exists(standardEnginePath)
                ? standardEnginePath
                : string.Empty;

        if (string.IsNullOrWhiteSpace(enginePath))
        {
            return EzTransXpInstallationInfo.Unavailable("EzTransXP 엔진 DLL(J2KEngine.dll)을 찾지 못했습니다.", resolvedPath);
        }

        var executablePath = Path.Combine(resolvedPath, "ezTrans.exe");
        return new EzTransXpInstallationInfo(
            true,
            "EzTransXP 설치를 확인했습니다.",
            resolvedPath,
            datPath,
            enginePath,
            File.Exists(executablePath) ? executablePath : string.Empty,
            string.Equals(Path.GetFileName(enginePath), "J2KEngineH.dll", StringComparison.OrdinalIgnoreCase));
    }

    public string ResolveInstallationPath(string? configuredInstallationPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredInstallationPath))
        {
            return configuredInstallationPath.Trim();
        }

        var registryDirectory = Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\ChangShin\ezTrans", "FilePath", null) as string;
        if (!string.IsNullOrWhiteSpace(registryDirectory))
        {
            return registryDirectory;
        }

        var registryExecutable = Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\ChangShin\ezTrans", "FileName", null) as string;
        if (!string.IsNullOrWhiteSpace(registryExecutable))
        {
            return Path.GetDirectoryName(registryExecutable) ?? string.Empty;
        }

        return string.Empty;
    }
}

public sealed record EzTransXpInstallationInfo(
    bool IsAvailable,
    string StatusText,
    string InstallationPath,
    string DatPath,
    string EnginePath,
    string ExecutablePath,
    bool UsesEnhancedEngine)
{
    public static EzTransXpInstallationInfo Unavailable(string statusText, string installationPath = "")
    {
        return new EzTransXpInstallationInfo(
            false,
            statusText,
            installationPath,
            string.Empty,
            string.Empty,
            string.Empty,
            false);
    }
}
