using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using PotatoLauncher.Core.Models;

namespace PotatoLauncher.Core.Services;

public interface ILaunchPlanner
{
    Task<LaunchPlan> BuildAsync(
        LaunchRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class LaunchPlanner : ILaunchPlanner
{
    private readonly IVersionResolver _resolver;

    public LaunchPlanner(IVersionResolver resolver)
    {
        _resolver = resolver;
    }

    public async Task<LaunchPlan> BuildAsync(
        LaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        var minecraftDirectory = Path.GetFullPath(request.MinecraftDirectory);
        var resolved = await _resolver.ResolveAsync(
            minecraftDirectory,
            request.Instance.Id,
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(resolved.Metadata.MainClass))
        {
            throw new InvalidDataException("版本 JSON 缺少 mainClass。");
        }

        var gameDirectory = Path.GetFullPath(request.GameDirectory);
        Directory.CreateDirectory(gameDirectory);
        var assetRoot = Path.Combine(minecraftDirectory, "assets");
        var librariesRoot = Path.Combine(minecraftDirectory, "libraries");
        var nativeDirectory = Path.Combine(gameDirectory, request.Instance.Id + "-natives");
        Directory.CreateDirectory(nativeDirectory);

        var classpathEntries = new List<string>();
        foreach (var lineageId in resolved.Lineage.AsEnumerable().Reverse())
        {
            var versionJar = Path.Combine(minecraftDirectory, "versions", lineageId, lineageId + ".jar");
            if (File.Exists(versionJar))
            {
                classpathEntries.Add(versionJar);
            }
        }

        foreach (var library in resolved.Metadata.Libraries.Where(library => RuleEvaluator.IsAllowed(library.Rules)))
        {
            var artifact = library.Downloads?.Artifact;
            var relativeFromUrl = artifact is null && !string.IsNullOrWhiteSpace(library.Url)
                ? MavenPath.FromCoordinate(library.Name)
                : null;
            if (artifact is null && relativeFromUrl is null)
            {
                continue;
            }
            var relative = artifact?.Path ?? relativeFromUrl!;
            var path = Path.Combine(librariesRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path))
            {
                classpathEntries.Add(path);
            }

            var nativeClassifier = ResolveNativeClassifier(library);
            if (!string.IsNullOrWhiteSpace(nativeClassifier) &&
                library.Downloads?.Classifiers?.TryGetValue(nativeClassifier, out var nativeDownload) == true)
            {
                var nativeRelative = nativeDownload.Path ?? MavenPath.FromCoordinate(library.Name + ":" + nativeClassifier);
                var nativeArchive = Path.Combine(librariesRoot, nativeRelative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(nativeArchive))
                {
                    ExtractNatives(nativeArchive, nativeDirectory, library.Extract?.Exclude ?? []);
                }
            }
        }

        var classpath = string.Join(Path.PathSeparator, classpathEntries.Distinct(StringComparer.OrdinalIgnoreCase));
        var assetIndex = resolved.Metadata.AssetIndex?.Id ?? resolved.Metadata.Assets ?? "legacy";
        var loggingArgument = resolved.Metadata.Logging?.Client?.Argument;
        var loggingFile = resolved.Metadata.Logging?.Client?.File;

        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_player_name"] = request.Account.Name,
            ["version_name"] = request.Instance.Id,
            ["game_directory"] = gameDirectory,
            ["assets_root"] = assetRoot,
            ["assets_index_name"] = assetIndex,
            ["auth_uuid"] = request.Account.Uuid.Replace("-", ""),
            ["auth_access_token"] = request.Account.AccessToken,
            ["auth_session"] = $"token:{request.Account.AccessToken}:{request.Account.Uuid.Replace("-", "")}",
            ["clientid"] = request.Account.Kind == AccountKind.Microsoft ? request.MicrosoftClientId ?? "" : "",
            ["auth_xuid"] = request.Account.Xuid ?? "",
            ["user_type"] = request.Account.Kind == AccountKind.Microsoft ? "msa" : "legacy",
            ["version_type"] = "PotatoLauncher",
            ["natives_directory"] = nativeDirectory,
            ["launcher_name"] = "PotatoLauncher",
            ["launcher_version"] = "1.0.1",
            ["classpath"] = classpath,
            ["classpath_separator"] = Path.PathSeparator.ToString(),
            ["library_directory"] = librariesRoot,
            ["resolution_width"] = request.Width.ToString(),
            ["resolution_height"] = request.Height.ToString(),
            ["user_properties"] = "{}"
        };
        if (loggingFile is not null)
        {
            placeholders["path"] = Path.Combine(
                assetRoot,
                "log_configs",
                Path.GetFileName(new Uri(loggingFile.Url).LocalPath));
        }

        var jwtArgs = resolved.Metadata.Arguments?.Jvm
            .Where(argument => RuleEvaluator.IsAllowed(argument.Rules, CreateFeatureMap(request)))
            .SelectMany(argument => argument.Values)
            .SelectMany(value => ExpandArgument(value, placeholders))
            .ToList() ?? [];

        if (!jwtArgs.Any(arg => arg.StartsWith("-cp", StringComparison.Ordinal)) &&
            !jwtArgs.Any(arg => arg == "-classpath"))
        {
            jwtArgs.Add("-cp");
            jwtArgs.Add(Expand(classpath, placeholders));
        }

        if (!string.IsNullOrWhiteSpace(loggingArgument) && loggingFile is not null)
        {
            jwtArgs.Add(Expand(loggingArgument, placeholders));
        }

        jwtArgs.Insert(0, $"-Xmx{Math.Clamp(request.MemoryMb, 512, 65536)}m");
        jwtArgs.Add("-Dminecraft.launcher.brand=PotatoLauncher");
        jwtArgs.Add("-Dminecraft.launcher.version=1.0.1");

        if (request.TextureServer is not null &&
            request.Account.Kind == AccountKind.Offline &&
            File.Exists(request.AuthlibInjectorPath))
        {
            jwtArgs.Add($"-javaagent:{request.AuthlibInjectorPath}={request.TextureServer.BaseUri}");
        }

        foreach (var extra in request.ExtraJvmArguments.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            jwtArgs.AddRange(Tokenize(extra).SelectMany(value => ExpandArgument(value, placeholders)));
        }

        var gameArgs = BuildGameArguments(resolved.Metadata, request, placeholders);
        var args = new List<string>();
        args.AddRange(jwtArgs);
        args.Add(resolved.Metadata.MainClass);
        args.AddRange(gameArgs);

        var redacted = args.Select(arg =>
            string.IsNullOrWhiteSpace(request.Account.AccessToken)
                ? arg
                : arg.Replace(request.Account.AccessToken, "****", StringComparison.Ordinal)).ToArray();
        var redactedCommand = request.Java.ExecutablePath + " " +
                              string.Join(' ', redacted.Select(QuoteForDisplay));
        return new LaunchPlan(
            request.Java.ExecutablePath,
            args,
            gameDirectory,
            new Dictionary<string, string>(),
            resolved.Metadata.MainClass,
            classpath,
            nativeDirectory,
            redactedCommand);
    }

