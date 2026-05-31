using EraTranslator.ViewModels;
using EraTranslator.Services;

namespace EraTranslator.Models;

public sealed class ExtractedTextItem : BindableBase
{
    private const string PendingStatus = "번역 대기";
    private const string ManualExcludedValidationStatus = "수동 제외";
    private string _translatedText = string.Empty;
    private string _status = PendingStatus;
    private string _translationError = string.Empty;
    private string _translationSource = string.Empty;
    private string _validationStatus = "검증 전";
    private bool _canSave = true;

    public required string SegmentId { get; init; }

    public required string DocumentId { get; init; }

    public required string FileType { get; init; }

    public required string RelativePath { get; init; }

    public required string EncodingName { get; init; }

    public required string SegmentType { get; init; }

    public required int LineNumber { get; init; }

    public required string OriginalText { get; init; }

    public string? SourceKey { get; init; }

    public int? FieldIndex { get; init; }

    public CsvFieldRole CsvFieldRole { get; init; }

    public bool PreserveWhitespace { get; init; }

    public string WarningText { get; init; } = string.Empty;

    public string SymbolNamespace { get; init; } = string.Empty;

    public string OriginalSymbolKey { get; init; } = string.Empty;

    public bool IsReferenceBearingKey { get; init; }

    public string ReferenceOriginalSymbolKey { get; set; } = string.Empty;

    public int ReferenceImpactCount { get; set; }

    public bool RequiresReferenceRewrite { get; set; }

    public string ReferenceResolutionStatus { get; set; } = string.Empty;

