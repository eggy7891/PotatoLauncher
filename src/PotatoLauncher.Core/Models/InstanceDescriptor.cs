namespace PotatoLauncher.Core.Models;

public enum LoaderKind
{
    Unknown,
    Vanilla,
    Fabric,
    Quilt,
    Forge,
    NeoForge
}

public sealed record InstanceDescriptor(
    string Id,
    string DisplayName,
    string DirectoryPath,
    string JsonPath,
    string Type,
    string? GameVersion,
    LoaderKind Loader,
    int? JavaMajorHint,
    DateTimeOffset LastModified)
{
    public string LoaderLabel => Loader switch
    {
        LoaderKind.Vanilla => "原版",
        LoaderKind.Fabric => "Fabric",
        LoaderKind.Quilt => "Quilt",
        LoaderKind.Forge => "Forge",
        LoaderKind.NeoForge => "NeoForge",
        _ => "未知"
    };
}
