using System.IO;
using System.Text.Json;
using PotatoLauncher.Core.Models;

namespace PotatoLauncher.App.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SettingsService(AppPaths paths)
    {
        Paths = paths;
    }

    public AppPaths Paths { get; }
    public LauncherSettings Settings { get; private set; } = new();

    public async Task LoadAsync()
    {
        Paths.EnsureDirectories();
        if (File.Exists(Paths.SettingsFile))
        {
            try
            {
                Settings = JsonSerializer.Deserialize<LauncherSettings>(
                    await File.ReadAllTextAsync(Paths.SettingsFile), JsonOptions) ?? new LauncherSettings();
            }
            catch (JsonException)
            {
                Settings = new LauncherSettings();
            }
        }

        if (string.IsNullOrWhiteSpace(Settings.MinecraftDirectory))
        {
            var roaming = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ".minecraft");
            Settings.MinecraftDirectory = Directory.Exists(roaming)
                ? roaming
                : Path.Combine(Paths.Root, "game", ".minecraft");
        }
        Settings.DataDirectory = Paths.Root;
        Directory.CreateDirectory(Settings.MinecraftDirectory);
    }

    public async Task SaveAsync()
    {
        Paths.EnsureDirectories();
        var temp = Paths.SettingsFile + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(Settings, JsonOptions));
        File.Move(temp, Paths.SettingsFile, overwrite: true);
    }

    public static AppPaths ResolveDataPaths()
    {
        var portableRoot = Path.Combine(AppContext.BaseDirectory, "data");
        try
        {
            Directory.CreateDirectory(portableRoot);
            var probe = Path.Combine(portableRoot, ".write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return AppPaths.Create(portableRoot);
        }
        catch (UnauthorizedAccessException)
        {
            var fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PotatoLauncher");
            return AppPaths.Create(fallback);
        }
    }
}
