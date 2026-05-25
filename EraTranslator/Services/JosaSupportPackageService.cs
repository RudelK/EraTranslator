using System.Text.RegularExpressions;
using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed partial class JosaSupportPackageService
{
    private static readonly string[] RequiredFunctionNames =
    [
        "@조사선택",
        "@조사만선택",
        "@조사처리",
        "@조사만처리",
    ];

    private static readonly string[] RequiredMacroNames =
    [
        "#DEFINE 플레이어는",
        "#DEFINE 타겟은",
        "#DEFINE ARG은",
        "#DEFINE 조교자는",
    ];

    private static readonly string[] DefaultSupportedParticles =
    [
        "는", "이", "을", "과", "으로", "랑", "며", "고", "라", "다",
        "였", "여", "야", "이나", "이면", "의", "에게",
    ];

    public JosaSupportPackageInfo InspectProject(string gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            return new JosaSupportPackageInfo
            {
                SupportedParticles = DefaultSupportedParticles,
            };
        }

        var erbPath = Directory.EnumerateFiles(gameRoot, "ZNAME.ERB", SearchOption.AllDirectories).FirstOrDefault() ?? string.Empty;
        var erhPath = Directory.EnumerateFiles(gameRoot, "ZNAME.ERH", SearchOption.AllDirectories).FirstOrDefault() ?? string.Empty;
        var erbText = File.Exists(erbPath) ? File.ReadAllText(erbPath) : string.Empty;
        var erhText = File.Exists(erhPath) ? File.ReadAllText(erhPath) : string.Empty;
        var hasIncludeLinkage = Directory.EnumerateFiles(gameRoot, "*.*", SearchOption.AllDirectories)
            .Where(path =>
            {
                var extension = Path.GetExtension(path);
                return extension.Equals(".erb", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".era", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".erh", StringComparison.OrdinalIgnoreCase);
            })
            .Any(path => IncludePattern().IsMatch(File.ReadAllText(path)));

        return new JosaSupportPackageInfo
        {
            ErbExists = File.Exists(erbPath),
            ErhExists = File.Exists(erhPath),
            ErbPath = erbPath,
            ErhPath = erhPath,
            HasFunctionSignatures = RequiredFunctionNames.All(name => erbText.Contains(name, StringComparison.Ordinal)),
            HasMacroDefines = RequiredMacroNames.All(name => erhText.Contains(name, StringComparison.Ordinal)),
            SupportsLBatchimRoroException = erbText.Contains("% 28 == 8", StringComparison.Ordinal),
            SupportsImplicitYiFallback = erbText.Contains("조사처리 불필요 조사 우선처리", StringComparison.Ordinal)
                || erbText.Contains("무조건 조사처리", StringComparison.Ordinal),
            SupportsParticlePassThrough = erbText.Contains("\"의\"", StringComparison.Ordinal)
                && erbText.Contains("\"에게\"", StringComparison.Ordinal),
            SupportsMacroDefines = !string.IsNullOrWhiteSpace(erhText),
            HasErhIncludeLinkage = hasIncludeLinkage,
            SupportedParticles = DefaultSupportedParticles,
        };
    }

    public (string erbText, string erhText) LoadBundledPackage()
    {
        var assetRoot = ResolveAssetRoot();
        var erbPath = Path.Combine(assetRoot, "ZNAME.ERB");
        var erhPath = Path.Combine(assetRoot, "ZNAME.ERH");
        if (!File.Exists(erbPath) || !File.Exists(erhPath))
        {
            throw new FileNotFoundException("번들 ZNAME 패키지를 찾지 못했습니다.");
        }

        return (File.ReadAllText(erbPath), File.ReadAllText(erhPath));
    }

    public string GetDefaultErbTargetPath(string rootDirectory)
    {
        return Path.Combine(rootDirectory, "ERB", "ZNAME.ERB");
    }

    public string GetDefaultErhTargetPath(string rootDirectory)
    {
        return Path.Combine(rootDirectory, "ERB", "ZNAME.ERH");
    }

    public bool HasErhInclude(string content)
    {
        return IncludePattern().IsMatch(content);
    }

    private static string ResolveAssetRoot()
    {
        var direct = Path.Combine(AppContext.BaseDirectory, "Assets");
        if (Directory.Exists(direct))
        {
            return direct;
        }

        var current = AppContext.BaseDirectory;
        for (var depth = 0; depth < 8 && current is not null; depth++)
        {
            var candidate = Path.Combine(current, "EraTranslator", "Assets");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        return direct;
    }

    [GeneratedRegex("#INCLUDE\\s+\"ZNAME\\.ERH\"", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex IncludePattern();
}
