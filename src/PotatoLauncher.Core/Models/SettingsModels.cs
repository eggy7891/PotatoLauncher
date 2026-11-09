using System.Text.Json.Serialization;

namespace PotatoLauncher.Core.Models;

public enum ThemeMode
{
    Dark,
    Light,
    System
}

public sealed class LauncherSettings
{
    public int SchemaVersion { get; set; } = 1;
    public ThemeMode ThemeMode { get; set; } = ThemeMode.Dark;
    public string SeedColor { get; set; } = "#E9A84C";
    public bool ReducedMotion { get; set; }
    public string MinecraftDirectory { get; set; } = "";
    public int DownloadConcurrency { get; set; } = 16;
    public string MirrorBaseUrl { get; set; } = "https://bmclapi2.bangbang93.com";
    public string? JavaOverride { get; set; }
    public int MemoryMb { get; set; } = 4096;
    public int WindowWidth { get; set; } = 854;
    public int WindowHeight { get; set; } = 480;
    public bool VersionIsolation { get; set; } = true;
    public string ExtraJvmArguments { get; set; } = "";
    public string? LastInstanceId { get; set; }
    public string? LastAccountId { get; set; }
    public string MicrosoftClientId { get; set; } = "";
    public string MicrosoftRedirectUri { get; set; } = "http://localhost:53682/callback";
    public string DataDirectory { get; set; } = "";
    public int WindowX { get; set; }
    public int WindowY { get; set; }
    public int LauncherWidth { get; set; } = 1180;
    public int LauncherHeight { get; set; } = 760;
}

public sealed class LauncherState
{
    public int SchemaVersion { get; set; } = 1;
    public List<AccountSession> Accounts { get; set; } = [];
    public string? SelectedAccountId { get; set; }
    public List<string> RecentInstanceIds { get; set; } = [];
}

public sealed record AppPaths(
    string Root,
    string SettingsFile,
    string AccountsFile,
    string InstancesFile,
    string TexturesFile,
    string TokenFile,
    string CacheDirectory,
    string LogsDirectory,
    string DownloadsDirectory,
    string AuthlibInjectorFile)
{
    public static AppPaths Create(string root)
    {
        var full = Path.GetFullPath(root);
        return new AppPaths(
            full,
            Path.Combine(full, "settings.json"),
            Path.Combine(full, "accounts.json"),
            Path.Combine(full, "instances.json"),
            Path.Combine(full, "textures.json"),
            Path.Combine(full, "tokens.bin"),
            Path.Combine(full, "cache"),
            Path.Combine(full, "logs"),
            Path.Combine(full, "downloads"),
            Path.Combine(full, "dependencies", "authlib-injector-1.2.8.jar"));
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(DownloadsDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(AuthlibInjectorFile)!);
    }
}
