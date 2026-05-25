using System.Windows;
using EraTranslator.ViewModels;

namespace EraTranslator;

public partial class UserDictionaryWindow : Window
{
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
        _viewModel.RemoveSelectedGlobalEntry();
    }

    private void AddProjectEntry_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddProjectEntry();
    }

    private void RemoveProjectEntry_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RemoveSelectedProjectEntry();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
