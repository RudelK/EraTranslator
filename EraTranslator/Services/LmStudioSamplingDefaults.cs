using EraTranslator.Models;

namespace EraTranslator.Services;

internal enum LmStudioModelFamily
{
    Unknown,
    Gemma,
    Qwen,
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
        Temperature: 0.1,
        TopP: 0.9,
        TopK: 40,
        RepeatPenalty: 1.10,
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

    public static LmStudioModelFamily DetectModelFamily(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return LmStudioModelFamily.Unknown;
        }

        var normalized = model.Trim();
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
            LmStudioPresetProfile.Qwen35_9B => LmStudioModelFamily.Qwen,
            _ => DetectModelFamily(model),
        };
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
            _ => GemmaPreset,
        };
    }

    public static LmStudioThinkingControlMode GetThinkingControlMode(string? model, bool disableThinking)
    {
        if (!disableThinking)
        {
            return LmStudioThinkingControlMode.None;
        }

        return DetectModelFamily(model) is LmStudioModelFamily.Qwen or LmStudioModelFamily.Gemma
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
            LmStudioModelFamily.Gemma when profile == LmStudioPresetProfile.Auto => "Gemma (auto)",
            LmStudioModelFamily.Gemma => "Gemma 4",
            _ => "Gemma-style",
        };

        return $"현재 권장 시작값({familyLabel}): Temperature {preset.Temperature:0.##}, Top P {FormatNullableDouble(preset.TopP)}, Top K {FormatNullableInt(preset.TopK)}, Repeat Penalty {FormatNullableDouble(preset.RepeatPenalty)}, Presence Penalty {FormatNullableDouble(preset.PresencePenalty)}";
    }

    public static string GetPresetDisplayName(LmStudioPresetProfile profile)
    {
        return profile switch
        {
            LmStudioPresetProfile.Gemma4 => "Gemma 4",
            LmStudioPresetProfile.Qwen35_9B => "Qwen 3.5 9B",
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
