namespace EraTranslator.Models;

public enum LmStudioPresetProfile
{
    Auto,
    Gemma4,
    Qwen35_9B,
    TranslateGemma,
    HyMt2_7B,
    HyMt2_30B_A3B,
}

public sealed class LmStudioPresetOption
{
    public LmStudioPresetProfile Profile { get; init; }

    public string DisplayName { get; init; } = string.Empty;
}
