namespace EraTranslator.Services;

public static class SensitiveDataMasker
{
    public static string MaskSecret(string value, int visiblePrefixLength = 4)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (value.Length <= 8)
        {
            return "****";
        }

        var prefixLength = Math.Clamp(visiblePrefixLength, 1, Math.Max(1, value.Length - 4));
        return $"{value[..prefixLength]}****{value[^4..]}";
    }
}
