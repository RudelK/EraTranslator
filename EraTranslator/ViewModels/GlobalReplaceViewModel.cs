namespace EraTranslator.ViewModels;

public sealed class GlobalReplaceViewModel : BindableBase
{
    private string _searchText = string.Empty;
    private string _replaceText = string.Empty;
    private bool _useRegex;

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public string ReplaceText
    {
        get => _replaceText;
        set => SetProperty(ref _replaceText, value);
    }

    public bool UseRegex
    {
        get => _useRegex;
        set => SetProperty(ref _useRegex, value);
    }

    public string ScopeDescription { get; init; } = string.Empty;

    public string HelpText =>
        UseRegex
            ? "정규식으로 번역문 전체를 검색/치환합니다."
            : "일반 텍스트로 번역문 전체를 검색/치환합니다.";
}
