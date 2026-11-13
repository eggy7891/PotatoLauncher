using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using PotatoLauncher.App.Infrastructure;
using PotatoLauncher.App.Services;
using PotatoLauncher.Core.Models;
using PotatoLauncher.Core.Services;
using ThemeMode = PotatoLauncher.Core.Models.ThemeMode;

namespace PotatoLauncher.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AppRuntime _runtime;
    private string _selectedPage = "home";
    private InstanceDescriptor? _selectedInstance;
    private AccountSession? _selectedAccount;
    private JavaRuntime? _selectedJava;
    private bool _isBusy;
    private string _statusText = "准备就绪";
    private double _progressValue;
    private bool _isProgressIndeterminate;
    private VersionManifestEntry? _selectedVanillaVersion;
    private bool _includeSnapshots = true;
    private LoaderKind _selectedLoader = LoaderKind.Fabric;
    private string _loaderGameVersion = "1.20.1";
    private string? _selectedLoaderVersion;
    private string _selectedDownloadSection = "vanilla";
    private CatalogProject? _selectedCatalogProject;
    private CatalogVersion? _selectedCatalogVersion;
    private string? _selectedProjectGameVersion;
    private string? _selectedProjectLoader;
    private InstanceDescriptor? _selectedDownloadInstance;
    private bool _isProjectDetailVisible;
    private string _catalogStatus = "进入下载页后会自动刷新";
    private string _newOfflineName = "";
    private bool _showLogPanel;
    private int _busyDepth;

    public MainViewModel(AppRuntime runtime)
    {
        _runtime = runtime;
        Navigation =
        [
            new NavigationItem("home", "首页", "M3 12L12 3l9 9v9h-6v-6H9v6H3z"),
            new NavigationItem("instances", "实例", "M4 5h16v5H4zm0 9h16v5H4zM7 7h2m-2 9h2"),
            new NavigationItem("downloads", "下载", "M12 3v12m0 0l-4-4m4 4l4-4M5 20h14"),
            new NavigationItem("accounts", "账号与外观", "M12 12a4 4 0 100-8 4 4 0 000 8zm-8 8a8 8 0 0116 0"),
            new NavigationItem("tasks", "任务与日志", "M5 5h14v14H5zm4 4h6m-6 4h6"),
            new NavigationItem("settings", "设置", "M12 8a4 4 0 100 8 4 4 0 000-8zm0-5v3m0 12v3M3 12h3m12 0h3")
        ];
        Navigation[0].IsSelected = true;
        DownloadSections =
        [
            new DownloadSectionItem("vanilla", "原版游戏", null) { IsSelected = true },
            new DownloadSectionItem("mod", "模组", CatalogContentType.Mod),
            new DownloadSectionItem("resourcepack", "资源包", CatalogContentType.ResourcePack),
            new DownloadSectionItem("shader", "光影", CatalogContentType.Shader),
            new DownloadSectionItem("modpack", "整合包", CatalogContentType.Modpack)
        ];
        NavigateCommand = new RelayCommand(parameter =>
        {
            if (parameter is string page)
            {
                Navigate(page);
            }
        });
        RefreshInstancesCommand = new AsyncRelayCommand(RefreshInstancesAsync);
        ChooseMinecraftFolderCommand = new AsyncRelayCommand(ChooseMinecraftFolderAsync);
        RefreshJavaCommand = new AsyncRelayCommand(RefreshJavaAsync);
        LaunchCommand = new AsyncRelayCommand(LaunchAsync, () => SelectedInstance is not null);
        AddOfflineAccountCommand = new AsyncRelayCommand(AddOfflineAccountAsync);
        MicrosoftLoginCommand = new AsyncRelayCommand(MicrosoftLoginAsync);
        RemoveAccountCommand = new AsyncRelayCommand(RemoveAccountAsync, () => SelectedAccount is not null);
        ImportSkinCommand = new AsyncRelayCommand(parameter => ImportTextureAsync(parameter as string == "cape"));
        SaveSettingsCommand = new AsyncRelayCommand(SaveAndApplySettingsAsync);
        ApplyThemeCommand = new AsyncRelayCommand(ApplyThemeAsync);
        LoadVanillaVersionsCommand = new AsyncRelayCommand(LoadVanillaVersionsAsync);
        InstallVanillaCommand = new AsyncRelayCommand(parameter =>
            InstallVanillaVersionAsync(parameter as VersionManifestEntry ?? SelectedVanillaVersion));
        LoadLoaderVersionsCommand = new AsyncRelayCommand(LoadLoaderVersionsAsync);
        InstallLoaderCommand = new AsyncRelayCommand(InstallLoaderAsync, () => !string.IsNullOrWhiteSpace(SelectedLoaderVersion));
        SelectDownloadSectionCommand = new AsyncRelayCommand(parameter =>
            SelectDownloadSectionAsync(parameter as string ?? "vanilla"));
        OpenCatalogProjectCommand = new AsyncRelayCommand(parameter =>
            OpenCatalogProjectAsync(parameter as CatalogProject ?? SelectedCatalogProject));
        CloseCatalogProjectCommand = new RelayCommand(CloseCatalogProject);
        DownloadCatalogVersionCommand = new AsyncRelayCommand(
            DownloadCatalogVersionAsync,
            () => SelectedCatalogVersion is not null && SelectedDownloadInstance is not null);
        OpenDataFolderCommand = new RelayCommand(OpenDataFolder);
        OpenLogFolderCommand = new RelayCommand(OpenLogFolder);
        ClearVisibleLogCommand = new RelayCommand(() =>
        {
            Logs.Clear();
            Log("日志显示已清空；磁盘日志仍保留。");
        });
        ToggleLogPanelCommand = new RelayCommand(() => ShowLogPanel = !ShowLogPanel);
        Settings = runtime.Settings.Settings;
    }

    public LauncherSettings Settings { get; }
    public ObservableCollection<NavigationItem> Navigation { get; }
    public ObservableCollection<DownloadSectionItem> DownloadSections { get; }
    public ObservableCollection<InstanceDescriptor> Instances { get; } = [];
    public ObservableCollection<AccountSession> Accounts { get; } = [];
    public ObservableCollection<JavaRuntime> JavaRuntimes { get; } = [];
    public ObservableCollection<VersionManifestEntry> VanillaVersions { get; } = [];
    public ObservableCollection<string> GameVersionChoices { get; } = [];
    public ObservableCollection<string> LoaderVersions { get; } = [];
    public ObservableCollection<CatalogProject> CatalogProjects { get; } = [];
    public ObservableCollection<CatalogVersion> ProjectVersions { get; } = [];
    public ObservableCollection<CatalogVersion> FilteredProjectVersions { get; } = [];
    public ObservableCollection<string> ProjectGameVersions { get; } = [];
    public ObservableCollection<string> ProjectLoaders { get; } = [];
    public ObservableCollection<InstanceDescriptor> MatchingDownloadInstances { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];
    public IReadOnlyList<LoaderKind> LoaderKinds { get; } =
        [LoaderKind.Fabric, LoaderKind.Quilt, LoaderKind.Forge, LoaderKind.NeoForge];
    public IReadOnlyList<ThemeMode> ThemeModes { get; } = [ThemeMode.Dark, ThemeMode.Light, ThemeMode.System];

    public string SelectedPage
    {
        get => _selectedPage;
        private set => SetProperty(ref _selectedPage, value);
    }

    public InstanceDescriptor? SelectedInstance
    {
        get => _selectedInstance;
        set
        {
            if (SetProperty(ref _selectedInstance, value))
            {
                Settings.LastInstanceId = value?.Id;
                LaunchCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(SelectedInstanceSummary));
            }
        }
    }

    public string SelectedInstanceSummary => SelectedInstance is null
        ? "尚未选择实例"
        : $"{SelectedInstance.LoaderLabel} · {SelectedInstance.GameVersion ?? "未知版本"}";

    public AccountSession? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (SetProperty(ref _selectedAccount, value))
            {
                _runtime.Accounts.State.SelectedAccountId = value?.Id;
                RemoveAccountCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(SelectedAccountName));
                OnPropertyChanged(nameof(SelectedAccountKind));
            }
        }
    }

    public string SelectedAccountName => SelectedAccount?.Name ?? "未选择账号";
    public string SelectedAccountKind => SelectedAccount?.KindLabel ?? "";

    public JavaRuntime? SelectedJava
    {
        get => _selectedJava;
        set => SetProperty(ref _selectedJava, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetProperty(ref _isProgressIndeterminate, value);
    }

    public VersionManifestEntry? SelectedVanillaVersion
    {
        get => _selectedVanillaVersion;
        set
        {
            if (SetProperty(ref _selectedVanillaVersion, value))
            {
                if (value is not null)
                {
                    LoaderGameVersion = value.Id;
                }
            }
        }
    }

    public bool IncludeSnapshots
    {
        get => _includeSnapshots;
        set => SetProperty(ref _includeSnapshots, value);
    }

    public LoaderKind SelectedLoader
    {
        get => _selectedLoader;
        set => SetProperty(ref _selectedLoader, value);
    }

    public string LoaderGameVersion
    {
        get => _loaderGameVersion;
        set => SetProperty(ref _loaderGameVersion, value);
    }

    public string? SelectedLoaderVersion
    {
        get => _selectedLoaderVersion;
        set
        {
            if (SetProperty(ref _selectedLoaderVersion, value))
            {
                InstallLoaderCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SelectedDownloadSection
    {
        get => _selectedDownloadSection;
        private set
        {
            if (SetProperty(ref _selectedDownloadSection, value))
            {
                OnPropertyChanged(nameof(IsVanillaSection));
                OnPropertyChanged(nameof(IsCatalogSection));
                OnPropertyChanged(nameof(IsCatalogListVisible));
                OnPropertyChanged(nameof(DownloadSectionTitle));
            }
        }
    }

    public bool IsVanillaSection => SelectedDownloadSection == "vanilla";
    public bool IsCatalogSection => !IsVanillaSection;

    public string DownloadSectionTitle => DownloadSections
        .FirstOrDefault(section => section.Key == SelectedDownloadSection)?.Label ?? "";

    public bool IsProjectDetailVisible
    {
        get => _isProjectDetailVisible;
        private set
        {
            if (SetProperty(ref _isProjectDetailVisible, value))
            {
                OnPropertyChanged(nameof(IsCatalogListVisible));
            }
        }
    }

    public bool IsCatalogListVisible => IsCatalogSection && !IsProjectDetailVisible;

    public CatalogProject? SelectedCatalogProject
    {
        get => _selectedCatalogProject;
        private set
        {
            if (SetProperty(ref _selectedCatalogProject, value))
            {
                OnPropertyChanged(nameof(ShowCatalogLoaderFilter));
            }
        }
    }

    public bool ShowCatalogLoaderFilter =>
        SelectedCatalogProject?.ContentType == CatalogContentType.Mod;

    public CatalogVersion? SelectedCatalogVersion
    {
        get => _selectedCatalogVersion;
        set
        {
            if (SetProperty(ref _selectedCatalogVersion, value))
            {
                DownloadCatalogVersionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? SelectedProjectGameVersion
    {
        get => _selectedProjectGameVersion;
        set
        {
            if (SetProperty(ref _selectedProjectGameVersion, value))
            {
                RefreshCatalogVersionFilters();
            }
        }
    }

    public string? SelectedProjectLoader
    {
        get => _selectedProjectLoader;
        set
        {
            if (SetProperty(ref _selectedProjectLoader, value))
            {
                RefreshCatalogVersionFilters();
            }
        }
    }

    public InstanceDescriptor? SelectedDownloadInstance
    {
        get => _selectedDownloadInstance;
        set
        {
            if (SetProperty(ref _selectedDownloadInstance, value))
            {
                DownloadCatalogVersionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string CatalogStatus
    {
        get => _catalogStatus;
        private set => SetProperty(ref _catalogStatus, value);
    }

    public string NewOfflineName
    {
        get => _newOfflineName;
        set => SetProperty(ref _newOfflineName, value);
    }

    public bool ShowLogPanel
    {
        get => _showLogPanel;
        private set => SetProperty(ref _showLogPanel, value);
    }

    public RelayCommand NavigateCommand { get; }
    public AsyncRelayCommand RefreshInstancesCommand { get; }
    public AsyncRelayCommand ChooseMinecraftFolderCommand { get; }
    public AsyncRelayCommand RefreshJavaCommand { get; }
    public AsyncRelayCommand LaunchCommand { get; }
    public AsyncRelayCommand AddOfflineAccountCommand { get; }
    public AsyncRelayCommand MicrosoftLoginCommand { get; }
    public AsyncRelayCommand RemoveAccountCommand { get; }
    public AsyncRelayCommand ImportSkinCommand { get; }
    public AsyncRelayCommand SaveSettingsCommand { get; }
    public AsyncRelayCommand ApplyThemeCommand { get; }
    public AsyncRelayCommand LoadVanillaVersionsCommand { get; }
    public AsyncRelayCommand InstallVanillaCommand { get; }
    public AsyncRelayCommand LoadLoaderVersionsCommand { get; }
    public AsyncRelayCommand InstallLoaderCommand { get; }
    public AsyncRelayCommand SelectDownloadSectionCommand { get; }
    public AsyncRelayCommand OpenCatalogProjectCommand { get; }
    public RelayCommand CloseCatalogProjectCommand { get; }
    public AsyncRelayCommand DownloadCatalogVersionCommand { get; }
    public RelayCommand OpenDataFolderCommand { get; }
    public RelayCommand OpenLogFolderCommand { get; }
    public RelayCommand ClearVisibleLogCommand { get; }
    public RelayCommand ToggleLogPanelCommand { get; }

    public async Task InitializeAsync()
    {
        Log($"数据目录：{_runtime.Paths.Root}");
        Log($"Minecraft 目录：{Settings.MinecraftDirectory}");
        RefreshAccounts();
        await RefreshInstancesAsync();
        await RefreshJavaAsync();
        await ApplyThemeAsync();
    }

    public async Task SaveOnExitAsync()
    {
        Settings.LastInstanceId = SelectedInstance?.Id;
        Settings.LastAccountId = SelectedAccount?.Id;
        await _runtime.Settings.SaveAsync();
        await _runtime.Accounts.SaveAsync();
    }

    private void Navigate(string page)
    {
        SelectedPage = page;
        foreach (var item in Navigation)
        {
            item.IsSelected = item.Key == page;
        }
        if (page == "downloads")
        {
            _ = RefreshDownloadsAsync();
        }
    }

    private async Task RefreshInstancesAsync()
    {
        await RunBusyAsync("正在扫描 Minecraft 实例…", async () =>
        {
            var previous = SelectedInstance?.Id ?? Settings.LastInstanceId;
            var instances = await _runtime.Resolver.ScanAsync(Settings.MinecraftDirectory);
            Instances.Clear();
            foreach (var instance in instances)
            {
                Instances.Add(instance);
            }
            SelectedInstance = Instances.FirstOrDefault(item => item.Id == previous) ??
                               Instances.FirstOrDefault(item => item.Loader != LoaderKind.Unknown) ??
                               Instances.FirstOrDefault();
            if (SelectedInstance?.GameVersion is { } gameVersion)
            {
                LoaderGameVersion = gameVersion;
            }
            Log($"扫描完成，共找到 {Instances.Count} 个实例。");
        });
    }

    private async Task ChooseMinecraftFolderAsync()
    {
        var selected = _runtime.Dialogs.PickFolder("选择 .minecraft 目录", Settings.MinecraftDirectory);
        if (selected is null)
        {
            return;
        }
        if (!Directory.Exists(Path.Combine(selected, "versions")))
        {
            _runtime.Dialogs.Error("所选目录下没有 versions 文件夹，请选择正确的 .minecraft 目录。");
            return;
        }
        Settings.MinecraftDirectory = selected;
        await _runtime.Settings.SaveAsync();
        await RefreshInstancesAsync();
    }

    private async Task RefreshJavaAsync()
    {
        await RunBusyAsync("正在探测 Java…", async () =>
        {
            var currentPath = SelectedJava?.ExecutablePath;
            var runtimes = await _runtime.JavaLocator.DiscoverAsync();
            JavaRuntimes.Clear();
            foreach (var runtime in runtimes)
            {
                JavaRuntimes.Add(runtime);
            }
            SelectedJava = JavaRuntimes.FirstOrDefault(item => item.ExecutablePath == currentPath) ??
                           JavaRuntimes.FirstOrDefault();
            Log($"检测到 {JavaRuntimes.Count} 个 Java 运行时。");
        });
    }

    private async Task LaunchAsync()
    {
        if (SelectedInstance is null || SelectedAccount is null)
        {
            _runtime.Dialogs.Error("请先选择实例和账号。");
            return;
        }

        LocalTextureServer? textureServer = null;
        try
        {
            await RunBusyAsync("正在准备启动…", async () =>
            {
                var resolved = await _runtime.Resolver.ResolveAsync(
                    Settings.MinecraftDirectory,
                    SelectedInstance.Id);
                var java = SelectedJava;
                if (java is null)
                {
                    java = await _runtime.JavaLocator.SelectBestAsync(
                        resolved.Metadata.JavaVersion?.MajorVersion,
                        Settings.JavaOverride);
                }
                if (java is null)
                {
                    throw new InvalidOperationException("没有找到可用的 Java。请安装 Java 或前往设置选择 java.exe。");
                }
                if (resolved.Metadata.JavaVersion?.MajorVersion is { } required &&
                    java.MajorVersion < required)
                {
                    throw new InvalidOperationException(
                        $"该实例需要 Java {required} 或更高版本，当前选择的是 Java {java.MajorVersion}。");
                }

                if (SelectedAccount.Kind == AccountKind.Microsoft &&
                    !SelectedAccount.HasUsableAccessToken)
                {
                    await RefreshMicrosoftAccountAsync();
                }

                string? injector = null;
                TextureBundle? textures = null;
                if (SelectedAccount.Kind == AccountKind.Offline &&
                    (!string.IsNullOrWhiteSpace(SelectedAccount.SkinPath) ||
                     !string.IsNullOrWhiteSpace(SelectedAccount.CapePath)))
                {
                    injector = await _runtime.Authlib.EnsureAsync(Log);
                    textures = new TextureBundle(
                        SelectedAccount.Name,
                        Guid.ParseExact(SelectedAccount.Uuid, "N"),
                        SelectedAccount.SkinPath,
                        SelectedAccount.SkinModel,
                        SelectedAccount.CapePath);
                    textureServer = new LocalTextureServer(textures);
                    textureServer.Start();
                    Log($"离线纹理服务已启动：{textureServer.BaseUri}");
                }

                var gameDirectory = Settings.VersionIsolation
                    ? SelectedInstance.DirectoryPath
                    : Settings.MinecraftDirectory;
                var plan = await _runtime.LaunchPlanner.BuildAsync(new LaunchRequest(
                    SelectedInstance,
                    SelectedAccount.Clone(),
                    java,
                    Settings.MinecraftDirectory,
                    gameDirectory,
                    Settings.MemoryMb,
                    Settings.WindowWidth,
                    Settings.WindowHeight,
                    null,
                    LaunchPlanner.Tokenize(Settings.ExtraJvmArguments),
                    textures,
                    textureServer)
                {
                    AuthlibInjectorPath = injector,
                    MicrosoftClientId = Settings.MicrosoftClientId
                });
                Log(plan.RedactedCommandLine);
                StatusText = $"Minecraft {SelectedInstance.DisplayName} 正在运行";
                var result = await _runtime.ProcessLauncher.LaunchAsync(plan, Log);
                StatusText = result.Succeeded ? "Minecraft 已正常退出" : result.FailureHint ?? "Minecraft 异常退出";
                if (!result.Succeeded)
                {
                    Log(result.FailureHint ?? $"退出代码：{result.ExitCode}");
                    ShowLogPanel = true;
                }
            });
        }
        catch (Exception ex)
        {
            Log("启动失败：" + ex.Message);
            _runtime.Dialogs.Error(ex.Message, "启动失败");
            ShowLogPanel = true;
        }
        finally
        {
            if (textureServer is not null)
            {
                await textureServer.DisposeAsync();
                Log("离线纹理服务已关闭。");
            }
        }
    }

    private void RefreshAccounts()
    {
        Accounts.Clear();
        foreach (var account in _runtime.Accounts.State.Accounts)
        {
            Accounts.Add(account);
        }
        SelectedAccount = Accounts.FirstOrDefault(account =>
                              account.Id == (_runtime.Accounts.State.SelectedAccountId ?? Settings.LastAccountId)) ??
                          Accounts.FirstOrDefault();
    }

    private async Task AddOfflineAccountAsync()
    {
        var name = _runtime.Dialogs.Prompt(
            "新建离线账号",
            "输入游戏内名称（1–16 个字符）。离线账号不能进入需要正版验证的服务器。",
            string.IsNullOrWhiteSpace(NewOfflineName) ? "Player" : NewOfflineName);
        name = name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        if (name.Length is < 1 or > 16)
        {
            _runtime.Dialogs.Error("玩家名称必须为 1–16 个字符。");
            return;
        }
        if (_runtime.Accounts.State.Accounts.Any(account =>
                account.Kind == AccountKind.Offline &&
                account.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            _runtime.Dialogs.Error("已存在同名离线账号。");
            return;
        }
        var account = new AccountSession
        {
            Kind = AccountKind.Offline,
            Name = name,
            Uuid = IdentityUtil.OfflineUuid(name)
        };
        _runtime.Accounts.State.Accounts.Add(account);
        await _runtime.Accounts.SaveAsync();
        RefreshAccounts();
        SelectedAccount = Accounts.FirstOrDefault(item => item.Id == account.Id);
        Log($"已创建离线账号：{name}");
    }

    private async Task MicrosoftLoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Settings.MicrosoftClientId))
        {
            await OpenMicrosoftSetupAsync();
            return;
        }
        try
        {
            await RunBusyAsync("等待微软浏览器授权…", async () =>
            {
                var account = await _runtime.MicrosoftAuth.SignInAsync(
                    new MicrosoftAuthConfiguration(
                        Settings.MicrosoftClientId,
                        Settings.MicrosoftRedirectUri),
                    Log);
                _runtime.Accounts.State.Accounts.Add(account);
                await _runtime.Accounts.SaveAsync();
                RefreshAccounts();
                SelectedAccount = Accounts.FirstOrDefault(item => item.Id == account.Id);
                Log($"微软账号登录成功：{account.Name}");
            });
        }
        catch (Exception ex)
        {
            _runtime.Dialogs.Error(ex.Message, "微软登录失败");
        }
    }

    private async Task OpenMicrosoftSetupAsync()
    {
        if (_runtime.Dialogs.Confirm(
                "微软登录需要一个 Azure 公共客户端 Client ID。是否现在打开注册说明并填写 Client ID？",
                "配置微软登录"))
        {
            Process.Start(new ProcessStartInfo("https://learn.microsoft.com/entra/identity-platform/quickstart-register-app")
            {
                UseShellExecute = true
            });
            var clientId = _runtime.Dialogs.Prompt(
                "Microsoft Client ID",
                "粘贴 Azure 应用的 Application (client) ID。回调地址应为 http://localhost:53682/callback。",
                Settings.MicrosoftClientId);
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                Settings.MicrosoftClientId = clientId.Trim();
                await SaveAndApplySettingsAsync();
            }
        }
    }

    private async Task RefreshMicrosoftAccountAsync()
    {
        if (SelectedAccount is null)
        {
            return;
        }
        var updated = await _runtime.MicrosoftAuth.RefreshAsync(
            SelectedAccount,
            new MicrosoftAuthConfiguration(Settings.MicrosoftClientId, Settings.MicrosoftRedirectUri));
        var index = _runtime.Accounts.State.Accounts.FindIndex(account => account.Id == SelectedAccount.Id);
        if (index >= 0)
        {
            _runtime.Accounts.State.Accounts[index] = updated;
        }
        await _runtime.Accounts.SaveAsync();
        RefreshAccounts();
        SelectedAccount = Accounts.FirstOrDefault(account => account.Id == updated.Id);
        Log("微软账号令牌已刷新。");
    }

    private async Task RemoveAccountAsync()
    {
        if (SelectedAccount is null ||
            !_runtime.Dialogs.Confirm($"确定删除账号“{SelectedAccount.Name}”吗？"))
        {
            return;
        }
        _runtime.Accounts.State.Accounts.RemoveAll(account => account.Id == SelectedAccount.Id);
        await _runtime.Accounts.SaveAsync();
        RefreshAccounts();
        Log("账号已删除。");
    }

    private async Task ImportTextureAsync(bool cape)
    {
        if (SelectedAccount is null)
        {
            _runtime.Dialogs.Error("请先选择账号。");
            return;
        }
        if (SelectedAccount.Kind == AccountKind.Microsoft)
        {
            _runtime.Dialogs.Error(
                "官方账号外观为只读。第三方启动器不能上传官方皮肤或更改官方披风。若要使用离线自定义外观，请新建离线账号。");
            return;
        }
        var path = _runtime.Dialogs.PickFile(
            cape ? "选择披风 PNG" : "选择皮肤 PNG",
            "PNG 图像 (*.png)|*.png",
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        if (path is null)
        {
            return;
        }
        try
        {
            var imported = await _runtime.Textures.ImportAsync(path, cape);
            if (cape)
            {
                SelectedAccount.CapePath = imported;
            }
            else
            {
                SelectedAccount.SkinPath = imported;
                var model = _runtime.Dialogs.Confirm(
                    "是否使用 Slim（Alex）模型？选择“否”使用 Classic（Steve）模型。",
                    "皮肤模型");
                SelectedAccount.SkinModel = model ? "slim" : "classic";
            }
            await _runtime.Accounts.SaveAsync();
            Log(cape ? "披风已导入。" : "皮肤已导入。");
            OnPropertyChanged(nameof(SelectedAccount));
        }
        catch (Exception ex)
        {
            _runtime.Dialogs.Error(ex.Message, "纹理导入失败");
        }
    }

    private async Task SaveAndApplySettingsAsync()
    {
        Settings.DownloadConcurrency = Math.Clamp(Settings.DownloadConcurrency, 4, 32);
        Settings.MemoryMb = Math.Clamp(Settings.MemoryMb, 512, 65536);
        await _runtime.Settings.SaveAsync();
        await ApplyThemeAsync();
        Log("设置已保存。");
        _runtime.Dialogs.Info("设置已保存。");
    }

    private async Task ApplyThemeAsync()
    {
        _runtime.Theme.Apply(Settings.ThemeMode, Settings.SeedColor, Settings.ReducedMotion);
        await _runtime.Settings.SaveAsync();
    }

    private async Task LoadVanillaVersionsAsync()
    {
        await RunBusyAsync("正在获取 Mojang 版本清单…", async () =>
        {
            var versions = await _runtime.Installer.GetVanillaVersionsAsync(true);
            VanillaVersions.Clear();
            GameVersionChoices.Clear();
            foreach (var version in versions)
            {
                VanillaVersions.Add(version);
                GameVersionChoices.Add(version.Id);
            }
            SelectedVanillaVersion = VanillaVersions.FirstOrDefault(version => version.Id == LoaderGameVersion) ??
                                     VanillaVersions.FirstOrDefault();
            Log($"获取到 {VanillaVersions.Count} 个原版版本。");
        });
    }

    private async Task InstallVanillaVersionAsync(VersionManifestEntry? version)
    {
        if (version is null)
        {
            return;
        }
        try
        {
            await RunBusyAsync($"正在安装 Minecraft {version.Id}…", async () =>
            {
                var progress = CreateDownloadProgress();
                var id = await _runtime.Installer.InstallVanillaAsync(
                    Settings.MinecraftDirectory,
                    version,
                    Settings.DownloadConcurrency,
                    Settings.MirrorBaseUrl,
                    ApproveMirrorAsync,
                    progress,
                    Log);
                Log($"原版 {id} 安装完成。");
                await RefreshInstancesAsync();
            });
        }
        catch (Exception ex)
        {
            _runtime.Dialogs.Error(ex.Message, "安装失败");
        }
    }

    private async Task LoadLoaderVersionsAsync()
    {
        await RunBusyAsync("正在获取加载器版本…", async () =>
        {
            var versions = await _runtime.Installer.GetLoaderVersionsAsync(LoaderGameVersion, SelectedLoader);
            LoaderVersions.Clear();
            foreach (var version in versions)
            {
                LoaderVersions.Add(version);
            }
            SelectedLoaderVersion = LoaderVersions.FirstOrDefault();
            Log($"获取到 {LoaderVersions.Count} 个 {SelectedLoader} 版本。");
        });
    }

    private async Task InstallLoaderAsync()
    {
        if (SelectedJava is null)
        {
            _runtime.Dialogs.Error("请先选择一个 Java，Forge/NeoForge 安装器需要使用它。");
            return;
        }
        if (string.IsNullOrWhiteSpace(SelectedLoaderVersion))
        {
            return;
        }
        try
        {
            await RunBusyAsync($"正在安装 {SelectedLoader} {SelectedLoaderVersion}…", async () =>
            {
                var id = await _runtime.Installer.InstallLoaderAsync(
                    Settings.MinecraftDirectory,
                    LoaderGameVersion,
                    SelectedLoader,
                    SelectedLoaderVersion,
                    SelectedJava.ExecutablePath,
                    Settings.DownloadConcurrency,
                    Settings.MirrorBaseUrl,
                    ApproveMirrorAsync,
                    CreateDownloadProgress(),
                    Log);
                Log($"加载器实例安装完成：{id}");
                await RefreshInstancesAsync();
            });
        }
        catch (Exception ex)
        {
            _runtime.Dialogs.Error(ex.Message, "加载器安装失败");
        }
    }

    private async Task RefreshDownloadsAsync()
    {
        try
        {
            await LoadVanillaVersionsAsync();
            if (IsCatalogSection)
            {
                await LoadCatalogProjectsAsync();
            }
        }
        catch (Exception ex)
        {
            CatalogStatus = ex.Message;
            Log("下载内容刷新失败：" + ex.Message);
        }
    }

    private async Task SelectDownloadSectionAsync(string key)
    {
        SelectedDownloadSection = key;
        foreach (var section in DownloadSections)
        {
            section.IsSelected = section.Key == key;
        }
        CloseCatalogProject();
        if (IsVanillaSection)
        {
            await LoadVanillaVersionsAsync();
            return;
        }
        await LoadCatalogProjectsAsync();
    }

    private async Task LoadCatalogProjectsAsync()
    {
        var section = DownloadSections.FirstOrDefault(item => item.Key == SelectedDownloadSection);
        if (section?.ContentType is not { } contentType)
        {
            return;
        }

        try
        {
            await RunBusyAsync($"正在刷新{section.Label}列表…", async () =>
            {
                var modrinthType = ToModrinthType(contentType);
                var modrinthTask = _runtime.Modrinth.SearchAsync(
                    "",
                    modrinthType,
                    null,
                    null,
                    0,
                    50,
                    "downloads");
                var modrinthResult = await modrinthTask;

                CatalogProjects.Clear();
                foreach (var project in modrinthResult.Hits.Select(hit =>
                             new CatalogProject(
                                 CatalogSource.Modrinth,
                                 contentType,
                                 hit.Id,
                                 hit.Slug,
                                 hit.Title,
                                 hit.Description,
                                 hit.IconUrl,
                                 hit.Downloads,
                                 $"https://modrinth.com/{modrinthType}/{hit.Slug}")))
                {
                    CatalogProjects.Add(project);
                }
                CatalogStatus = $"已按下载量显示 {CatalogProjects.Count} 个 Modrinth 项目。";
                Log($"已刷新{section.Label}：{CatalogProjects.Count} 个项目。");
            });
        }
        catch (Exception ex)
        {
            CatalogProjects.Clear();
            CatalogStatus = $"资源列表加载失败：{ex.Message}";
            Log(CatalogStatus);
        }
    }

    private async Task OpenCatalogProjectAsync(CatalogProject? project)
    {
        if (project is null)
        {
            return;
        }

        try
        {
            await RunBusyAsync($"正在加载 {project.Title}…", async () =>
            {
                SelectedCatalogProject = project;
                IsProjectDetailVisible = true;
                ProjectVersions.Clear();
                FilteredProjectVersions.Clear();
                ProjectGameVersions.Clear();
                ProjectLoaders.Clear();
                MatchingDownloadInstances.Clear();

                var versions = await LoadModrinthVersionsAsync(project);
                foreach (var version in versions)
                {
                    ProjectVersions.Add(version);
                }

                foreach (var gameVersion in ProjectVersions
                             .SelectMany(version => version.GameVersions)
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .OrderByDescending(value => value, NaturalVersionComparer.Instance))
                {
                    ProjectGameVersions.Add(gameVersion);
                }
                foreach (var loader in ProjectVersions
                             .SelectMany(version => version.Loaders)
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                {
                    ProjectLoaders.Add(loader);
                }

                SelectedProjectGameVersion =
                    ProjectGameVersions.FirstOrDefault(value =>
                        value.Equals(SelectedInstance?.GameVersion, StringComparison.OrdinalIgnoreCase)) ??
                    ProjectGameVersions.FirstOrDefault();
                SelectedProjectLoader =
                    ProjectLoaders.FirstOrDefault(value =>
                        value.Equals(SelectedInstance?.Loader.ToString(), StringComparison.OrdinalIgnoreCase)) ??
                    ProjectLoaders.FirstOrDefault();
                RefreshCatalogVersionFilters();
                CatalogStatus = ProjectVersions.Count == 0
                    ? "该项目没有可用的官方文件。"
                    : $"已加载 {ProjectVersions.Count} 个官方版本。";
            });
        }
        catch (Exception ex)
        {
            CatalogStatus = $"项目详情加载失败：{ex.Message}";
            _runtime.Dialogs.Error(ex.Message, "项目详情加载失败");
        }
    }

    private void CloseCatalogProject()
    {
        IsProjectDetailVisible = false;
        SelectedCatalogProject = null;
        SelectedCatalogVersion = null;
        ProjectVersions.Clear();
        FilteredProjectVersions.Clear();
        MatchingDownloadInstances.Clear();
    }

    private async Task<IReadOnlyList<CatalogVersion>> LoadModrinthVersionsAsync(CatalogProject project)
    {
        var versions = await _runtime.Modrinth.GetVersionsAsync(project.Id, null, null);
        return versions.Select(version => new CatalogVersion(
            CatalogSource.Modrinth,
            project.Id,
            version.Id,
            version.Name,
            version.VersionNumber,
            null,
            version.GameVersions,
            version.Loaders,
            version.Dependencies.Select(id => new CatalogDependency(id, null, id, true)).ToArray(),
            version.Files.Select(file => new CatalogFile(
                file.Url,
                file.FileName,
                string.IsNullOrWhiteSpace(file.Sha512) ? null : file.Sha512,
                null,
                file.Size,
                file.Primary)).ToArray())).ToArray();
    }

    private void RefreshCatalogVersionFilters()
    {
        if (!IsProjectDetailVisible)
        {
            return;
        }
        var filtered = ProjectVersions.Where(version =>
            (string.IsNullOrWhiteSpace(SelectedProjectGameVersion) ||
             version.GameVersions.Contains(SelectedProjectGameVersion, StringComparer.OrdinalIgnoreCase)) &&
            (SelectedCatalogProject?.ContentType != CatalogContentType.Mod ||
             string.IsNullOrWhiteSpace(SelectedProjectLoader) ||
             version.Loaders.Contains(SelectedProjectLoader, StringComparer.OrdinalIgnoreCase)))
            .ToArray();
        FilteredProjectVersions.Clear();
        foreach (var version in filtered)
        {
            FilteredProjectVersions.Add(version);
        }
        SelectedCatalogVersion = FilteredProjectVersions.FirstOrDefault();

        MatchingDownloadInstances.Clear();
        foreach (var instance in Instances.Where(instance =>
                     (string.IsNullOrWhiteSpace(SelectedProjectGameVersion) ||
                      instance.GameVersion?.Equals(SelectedProjectGameVersion, StringComparison.OrdinalIgnoreCase) == true) &&
                     (SelectedCatalogProject?.ContentType != CatalogContentType.Mod ||
                      string.IsNullOrWhiteSpace(SelectedProjectLoader) ||
                      instance.Loader.ToString().Equals(SelectedProjectLoader, StringComparison.OrdinalIgnoreCase))))
        {
            MatchingDownloadInstances.Add(instance);
        }
        SelectedDownloadInstance = MatchingDownloadInstances.FirstOrDefault();
        DownloadCatalogVersionCommand.RaiseCanExecuteChanged();
    }

    private async Task DownloadCatalogVersionAsync()
    {
        if (SelectedCatalogProject is null ||
            SelectedCatalogVersion is null ||
            SelectedDownloadInstance is null)
        {
            _runtime.Dialogs.Error("请选择官方文件版本和匹配的本地实例。");
            return;
        }

        var file = SelectedCatalogVersion.Files.FirstOrDefault(item => item.Primary) ??
                   SelectedCatalogVersion.Files.FirstOrDefault();
        if (file is null)
        {
            _runtime.Dialogs.Error(
                "该文件没有可用的直接下载地址，请打开项目页面手动下载。",
                "无法直接下载");
            Process.Start(new ProcessStartInfo(SelectedCatalogProject.ProjectUrl) { UseShellExecute = true });
            return;
        }

        var folder = SelectedCatalogProject.ContentType switch
        {
            CatalogContentType.Mod => "mods",
            CatalogContentType.ResourcePack => "resourcepacks",
            CatalogContentType.Shader => "shaderpacks",
            CatalogContentType.Modpack => "modpacks",
            _ => "mods"
        };
        try
        {
            await RunBusyAsync($"正在下载 {file.FileName}…", async () =>
            {
                var item = new DownloadItem(
                    $"{SelectedCatalogProject.Title} {SelectedCatalogVersion.VersionNumber}",
                    new Uri(file.Url),
                    Path.Combine(SelectedDownloadInstance.DirectoryPath, folder, file.FileName),
                    Sha1: file.Sha1,
                    Sha512: file.Sha512,
                    Size: file.Size > 0 ? file.Size : null,
                    Kind: SelectedCatalogProject.ContentType switch
                    {
                        CatalogContentType.ResourcePack => DownloadKind.ResourcePack,
                        CatalogContentType.Shader => DownloadKind.Shader,
                        CatalogContentType.Modpack => DownloadKind.Loader,
                        _ => DownloadKind.Mod
                    });
                await _runtime.Downloads.DownloadAsync(
                    new DownloadPlan(SelectedCatalogProject.Title, SelectedDownloadInstance.Id, [item]),
                    Settings.DownloadConcurrency,
                    ApproveMirrorAsync,
                    CreateDownloadProgress(),
                    Log);
                Log($"已下载到实例：{SelectedDownloadInstance.DirectoryPath}");
            });
        }
        catch (Exception ex)
        {
            _runtime.Dialogs.Error(ex.Message, "下载失败");
        }
    }

    private static string ToModrinthType(CatalogContentType type) => type switch
    {
        CatalogContentType.Mod => "mod",
        CatalogContentType.ResourcePack => "resourcepack",
        CatalogContentType.Shader => "shader",
        CatalogContentType.Modpack => "modpack",
        _ => "mod"
    };

    private IProgress<DownloadProgress> CreateDownloadProgress() => new Progress<DownloadProgress>(progress =>
    {
        IsProgressIndeterminate = false;
        ProgressValue = progress.OverallPercent;
        StatusText = $"{progress.CurrentFile} ({progress.Completed}/{progress.Total})";
    });

    private Task<bool> ApproveMirrorAsync(MirrorDecision decision)
    {
        return Task.FromResult(_runtime.Dialogs.AskMirror(decision.Reason));
    }

    private async Task RunBusyAsync(string status, Func<Task> action)
    {
        var outer = _busyDepth++ == 0;
        if (outer)
        {
            IsBusy = true;
            ProgressValue = 0;
            IsProgressIndeterminate = true;
        }
        StatusText = status;
        Log(status);
        try
        {
            await action();
            if (outer)
            {
                StatusText = "操作完成";
            }
        }
        finally
        {
            _busyDepth--;
            if (outer)
            {
                IsBusy = false;
                IsProgressIndeterminate = false;
            }
        }
    }

    private void Log(string message)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            AddLogLine(message);
        }
        else
        {
            Application.Current.Dispatcher.Invoke(() => AddLogLine(message));
        }
    }

    private void AddLogLine(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Logs.Add(line);
        while (Logs.Count > 2000)
        {
            Logs.RemoveAt(0);
        }
        _runtime.Log(message);
    }

    private void OpenDataFolder()
    {
        Directory.CreateDirectory(_runtime.Paths.Root);
        Process.Start(new ProcessStartInfo(_runtime.Paths.Root) { UseShellExecute = true });
    }

    private void OpenLogFolder()
    {
        Directory.CreateDirectory(_runtime.Paths.LogsDirectory);
        Process.Start(new ProcessStartInfo(_runtime.Paths.LogsDirectory) { UseShellExecute = true });
    }
}
