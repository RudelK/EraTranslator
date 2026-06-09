using System.Windows;

namespace EraTranslator;

public partial class TeamSettingsWindow : Window
{
    public TeamSettingsWindow()
    {
        InitializeComponent();
    }

    private async void RefreshTeamProjects_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            await viewModel.RefreshTeamProjectsAsync();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
