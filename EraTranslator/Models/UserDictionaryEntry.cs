using EraTranslator.ViewModels;

namespace EraTranslator.Models;

public enum UserDictionaryApplyMode
{
    Replace,
    Prompting,
}

public sealed class UserDictionaryEntry : BindableBase
{
    private bool _isEnabled = true;
    private string _source = string.Empty;
    private string _target = string.Empty;
    private UserDictionaryApplyMode _applyMode = UserDictionaryApplyMode.Replace;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public string Source
    {
        get => _source;
        set => SetProperty(ref _source, value);
    }

    public string Target
    {
        get => _target;
        set => SetProperty(ref _target, value);
    }

    public UserDictionaryApplyMode ApplyMode
    {
        get => _applyMode;
        set => SetProperty(ref _applyMode, value);
    }

    public UserDictionaryEntry Clone()
    {
        return new UserDictionaryEntry
        {
            IsEnabled = IsEnabled,
            Source = Source,
            Target = Target,
            ApplyMode = ApplyMode,
        };
    }
}
