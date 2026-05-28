namespace EraTranslator.Services;

internal static class KoreanParticleClassifier
{
    internal static TokenBatchimKind ClassifyToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return TokenBatchimKind.None;
        }

        var lastCharacter = token[^1];
        if (lastCharacter is >= '0' and <= '9')
        {
            return lastCharacter is '2' or '4' or '5' or '9'
                ? TokenBatchimKind.None
                : TokenBatchimKind.HasBatchim;
        }

        if (lastCharacter is < '\uAC00' or > '\uD7A3')
        {
            return TokenBatchimKind.HasBatchim;
        }

        var jongseong = (lastCharacter - '\uAC00') % 28;
        return jongseong switch
        {
            0 => TokenBatchimKind.None,
            8 => TokenBatchimKind.RieulBatchim,
            _ => TokenBatchimKind.HasBatchim,
        };
    }

    internal static bool IsLiteralTokenCharacter(char character)
    {
        return character is >= '\uAC00' and <= '\uD7A3'
            || character is >= '0' and <= '9';
    }
}

internal enum TokenBatchimKind
{
    None,
    HasBatchim,
    RieulBatchim,
}
