namespace PotatoLauncher.Core.Models;

public sealed record ModrinthProject(
    string Id,
    string Slug,
    string Title,
    string Description,
    string ProjectType,
    string IconUrl,
    long Downloads,
    IReadOnlyList<string> GameVersions,
    IReadOnlyList<string> Loaders);

public sealed record ModrinthVersionFile(
    string Url,
    string FileName,
    string Sha512,
    long Size,
    bool Primary);

public sealed record ModrinthVersion(
    string Id,
    string ProjectId,
    string Name,
    string VersionNumber,
    string VersionType,
    IReadOnlyList<string> GameVersions,
    IReadOnlyList<string> Loaders,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<ModrinthVersionFile> Files);

public sealed record ModrinthSearchResult(
    IReadOnlyList<ModrinthProject> Hits,
    int Offset,
    int Limit,
    int TotalHits);
