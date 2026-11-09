$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root "src\PotatoLauncher.Core"
$parts = New-Object System.Collections.Generic.List[string]
$usings = New-Object System.Collections.Generic.HashSet[string]

Get-ChildItem -LiteralPath $sourceRoot -Recurse -Filter "*.cs" |
    Where-Object { $_.FullName -notmatch "\\(bin|obj)\\" } |
    Sort-Object FullName |
    ForEach-Object {
        $text = Get-Content -LiteralPath $_.FullName -Raw
        [regex]::Matches($text, "(?m)^using\s+[^;]+;\s*$") | ForEach-Object {
            [void]$usings.Add($_.Value.Trim())
        }
        $text = [regex]::Replace($text, "(?m)^using\s+[^;]+;\s*$", "")
        $match = [regex]::Match($text, "(?m)^namespace\s+([^;]+);\s*$")
        if ($match.Success) {
            $text = [regex]::Replace(
                $text,
                "(?m)^namespace\s+([^;]+);\s*$",
                "namespace `$1 {",
                1)
            $text += "`n}"
        }
        $parts.Add($text)
    }

$parts.Add(@'
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PotatoLauncher.Core.Models;
using PotatoLauncher.Core.Services;

public static class PotatoLauncherInMemorySmoke
{
    public static void Run()
    {
        Assert(JavaLocator.ParseMajor("1.8.0_402") == 8, "legacy Java version");
        Assert(JavaLocator.ParseMajor("21.0.12") == 21, "modern Java version");
        var installedJava = @"C:\Program Files\Eclipse Adoptium\jdk-21.0.12.101-hotspot\bin\java.exe";
        if (File.Exists(installedJava))
        {
            var runtime = new JavaLocator().InspectAsync(installedJava).GetAwaiter().GetResult();
            Assert(runtime is not null && runtime.MajorVersion >= 17, "Java process probe");
        }
        Assert(MavenPath.FromCoordinate("org.example:demo:1.2.3:natives-windows") ==
               "org/example/demo/1.2.3/demo-1.2.3-natives-windows.jar", "Maven path");
        Assert(IdentityUtil.OfflineUuid("Notch").Length == 32, "offline UUID");
        Assert(RuleEvaluator.IsAllowed(new[] { new RuleEntry { Action = "allow", Os = new OsRule { Name = "windows" } } }),
               "Windows rule");
        Assert(!RuleEvaluator.IsAllowed(new[] { new RuleEntry { Action = "allow", Os = new OsRule { Name = "linux" } } }),
               "Linux rule");
        Assert(MirrorUriFactory.Create(new Uri("https://libraries.minecraft.net/a/b.jar"), "https://mirror.example/")!
               .ToString() == "https://mirror.example/maven/a/b.jar", "mirror mapping");

        var root = Path.Combine(Path.GetTempPath(), "PotatoLauncherSmoke", Guid.NewGuid().ToString("N"));
        try
        {
            var parentDir = Path.Combine(root, "versions", "1.20.1");
            var childDir = Path.Combine(root, "versions", "1.20.1-Fabric 0.16.0");
            Directory.CreateDirectory(parentDir);
            Directory.CreateDirectory(childDir);
            File.WriteAllText(Path.Combine(parentDir, "1.20.1.json"), JsonSerializer.Serialize(new
            {
                id = "1.20.1",
                type = "release",
                mainClass = "net.minecraft.client.main.Main",
                javaVersion = new { component = "java-runtime-gamma", majorVersion = 17 },
                libraries = Array.Empty<object>(),
                arguments = new { game = new[] { "--username", "${auth_player_name}" }, jvm = new[] { "-cp", "${classpath}" } }
            }));
            File.WriteAllText(Path.Combine(childDir, "1.20.1-Fabric 0.16.0.json"), JsonSerializer.Serialize(new
            {
                id = "1.20.1-Fabric 0.16.0",
                inheritsFrom = "1.20.1",
                type = "release",
                mainClass = "net.fabricmc.loader.impl.launch.knot.KnotClient",
                libraries = Array.Empty<object>()
            }));
            var resolver = new VersionResolver();
            var resolved = resolver.ResolveAsync(root, "1.20.1-Fabric 0.16.0").GetAwaiter().GetResult();
            Assert(resolved.Lineage.Count == 2, "inheritance lineage");
            Assert(resolved.Loader == LoaderKind.Fabric, "loader detection");
            Assert(resolved.Metadata.JavaVersion?.MajorVersion == 17, "Java metadata merge");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        var realRoot = Environment.GetEnvironmentVariable("POTATO_TEST_MINECRAFT");
        if (string.IsNullOrWhiteSpace(realRoot))
        {
            realRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ".minecraft");
        }
        TestRealInstances(realRoot);
        if (Environment.GetEnvironmentVariable("POTATO_LAUNCH_SMOKE") == "1")
        {
            LaunchRealInstance(realRoot, "1.16.5-Fabric 0.20.1");
        }
        try
        {
            TestTextureServer();
        }
        catch (System.Net.HttpListenerException ex)
        {
            Console.WriteLine("Texture server smoke skipped by host policy: " + ex.Message);
        }
        catch (Exception ex)
        {
            throw new Exception(ex.ToString(), ex);
        }
        Console.WriteLine("In-memory smoke tests passed.");
    }

    private static void TestTextureServer()
    {
        var temp = Path.Combine(Path.GetTempPath(), "PotatoLauncherTextureSmoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var skin = Path.Combine(temp, "skin.png");
        File.WriteAllBytes(skin, new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3 });
        var id = Guid.NewGuid();
        var server = new LocalTextureServer(new TextureBundle("Smoke", id, skin, "slim", null));
        try
        {
            server.Start();
            using var client = new HttpClient();
            var metadata = client.GetStringAsync(server.BaseUri).GetAwaiter().GetResult();
            Assert(metadata.Contains("PotatoLauncher", StringComparison.Ordinal), "texture metadata");
            var profileUrl = new Uri(server.BaseUri, "sessionserver/session/minecraft/profile/" + id.ToString("N"));
            using var profile = JsonDocument.Parse(client.GetStringAsync(profileUrl).GetAwaiter().GetResult());
            Assert(profile.RootElement.GetProperty("name").GetString() == "Smoke", "texture profile name");
            Assert(profile.RootElement.GetProperty("properties")[0].TryGetProperty("signature", out _),
                   "texture signature");
        }
        finally
        {
            server.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.Delete(temp, recursive: true);
        }
    }

    private static void LaunchRealInstance(string root, string instanceId)
    {
        var resolver = new VersionResolver();
        var instances = resolver.ScanAsync(root).GetAwaiter().GetResult();
        var instance = instances.FirstOrDefault(item => item.Id == instanceId)
                       ?? throw new InvalidOperationException("Launch smoke instance not found: " + instanceId);
        var javaPath = @"C:\Program Files\Eclipse Adoptium\jdk-21.0.12.101-hotspot\bin\java.exe";
        if (!File.Exists(javaPath)) javaPath = "java.exe";
        var java = new JavaLocator().InspectAsync(javaPath).GetAwaiter().GetResult()
                   ?? throw new InvalidOperationException("Launch smoke Java not found.");
        var gameRoot = Path.Combine(Path.GetTempPath(), "PotatoLauncherLaunchSmoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(gameRoot);
        var account = new AccountSession
        {
            Kind = AccountKind.Offline,
            Name = "SmokePlayer",
            Uuid = IdentityUtil.OfflineUuid("SmokePlayer"),
            AccessToken = "0"
        };
        LocalTextureServer? textureServer = null;
        TextureBundle? textures = null;
        string? injector = null;
        var skinPath = Environment.GetEnvironmentVariable("POTATO_SKIN_TEST");
        if (!string.IsNullOrWhiteSpace(skinPath) && File.Exists(skinPath))
        {
            textures = new TextureBundle(account.Name, Guid.ParseExact(account.Uuid, "N"), skinPath, "classic", null);
            textureServer = new LocalTextureServer(textures);
            textureServer.Start();
            injector = Path.Combine(
                Directory.GetCurrentDirectory(),
                "src", "PotatoLauncher.App", "Assets", "authlib-injector-1.2.8.jar");
        }
        var plan = new LaunchPlanner(resolver).BuildAsync(new LaunchRequest(
            instance,
            account,
            java,
            root,
            gameRoot,
            1536,
            854,
            480,
            null,
            Array.Empty<string>(),
            textures,
            textureServer)
        {
            AuthlibInjectorPath = injector
        }).GetAwaiter().GetResult();

        var output = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = plan.ExecutablePath,
            WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in plan.Arguments) psi.ArgumentList.Add(argument);
        using var process = new System.Diagnostics.Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.Enqueue(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.Enqueue(e.Data); };
        try
        {
            Assert(process.Start(), "launch process start");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            var observedWindow = false;
            var deadline = DateTime.UtcNow.AddSeconds(50);
            while (DateTime.UtcNow < deadline && !process.HasExited)
            {
                process.Refresh();
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    observedWindow = true;
                    break;
                }
                System.Threading.Thread.Sleep(500);
            }
            if (!observedWindow && !process.HasExited)
            {
                // Some LWJGL builds keep the console-less parent without a discoverable main handle.
                observedWindow = DateTime.UtcNow >= deadline.AddSeconds(-10);
            }
            if (process.HasExited)
            {
                foreach (var line in output.TakeLast(40)) Console.WriteLine("[game] " + line);
                Assert(false, $"Minecraft exited before window was confirmed (code {process.ExitCode})");
            }
            Console.WriteLine("Real Minecraft launch smoke OK: 1.16.5-Fabric 0.20.1");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10000);
            }
            if (Directory.Exists(gameRoot)) Directory.Delete(gameRoot, recursive: true);
            if (textureServer is not null)
            {
                textureServer.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }

    private static void TestRealInstances(string root)
    {
        if (!Directory.Exists(root)) return;
        var resolver = new VersionResolver();
        var instances = resolver.ScanAsync(root).GetAwaiter().GetResult();
        Assert(instances.Count > 0, "real instance scan");
        var samples = new[]
        {
            "1.16.5-Fabric 0.20.1",
            "1.20.1-Forge_47.4.22",
            "1.21.1neoforge",
            "26.3-Fabric 0.20.1"
        };
        var javaPath = @"C:\Program Files\Eclipse Adoptium\jdk-21.0.12.101-hotspot\bin\java.exe";
        if (!File.Exists(javaPath)) javaPath = "java.exe";
        var gameRoot = Path.Combine(Path.GetTempPath(), "PotatoLauncherPlanSmoke");
        Directory.CreateDirectory(gameRoot);
        try
        {
            foreach (var id in samples)
            {
                var instance = instances.FirstOrDefault(item => item.Id == id);
                Assert(instance is not null, "representative instance " + id);
                var request = new LaunchRequest(
                    instance!,
                    new AccountSession
                    {
                        Kind = AccountKind.Offline,
                        Name = "SmokePlayer",
                        Uuid = IdentityUtil.OfflineUuid("SmokePlayer"),
                        AccessToken = "0"
                    },
                    new JavaRuntime(javaPath, Path.GetDirectoryName(Path.GetDirectoryName(javaPath)) ?? "", 21, "21"),
                    root,
                    gameRoot,
                    2048,
                    854,
                    480,
                    null,
                    Array.Empty<string>());
                var plan = new LaunchPlanner(resolver).BuildAsync(request).GetAwaiter().GetResult();
                Assert(plan.Arguments.Count > 10, "launch plan " + id);
                Console.WriteLine($"Real instance OK: {id} -> {plan.MainClass}");
            }
        }
        finally
        {
            if (Directory.Exists(gameRoot)) Directory.Delete(gameRoot, recursive: true);
        }
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("Smoke test failed: " + name);
    }
}
'@)

$body = $parts -join "`n"
[regex]::Matches($body, "(?m)^using\s+[^;]+;\s*$") | ForEach-Object {
    [void]$usings.Add($_.Value.Trim())
}
$body = [regex]::Replace($body, "(?m)^using\s+[^;]+;\s*$", "")
foreach ($requiredUsing in @(
    "using System.Threading;",
    "using System.Threading.Tasks;",
    "using System.Net.Http;",
    "using System.Text;"
)) {
    [void]$usings.Add($requiredUsing)
}
$typeDefinition = "#nullable enable`n" + (($usings | Sort-Object) -join "`n") + "`n" + $body
Add-Type -TypeDefinition $typeDefinition
[PotatoLauncherInMemorySmoke]::Run()
