using System.Text;

namespace EraTranslator.Services;

public sealed class EncodingDetector
{
    private static readonly UTF8Encoding Utf8BomEncoding = new(true);
    private static readonly UTF8Encoding Utf8NoBomEncoding = new(false, true);
    private static readonly UTF8Encoding Utf8FallbackEncoding = new(false, false);
    private static readonly UnicodeEncoding Utf16LeEncoding = new(false, true, true);

    public DetectedEncodingInfo Detect(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Create(Utf8BomEncoding, "UTF-8 BOM", DetectedEncodingKind.Utf8Bom, true);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Create(Utf16LeEncoding, "UTF-16 LE", DetectedEncodingKind.Unicode, true);
        }

        if (IsValidUtf8(bytes))
        {
            return Create(Utf8NoBomEncoding, "UTF-8", DetectedEncodingKind.Utf8, false);
        }

        var shiftJis = TryDecode(bytes, 932);
        var eucJp = TryDecode(bytes, 51932);

        if (shiftJis.score >= eucJp.score && shiftJis.text is not null)
        {
            return Create(Encoding.GetEncoding(932), "Shift-JIS", DetectedEncodingKind.ShiftJis, false);
        }

        if (eucJp.text is not null)
        {
            return Create(Encoding.GetEncoding(51932), "EUC-JP", DetectedEncodingKind.EucJp, false);
        }

        return Create(Utf8FallbackEncoding, "UTF-8", DetectedEncodingKind.Unknown, false);
    }

    private static DetectedEncodingInfo Create(Encoding encoding, string name, DetectedEncodingKind kind, bool hasBom)
    {
        return new DetectedEncodingInfo
        {
            Encoding = encoding,
            Name = name,
            Kind = kind,
            HasBom = hasBom,
        };
    }

    private static bool IsValidUtf8(byte[] bytes)
    {
        try
        {
            _ = Utf8NoBomEncoding.GetString(bytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static (string? text, int score) TryDecode(byte[] bytes, int codePage)
    {
        try
        {
            var encoding = Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            var text = encoding.GetString(bytes);
            var score = ScoreDecodedText(text);
            return (text, score);
        }
        catch
        {
            return (null, int.MinValue);
        }
    }

    private static int ScoreDecodedText(string text)
    {
        var score = 0;

        foreach (var ch in text)
        {
            if (char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t')
            {
                score -= 5;
                continue;
            }

            if (ch is >= '\u3040' and <= '\u30ff')
            {
                score += 3;
                continue;
            }

            if (ch is >= '\u4e00' and <= '\u9fff')
            {
                score += 2;
                continue;
            }

            if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch) || char.IsPunctuation(ch))
            {
                score += 1;
            }
        }

        return score;
    }
}
