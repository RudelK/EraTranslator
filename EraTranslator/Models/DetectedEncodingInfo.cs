using System.Text;

namespace EraTranslator.Models;

public sealed class DetectedEncodingInfo
{
    public required Encoding Encoding { get; init; }

    public required string Name { get; init; }

    public required DetectedEncodingKind Kind { get; init; }

    public bool HasBom { get; init; }

    public bool CanConvertToUtf8Bom => Kind is DetectedEncodingKind.ShiftJis or DetectedEncodingKind.EucJp;
}