    public string TranslatedText
    {
        get => _translatedText;
        set => SetProperty(ref _translatedText, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string TranslationError
    {
        get => _translationError;
        set => SetProperty(ref _translationError, value);
    }

    public string TranslationSource
    {
        get => _translationSource;
        set => SetProperty(ref _translationSource, value);
    }

    public string ValidationStatus
    {
        get => _validationStatus;
        set => SetProperty(ref _validationStatus, value);
    }

    public bool CanSave
    {
        get => _canSave;
        set => SetProperty(ref _canSave, value);
    }

    public string EditableStatus
    {
        get => _status;
        set => ApplyManualStatusOverride(value);
    }

    public bool IsTranslatedSuccessfully =>
        (string.Equals(_status, "번역 완료", StringComparison.Ordinal)
        || string.Equals(_status, "검수 필요", StringComparison.Ordinal)
        || string.Equals(_status, "수동 수정", StringComparison.Ordinal))
        && string.Equals(_validationStatus, "통과", StringComparison.Ordinal)
        && _canSave
        && !string.IsNullOrWhiteSpace(_translatedText);

    public bool IsExcluded => string.Equals(_status, "제외됨", StringComparison.Ordinal);

    public bool NeedsTranslation =>
        IsPendingStatus(_status)
        || string.Equals(_status, "번역 실패", StringComparison.Ordinal)
        || string.Equals(_status, "중지됨", StringComparison.Ordinal);

    public bool HasPersistableState =>
        IsTranslatedSuccessfully
        || !IsPendingStatus(_status)
        || !string.IsNullOrWhiteSpace(_translatedText)
        || !string.IsNullOrWhiteSpace(_translationError)
        || !string.Equals(_validationStatus, "검증 전", StringComparison.Ordinal);

    public string StateText
    {
        get
        {
            var parts = new List<string> { _status };
            if (!string.IsNullOrWhiteSpace(_validationStatus)
                && !string.Equals(_validationStatus, "통과", StringComparison.Ordinal)
                && !string.Equals(_validationStatus, "검증 전", StringComparison.Ordinal))
            {
                parts.Add(_validationStatus);
            }

            if (!string.IsNullOrWhiteSpace(WarningText))
            {
                parts.Add("경고");
            }

            if (!string.IsNullOrWhiteSpace(_translationSource))
            {
                parts.Add(_translationSource);
            }

            return string.Join(" / ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }
    }

    public void MarkTranslating()
    {
        _status = "번역 중";
        _translationError = string.Empty;
        _translationSource = string.Empty;
        _validationStatus = "검증 전";
        _canSave = true;
        RaiseStateChangedProperties();
    }

    public void MarkRetrying()
    {
        _status = "재시도 중";
        _translationError = string.Empty;
        _translationSource = string.Empty;
        _validationStatus = "검증 전";
        _canSave = true;
        RaiseStateChangedProperties();
    }

    public void MarkStopped()
    {
        _status = "중지됨";
        _translationError = "사용자가 번역을 중지했습니다.";
        _translationSource = string.Empty;
        _validationStatus = "검증 전";
        _canSave = false;
        RaiseStateChangedProperties();
    }

    public void ApplyManualTranslationEdit()
    {
        if (string.IsNullOrWhiteSpace(_translatedText))
        {
            ResetTranslationState();
            return;
        }

        _translatedText = TranslationQualityRules.NormalizeTranslatedText(FileType, _translatedText, PreserveWhitespace);
        var reviewReason = TranslationQualityRules.GetReviewReason(OriginalText, _translatedText);
        _status = reviewReason is null ? "수동 수정" : "검수 필요";
        _translationError = reviewReason ?? string.Empty;
        _translationSource = string.Empty;
        _validationStatus = "통과";
        _canSave = true;
        RaiseStateChangedProperties();
    }

    public void ResetTranslationState()
    {
        _translatedText = string.Empty;
        _status = PendingStatus;
        _translationError = string.Empty;
        _translationSource = string.Empty;
        _validationStatus = "검증 전";
        _canSave = true;
        RaiseStateChangedProperties();
    }

    public void ApplyManualStatusOverride(string? status)
    {
        switch (status)
        {
            case PendingStatus:
            case "대기":
                _status = PendingStatus;
                _translationError = string.Empty;
                _translationSource = string.Empty;
                _validationStatus = "검증 전";
                _canSave = true;
                break;
            case "중지됨":
                _status = "중지됨";
                _translationError = string.IsNullOrWhiteSpace(_translationError) ? "수동으로 중지 상태로 표시했습니다." : _translationError;
                _translationSource = string.Empty;
                _validationStatus = "검증 전";
                _canSave = false;
                break;
            case "제외됨":
                _status = "제외됨";
                _translationError = "수동으로 제외 상태로 표시했습니다.";
                _translationSource = string.Empty;
                _validationStatus = ManualExcludedValidationStatus;
                _canSave = true;
                _translatedText = string.Empty;
                break;
            case "수동 수정":
                _status = "수동 수정";
                _translationError = string.Empty;
                _translationSource = string.Empty;
                _validationStatus = "통과";
                _canSave = !string.IsNullOrWhiteSpace(_translatedText);
                break;
            case "검수 필요":
                _status = "검수 필요";
                _translationError = string.IsNullOrWhiteSpace(_translationError) ? "원문과 번역문 길이 차이가 커서 검토가 필요합니다." : _translationError;
                _translationSource = string.Empty;
                _validationStatus = "통과";
                _canSave = !string.IsNullOrWhiteSpace(_translatedText);
                break;
            case "번역 완료":
                _status = "번역 완료";
                _translationError = string.Empty;
                _translationSource = string.Empty;
                _validationStatus = "통과";
                _canSave = !string.IsNullOrWhiteSpace(_translatedText);
                break;
            case "번역 실패":
                _status = "번역 실패";
                _validationStatus = "검증 전";
                _translationSource = string.Empty;
                _canSave = false;
                if (string.IsNullOrWhiteSpace(_translationError))
                {
                    _translationError = "수동으로 실패 상태로 표시했습니다.";
                }
                break;
            case "검증 실패":
                _status = "검수 필요";
                _validationStatus = "검증 실패";
                _translationSource = string.Empty;
                _canSave = false;
                if (string.IsNullOrWhiteSpace(_translationError))
                {
                    _translationError = "수동으로 저장 불가 검토 상태로 표시했습니다.";
                }
                break;
            default:
                return;
        }

        RaiseStateChangedProperties();
    }

    public void ApplyTranslationState(
        string status,
        string validationStatus,
        string translationError,
        bool canSave,
        string? translatedText = null)
    {
        _status = status;
        _validationStatus = validationStatus;
        _translationError = translationError;
        _translationSource = string.Empty;
        _canSave = canSave;
        _translatedText = translatedText ?? string.Empty;
        RaiseStateChangedProperties();
    }

    public void ApplyPersistedState(TranslationProgressItemState state)
    {
        var status = state.Status is "번역 중" or "재시도 중"
            ? "중지됨"
            : state.Status == "검증 실패"
                ? "검수 필요"
                : NormalizePersistedStatus(state.Status);
        var validationStatus = status == "중지됨"
            ? "검증 전"
            : string.IsNullOrWhiteSpace(state.ValidationStatus) ? "검증 전" : state.ValidationStatus;
        var translationError = status == "중지됨" && string.IsNullOrWhiteSpace(state.TranslationError)
            ? "이전 실행에서 번역이 중단되었습니다."
            : state.TranslationError;
        var canSave = status switch
        {
            "중지됨" => false,
            "제외됨" => true,
            _ => state.CanSave,
        };

        if (status is "번역 완료" or "검수 필요" or "수동 수정"
            && string.IsNullOrWhiteSpace(state.TranslatedText))
        {
            ApplyTranslationState(
                "번역 실패",
                "빈 번역문",
                "저장된 진행 상태의 번역문이 비어 있어 다시 번역 대상으로 되돌렸습니다.",
                false,
                string.Empty);
            return;
        }

        ApplyTranslationState(
            status,
            validationStatus,
            translationError,
            canSave,
            state.TranslatedText);
    }

    public string CsvRoleText => CsvFieldRole switch
    {
        CsvFieldRole.Key => "키",
        CsvFieldRole.MetaKey => "메타키",
        CsvFieldRole.TranslatableValue => "값",
        CsvFieldRole.NonTranslatableValue => "고정값",
        _ => string.Empty,
    };

    public string TranslatedSymbolKey =>
        IsReferenceBearingKey && !string.IsNullOrWhiteSpace(TranslatedText)
            ? TranslationQualityRules.NormalizeTranslatedText(FileType, TranslatedText, PreserveWhitespace)
            : string.Empty;

    public IEnumerable<string> GetReferenceLookupKeys()
    {
        if (!string.IsNullOrWhiteSpace(ReferenceOriginalSymbolKey))
        {
            yield return ReferenceOriginalSymbolKey;
        }

        if (!string.IsNullOrWhiteSpace(OriginalSymbolKey)
            && !string.Equals(OriginalSymbolKey, ReferenceOriginalSymbolKey, StringComparison.Ordinal))
        {
            yield return OriginalSymbolKey;
        }
    }

    private static bool IsPendingStatus(string? status)
    {
        return string.Equals(status, PendingStatus, StringComparison.Ordinal)
            || string.Equals(status, "대기", StringComparison.Ordinal);
    }

    private static string NormalizePersistedStatus(string? status)
    {
        return string.IsNullOrWhiteSpace(status) || string.Equals(status, "대기", StringComparison.Ordinal)
            ? PendingStatus
            : status;
    }

    private void RaiseStateChangedProperties()
    {
        RaisePropertyChanged(nameof(TranslatedText));
        RaisePropertyChanged(nameof(Status));
        RaisePropertyChanged(nameof(TranslationError));
        RaisePropertyChanged(nameof(TranslationSource));
        RaisePropertyChanged(nameof(ValidationStatus));
        RaisePropertyChanged(nameof(CanSave));
        RaisePropertyChanged(nameof(EditableStatus));
        RaisePropertyChanged(nameof(IsTranslatedSuccessfully));
        RaisePropertyChanged(nameof(IsExcluded));
        RaisePropertyChanged(nameof(NeedsTranslation));
        RaisePropertyChanged(nameof(HasPersistableState));
        RaisePropertyChanged(nameof(StateText));
        RaisePropertyChanged(nameof(TranslatedSymbolKey));
    }
}
