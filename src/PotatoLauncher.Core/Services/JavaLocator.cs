using System.Diagnostics;
using System.Text.RegularExpressions;
using PotatoLauncher.Core.Models;

namespace PotatoLauncher.Core.Services;

public interface IJavaLocator
{
    Task<IReadOnlyList<JavaRuntime>> DiscoverAsync(CancellationToken cancellationToken = default);
    Task<JavaRuntime?> InspectAsync(string executablePath, CancellationToken cancellationToken = default);
    Task<JavaRuntime?> SelectBestAsync(int? requiredMajor, string? overridePath, CancellationToken cancellationToken = default);
}

public sealed partial class JavaLocator : IJavaLocator
{
    public async Task<IReadOnlyList<JavaRuntime>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var executableName = OperatingSystem.IsWindows() ? "java.exe" : "java";

        AddFromPath(candidates, executableName);
        AddJavaHome(candidates, Environment.GetEnvironmentVariable("JAVA_HOME"), executableName);

        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jdks"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "runtime"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages")
        };

        foreach (var root in roots.Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path)))
        {
            AddJavaHomesUnder(candidates, root, executableName, maxDepth: root.EndsWith("Packages", StringComparison.OrdinalIgnoreCase) ? 8 : 3);
        }

        var results = new List<JavaRuntime>();
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runtime = await InspectAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (runtime is not null && results.All(item => !item.HomePath.Equals(runtime.HomePath, StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(runtime);
            }
        }

        return results
            .OrderByDescending(runtime => runtime.MajorVersion)
            .ThenBy(runtime => runtime.HomePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<JavaRuntime?> InspectAsync(string executablePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = "-XshowSettings:properties -version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false) + "\n" + await errorTask.ConfigureAwait(false);

            var version = MatchProperty(output, "java.version");
            var home = MatchProperty(output, "java.home");
            var vendor = MatchProperty(output, "java.vendor");
            if (string.IsNullOrWhiteSpace(version))
            {
                var shortVersion = Regex.Match(output, @"version\s+""([^""]+)""", RegexOptions.IgnoreCase);
                version = shortVersion.Success ? shortVersion.Groups[1].Value : null;
            }

            if (string.IsNullOrWhiteSpace(version))
            {
                return null;
            }

            var major = ParseMajor(version);
            return new JavaRuntime(
                executablePath,
                string.IsNullOrWhiteSpace(home) ? Directory.GetParent(executablePath)?.Parent?.FullName ?? "" : home,
                major,
                version,
                string.IsNullOrWhiteSpace(vendor) ? "Unknown" : vendor);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task<JavaRuntime?> SelectBestAsync(
        int? requiredMajor,
        string? overridePath,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var runtimePath = overridePath.EndsWith("java.exe", StringComparison.OrdinalIgnoreCase)
                ? overridePath
                : Path.Combine(overridePath, OperatingSystem.IsWindows() ? "java.exe" : "bin/java");
            return await InspectAsync(runtimePath, cancellationToken).ConfigureAwait(false);
        }

        var all = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        if (requiredMajor is null)
        {
            return all.FirstOrDefault();
        }

        return all.FirstOrDefault(item => item.MajorVersion == requiredMajor)
               ?? all.Where(item => item.MajorVersion > requiredMajor).OrderBy(item => item.MajorVersion).FirstOrDefault()
               ?? all.OrderByDescending(item => item.MajorVersion).FirstOrDefault();
    }

    public static int ParseMajor(string version)
    {
        var normalized = version.Trim();
        if (normalized.StartsWith("1.", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }
        var match = Regex.Match(normalized, @"^\d+");
        return match.Success ? int.Parse(match.Value) : 0;
    }

    private static string? MatchProperty(string output, string key)
    {
        var match = Regex.Match(output, $@"^\s*{Regex.Escape(key)}\s*=\s*(.+?)\s*$", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static void AddFromPath(ISet<string> candidates, string executableName)
    {
        foreach (var path in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }
            var candidate = Path.Combine(path.Trim('"'), executableName);
            if (File.Exists(candidate))
            {
                candidates.Add(Path.GetFullPath(candidate));
            }
        }
    }

    private static void AddJavaHome(ISet<string> candidates, string? home, string executableName)
    {
        if (string.IsNullOrWhiteSpace(home))
        {
            return;
        }
        var candidate = home.EndsWith(executableName, StringComparison.OrdinalIgnoreCase)
            ? home
            : Path.Combine(home, "bin", executableName);
        if (File.Exists(candidate))
        {
            candidates.Add(Path.GetFullPath(candidate));
        }
    }

    private static void AddJavaHomesUnder(ISet<string> candidates, string root, string executableName, int maxDepth)
    {
        try
        {
            var rootCandidate = Path.Combine(root, "bin", executableName);
            if (File.Exists(rootCandidate))
            {
                candidates.Add(Path.GetFullPath(rootCandidate));
            }
            foreach (var java in EnumerateFilesBounded(root, executableName, maxDepth, new EnumerationOptions
                     {
                         IgnoreInaccessible = true,
                         RecurseSubdirectories = true,
                         AttributesToSkip = FileAttributes.ReparsePoint,
                         MaxRecursionDepth = maxDepth
                     }))
            {
                var directory = Path.GetFileName(Path.GetDirectoryName(java));
                if (directory?.Equals("bin", StringComparison.OrdinalIgnoreCase) == true)
                {
                    candidates.Add(Path.GetFullPath(java));
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Discovery is best-effort. A user can always select javaw.exe manually.
        }
    }

    private static IEnumerable<string> EnumerateFilesBounded(
        string root,
        string fileName,
        int maxDepth,
        EnumerationOptions options)
    {
        var rootDepth = root.Count(c => c == Path.DirectorySeparatorChar);
        foreach (var directory in Directory.EnumerateDirectories(root, "*", options))
        {
            var depth = directory.Count(c => c == Path.DirectorySeparatorChar) - rootDepth;
            if (depth > maxDepth)
            {
                continue;
            }

            var candidate = Path.Combine(directory, "bin", fileName);
            if (File.Exists(candidate))
            {
                yield return candidate;
            }
        }
    }
}
