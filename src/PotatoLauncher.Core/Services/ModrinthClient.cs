using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using PotatoLauncher.Core.Models;

namespace PotatoLauncher.Core.Services;

public interface IModrinthClient
{
    Task<ModrinthSearchResult> SearchAsync(
        string query,
        string projectType,
        string? gameVersion,
        string? loader,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModrinthVersion>> GetVersionsAsync(
        string projectId,
        string? gameVersion,
        string? loader,
        CancellationToken cancellationToken = default);

    Task<DownloadItem> CreateDownloadAsync(
        ModrinthProject project,
        ModrinthVersion version,
        string targetDirectory,
        CancellationToken cancellationToken = default);
}

public sealed class ModrinthClient : IModrinthClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public ModrinthClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PotatoLauncher/1.0.0 (+https://github.com/eggy7891/PotatoLauncher)");
    }

    public async Task<ModrinthSearchResult> SearchAsync(
        string query,
        string projectType,
        string? gameVersion,
        string? loader,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var facets = new List<List<string>>
        {
            new() { $"project_type:{projectType}" }
        };
        if (!string.IsNullOrWhiteSpace(gameVersion))
        {
            facets.Add([$"versions:{gameVersion}"]);
        }
        if (!string.IsNullOrWhiteSpace(loader) && projectType is "mod" or "modpack")
        {
            facets.Add([$"categories:{loader}"]);
        }

        var url = "https://api.modrinth.com/v2/search" +
                  $"?query={Uri.EscapeDataString(query ?? "")}" +
                  $"&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}" +
                  $"&index=relevance&offset={Math.Max(0, offset)}&limit={Math.Clamp(limit, 1, 100)}";
        var data = await _httpClient.GetFromJsonAsync<SearchResponse>(url, JsonOptions, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException("Modrinth 搜索响应为空。");

        var hits = data.Hits.Select(hit => new ModrinthProject(
            hit.ProjectId,
            hit.Slug,
            hit.Title,
            hit.Description,
            hit.ProjectType,
            hit.IconUrl ?? "",
            hit.Downloads,
            hit.Versions ?? [],
            hit.Categories ?? [])).ToArray();
        return new ModrinthSearchResult(hits, data.Offset, data.Limit, data.TotalHits);
    }

    public async Task<IReadOnlyList<ModrinthVersion>> GetVersionsAsync(
        string projectId,
        string? gameVersion,
        string? loader,
        CancellationToken cancellationToken = default)
    {
        var url = $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(projectId)}/version";
        var versions = await _httpClient.GetFromJsonAsync<List<VersionResponse>>(url, JsonOptions, cancellationToken)
            .ConfigureAwait(false) ?? [];
        return versions
            .Where(version =>
                (string.IsNullOrWhiteSpace(gameVersion) || version.GameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(loader) || version.Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase)))
            .Select(version => new ModrinthVersion(
                version.Id,
                version.ProjectId,
                version.Name,
                version.VersionNumber,
                version.VersionType,
                version.GameVersions,
                version.Loaders,
                version.Dependencies?
                    .Where(dependency => !string.IsNullOrWhiteSpace(dependency.ProjectId) &&
                                         dependency.DependencyType == "required")
                    .Select(dependency => dependency.ProjectId!)
                    .ToArray() ?? [],
                version.Files.Select(file => new ModrinthVersionFile(
                    file.Url,
                    file.Filename,
                    file.Hashes?.Sha512 ?? "",
                    file.Size,
                    file.Primary)).ToArray()))
            .OrderByDescending(version => version.VersionType.Equals("release", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(version => version.VersionNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task<DownloadItem> CreateDownloadAsync(
        ModrinthProject project,
        ModrinthVersion version,
        string targetDirectory,
        CancellationToken cancellationToken = default)
    {
        var file = version.Files.FirstOrDefault(item => item.Primary) ?? version.Files.FirstOrDefault()
            ?? throw new InvalidDataException("该版本没有可下载文件。");
        var subfolder = project.ProjectType switch
        {
            "resourcepack" => "resourcepacks",
            "shader" => "shaderpacks",
            "modpack" => "modpacks",
            _ => "mods"
        };
        var item = new DownloadItem(
            project.Title + " " + version.VersionNumber,
            new Uri(file.Url),
            Path.Combine(targetDirectory, subfolder, SanitizeFileName(file.FileName)),
            Sha512: string.IsNullOrWhiteSpace(file.Sha512) ? null : file.Sha512,
            Size: file.Size > 0 ? file.Size : null,
            Kind: subfolder switch
            {
                "resourcepacks" => DownloadKind.ResourcePack,
                "shaderpacks" => DownloadKind.Shader,
                "modpacks" => DownloadKind.Loader,
                _ => DownloadKind.Mod
            });
        return Task.FromResult(item);
    }

    private static string SanitizeFileName(string filename)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(filename.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "download.bin" : sanitized;
    }

    private sealed class SearchResponse
    {
        [JsonPropertyName("hits")]
        public List<SearchHit> Hits { get; set; } = [];

        [JsonPropertyName("offset")]
        public int Offset { get; set; }

        [JsonPropertyName("limit")]
        public int Limit { get; set; }

        [JsonPropertyName("total_hits")]
        public int TotalHits { get; set; }
    }

    private sealed class SearchHit
    {
        [JsonPropertyName("project_id")]
        public string ProjectId { get; set; } = "";

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("project_type")]
        public string ProjectType { get; set; } = "";

        [JsonPropertyName("icon_url")]
        public string? IconUrl { get; set; }

        [JsonPropertyName("downloads")]
        public long Downloads { get; set; }

        [JsonPropertyName("versions")]
        public List<string>? Versions { get; set; }

        [JsonPropertyName("categories")]
        public List<string>? Categories { get; set; }
    }

    private sealed class VersionResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("project_id")]
        public string ProjectId { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("version_number")]
        public string VersionNumber { get; set; } = "";

        [JsonPropertyName("version_type")]
        public string VersionType { get; set; } = "";

        [JsonPropertyName("game_versions")]
        public List<string> GameVersions { get; set; } = [];

        [JsonPropertyName("loaders")]
        public List<string> Loaders { get; set; } = [];

        [JsonPropertyName("dependencies")]
        public List<DependencyResponse>? Dependencies { get; set; }

        [JsonPropertyName("files")]
        public List<FileResponse> Files { get; set; } = [];
    }

    private sealed class DependencyResponse
    {
        [JsonPropertyName("project_id")]
        public string? ProjectId { get; set; }

        [JsonPropertyName("dependency_type")]
        public string DependencyType { get; set; } = "";
    }

    private sealed class FileResponse
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("filename")]
        public string Filename { get; set; } = "";

        [JsonPropertyName("primary")]
        public bool Primary { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("hashes")]
        public HashResponse? Hashes { get; set; }
    }

    private sealed class HashResponse
    {
        [JsonPropertyName("sha512")]
        public string? Sha512 { get; set; }
    }
}
