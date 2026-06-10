using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using EraTranslator.Models;
using EraTranslator.ViewModels;
using Forms = System.Windows.Forms;
using WpfBinding = System.Windows.Data.Binding;
using WpfTextBox = System.Windows.Controls.TextBox;
using Win32 = Microsoft.Win32;

namespace EraTranslator;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new(restoreLastSessionOnStartup: false);

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        ContentRendered += MainWindow_ContentRendered;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.StopCurrentOperation();
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (await _viewModel.ScanAsync())
        {
            System.Windows.MessageBox.Show(this, "추출이 완료되었습니다.", "추출 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void ConvertEncoding_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.ConvertEncodingsAsync();
    }

    private async void Translate_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingTranslationEdits();
        if (await _viewModel.TranslatePendingAsync())
        {
            System.Windows.MessageBox.Show(this, "번역이 완료되었습니다.", "번역 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingTranslationEdits();
        await _viewModel.SaveAsync();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.StopCurrentOperation();
    }

    private void ResetTranslations_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmReset(
                "현재 번역문, 실패 상태, 검수 상태를 모두 지웁니다.\n정말 번역 리셋을 진행하시겠습니까?",
                "번역 리셋 확인"))
        {
            return;
        }

        _viewModel.ResetTranslations();
    }

    private void ResetExtraction_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmReset(
                "현재 추출 결과와 저장된 번역 진행 상태를 모두 지웁니다.\n정말 추출 리셋을 진행하시겠습니까?",
                "추출 리셋 확인"))
        {
            return;
        }

        _viewModel.ResetExtraction();
    }

    private void ExportTranslationsText_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Win32.SaveFileDialog
        {
            Title = "번역 텍스트 내보내기",
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            FileName = "EraTranslator-export.txt",
            InitialDirectory = Directory.Exists(_viewModel.GameDirectory)
                ? _viewModel.GameDirectory
                : Environment.CurrentDirectory,
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.ExportTranslationsToText(dialog.FileName);
        }
    }

    private void ImportTranslationsText_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Win32.OpenFileDialog
        {
            Title = "번역 텍스트 가져오기",
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            InitialDirectory = Directory.Exists(_viewModel.GameDirectory)
                ? _viewModel.GameDirectory
                : Environment.CurrentDirectory,
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.ImportTranslationsFromText(dialog.FileName);
        }
    }

    private void OpenGlobalReplace_Click(object sender, RoutedEventArgs e)
    {
        var replaceViewModel = _viewModel.CreateGlobalReplaceViewModel();
        var dialog = new GlobalReplaceWindow(replaceViewModel)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() == true)
        {
            _viewModel.ApplyGlobalReplace(replaceViewModel);
        }
    }

    private void RefreshFilter_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RefreshItemsView();
    }

    private void ApplySameOriginalCorrection_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingTranslationEdits();
        _viewModel.ApplySameOriginalCorrection();
    }

    private void ApplyJosaRewrite_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingTranslationEdits();
        _viewModel.ApplyJosaRewriteToCurrentScope();
    }

    private async void TeamSync_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingTranslationEdits();
        await _viewModel.TeamSyncAsync();
    }

    private async void TeamManifestUpload_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingTranslationEdits();
        await _viewModel.UploadTeamScanManifestAsync();
    }

    private async void TeamSubmit_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingTranslationEdits();
        await _viewModel.SubmitTeamChangesAsync();
    }

    private async void TeamRetryQueue_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingTranslationEdits();
        await _viewModel.RetryTeamOfflineQueueAsync();
    }

    private void ApplyErbFunctionCorrection_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingTranslationEdits();
        _viewModel.ApplyErbFunctionCorrectionToCurrentScope();
    }

    private void BrowseGameDirectory_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Emuera 게임 루트를 선택하세요."
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            _viewModel.GameDirectory = dialog.SelectedPath;
        }
    }

    private void BrowseOutputDirectory_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "번역 결과를 저장할 폴더를 선택하세요."
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            if (!_viewModel.TryApplyOutputDirectory(dialog.SelectedPath, out var errorMessage))
            {
                System.Windows.MessageBox.Show(
                    this,
                    errorMessage,
                    "출력 폴더 선택 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    private void OpenTranslationSettings_Click(object sender, RoutedEventArgs e)
    {
        var settingsViewModel = _viewModel.CreateTranslationSettingsViewModel();
        var dialog = new TranslationSettingsWindow(settingsViewModel)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() == true)
        {
            _viewModel.ApplyTranslationSettings(settingsViewModel);
        }
    }

    private void OpenUserDictionary_Click(object sender, RoutedEventArgs e)
    {
        var dictionaryViewModel = _viewModel.CreateUserDictionaryViewModel();
        var dialog = new UserDictionaryWindow(dictionaryViewModel)
        {
            Owner = this,
        };

        dialog.ShowDialog();
        _viewModel.ApplyUserDictionary(dictionaryViewModel);
    }

    private void OpenTeamSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TeamSettingsWindow
        {
            Owner = this,
            DataContext = _viewModel,
        };

        dialog.ShowDialog();
    }

    private void TranslationGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit
            || e.Row.Item is not ExtractedTextItem item
            || e.Column is not DataGridBoundColumn boundColumn
            || boundColumn.Binding is not WpfBinding binding
            || !string.Equals(binding.Path?.Path, nameof(ExtractedTextItem.TranslatedText), StringComparison.Ordinal))
        {
            return;
        }

        var editedText = e.EditingElement is WpfTextBox textBox ? textBox.Text : item.TranslatedText;

        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => _viewModel.HandleTranslatedTextEdited(item, editedText)));
    }

    private void SelectedTranslationEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        var editedText = sender is WpfTextBox textBox
            ? textBox.Text
            : null;
        _viewModel.CommitSelectedItemTranslatedTextEdit(editedText);
    }

    private void SelectedTranslationEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        var editedText = sender is WpfTextBox textBox
            ? textBox.Text
            : null;
        _viewModel.PreviewSelectedItemTranslatedTextEdit(editedText);
    }

    private void CommitPendingTranslationEdits()
    {
        TranslationGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        TranslationGrid.CommitEdit(DataGridEditingUnit.Row, true);
        _viewModel.CommitSelectedItemTranslatedTextEdit();
    }

    private bool ConfirmReset(string message, string title)
    {
        return System.Windows.MessageBox.Show(
                this,
                message,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No)
            == MessageBoxResult.Yes;
    }

    private void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= MainWindow_ContentRendered;
        if (!_viewModel.HasStartupProjectContextCandidate())
        {
            return;
        }

        var loadingWindow = new StartupLoadingWindow
        {
            Owner = this,
            DataContext = _viewModel,
        };

        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(async () =>
            {
                try
                {
                    await _viewModel.RestoreStartupProjectContextIfAvailableAsync();
                }
                finally
                {
                    loadingWindow.AllowClose();
                    loadingWindow.Close();
                }
            }));

        loadingWindow.ShowDialog();
    }
}
