using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class ErbReferenceExtractorTests
{
    [Fact]
    public void Extract_FindsDirectAndIndirectSymbolReferences()
    {
        var extractor = new ErbReferenceExtractor();
        var content = """
IF CFLAG:外見年齢
PRINTFORML %CSTR:あなた呼び方／陥落前%
flagName = "外見年齢"
IF CFLAG:{flagName}
""";

        var result = extractor.Extract("ERB/Test.ERB", content);

        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "CFLAG"
            && reference.OriginalKey == "外見年齢");
        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.DirectLiteral
            && reference.Namespace == "CSTR"
            && reference.OriginalKey == "あなた呼び方／陥落前");
        Assert.Contains(result.references, reference =>
            reference.Kind == ErbSymbolReferenceKind.IndirectVariable
            && reference.Namespace == "CFLAG"
            && reference.VariableName == "flagName"
            && reference.ResolutionKind == SymbolReferenceResolutionKind.Resolved
            && reference.CandidateKeys.Contains("外見年齢", StringComparer.Ordinal));
        Assert.Contains(result.variableLiterals, occurrence =>
            occurrence.VariableName == "flagName"
            && occurrence.LiteralValue == "外見年齢"
            && occurrence.IsExactValue);
    }
}
