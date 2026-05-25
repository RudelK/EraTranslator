using System.Collections.Specialized;
using System.ComponentModel;
using System.Collections.ObjectModel;
using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.ViewModels;

public sealed class UserDictionaryViewModel : BindableBase
{
    private readonly UserDictionaryService _dictionaryService;
    private readonly List<UserDictionaryEntry> _originalGlobalEntries;
    private readonly List<UserDictionaryEntry> _originalProjectEntries;
    private UserDictionaryEntry? _selectedGlobalEntry;
    private UserDictionaryEntry? _selectedProjectEntry;
    private bool _isInitializing;

    public UserDictionaryViewModel(
        string gameDirectory,
        IEnumerable<UserDictionaryEntry> globalEntries,
        IEnumerable<UserDictionaryEntry> projectEntries,
        UserDictionaryService? dictionaryService = null)
    {
        _dictionaryService = dictionaryService ?? new UserDictionaryService();
        _isInitializing = true;
        GameDirectory = gameDirectory;
        _originalGlobalEntries = globalEntries.Select(entry => entry.Clone()).ToList();
        _originalProjectEntries = projectEntries.Select(entry => entry.Clone()).ToList();
        GlobalEntries = new ObservableCollection<UserDictionaryEntry>(globalEntries.Select(entry => entry.Clone()));
        ProjectEntries = new ObservableCollection<UserDictionaryEntry>(projectEntries.Select(entry => entry.Clone()));
        AttachCollection(GlobalEntries, isProjectScope: false);
        AttachCollection(ProjectEntries, isProjectScope: true);
        _isInitializing = false;
    }

    public string GameDirectory { get; }

    public ObservableCollection<UserDictionaryEntry> GlobalEntries { get; }

    public ObservableCollection<UserDictionaryEntry> ProjectEntries { get; }

    public UserDictionaryEntry? SelectedGlobalEntry
    {
        get => _selectedGlobalEntry;
        set => SetProperty(ref _selectedGlobalEntry, value);
    }

    public UserDictionaryEntry? SelectedProjectEntry
    {
        get => _selectedProjectEntry;
        set => SetProperty(ref _selectedProjectEntry, value);
    }

    public bool CanEditProjectDictionary => !string.IsNullOrWhiteSpace(GameDirectory);

    public string GlobalDictionaryPath => _dictionaryService.GetGlobalDictionaryPath();

    public string ProjectDictionaryPath => _dictionaryService.GetProjectDictionaryPath(GameDirectory) ?? "게임 폴더를 먼저 지정하세요.";

    public string ProjectScopeText => CanEditProjectDictionary
        ? "현재 프로젝트에만 적용됩니다. 같은 원문이 있으면 프로젝트 사전이 전역 사전을 덮어씁니다."
        : "프로젝트 사전을 사용하려면 메인 화면에서 게임 폴더를 먼저 지정하세요.";

    public string SummaryText => $"전역 {GlobalEntries.Count}개 / 프로젝트 {ProjectEntries.Count}개";

    public void AddGlobalEntry()
    {
        var entry = CreateEmptyEntry();
        GlobalEntries.Add(entry);
        SelectedGlobalEntry = entry;
    }

    public void RemoveSelectedGlobalEntry()
    {
        if (SelectedGlobalEntry is null)
        {
            return;
        }

        GlobalEntries.Remove(SelectedGlobalEntry);
        SelectedGlobalEntry = null;
    }

    public void AddProjectEntry()
    {
        if (!CanEditProjectDictionary)
        {
            return;
        }

        var entry = CreateEmptyEntry();
        ProjectEntries.Add(entry);
        SelectedProjectEntry = entry;
    }

    public void RemoveSelectedProjectEntry()
    {
        if (SelectedProjectEntry is null)
        {
            return;
        }

        ProjectEntries.Remove(SelectedProjectEntry);
        SelectedProjectEntry = null;
    }

    public IReadOnlyList<UserDictionaryEntry> GetGlobalEntries()
    {
        return GlobalEntries.Select(entry => entry.Clone()).ToList();
    }

    public IReadOnlyList<UserDictionaryEntry> GetProjectEntries()
    {
        return ProjectEntries.Select(entry => entry.Clone()).ToList();
    }

    public void RestorePersistedEntries()
    {
        _dictionaryService.SaveGlobal(_originalGlobalEntries);
        _dictionaryService.SaveProject(GameDirectory, _originalProjectEntries);
    }

    private static UserDictionaryEntry CreateEmptyEntry()
    {
        return new UserDictionaryEntry
        {
            IsEnabled = true,
            Source = string.Empty,
            Target = string.Empty,
        };
    }

    private void AttachCollection(ObservableCollection<UserDictionaryEntry> entries, bool isProjectScope)
    {
        entries.CollectionChanged += (_, eventArgs) => OnCollectionChanged(entries, eventArgs, isProjectScope);

        foreach (var entry in entries)
        {
            AttachEntry(entry, isProjectScope);
        }
    }

    private void OnCollectionChanged(
        ObservableCollection<UserDictionaryEntry> entries,
        NotifyCollectionChangedEventArgs eventArgs,
        bool isProjectScope)
    {
        if (eventArgs.OldItems is not null)
        {
            foreach (var item in eventArgs.OldItems.OfType<UserDictionaryEntry>())
            {
                item.PropertyChanged -= isProjectScope ? OnProjectEntryPropertyChanged : OnGlobalEntryPropertyChanged;
            }
        }

        if (eventArgs.NewItems is not null)
        {
            foreach (var item in eventArgs.NewItems.OfType<UserDictionaryEntry>())
            {
                AttachEntry(item, isProjectScope);
            }
        }

        RaisePropertyChanged(nameof(SummaryText));
        Persist(entries, isProjectScope);
    }

    private void AttachEntry(UserDictionaryEntry entry, bool isProjectScope)
    {
        entry.PropertyChanged += isProjectScope ? OnProjectEntryPropertyChanged : OnGlobalEntryPropertyChanged;
    }

    private void OnGlobalEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Persist(GlobalEntries, isProjectScope: false);
    }

    private void OnProjectEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Persist(ProjectEntries, isProjectScope: true);
    }

    private void Persist(IEnumerable<UserDictionaryEntry> entries, bool isProjectScope)
    {
        if (_isInitializing)
        {
            return;
        }

        if (isProjectScope)
        {
            _dictionaryService.SaveProject(GameDirectory, entries);
            return;
        }

        _dictionaryService.SaveGlobal(entries);
    }
}
