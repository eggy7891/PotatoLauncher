using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using PotatoLauncher.Core.Models;

namespace PotatoLauncher.Core.Services;

public interface IVersionInstaller
{
    Task<IReadOnlyList<VersionManifestEntry>> GetVanillaVersionsAsync(
        bool includeSnapshots,
        CancellationToken cancellationToken = default);

    Task<string> InstallVanillaAsync(
        string minecraftDirectory,
        VersionManifestEntry version,
        int concurrency,
        string mirrorBaseUrl,
        Func<MirrorDecision, Task<bool>> approveMirror,
        IProgress<DownloadProgress>? progress = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetLoaderVersionsAsync(
        string gameVersion,
        LoaderKind loader,
        CancellationToken cancellationToken = default);

    Task<string> InstallLoaderAsync(
        string minecraftDirectory,
        string gameVersion,
        LoaderKind loader,
        string loaderVersion,
        string javaExecutable,
        int concurrency,
        string mirrorBaseUrl,
        Func<MirrorDecision, Task<bool>> approveMirror,
        IProgress<DownloadProgress>? progress = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default);
}

public sealed record VersionManifestEntry(
    string Id,
    string Type,
    string Url,
    string? Sha1,
    DateTimeOffset? ReleaseTime);

public sealed class VersionInstaller : IVersionInstaller
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly HttpClient _httpClient;
    private readonly IDownloadQueue _downloadQueue;
    private readonly IVersionResolver _resolver;

