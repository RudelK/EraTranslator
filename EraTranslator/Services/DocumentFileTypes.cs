namespace EraTranslator.Services;

public static class DocumentFileTypes
{
    public const string Csv = "CSV";
    public const string Erb = "ERB";
    public const string Erh = "ERH";

    public static bool IsCsvLike(string fileType)
    {
        return string.Equals(fileType, Csv, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsErbLike(string fileType)
    {
        return string.Equals(fileType, Erb, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileType, Erh, StringComparison.OrdinalIgnoreCase);
    }
}
