using System.ComponentModel;
using System.Windows;
using PotatoLauncher.App.ViewModels;

namespace PotatoLauncher.App;

public partial class MainWindow : Window
{
    private bool _canClose;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_canClose || DataContext is not MainViewModel viewModel)
        {
            return;
        }
        e.Cancel = true;
        await viewModel.SaveOnExitAsync();
        _canClose = true;
        Close();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
