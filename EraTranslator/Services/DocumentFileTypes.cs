namespace EraTranslator.Services;

public static class DocumentFileTypes
{
    public const string Csv = "CSV";
    public const string Erd = "ERD";
    public const string Erb = "ERB";
    public const string Erh = "ERH";

    public static bool IsCsvLike(string fileType)
    {
        return string.Equals(fileType, Csv, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileType, Erd, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsErbLike(string fileType)
    {
        return string.Equals(fileType, Erb, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileType, Erh, StringComparison.OrdinalIgnoreCase);
    }

    public static bool SupportsJosaRewrite(string fileType)
    {
        return string.Equals(fileType, Erb, StringComparison.OrdinalIgnoreCase);
    }
}
