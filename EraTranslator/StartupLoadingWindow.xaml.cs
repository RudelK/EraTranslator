using System.ComponentModel;
using System.Windows;

namespace EraTranslator;

public partial class StartupLoadingWindow : Window
{
    private bool _allowClose;

    public StartupLoadingWindow()
    {
        InitializeComponent();
    }

    public void AllowClose()
    {
        _allowClose = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
