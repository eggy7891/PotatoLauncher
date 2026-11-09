using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace PotatoLauncher.App.Services;

public sealed class DialogService
{
    public string? PickFolder(string title, string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? PickFile(string title, string filter, string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false
        };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void Info(string message, string title = "PotatoLauncher") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void Error(string message, string title = "PotatoLauncher") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirm(string message, string title = "PotatoLauncher") =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public bool AskMirror(string reason) =>
        Confirm(
            $"官方下载源连接失败。{Environment.NewLine}{Environment.NewLine}{reason}{Environment.NewLine}{Environment.NewLine}是否改用已配置镜像继续下载？选择“否”将停止本次任务。",
            "下载源不可用");

    public string? Prompt(string title, string message, string? defaultValue = null)
    {
        var window = new PromptWindow(title, message, defaultValue);
        return window.ShowDialog() == true ? window.Value : null;
    }
}
