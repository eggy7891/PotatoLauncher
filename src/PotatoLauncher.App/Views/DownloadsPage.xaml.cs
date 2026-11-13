using System.Windows.Controls;
using System.Diagnostics;
using PotatoLauncher.App.ViewModels;

namespace PotatoLauncher.App.Views;

public partial class DownloadsPage : UserControl
{
    public DownloadsPage() => InitializeComponent();

    private void OpenProjectPage_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { SelectedCatalogProject.ProjectUrl: { Length: > 0 } url })
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}