    public VersionInstaller(HttpClient httpClient, IDownloadQueue downloadQueue, IVersionResolver resolver)
    {
        _httpClient = httpClient;
        _downloadQueue = downloadQueue;
        _resolver = resolver;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PotatoLauncher/1.0.1 (+https://github.com/eggy7891/PotatoLauncher)");
    }

    public async Task<IReadOnlyList<VersionManifestEntry>> GetVanillaVersionsAsync(
        bool includeSnapshots,
        CancellationToken cancellationToken = default)
    {
        var manifest = await _httpClient.GetFromJsonAsync<MojangVersionManifest>(
            "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
            JsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Mojang 版本清单为空。");

        return manifest.Versions
            .Where(item => includeSnapshots ||
                           item.Type.Equals("release", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.ReleaseTime)
            .Select(item => new VersionManifestEntry(item.Id, item.Type, item.Url, item.Sha1, item.ReleaseTime))
            .ToArray();
    }

    public async Task<string> InstallVanillaAsync(
        string minecraftDirectory,
        VersionManifestEntry version,
        int concurrency,
        string mirrorBaseUrl,
        Func<MirrorDecision, Task<bool>> approveMirror,
        IProgress<DownloadProgress>? progress = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        log?.Invoke($"开始安装 Minecraft {version.Id}");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", version.Id);
        Directory.CreateDirectory(versionDirectory);
        var versionJsonPath = Path.Combine(versionDirectory, version.Id + ".json");

        var versionJsonUri = new Uri(version.Url);
        await _downloadQueue.DownloadAsync(new DownloadPlan(
            $"Minecraft {version.Id}",
            version.Id,
            [new DownloadItem(
                version.Id + ".json",
                versionJsonUri,
                versionJsonPath,
                Sha1: version.Sha1,
                MirrorUri: MirrorUriFactory.Create(versionJsonUri, mirrorBaseUrl),
                Kind: DownloadKind.VersionJson)]),
            concurrency,
            approveMirror,
            progress,
            log,
            cancellationToken).ConfigureAwait(false);

        var resolved = await _resolver.ResolveAsync(minecraftDirectory, version.Id, cancellationToken).ConfigureAwait(false);
        await EnsureVersionFilesAsync(
            minecraftDirectory,
            resolved,
            concurrency,
            mirrorBaseUrl,
            approveMirror,
            progress,
            log,
            cancellationToken).ConfigureAwait(false);
        log?.Invoke($"Minecraft {version.Id} 安装完成");
        return version.Id;
    }

    public async Task<IReadOnlyList<string>> GetLoaderVersionsAsync(
        string gameVersion,
        LoaderKind loader,
        CancellationToken cancellationToken = default)
    {
        if (loader is LoaderKind.Fabric or LoaderKind.Quilt)
        {
            var baseUri = loader == LoaderKind.Fabric
                ? "https://meta.fabricmc.net/v2/versions/loader/"
                : "https://meta.quiltmc.org/v3/versions/loader/";
            var entries = await _httpClient.GetFromJsonAsync<List<FabricLikeLoaderEntry>>(
                baseUri + Uri.EscapeDataString(gameVersion),
                JsonOptions,
                cancellationToken).ConfigureAwait(false) ?? [];
            return entries
                .Where(item => item.Loader is not null)
                .OrderByDescending(item => item.Loader!.Stable)
                .ThenByDescending(item => ParseVersion(item.Loader!.Version))
                .Select(item => item.Loader!.Version)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (loader == LoaderKind.Forge)
        {
            using var response = await _httpClient.GetAsync(
                "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json",
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var promos = root?["promos"]?.AsObject();
            if (promos is null)
            {
                return [];
            }

            var versions = new List<string>();
            foreach (var suffix in new[] { "recommended", "latest" })
            {
                if (promos[$"{gameVersion}-{suffix}"]?.GetValue<string>() is { } value)
                {
                    versions.Add(value);
                }
            }
            return versions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        if (loader == LoaderKind.NeoForge)
        {
            using var response = await _httpClient.GetAsync(
                "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge",
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var versions = root?["versions"]?.AsArray()
                .Select(node => node?.GetValue<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .ToArray() ?? [];
            var neoPrefix = gameVersion switch
            {
                "1.20.1" => "47.1",
                _ when gameVersion.StartsWith("1.", StringComparison.OrdinalIgnoreCase) => gameVersion[2..],
                _ => gameVersion
            };
            return versions
                .Where(value => value.StartsWith(neoPrefix + ".", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(ParseVersion)
                .Take(30)
                .ToArray();
        }

        return [];
    }

    public async Task<string> InstallLoaderAsync(
        string minecraftDirectory,
        string gameVersion,
        LoaderKind loader,
        string loaderVersion,
        string javaExecutable,
        int concurrency,
        string mirrorBaseUrl,
        Func<MirrorDecision, Task<bool>> approveMirror,
        IProgress<DownloadProgress>? progress = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        return loader switch
        {
            LoaderKind.Fabric => await InstallFabricLikeAsync(
                minecraftDirectory, gameVersion, loaderVersion, false, concurrency, mirrorBaseUrl,
                approveMirror, progress, log, cancellationToken).ConfigureAwait(false),
            LoaderKind.Quilt => await InstallFabricLikeAsync(
                minecraftDirectory, gameVersion, loaderVersion, true, concurrency, mirrorBaseUrl,
                approveMirror, progress, log, cancellationToken).ConfigureAwait(false),
            LoaderKind.Forge => await InstallForgeLikeAsync(
                minecraftDirectory, gameVersion, loaderVersion, false, javaExecutable, concurrency,
                mirrorBaseUrl, approveMirror, progress, log, cancellationToken).ConfigureAwait(false),
            LoaderKind.NeoForge => await InstallForgeLikeAsync(
                minecraftDirectory, gameVersion, loaderVersion, true, javaExecutable, concurrency,
                mirrorBaseUrl, approveMirror, progress, log, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException($"不支持的加载器：{loader}")
        };
    }

    private async Task<string> InstallFabricLikeAsync(
        string minecraftDirectory,
        string gameVersion,
        string loaderVersion,
        bool quilt,
        int concurrency,
        string mirrorBaseUrl,
        Func<MirrorDecision, Task<bool>> approveMirror,
        IProgress<DownloadProgress>? progress,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var endpoint = quilt
            ? $"https://meta.quiltmc.org/v3/versions/loader/{Uri.EscapeDataString(gameVersion)}/{Uri.EscapeDataString(loaderVersion)}/profile/json"
            : $"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(gameVersion)}/{Uri.EscapeDataString(loaderVersion)}/profile/json";
        var profile = await _httpClient.GetStringAsync(endpoint, cancellationToken).ConfigureAwait(false);
        var node = JsonNode.Parse(profile) ?? throw new InvalidDataException("加载器 profile 为空。");
        var profileId = node["id"]?.GetValue<string>() ?? $"{gameVersion}-{(quilt ? "Quilt" : "Fabric")}";
        var instanceId = quilt
            ? $"{gameVersion}-Quilt {loaderVersion}"
            : $"{gameVersion}-Fabric {loaderVersion}";
        node["id"] = instanceId;

        var instanceDirectory = Path.Combine(minecraftDirectory, "versions", instanceId);
        Directory.CreateDirectory(instanceDirectory);
        var path = Path.Combine(instanceDirectory, instanceId + ".json");
        await File.WriteAllTextAsync(
            path,
            node.ToJsonString(JsonOptions),
            cancellationToken).ConfigureAwait(false);

        var resolved = await _resolver.ResolveAsync(minecraftDirectory, instanceId, cancellationToken).ConfigureAwait(false);
        await EnsureVersionFilesAsync(
            minecraftDirectory, resolved, concurrency, mirrorBaseUrl, approveMirror, progress, log,
            cancellationToken).ConfigureAwait(false);
        log?.Invoke($"已安装 {profileId} 为 {instanceId}");
        return instanceId;
    }

    private async Task<string> InstallForgeLikeAsync(
        string minecraftDirectory,
        string gameVersion,
        string loaderVersion,
        bool neoForge,
        string javaExecutable,
        int concurrency,
        string mirrorBaseUrl,
        Func<MirrorDecision, Task<bool>> approveMirror,
        IProgress<DownloadProgress>? progress,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var installerUri = neoForge
            ? new Uri($"https://maven.neoforged.net/releases/net/neoforged/neoforge/{loaderVersion}/neoforge-{loaderVersion}-installer.jar")
            : new Uri($"https://maven.minecraftforge.net/net/minecraftforge/forge/{gameVersion}-{loaderVersion}/forge-{gameVersion}-{loaderVersion}-installer.jar");
        var tempDirectory = Path.Combine(minecraftDirectory, ".potato", "installers");
        Directory.CreateDirectory(tempDirectory);
        var installerPath = Path.Combine(tempDirectory, Path.GetFileName(installerUri.LocalPath));

        await _downloadQueue.DownloadAsync(new DownloadPlan(
            "加载器安装器",
            gameVersion,
            [new DownloadItem(
                Path.GetFileName(installerPath),
                installerUri,
                installerPath,
                MirrorUri: MirrorUriFactory.Create(installerUri, mirrorBaseUrl),
                Kind: DownloadKind.Loader)]),
            concurrency,
            approveMirror,
            progress,
            log,
            cancellationToken).ConfigureAwait(false);

        var launcherProfiles = Path.Combine(minecraftDirectory, "launcher_profiles.json");
        if (!File.Exists(launcherProfiles))
        {
            await File.WriteAllTextAsync(
                launcherProfiles,
                """{"profiles":{},"settings":{},"version":3}""",
                cancellationToken).ConfigureAwait(false);
        }

        log?.Invoke("正在运行加载器安装器，这可能需要几分钟。");
        var psi = new ProcessStartInfo
        {
            FileName = javaExecutable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = minecraftDirectory
        };
        psi.ArgumentList.Add("-jar");
        psi.ArgumentList.Add(installerPath);
        psi.ArgumentList.Add("--installClient");
        psi.ArgumentList.Add(minecraftDirectory);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动加载器安装器。");
        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                log?.Invoke(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                log?.Invoke(args.Data);
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"加载器安装器退出代码：{process.ExitCode}");
        }

        var expectedPrefix = neoForge ? $"{gameVersion}-neoforge-" : $"{gameVersion}-forge-";
        var installed = Directory.EnumerateDirectories(Path.Combine(minecraftDirectory, "versions"))
            .Select(Path.GetFileName)
            .Where(name => name is not null &&
                           (name.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) ||
                            name.Contains(loaderVersion, StringComparison.OrdinalIgnoreCase) &&
                            name.Contains(neoForge ? "neoforge" : "forge", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(name => name)
            .FirstOrDefault();
        if (installed is null)
        {
            throw new InvalidDataException("安装器完成，但没有找到新版本目录。请检查安装日志。");
        }

        var resolved = await _resolver.ResolveAsync(minecraftDirectory, installed, cancellationToken).ConfigureAwait(false);
        await EnsureVersionFilesAsync(
            minecraftDirectory, resolved, concurrency, mirrorBaseUrl, approveMirror, progress, log,
            cancellationToken).ConfigureAwait(false);
        return installed;
    }

    private async Task EnsureVersionFilesAsync(
        string minecraftDirectory,
        ResolvedVersion resolved,
        int concurrency,
        string mirrorBaseUrl,
        Func<MirrorDecision, Task<bool>> approveMirror,
        IProgress<DownloadProgress>? progress,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var items = new List<DownloadItem>();
        if (resolved.Metadata.Downloads.TryGetValue("client", out var clientDownload) &&
            !string.IsNullOrWhiteSpace(clientDownload.Url))
        {
            var baseVersionId = resolved.Lineage.Last();
            items.Add(CreateDownload("client.jar", clientDownload,
                Path.Combine(minecraftDirectory, "versions", baseVersionId, baseVersionId + ".jar"),
                DownloadKind.Client, mirrorBaseUrl));
        }

        foreach (var library in resolved.Metadata.Libraries.Where(library => RuleEvaluator.IsAllowed(library.Rules)))
        {
            var artifact = library.Downloads?.Artifact;
            if (artifact is null || string.IsNullOrWhiteSpace(artifact.Url))
            {
                if (string.IsNullOrWhiteSpace(library.Url))
                {
                    continue;
                }
                var relativeFromUrl = MavenPath.FromCoordinate(library.Name);
                var baseUri = new Uri(library.Url.EndsWith('/') ? library.Url : library.Url + "/");
                var uri = library.Url.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(library.Url)
                    : new Uri(baseUri, relativeFromUrl);
                items.Add(new DownloadItem(
                    Path.GetFileName(relativeFromUrl),
                    uri,
                    Path.Combine(minecraftDirectory, "libraries",
                        relativeFromUrl.Replace('/', Path.DirectorySeparatorChar)),
                    MirrorUri: MirrorUriFactory.Create(uri, mirrorBaseUrl),
                    Kind: DownloadKind.Library));
                continue;
            }
            var relativePath = artifact.Path ?? MavenPath.FromCoordinate(library.Name);
            items.Add(CreateDownload(Path.GetFileName(relativePath), artifact,
                Path.Combine(minecraftDirectory, "libraries", relativePath.Replace('/', Path.DirectorySeparatorChar)),
                DownloadKind.Library, mirrorBaseUrl));

            if (library.Downloads?.Classifiers is { Count: > 0 } classifiers)
            {
                var osKey = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "osx" : "linux";
                if (library.Natives?.TryGetValue(osKey, out var nativeClassifier) == true)
                {
                    nativeClassifier = nativeClassifier.Replace(
                        "${arch}",
                        Environment.Is64BitOperatingSystem ? "64" : "32",
                        StringComparison.Ordinal);
                    if (classifiers.TryGetValue(nativeClassifier, out var nativeDownload) &&
                        !string.IsNullOrWhiteSpace(nativeDownload.Url))
                    {
                        var nativeRelative = nativeDownload.Path ??
                                             MavenPath.FromCoordinate(library.Name + ":" + nativeClassifier);
                        items.Add(CreateDownload(Path.GetFileName(nativeRelative), nativeDownload,
                            Path.Combine(minecraftDirectory, "libraries",
                                nativeRelative.Replace('/', Path.DirectorySeparatorChar)),
                            DownloadKind.Library, mirrorBaseUrl));
                    }
                }
            }
        }

        if (resolved.Metadata.AssetIndex is { } assetIndex && !string.IsNullOrWhiteSpace(assetIndex.Url))
        {
            var assetIndexPath = Path.Combine(minecraftDirectory, "assets", "indexes", assetIndex.Id + ".json");
            items.Add(CreateDownload(assetIndex.Id + ".json",
                new VersionDownload { Url = assetIndex.Url, Sha1 = assetIndex.Sha1, Size = assetIndex.Size, Path = assetIndexPath },
                assetIndexPath, DownloadKind.AssetIndex, mirrorBaseUrl));
            await _downloadQueue.DownloadAsync(new DownloadPlan("资源索引", resolved.RequestedId, items),
                concurrency, approveMirror, progress, log, cancellationToken).ConfigureAwait(false);

            var assetIndexJson = await File.ReadAllTextAsync(assetIndexPath, cancellationToken).ConfigureAwait(false);
            var parsed = JsonNode.Parse(assetIndexJson);
            if (parsed?["objects"] is JsonObject objects)
            {
                foreach (var property in objects)
                {
                    if (property.Value?["hash"]?.GetValue<string>() is not { Length: > 1 } hash)
                    {
                        continue;
                    }
                    var resourceUri = new Uri($"https://resources.download.minecraft.net/{hash[..2]}/{hash}");
                    var destination = Path.Combine(minecraftDirectory, "assets", "objects", hash[..2], hash);
                    items.Add(new DownloadItem(
                        property.Key,
                        resourceUri,
                        destination,
                        Sha1: hash,
                        Size: property.Value["size"]?.GetValue<long>(),
                        MirrorUri: MirrorUriFactory.Create(resourceUri, mirrorBaseUrl),
                        Kind: DownloadKind.Asset));
                }
            }
        }

        if (resolved.Metadata.Logging?.Client?.File is { } loggingFile &&
            !string.IsNullOrWhiteSpace(loggingFile.Url))
        {
            var loggingPath = Path.Combine(minecraftDirectory, "assets", "log_configs",
                Path.GetFileName(new Uri(loggingFile.Url).LocalPath));
            items.Add(CreateDownload(Path.GetFileName(loggingPath), loggingFile, loggingPath,
                DownloadKind.Logging, mirrorBaseUrl));
        }

        await _downloadQueue.DownloadAsync(new DownloadPlan(resolved.RequestedId, resolved.RequestedId, items),
            concurrency, approveMirror, progress, log, cancellationToken).ConfigureAwait(false);
    }

    private static DownloadItem CreateDownload(
        string name,
        VersionDownload source,
        string destination,
        DownloadKind kind,
        string mirrorBaseUrl)
    {
        var primary = new Uri(source.Url);
        return new DownloadItem(
            name,
            primary,
            destination,
            Sha1: source.Sha1,
            Size: source.Size > 0 ? source.Size : null,
            MirrorUri: MirrorUriFactory.Create(primary, mirrorBaseUrl),
            Kind: kind);
    }

    private static VersionNumber ParseVersion(string value)
    {
        var pieces = value.Split(['.', '-', '+'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number
                : 0)
            .ToArray();
        return new VersionNumber(pieces);
    }
}

public static class MirrorUriFactory
{
    public static Uri? Create(Uri officialUri, string mirrorBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(mirrorBaseUrl))
        {
            return null;
        }
        if (!Uri.TryCreate(mirrorBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var mirror))
        {
            return null;
        }

        var path = officialUri.AbsolutePath.TrimStart('/');
        if (officialUri.Host.Equals("libraries.minecraft.net", StringComparison.OrdinalIgnoreCase))
        {
            path = "maven/" + path;
        }
        else if (officialUri.Host.Equals("piston-meta.mojang.com", StringComparison.OrdinalIgnoreCase) ||
                 officialUri.Host.Equals("launchermeta.mojang.com", StringComparison.OrdinalIgnoreCase) ||
                 officialUri.Host.Equals("piston-data.mojang.com", StringComparison.OrdinalIgnoreCase))
        {
            // Mojang hosts are mirrored at the same path.
        }
        else if (officialUri.Host.Equals("resources.download.minecraft.net", StringComparison.OrdinalIgnoreCase))
        {
            path = "assets/" + path;
        }
        else
        {
            return null;
        }

        return new Uri(mirror, path + officialUri.Query);
    }
}

internal sealed record VersionNumber(int[] Parts) : IComparable<VersionNumber>
{
    public int CompareTo(VersionNumber? other)
    {
        if (other is null)
        {
            return 1;
        }
        var length = Math.Max(Parts.Length, other.Parts.Length);
        for (var index = 0; index < length; index++)
        {
            var left = index < Parts.Length ? Parts[index] : 0;
            var right = index < other.Parts.Length ? other.Parts[index] : 0;
            if (left != right)
            {
                return left.CompareTo(right);
            }
        }
        return 0;
    }
}

internal sealed class MojangVersionManifest
{
    public List<MojangVersionEntry> Versions { get; set; } = [];
}

internal sealed class MojangVersionEntry
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Sha1 { get; set; }
    public DateTimeOffset ReleaseTime { get; set; }
}

internal sealed class FabricLikeLoaderEntry
{
    public FabricLoader? Loader { get; set; }
}

internal sealed class FabricLoader
{
    public string Version { get; set; } = "";
    public bool Stable { get; set; }
}
