using System.Windows;
using EraTranslator.ViewModels;

namespace EraTranslator;

public partial class GlobalReplaceWindow : Window
{
    public GlobalReplaceWindow(GlobalReplaceViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
