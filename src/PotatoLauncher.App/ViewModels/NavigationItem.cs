using PotatoLauncher.App.Infrastructure;

namespace PotatoLauncher.App.ViewModels;

public sealed class NavigationItem : ObservableObject
{
    private bool _isSelected;

    public NavigationItem(string key, string label, string icon)
    {
        Key = key;
        Label = label;
        Icon = icon;
    }

    public string Key { get; }
    public string Label { get; }
    public string Icon { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
