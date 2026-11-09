using System.Text.Json;
using PotatoLauncher.Core.Models;

namespace PotatoLauncher.Core.Services;

public interface IVersionResolver
{
    Task<IReadOnlyList<InstanceDescriptor>> ScanAsync(string minecraftDirectory, CancellationToken cancellationToken = default);
    Task<ResolvedVersion> ResolveAsync(string minecraftDirectory, string versionId, CancellationToken cancellationToken = default);
    Task<VersionMetadata> ReadMetadataAsync(string minecraftDirectory, string versionId, CancellationToken cancellationToken = default);
}

public sealed record ResolvedVersion(
    string RequestedId,
    VersionMetadata Metadata,
    IReadOnlyList<string> Lineage,
    LoaderKind Loader,
    string? GameVersion,
    string JsonPath);

public sealed class VersionResolver : IVersionResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public async Task<IReadOnlyList<InstanceDescriptor>> ScanAsync(
        string minecraftDirectory,
        CancellationToken cancellationToken = default)
    {
        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
        if (!Directory.Exists(versionsDirectory))
        {
            return [];
        }

        var result = new List<InstanceDescriptor>();
        foreach (var versionDirectory in Directory.EnumerateDirectories(versionsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Path.GetFileName(versionDirectory);
            var jsonPath = FindVersionJsonPath(versionDirectory, id);
            if (jsonPath is null)
            {
                continue;
            }

            try
            {
                var metadata = await ReadMetadataFromPathAsync(jsonPath, cancellationToken).ConfigureAwait(false);
                var lineage = await BuildLineageAsync(minecraftDirectory, metadata, cancellationToken).ConfigureAwait(false);
                var loader = DetectLoader(metadata.Id, metadata.Type, lineage);
                var gameVersion = DetectGameVersion(metadata.Id, lineage);
                result.Add(new InstanceDescriptor(
                    id,
                    string.IsNullOrWhiteSpace(metadata.Id) ? id : metadata.Id,
                    versionDirectory,
                    jsonPath,
                    metadata.Type,
                    gameVersion,
                    loader,
                    metadata.JavaVersion?.MajorVersion,
                    File.GetLastWriteTimeUtc(jsonPath)));
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
            {
                result.Add(new InstanceDescriptor(
                    id,
                    id,
                    versionDirectory,
                    jsonPath,
                    "invalid",
                    null,
                    LoaderKind.Unknown,
                    null,
                    File.Exists(jsonPath) ? File.GetLastWriteTimeUtc(jsonPath) : DateTimeOffset.MinValue));
            }
        }

        return result
            .OrderByDescending(instance => instance.LastModified)
            .ThenBy(instance => instance.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task<ResolvedVersion> ResolveAsync(
        string minecraftDirectory,
        string versionId,
        CancellationToken cancellationToken = default)
    {
        var jsonPath = GetVersionJsonPath(minecraftDirectory, versionId);
        var child = await ReadMetadataFromPathAsync(jsonPath, cancellationToken).ConfigureAwait(false);
        var lineage = new List<string> { versionId };
        var chain = new List<VersionMetadata>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = child;
        var currentPath = jsonPath;

        while (true)
        {
            if (!visited.Add(current.Id))
            {
                throw new InvalidDataException($"检测到循环继承：{string.Join(" -> ", lineage)}");
            }

            chain.Add(current);
            if (string.IsNullOrWhiteSpace(current.InheritsFrom))
            {
                break;
            }

            lineage.Add(current.InheritsFrom);
            currentPath = GetVersionJsonPath(minecraftDirectory, current.InheritsFrom);
            current = await ReadMetadataFromPathAsync(currentPath, cancellationToken).ConfigureAwait(false);
        }

        var merged = Merge(chain);
        var loader = DetectLoader(child.Id, child.Type, lineage);
        var gameVersion = DetectGameVersion(child.Id, lineage);
        return new ResolvedVersion(versionId, merged, lineage, loader, gameVersion, jsonPath);
    }

    public Task<VersionMetadata> ReadMetadataAsync(
        string minecraftDirectory,
        string versionId,
        CancellationToken cancellationToken = default)
    {
        return ReadMetadataFromPathAsync(GetVersionJsonPath(minecraftDirectory, versionId), cancellationToken);
    }

    public static string GetVersionJsonPath(string minecraftDirectory, string versionId)
    {
        var directory = Path.Combine(minecraftDirectory, "versions", versionId);
        var canonical = Path.Combine(directory, versionId + ".json");
        if (File.Exists(canonical))
        {
            return canonical;
        }

        return FindVersionJsonPath(directory, versionId) ?? canonical;
    }

    private static string? FindVersionJsonPath(string directory, string preferredId)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var canonical = Path.Combine(directory, preferredId + ".json");
        if (File.Exists(canonical))
        {
            return canonical;
        }

        var candidates = Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .Where(path => !Path.GetFileName(path).Equals("PCL.json", StringComparison.OrdinalIgnoreCase))
            .Where(path =>
            {
                var name = Path.GetFileNameWithoutExtension(path);
                return !name.Equals("usercache", StringComparison.OrdinalIgnoreCase) &&
                       !name.Equals("usernamecache", StringComparison.OrdinalIgnoreCase) &&
                       !name.Equals("launcher_profiles", StringComparison.OrdinalIgnoreCase) &&
                       !name.Equals("options", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        var versionCandidates = new List<string>();
        foreach (var candidate in candidates)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(candidate));
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("id", out _) &&
                    (root.TryGetProperty("mainClass", out _) ||
                     root.TryGetProperty("inheritsFrom", out _) ||
                     root.TryGetProperty("arguments", out _) ||
                     root.TryGetProperty("minecraftArguments", out _) ||
                     root.TryGetProperty("libraries", out _)))
                {
                    versionCandidates.Add(candidate);
                }
            }
            catch (JsonException)
            {
                // Ignore unrelated or malformed JSON files inside an instance directory.
            }
        }

        return versionCandidates
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .FirstOrDefault();
    }

    private static async Task<VersionMetadata> ReadMetadataFromPathAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("未找到版本 JSON。", path);
        }

        await using var stream = File.OpenRead(path);
        var metadata = await JsonSerializer.DeserializeAsync<VersionMetadata>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.Id))
        {
            throw new InvalidDataException("版本 JSON 缺少 id。");
        }

        return metadata;
    }

    private static async Task<IReadOnlyList<string>> BuildLineageAsync(
        string minecraftDirectory,
        VersionMetadata metadata,
        CancellationToken cancellationToken)
    {
        var lineage = new List<string> { metadata.Id };
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = metadata;
        while (!string.IsNullOrWhiteSpace(current.InheritsFrom) && visited.Add(current.InheritsFrom))
        {
            lineage.Add(current.InheritsFrom);
            var parentPath = GetVersionJsonPath(minecraftDirectory, current.InheritsFrom);
            if (!File.Exists(parentPath))
            {
                break;
            }
            current = await ReadMetadataFromPathAsync(parentPath, cancellationToken).ConfigureAwait(false);
        }
        return lineage;
    }

    private static VersionMetadata Merge(IReadOnlyList<VersionMetadata> childToParent)
    {
        var parentToChild = childToParent.Reverse().ToArray();
        var root = parentToChild[0];
        var result = new VersionMetadata
        {
            Id = childToParent[0].Id,
            InheritsFrom = childToParent[0].InheritsFrom,
            Type = childToParent[0].Type,
            MainClass = childToParent.Select(item => item.MainClass).LastOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            Assets = childToParent.Select(item => item.Assets).LastOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            AssetIndex = childToParent.Select(item => item.AssetIndex).LastOrDefault(value => value is not null),
            LegacyMinecraftArguments = childToParent.Select(item => item.LegacyMinecraftArguments)
                .LastOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            JavaVersion = childToChildJavaVersion(childToParent),
            Logging = childToParent.Select(item => item.Logging).LastOrDefault(value => value?.Client is not null),
            ReleaseTime = childToParent.Select(item => item.ReleaseTime).LastOrDefault(value => value is not null)
        };

        foreach (var entry in parentToChild.SelectMany(item => item.Downloads))
        {
            result.Downloads[entry.Key] = entry.Value;
        }

        var libraries = new List<LibraryEntry>();
        foreach (var library in parentToChild.SelectMany(item => item.Libraries))
        {
            var key = GetLibraryKey(library);
            var existingIndex = libraries.FindIndex(item => GetLibraryKey(item) == key);
            if (existingIndex >= 0)
            {
                libraries.RemoveAt(existingIndex);
            }
            libraries.Add(library);
        }
        result.Libraries = libraries;

        result.Arguments = new VersionArguments
        {
            Game = parentToChild.SelectMany(item => item.Arguments?.Game ?? []).ToList(),
            Jvm = parentToChild.SelectMany(item => item.Arguments?.Jvm ?? []).ToList()
        };
        return result;

        static JavaVersionRequirement? childToChildJavaVersion(IReadOnlyList<VersionMetadata> versions) =>
            versions.Select(item => item.JavaVersion).FirstOrDefault(value => value is not null);
    }

    private static string GetLibraryKey(LibraryEntry library)
    {
        var parts = library.Name.Split(':');
        return string.Join(':', parts.Take(Math.Min(parts.Length, 4)));
    }

    private static LoaderKind DetectLoader(string id, string type, IReadOnlyList<string> lineage)
    {
        var text = string.Join(' ', lineage.Append(id).Append(type));
        if (text.Contains("neoforge", StringComparison.OrdinalIgnoreCase))
        {
            return LoaderKind.NeoForge;
        }
        if (text.Contains("quilt", StringComparison.OrdinalIgnoreCase))
        {
            return LoaderKind.Quilt;
        }
        if (text.Contains("fabric", StringComparison.OrdinalIgnoreCase))
        {
            return LoaderKind.Fabric;
        }
        if (text.Contains("forge", StringComparison.OrdinalIgnoreCase))
        {
            return LoaderKind.Forge;
        }
        return type.Equals("release", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("snapshot", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("old_beta", StringComparison.OrdinalIgnoreCase) ||
               type.Equals("old_alpha", StringComparison.OrdinalIgnoreCase)
            ? LoaderKind.Vanilla
            : LoaderKind.Unknown;
    }

    private static string? DetectGameVersion(string id, IReadOnlyList<string> lineage)
    {
        foreach (var candidate in lineage)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(candidate, @"^\d+\.\d+(?:\.\d+)?$"))
            {
                return candidate;
            }
        }

        var match = System.Text.RegularExpressions.Regex.Match(id, @"(?:^|[-_])(\d+\.\d+(?:\.\d+)?)");
        return match.Success ? match.Groups[1].Value : null;
    }
}

