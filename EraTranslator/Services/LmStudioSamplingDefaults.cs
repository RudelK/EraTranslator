using EraTranslator.Models;

namespace EraTranslator.Services;

internal enum LmStudioModelFamily
{
    Unknown,
    Gemma,
    Gemma4E4B,
    Qwen,
    TranslateGemma,
    HyMt2,
}

internal enum LmStudioThinkingControlMode
{
    None,
    ApiCustomField,
    PromptFallback,
}

internal readonly record struct LmStudioSamplingPreset(
    double Temperature,
    double? TopP,
    int? TopK,
    double? RepeatPenalty,
    double? PresencePenalty);

internal static class LmStudioSamplingDefaults
{
    public static LmStudioSamplingPreset GemmaPreset { get; } = new(
        Temperature: 0.2,
        TopP: 0.9,
        TopK: 40,
        RepeatPenalty: 1.10,
        PresencePenalty: null);

    public static LmStudioSamplingPreset Gemma4E4BPreset { get; } = new(
        Temperature: 0.1,
        TopP: 0.85,
        TopK: 32,
        RepeatPenalty: 1.05,
        PresencePenalty: null);

    public static LmStudioSamplingPreset QwenNonThinkingPreset { get; } = new(
        Temperature: 0.7,
        TopP: 0.8,
        TopK: 20,
        RepeatPenalty: 1.0,
        PresencePenalty: 1.5);

    public static LmStudioSamplingPreset QwenThinkingPreset { get; } = new(
        Temperature: 1.0,
        TopP: 0.95,
        TopK: 20,
        RepeatPenalty: null,
        PresencePenalty: 1.5);

    public static LmStudioSamplingPreset TranslateGemmaPreset { get; } = new(
        Temperature: 0.2,
        TopP: 0.9,
        TopK: 40,
        RepeatPenalty: 1.05,
        PresencePenalty: null);

    public static LmStudioSamplingPreset HyMt2_7BPreset { get; } = new(
        Temperature: 0.7,
        TopP: 0.6,
        TopK: 20,
        RepeatPenalty: 1.05,
        PresencePenalty: null);

    public static LmStudioSamplingPreset HyMt2_30B_A3BPreset { get; } = new(
        Temperature: 0.7,
        TopP: 1.0,
        TopK: -1,
        RepeatPenalty: 1.0,
        PresencePenalty: null);

    public static LmStudioModelFamily DetectModelFamily(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return LmStudioModelFamily.Unknown;
        }

        var normalized = model.Trim();
        if (normalized.Contains("gemma-4-e4b", StringComparison.OrdinalIgnoreCase))
        {
            return LmStudioModelFamily.Gemma4E4B;
        }

        if (normalized.Contains("translategemma", StringComparison.OrdinalIgnoreCase))
        {
            return LmStudioModelFamily.TranslateGemma;
        }

        if (normalized.Contains("hy-mt2", StringComparison.OrdinalIgnoreCase))
        {
            return LmStudioModelFamily.HyMt2;
        }

        if (normalized.Contains("qwen", StringComparison.OrdinalIgnoreCase))
        {
            return LmStudioModelFamily.Qwen;
        }

        if (normalized.Contains("gemma", StringComparison.OrdinalIgnoreCase))
        {
            return LmStudioModelFamily.Gemma;
        }

