namespace PotatoLauncher.Core.Services;

public static class MavenPath
{
    public static string FromCoordinate(string coordinate)
    {
        var parts = coordinate.Split(':');
        if (parts.Length < 3)
        {
            throw new FormatException($"无效的 Maven 坐标：{coordinate}");
        }

        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[3]) ? "-" + parts[3] : "";
        var extension = parts.Length >= 5 && parts[3].Equals("ext", StringComparison.OrdinalIgnoreCase)
            ? parts[4]
            : "jar";
        return $"{group}/{artifact}/{version}/{artifact}-{version}{classifier}.{extension}".Replace('\\', '/');
    }

    public static bool IsNativeLibrary(string coordinate)
    {
        var lower = coordinate.ToLowerInvariant();
        return lower.Contains(":natives-") || lower.Contains(":natives:") ||
               lower.Contains("natives-windows") || lower.Contains("natives-linux") ||
               lower.Contains("natives-macos");
    }
}
