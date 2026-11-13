namespace PotatoLauncher.Core.Models;

public enum CatalogSource
{
    Modrinth
}

public enum CatalogContentType
{
    Mod,
    ResourcePack,
    Shader,
    Modpack
}

public sealed record CatalogProject(
    CatalogSource Source,
    CatalogContentType ContentType,
    string Id,
    string Slug,
    string Title,
    string Description,
    string IconUrl,
    long Downloads,
    string ProjectUrl)
{
    public string SourceLabel => "Modrinth";

    public string ContentTypeLabel => ContentType switch
    {
        CatalogContentType.Mod => "模组",
        CatalogContentType.ResourcePack => "资源包",
        CatalogContentType.Shader => "光影",
        CatalogContentType.Modpack => "整合包",
        _ => ""
    };

    public string DownloadsLabel => $"{Downloads:N0} 次下载";
}

public sealed record CatalogDependency(
    string ProjectId,
    string? VersionId,
    string Name,
    bool IsRequired);

public sealed record CatalogFile(
    string Url,
    string FileName,
    string? Sha512,
    string? Sha1,
    long Size,
    bool Primary);

public sealed record CatalogVersion(
    CatalogSource Source,
    string ProjectId,
    string Id,
    string Name,
    string VersionNumber,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<string> GameVersions,
    IReadOnlyList<string> Loaders,
    IReadOnlyList<CatalogDependency> Dependencies,
    IReadOnlyList<CatalogFile> Files)
{
    public string GameVersionsLabel => string.Join(" · ", GameVersions);

    public string LoadersLabel => Loaders.Count == 0 ? "通用" : string.Join(" · ", Loaders);

    public string PublishedLabel => PublishedAt?.ToLocalTime().ToString("yyyy-MM-dd") ?? "";
}
