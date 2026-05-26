using System.Windows;
using EraTranslator.ViewModels;

namespace EraTranslator;

public partial class GlobalReplaceWindow : Window
{
    public GlobalReplaceWindow(GlobalReplaceViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        PreviewKeyDown += GlobalReplaceWindow_OnPreviewKeyDown;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void GlobalReplaceWindow_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape)
        {
            return;
        }

        DialogResult = false;
        e.Handled = true;
    }
}
