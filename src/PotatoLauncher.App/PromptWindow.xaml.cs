using System.Windows;

namespace PotatoLauncher.App;

public partial class PromptWindow : Window
{
    public PromptWindow(string title, string message, string? defaultValue)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        ValueBox.Text = defaultValue ?? "";
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string? Value { get; private set; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Value = ValueBox.Text;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
