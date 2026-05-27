using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class SourceLanguageFilterService
{
    private const string AutoExcludedValidationStatus = "언어 제외";
    private const string AutoExcludedTranslationError = "원문이 현재 소스 언어와 일치하지 않아 자동 제외되었습니다.";

    public int Apply(IEnumerable<ExtractedTextItem> items, string sourceLanguage, bool enabled)
    {
        var changedCount = 0;
        foreach (var item in items)
        {
            if (TryRestoreAutoExcluded(item))
            {
                changedCount++;
            }

            if (!enabled)
            {
                continue;
            }

            if (!ShouldAutoExclude(item))
            {
                continue;
            }

            if (SourceLanguageHeuristics.IsLikelySourceText(item.OriginalText, sourceLanguage))
            {
                continue;
            }

            item.ApplyTranslationState(
                "제외됨",
                AutoExcludedValidationStatus,
                AutoExcludedTranslationError,
                true,
                string.Empty);
            changedCount++;
        }

        return changedCount;
    }

    private static bool ShouldAutoExclude(ExtractedTextItem item)
    {
        return string.IsNullOrWhiteSpace(item.TranslatedText)
            && item.Status is "번역 대기" or "대기" or "번역 실패" or "중지됨";
    }

    private static bool TryRestoreAutoExcluded(ExtractedTextItem item)
    {
        if (item.Status != "제외됨"
            || item.ValidationStatus != AutoExcludedValidationStatus
            || item.TranslationError != AutoExcludedTranslationError)
        {
            return false;
        }

        item.ResetTranslationState();
        return true;
    }
}
