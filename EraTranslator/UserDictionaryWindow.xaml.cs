using System.Windows;
using EraTranslator.ViewModels;
using Win32 = Microsoft.Win32;

namespace EraTranslator;

public partial class UserDictionaryWindow : Window
{
    private const string ImportFilter = "Dictionary/SRS (*.etdict;*.simplesrs;*.srs;*.txt)|*.etdict;*.simplesrs;*.srs;*.txt|All files (*.*)|*.*";
    private const string ExportFilter = "EraTranslator Dictionary (*.etdict)|*.etdict";
    private readonly UserDictionaryViewModel _viewModel;

    public UserDictionaryWindow(UserDictionaryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    private void AddGlobalEntry_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddGlobalEntry();
    }

    private void RemoveGlobalEntry_Click(object sender, RoutedEventArgs e)
    {
        var selectedEntries = GlobalEntriesGrid.SelectedItems.OfType<Models.UserDictionaryEntry>().ToList();
        if (selectedEntries.Count > 0)
        {
            _viewModel.RemoveGlobalEntries(selectedEntries);
            return;
        }

        _viewModel.RemoveSelectedGlobalEntry();
    }

    private void AddProjectEntry_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddProjectEntry();
    }

    private void RemoveProjectEntry_Click(object sender, RoutedEventArgs e)
    {
        var selectedEntries = ProjectEntriesGrid.SelectedItems.OfType<Models.UserDictionaryEntry>().ToList();
        if (selectedEntries.Count > 0)
        {
            _viewModel.RemoveProjectEntries(selectedEntries);
            return;
        }

        _viewModel.RemoveSelectedProjectEntry();
    }

    private void ImportGlobalDictionary_Click(object sender, RoutedEventArgs e)
    {
        ImportDictionary(isProjectScope: false);
    }

    private void ExportGlobalDictionary_Click(object sender, RoutedEventArgs e)
    {
        ExportDictionary(isProjectScope: false);
    }

    private void ImportProjectDictionary_Click(object sender, RoutedEventArgs e)
    {
        ImportDictionary(isProjectScope: true);
    }

    private void ExportProjectDictionary_Click(object sender, RoutedEventArgs e)
    {
        ExportDictionary(isProjectScope: true);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void ImportDictionary(bool isProjectScope)
    {
        var dialog = new Win32.OpenFileDialog
        {
            Title = isProjectScope ? "프로젝트 사용자 사전 가져오기" : "전역 사용자 사전 가져오기",
            Filter = ImportFilter,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var result = isProjectScope
                ? _viewModel.ImportProjectDictionary(dialog.FileName)
                : _viewModel.ImportGlobalDictionary(dialog.FileName);
            var message = $"가져오기 완료: 추가 {result.Added}개 / 갱신 {result.Updated}개 / 건너뜀 {result.Skipped}개";
            if (result.Warnings.Count > 0)
            {
                message += Environment.NewLine + string.Join(Environment.NewLine, result.Warnings);
            }

            System.Windows.MessageBox.Show(this, message, "사용자 사전 가져오기", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"사용자 사전을 가져오지 못했습니다.{Environment.NewLine}{ex.Message}", "사용자 사전 가져오기", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportDictionary(bool isProjectScope)
    {
        var dialog = new Win32.SaveFileDialog
        {
            Title = isProjectScope ? "프로젝트 사용자 사전 내보내기" : "전역 사용자 사전 내보내기",
            Filter = ExportFilter,
            DefaultExt = ".etdict",
            AddExtension = true,
            FileName = isProjectScope ? "project-user-dictionary.etdict" : "global-user-dictionary.etdict",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            if (isProjectScope)
            {
                _viewModel.ExportProjectDictionary(dialog.FileName);
            }
            else
            {
                _viewModel.ExportGlobalDictionary(dialog.FileName);
            }

            System.Windows.MessageBox.Show(this, $"사용자 사전을 내보냈습니다.{Environment.NewLine}{dialog.FileName}", "사용자 사전 내보내기", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"사용자 사전을 내보내지 못했습니다.{Environment.NewLine}{ex.Message}", "사용자 사전 내보내기", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
