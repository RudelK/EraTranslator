using System.Windows;
using EraTranslator.ViewModels;

namespace EraTranslator;

public partial class TranslationSettingsWindow : Window
{
    private readonly TranslationSettingsViewModel _viewModel;

    public TranslationSettingsWindow(TranslationSettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        SyncPasswordBoxes();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        base.OnClosed(e);
    }

    private async void LoadModels_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadModelsAsync(CancellationToken.None);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.ValidateBeforeSave())
        {
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void TranslationSettingsWindow_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape)
        {
            return;
        }

        e.Handled = true;
        DialogResult = false;
    }

    private void ResetPrompts_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ResetPromptTemplates();
    }

    private void ResetTranslationOptions_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ResetTranslationOptions();
    }

    private void ApplyLmStudioPreset_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ApplySelectedLmStudioPreset();
    }

    private void RefreshEzTransStatus_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RefreshEzTransInstallationStatus();
    }

    private void ApiKeyPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.ApiKey = ApiKeyPasswordBox.Password;
    }

    private void PapagoSecretPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.PapagoClientSecret = PapagoSecretPasswordBox.Password;
    }

    private void ViewModelOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TranslationSettingsViewModel.SelectedProviderOption)
            or nameof(TranslationSettingsViewModel.ApiKey)
            or nameof(TranslationSettingsViewModel.PapagoClientSecret))
        {
            SyncPasswordBoxes();
        }
    }

    private void SyncPasswordBoxes()
    {
        if (ApiKeyPasswordBox.Password != _viewModel.ApiKey)
        {
            ApiKeyPasswordBox.Password = _viewModel.ApiKey;
        }

        if (PapagoSecretPasswordBox.Password != _viewModel.PapagoClientSecret)
        {
            PapagoSecretPasswordBox.Password = _viewModel.PapagoClientSecret;
        }
    }
}
