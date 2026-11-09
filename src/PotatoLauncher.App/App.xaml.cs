using System.IO;
using System.Text;
using System.Windows;
using PotatoLauncher.App.Services;
using PotatoLauncher.App.ViewModels;

namespace PotatoLauncher.App;

public partial class App : Application
{
    public AppRuntime? Runtime { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        DispatcherUnhandledException += (_, args) =>
        {
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "startup-error.log"),
                $"[{DateTimeOffset.Now:O}] {args.Exception}{Environment.NewLine}");
            MessageBox.Show(args.Exception.Message, "PotatoLauncher 遇到错误", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        Runtime = await AppRuntime.CreateAsync();
        var window = new MainWindow
        {
            DataContext = new MainViewModel(Runtime)
        };
        MainWindow = window;
        window.Show();
    }
}
