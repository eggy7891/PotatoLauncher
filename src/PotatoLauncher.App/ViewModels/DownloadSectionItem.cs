using PotatoLauncher.App.Infrastructure;
using PotatoLauncher.Core.Models;

namespace PotatoLauncher.App.ViewModels;

public sealed class DownloadSectionItem : ObservableObject
{
    private bool _isSelected;

    public DownloadSectionItem(string key, string label, CatalogContentType? contentType)
    {
        Key = key;
        Label = label;
        ContentType = contentType;
    }

    public string Key { get; }
    public string Label { get; }
    public CatalogContentType? ContentType { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
