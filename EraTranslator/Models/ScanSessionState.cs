using System.Text;

namespace EraTranslator.Models;

public sealed class ScanSessionState
{
    public string GameRoot { get; init; } = string.Empty;

    public JosaSupportPackageInfoState JosaPackageInfo { get; init; } = new();

    public List<SourceFileDocumentState> Documents { get; init; } = [];

    public List<ExtractedTextItemState> Items { get; init; } = [];

    public Dictionary<string, int> Metrics { get; init; } = [];
}

public sealed class SourceFileDocumentState
{
    public string DocumentId { get; init; } = string.Empty;

    public string FullPath { get; init; } = string.Empty;

    public string RelativePath { get; init; } = string.Empty;

    public string FileType { get; init; } = string.Empty;

    public string OriginalText { get; init; } = string.Empty;

    public DetectedEncodingInfoState EncodingInfo { get; init; } = new();

    public string NewLineSequence { get; init; } = Environment.NewLine;

    public CsvDocumentKind CsvKind { get; init; }

    public List<TextSegmentState> Segments { get; init; } = [];

    public List<ErbSymbolReferenceState> SymbolReferences { get; init; } = [];

    public List<ErbVariableLiteralOccurrenceState> VariableLiteralOccurrences { get; init; } = [];

    public List<ErbIdentifierOccurrenceState> IdentifierOccurrences { get; init; } = [];

    public List<string> ScanWarnings { get; init; } = [];

    public JosaDocumentAnalysisState JosaAnalysis { get; init; } = new();
}

public sealed class JosaSupportPackageInfoState
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

    public List<string> SupportedParticles { get; init; } = [];
}

public sealed class JosaDocumentAnalysisState
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

public sealed class DetectedEncodingInfoState
{
    public int CodePage { get; init; } = Encoding.UTF8.CodePage;

    public string Name { get; init; } = "utf-8";

    public DetectedEncodingKind Kind { get; init; }

    public bool HasBom { get; init; }
}

public sealed class TextSegmentState
{
    public string SegmentId { get; init; } = string.Empty;

    public string DocumentId { get; init; } = string.Empty;

    public string SegmentType { get; init; } = string.Empty;

    public int AbsoluteStart { get; init; }

    public int Length { get; init; }

    public int LineNumber { get; init; }

    public string OriginalText { get; init; } = string.Empty;

    public int? FieldIndex { get; init; }

    public string? SourceKey { get; init; }

    public CsvFieldRole CsvFieldRole { get; init; }

    public bool PreserveWhitespace { get; init; }

    public string SymbolNamespace { get; init; } = string.Empty;

    public string OriginalSymbolKey { get; init; } = string.Empty;

    public bool IsReferenceBearingKey { get; init; }
}

public sealed class ErbSymbolReferenceState
{
    public string DocumentId { get; init; } = string.Empty;

    public string Namespace { get; init; } = string.Empty;

    public ErbSymbolReferenceKind Kind { get; init; }

    public SymbolReferenceResolutionKind ResolutionKind { get; init; }

    public string OriginalKey { get; init; } = string.Empty;

    public string VariableName { get; init; } = string.Empty;

    public string ExpressionText { get; init; } = string.Empty;

    public int AbsoluteStart { get; init; }

    public int Length { get; init; }

    public int LineNumber { get; init; }

    public List<string> CandidateKeys { get; init; } = [];
}

public sealed class ErbVariableLiteralOccurrenceState
{
    public string DocumentId { get; init; } = string.Empty;

    public string VariableName { get; init; } = string.Empty;

    public string LiteralValue { get; init; } = string.Empty;

    public int AbsoluteStart { get; init; }

    public int Length { get; init; }

    public int LineNumber { get; init; }

    public bool IsExactValue { get; init; }
}

public sealed class ErbIdentifierOccurrenceState
{
    public string DocumentId { get; init; } = string.Empty;

    public ErbIdentifierKind Kind { get; init; }

    public ErbIdentifierRole Role { get; init; }

    public string OriginalName { get; init; } = string.Empty;

    public int AbsoluteStart { get; init; }

    public int Length { get; init; }

    public int LineNumber { get; init; }
}

public sealed class ExtractedTextItemState
{
    public string SegmentId { get; init; } = string.Empty;

    public string DocumentId { get; init; } = string.Empty;

    public string FileType { get; init; } = string.Empty;

    public string RelativePath { get; init; } = string.Empty;

    public string EncodingName { get; init; } = string.Empty;

    public string SegmentType { get; init; } = string.Empty;

    public int LineNumber { get; init; }

    public string OriginalText { get; init; } = string.Empty;

    public string? SourceKey { get; init; }

    public int? FieldIndex { get; init; }

    public CsvFieldRole CsvFieldRole { get; init; }

    public bool PreserveWhitespace { get; init; }

    public string WarningText { get; init; } = string.Empty;

    public string SymbolNamespace { get; init; } = string.Empty;

    public string OriginalSymbolKey { get; init; } = string.Empty;

    public bool IsReferenceBearingKey { get; init; }

    public int ReferenceImpactCount { get; init; }

    public bool RequiresReferenceRewrite { get; init; }

    public string ReferenceResolutionStatus { get; init; } = string.Empty;
}