        return LmStudioModelFamily.Unknown;
    }

    public static LmStudioModelFamily GetModelFamily(LmStudioPresetProfile profile, string? model)
    {
        return profile switch
        {
            LmStudioPresetProfile.Gemma4 => LmStudioModelFamily.Gemma,
            LmStudioPresetProfile.Gemma4E4B => LmStudioModelFamily.Gemma4E4B,
            LmStudioPresetProfile.Qwen35_9B => LmStudioModelFamily.Qwen,
            LmStudioPresetProfile.TranslateGemma => LmStudioModelFamily.TranslateGemma,
            LmStudioPresetProfile.HyMt2_7B => LmStudioModelFamily.HyMt2,
            LmStudioPresetProfile.HyMt2_30B_A3B => LmStudioModelFamily.HyMt2,
            _ => DetectModelFamily(model),
        };
    }

    public static LmStudioPresetProfile DetectGemmaPresetProfile(string? model)
    {
        var normalized = model?.Trim() ?? string.Empty;
        return normalized.Contains("gemma-4-e4b", StringComparison.OrdinalIgnoreCase)
            ? LmStudioPresetProfile.Gemma4E4B
            : LmStudioPresetProfile.Gemma4;
    }

    public static LmStudioPresetProfile DetectHyMt2PresetProfile(string? model)
    {
        var normalized = model?.Trim() ?? string.Empty;
        if (normalized.Contains("30b-a3b", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("30b", StringComparison.OrdinalIgnoreCase))
        {
            return LmStudioPresetProfile.HyMt2_30B_A3B;
        }

        return LmStudioPresetProfile.HyMt2_7B;
    }

    public static LmStudioSamplingPreset GetRecommendedPreset(string? model, bool disableThinking)
    {
        return GetRecommendedPreset(LmStudioPresetProfile.Auto, model, disableThinking);
    }

    public static LmStudioSamplingPreset GetRecommendedPreset(
        LmStudioPresetProfile profile,
        string? model,
        bool disableThinking)
    {
        return GetModelFamily(profile, model) switch
        {
            LmStudioModelFamily.Qwen => disableThinking ? QwenNonThinkingPreset : QwenThinkingPreset,
            LmStudioModelFamily.Gemma => GemmaPreset,
            LmStudioModelFamily.Gemma4E4B => Gemma4E4BPreset,
            LmStudioModelFamily.TranslateGemma => TranslateGemmaPreset,
            LmStudioModelFamily.HyMt2 when profile == LmStudioPresetProfile.HyMt2_30B_A3B => HyMt2_30B_A3BPreset,
            LmStudioModelFamily.HyMt2 when profile == LmStudioPresetProfile.HyMt2_7B => HyMt2_7BPreset,
            LmStudioModelFamily.HyMt2 => DetectHyMt2PresetProfile(model) == LmStudioPresetProfile.HyMt2_30B_A3B
                ? HyMt2_30B_A3BPreset
                : HyMt2_7BPreset,
            _ => GemmaPreset,
        };
    }

    public static LmStudioThinkingControlMode GetThinkingControlMode(string? model, bool disableThinking)
    {
        if (!disableThinking)
        {
            return LmStudioThinkingControlMode.None;
        }

        return DetectModelFamily(model) is LmStudioModelFamily.Qwen or LmStudioModelFamily.Gemma or LmStudioModelFamily.Gemma4E4B
            ? LmStudioThinkingControlMode.ApiCustomField
            : LmStudioThinkingControlMode.PromptFallback;
    }

    public static int GetRecommendedStructuredMaxTokens(string? model, int requestCount)
    {
        var family = DetectModelFamily(model);
        if (family != LmStudioModelFamily.Qwen)
        {
            return 0;
        }

        if (requestCount <= 1)
        {
            return 160;
        }

        return Math.Clamp(120 + (requestCount * 80), 240, 2048);
    }

    public static bool MatchesPresetValue(double? currentValue, double? presetValue)
    {
        if (!currentValue.HasValue && !presetValue.HasValue)
        {
            return true;
        }

        if (!currentValue.HasValue || !presetValue.HasValue)
        {
            return false;
        }

        return Math.Abs(currentValue.Value - presetValue.Value) < 0.0001;
    }

    public static bool MatchesPresetValue(int? currentValue, int? presetValue)
    {
        return currentValue == presetValue;
    }

    public static bool MatchesPreset(ProviderSettings settings, string? model, bool disableThinking)
    {
        var preset = GetRecommendedPreset(model, disableThinking);
        return Math.Abs(settings.Temperature - preset.Temperature) < 0.0001
            && MatchesPresetValue(settings.TopP, preset.TopP)
            && MatchesPresetValue(settings.TopK, preset.TopK)
            && MatchesPresetValue(settings.RepeatPenalty, preset.RepeatPenalty)
            && MatchesPresetValue(settings.PresencePenalty, preset.PresencePenalty);
    }

    public static int? GetRecommendedMaxTokens(
        LmStudioPresetProfile profile,
        string? model)
    {
        return GetModelFamily(profile, model) == LmStudioModelFamily.HyMt2 ? 4096 : null;
    }

    public static string BuildPresetSummary(string? model, bool disableThinking)
    {
        return BuildPresetSummary(LmStudioPresetProfile.Auto, model, disableThinking);
    }

    public static string BuildPresetSummary(
        LmStudioPresetProfile profile,
        string? model,
        bool disableThinking)
    {
        var family = GetModelFamily(profile, model);
        var preset = GetRecommendedPreset(profile, model, disableThinking);
        var familyLabel = family switch
        {
            LmStudioModelFamily.Qwen when profile == LmStudioPresetProfile.Auto => disableThinking ? "Qwen non-thinking (auto)" : "Qwen thinking/general (auto)",
            LmStudioModelFamily.Qwen => disableThinking ? "Qwen 3.5 9B non-thinking" : "Qwen 3.5 9B thinking/general",
            LmStudioModelFamily.Gemma when profile == LmStudioPresetProfile.Auto => DetectGemmaPresetProfile(model) == LmStudioPresetProfile.Gemma4E4B ? "Gemma 4 E4B (auto)" : "Gemma (auto)",
            LmStudioModelFamily.Gemma => "Gemma 4",
            LmStudioModelFamily.Gemma4E4B when profile == LmStudioPresetProfile.Auto => "Gemma 4 E4B (auto)",
            LmStudioModelFamily.Gemma4E4B => "Gemma 4 E4B",
            LmStudioModelFamily.TranslateGemma when profile == LmStudioPresetProfile.Auto => "TranslateGemma (auto)",
            LmStudioModelFamily.TranslateGemma => "TranslateGemma",
            LmStudioModelFamily.HyMt2 when profile == LmStudioPresetProfile.Auto => DetectHyMt2PresetProfile(model) == LmStudioPresetProfile.HyMt2_30B_A3B ? "Hy-MT2 30B-A3B (auto)" : "Hy-MT2 7B (auto)",
            LmStudioModelFamily.HyMt2 when profile == LmStudioPresetProfile.HyMt2_30B_A3B => "Hy-MT2 30B-A3B",
            LmStudioModelFamily.HyMt2 => "Hy-MT2 7B",
            _ => "Gemma-style",
        };

        return $"현재 권장 시작값({familyLabel}): Temperature {preset.Temperature:0.##}, Top P {FormatNullableDouble(preset.TopP)}, Top K {FormatNullableInt(preset.TopK)}, Repeat Penalty {FormatNullableDouble(preset.RepeatPenalty)}, Presence Penalty {FormatNullableDouble(preset.PresencePenalty)}";
    }

    public static string GetPresetDisplayName(LmStudioPresetProfile profile)
    {
        return profile switch
        {
            LmStudioPresetProfile.Gemma4 => "Gemma 4",
            LmStudioPresetProfile.Gemma4E4B => "Gemma 4 E4B",
            LmStudioPresetProfile.Qwen35_9B => "Qwen 3.5 9B",
            LmStudioPresetProfile.TranslateGemma => "TranslateGemma",
            LmStudioPresetProfile.HyMt2_7B => "Hy-MT2 7B",
            LmStudioPresetProfile.HyMt2_30B_A3B => "Hy-MT2 30B-A3B",
            _ => "자동 (모델 기준)",
        };
    }

    private static string FormatNullableDouble(double? value)
    {
        return value?.ToString("0.##") ?? "자동";
    }

    private static string FormatNullableInt(int? value)
    {
        return value?.ToString() ?? "자동";
    }
}