public static class RuleEvaluator
{
    private static readonly IReadOnlyDictionary<string, bool> DefaultFeatures =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["is_demo_user"] = false,
            ["has_custom_resolution"] = false,
            ["has_quick_plays_support"] = false,
            ["is_quick_play_singleplayer"] = false,
            ["is_quick_play_multiplayer"] = false,
            ["is_quick_play_realms"] = false
        };

    public static bool IsAllowed(IReadOnlyList<RuleEntry>? rules)
    {
        if (rules is null || rules.Count == 0)
        {
            return true;
        }

        var allowed = false;
        foreach (var rule in rules)
        {
            if (Matches(rule))
            {
                allowed = rule.Action.Equals("allow", StringComparison.OrdinalIgnoreCase);
            }
        }
        return allowed;
    }

    public static bool IsAllowed(IReadOnlyList<RuleEntry>? rules, IReadOnlyDictionary<string, bool> features)
    {
        if (rules is null || rules.Count == 0)
        {
            return true;
        }

        var allowed = false;
        foreach (var rule in rules)
        {
            if (Matches(rule, features))
            {
                allowed = rule.Action.Equals("allow", StringComparison.OrdinalIgnoreCase);
            }
        }
        return allowed;
    }

    private static bool Matches(RuleEntry rule) => Matches(rule, DefaultFeatures);

    private static bool Matches(RuleEntry rule, IReadOnlyDictionary<string, bool> features)
    {
        if (rule.Os is not null)
        {
            if (!string.IsNullOrWhiteSpace(rule.Os.Name) &&
                !rule.Os.Name.Equals("windows", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(rule.Os.Arch))
            {
                var current = Environment.Is64BitOperatingSystem ? "x86_64" : "x86";
                if (!rule.Os.Arch.Equals(current, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        if (rule.Features is not null)
        {
            foreach (var feature in rule.Features)
            {
                if (!features.TryGetValue(feature.Key, out var current) || current != feature.Value)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