    private static IReadOnlyList<string> BuildGameArguments(
        VersionMetadata metadata,
        LaunchRequest request,
        IReadOnlyDictionary<string, string> placeholders)
    {
        var result = new List<string>();
        if (metadata.Arguments?.Game is { Count: > 0 } argumentList)
        {
            foreach (var argument in argumentList.Where(argument =>
                         RuleEvaluator.IsAllowed(argument.Rules, CreateFeatureMap(request))))
            {
                result.AddRange(argument.Values.SelectMany(value => ExpandArgument(value, placeholders)));
            }
        }
        else if (!string.IsNullOrWhiteSpace(metadata.LegacyMinecraftArguments))
        {
            result.AddRange(Tokenize(metadata.LegacyMinecraftArguments)
                .SelectMany(value => ExpandArgument(value, placeholders)));
        }

        if (!string.IsNullOrWhiteSpace(request.ServerAddress))
        {
            var address = request.ServerAddress.Trim();
            var colon = address.LastIndexOf(':');
            var host = address;
            var port = "25565";
            if (colon > 0 && int.TryParse(address[(colon + 1)..], out _))
            {
                host = address[..colon];
                port = address[(colon + 1)..];
            }
            result.Add("--server");
            result.Add(host);
            result.Add("--port");
            result.Add(port);
        }
        return result;
    }

    private static IReadOnlyDictionary<string, bool> CreateFeatureMap(LaunchRequest request) =>
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["is_demo_user"] = false,
            ["has_custom_resolution"] = request.Width > 0 && request.Height > 0,
            ["has_quick_plays_support"] = false,
            ["is_quick_play_singleplayer"] = false,
            ["is_quick_play_multiplayer"] = false,
            ["is_quick_play_realms"] = false
        };

    private static string? ResolveNativeClassifier(LibraryEntry library)
    {
        if (library.Natives is null)
        {
            return null;
        }
        var key = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        if (!library.Natives.TryGetValue(key, out var classifier))
        {
            return null;
        }
        return classifier
            .Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32", StringComparison.Ordinal)
            .Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32", StringComparison.Ordinal);
    }

    private static void ExtractNatives(string archivePath, string destination, IReadOnlyList<string> exclusions)
    {
        Directory.CreateDirectory(destination);
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) ||
                exclusions.Any(exclusion => entry.FullName.StartsWith(exclusion.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(Path.GetFullPath(destination) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("natives 归档包含路径穿越。");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static IEnumerable<string> ExpandArgument(string argument, IReadOnlyDictionary<string, string> placeholders)
    {
        yield return Expand(argument, placeholders);
    }

    private static string Expand(string value, IReadOnlyDictionary<string, string> placeholders)
    {
        foreach (var placeholder in placeholders)
        {
            value = value.Replace("${" + placeholder.Key + "}", placeholder.Value, StringComparison.Ordinal);
        }
        return value;
    }

    public static IReadOnlyList<string> Tokenize(string commandLine)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        for (var index = 0; index < commandLine.Length; index++)
        {
            var ch = commandLine[index];
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }
            current.Append(ch);
        }
        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }
        return result;
    }

    private static string QuoteForDisplay(string value) =>
        value.Contains(' ') ? $"\"{value}\"" : value;
}
