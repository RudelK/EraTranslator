using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class InlineSymbolReferenceRewriterTests
{
    [Fact]
    public void Rewrite_UsesStringLookupMapForGetNumCommandForm()
    {
        var rewriter = new InlineSymbolReferenceRewriter();
        var renameMap = new Dictionary<(string Namespace, string OriginalKey), string>
        {
            [("CFLAG", "依存度")] = "12",
        };
        var stringLookupRenameMap = new Dictionary<(string Namespace, string OriginalKey), string>
        {
            [("CFLAG", "依存度")] = "의존도",
        };

        var result = rewriter.Rewrite(
            "GETNUM CFLAG, \"依存度\"",
            renameMap,
            stringLookupRenameMap);

        Assert.Equal("GETNUM CFLAG, \"의존도\"", result);
    }
}
