using System.IO;
using System.Net.Http;
using System.Text;
using PotatoLauncher.Core.Models;
using PotatoLauncher.Core.Services;

namespace PotatoLauncher.App.Services;

public sealed class AppRuntime
{
    private AppRuntime(
        AppPaths paths,
        SettingsService settings,
        AccountStore accounts,
        DialogService dialogs,
        ThemeService theme,
        TextureFileService textures,
        IJavaLocator javaLocator,
        IVersionResolver resolver,
        IVersionInstaller installer,
        ILaunchPlanner launchPlanner,
        IProcessLauncher processLauncher,
        IMicrosoftAuthService microsoftAuth,
        IModrinthClient modrinth,
        IDownloadQueue downloads,
        AuthlibInjectorManager authlib)
    {
        Paths = paths;
        Settings = settings;
        Accounts = accounts;
        Dialogs = dialogs;
        Theme = theme;
        Textures = textures;
        JavaLocator = javaLocator;
        Resolver = resolver;
        Installer = installer;
        LaunchPlanner = launchPlanner;
        ProcessLauncher = processLauncher;
        MicrosoftAuth = microsoftAuth;
        Modrinth = modrinth;
        Downloads = downloads;
        Authlib = authlib;
    }

    public AppPaths Paths { get; }
    public SettingsService Settings { get; }
    public AccountStore Accounts { get; }
    public DialogService Dialogs { get; }
    public ThemeService Theme { get; }
    public TextureFileService Textures { get; }
    public IJavaLocator JavaLocator { get; }
    public IVersionResolver Resolver { get; }
    public IVersionInstaller Installer { get; }
    public ILaunchPlanner LaunchPlanner { get; }
    public IProcessLauncher ProcessLauncher { get; }
    public IMicrosoftAuthService MicrosoftAuth { get; }
    public IModrinthClient Modrinth { get; }
    public IDownloadQueue Downloads { get; }
    public AuthlibInjectorManager Authlib { get; }
    public string CurrentLogFile { get; private set; } = "";

    public static async Task<AppRuntime> CreateAsync()
    {
        var paths = SettingsService.ResolveDataPaths();
        var settings = new SettingsService(paths);
        await settings.LoadAsync();
        EnsureAuxiliarySchemaFiles(paths);
        var accounts = new AccountStore(paths);
        await accounts.LoadAsync();
        var apiHttp = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
        var downloadHttp = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var resolver = new VersionResolver();
        var downloads = new DownloadQueue(downloadHttp);
        var theme = new ThemeService();
        theme.Apply(settings.Settings.ThemeMode, settings.Settings.SeedColor, settings.Settings.ReducedMotion);
        var runtime = new AppRuntime(
            paths,
            settings,
            accounts,
            new DialogService(),
            theme,
            new TextureFileService(paths),
            new JavaLocator(),
            resolver,
            new VersionInstaller(apiHttp, downloads, resolver),
            new LaunchPlanner(resolver),
            new ProcessLauncher(),
            new MicrosoftAuthService(apiHttp),
            new ModrinthClient(apiHttp),
            downloads,
            new AuthlibInjectorManager(paths, downloadHttp));
        runtime.StartLogFile();
        return runtime;
    }

    private static void EnsureAuxiliarySchemaFiles(AppPaths paths)
    {
        var schemas = new[]
        {
            (paths.InstancesFile, """{"schemaVersion":1,"instances":[]}"""),
            (paths.TexturesFile, """{"schemaVersion":1,"textures":{}}""")
        };
        foreach (var (path, content) in schemas)
        {
            if (!File.Exists(path))
            {
                File.WriteAllText(path, content, Encoding.UTF8);
            }
        }
    }

    public void Log(string message)
    {
        var line = $"[{DateTimeOffset.Now:HH:mm:ss}] {message}";
        Directory.CreateDirectory(Paths.LogsDirectory);
        File.AppendAllText(CurrentLogFile, line + Environment.NewLine, Encoding.UTF8);
    }

    private void StartLogFile()
    {
        Directory.CreateDirectory(Paths.LogsDirectory);
        CurrentLogFile = Path.Combine(Paths.LogsDirectory, $"launcher-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        File.WriteAllText(CurrentLogFile,
            $"PotatoLauncher 1.0.2{Environment.NewLine}" +
            $"Data: {Paths.Root}{Environment.NewLine}" +
            $"Minecraft: {Settings.Settings.MinecraftDirectory}{Environment.NewLine}{Environment.NewLine}",
            Encoding.UTF8);
    }
}
