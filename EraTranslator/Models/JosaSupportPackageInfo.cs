namespace EraTranslator.Models;

public sealed class JosaSupportPackageInfo
{
    public bool ErbExists { get; init; }

    public bool ErhExists { get; init; }

    public string ErbPath { get; init; } = string.Empty;

    public string ErhPath { get; init; } = string.Empty;

    public bool HasFunctionSignatures { get; init; }

    public bool HasMacroDefines { get; init; }

    public bool SupportsLBatchimRoroException { get; init; }

    public bool SupportsImplicitYiFallback { get; init; }

    public bool SupportsParticlePassThrough { get; init; }

    public bool SupportsMacroDefines { get; init; }

    public bool HasErhIncludeLinkage { get; init; }

    public IReadOnlyList<string> SupportedParticles { get; init; } = [];

    public string CompatibilityStatus
    {
        get
        {
            if (!ErbExists && !ErhExists)
            {
                return "최신 ZNAME 패키지 없음";
            }

            if (!HasFunctionSignatures || !HasMacroDefines)
            {
                return "구버전 또는 불완전 ZNAME 패키지";
            }

            return HasErhIncludeLinkage
                ? "최신 ZNAME 패키지 호환"
                : "최신 ZNAME 패키지 호환 / ERH 연결 확인 필요";
        }
    }
}

public sealed class JosaDocumentAnalysis
{
    public int PatternCount { get; init; }

    public int AutoConvertibleCount { get; init; }

    public int GenericFunctionCount { get; init; }

    public int MacroPatternCount { get; init; }

    public int LegacyShorthandCount { get; init; }

    public bool RequiresErh { get; init; }

    public bool ErhLinked { get; init; }

    public string SyntaxType { get; init; } = "없음";

    public string ErhLinkStatus { get; init; } = "불필요";

    public string PackageCompatibilityStatus { get; init; } = string.Empty;
}
