namespace EraTranslator.Services;

public static class LanguageDisplayService
{
    public static string ToInstructionLabel(string languageCodeOrName)
    {
        if (string.IsNullOrWhiteSpace(languageCodeOrName))
        {
            return "Korean (ko)";
        }

        var normalized = languageCodeOrName.Trim().ToLowerInvariant();
        return normalized switch
        {
            "ko" or "ko-kr" or "korean" or "한국어" => "Korean (ko, 한국어)",
            "ja" or "ja-jp" or "japanese" or "日本語" => "Japanese (ja, 日本語)",
            "en" or "en-us" or "en-gb" or "english" => "English (en)",
            "zh" or "zh-cn" or "zh-tw" or "chinese" => "Chinese (zh)",
            _ => languageCodeOrName,
        };
    }
}
