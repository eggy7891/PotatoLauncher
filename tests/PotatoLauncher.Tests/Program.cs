using System.Net.Http;
using System.Text.Json;
using PotatoLauncher.Core.Models;
using PotatoLauncher.Core.Services;

namespace PotatoLauncher.Tests;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    public static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("Version inheritance merges parent and child", TestVersionInheritanceAsync),
            ("Windows rules and feature rules evaluate", TestRulesAsync),
            ("Maven coordinates produce normalized paths", TestMavenAsync),
            ("Java version parser handles legacy and modern", TestJavaVersionAsync),
            ("Offline UUID uses Java name UUID semantics", TestOfflineUuidAsync),
            ("Mirror URI preserves host-specific prefixes", TestMirrorAsync),
            ("Launch planner builds argument list", TestLaunchPlannerAsync),
            ("Local texture server exposes signed profile", TestTextureServerAsync)
        };

        foreach (var test in tests)
        {
            try
            {
                await test.Run();
                _passed++;
                Console.WriteLine($"PASS  {test.Name}");
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine($"FAIL  {test.Name}");
                Console.WriteLine("      " + ex);
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Passed: {_passed}, Failed: {_failed}");
        return _failed == 0 ? 0 : 1;
    }

    private static async Task TestVersionInheritanceAsync()
    {
        using var fixture = new VersionFixture();
        var resolved = await fixture.Resolver.ResolveAsync(fixture.Root, "1.20.1-Fabric test");
        Assert.Equal("net.fabricmc.test.Main", resolved.Metadata.MainClass);
        Assert.SequenceEqual(
            ["1.20.1-Fabric test", "1.20.1"],
            resolved.Lineage);
        Assert.Equal(2, resolved.Metadata.Libraries.Count);
        Assert.Equal("example.parent", resolved.Metadata.Libraries[0].Name.Split(':')[0]);
        Assert.Equal("fabric-loader", resolved.Metadata.Libraries[1].Name.Split(':')[0]);
        Assert.Equal(LoaderKind.Fabric, resolved.Loader);
    }

    private static Task TestRulesAsync()
    {
        var allowWindows = RuleEvaluator.IsAllowed(
        [
            new RuleEntry { Action = "allow", Os = new OsRule { Name = "windows" } }
        ]);
        Assert.True(allowWindows);

        var denyLinux = RuleEvaluator.IsAllowed(
        [
            new RuleEntry { Action = "allow", Os = new OsRule { Name = "linux" } }
        ]);
        Assert.False(denyLinux);

        var quickPlay = RuleEvaluator.IsAllowed(
        [
            new RuleEntry
            {
                Action = "allow",
                Features = new Dictionary<string, bool> { ["is_quick_play_multiplayer"] = true }
            }
        ], new Dictionary<string, bool> { ["is_quick_play_multiplayer"] = false });
        Assert.False(quickPlay);
        return Task.CompletedTask;
    }

    private static Task TestMavenAsync()
    {
        Assert.Equal(
            "org/example/demo/1.2.3/demo-1.2.3-natives-windows.jar",
            MavenPath.FromCoordinate("org.example:demo:1.2.3:natives-windows"));
        return Task.CompletedTask;
    }

    private static Task TestJavaVersionAsync()
    {
        Assert.Equal(8, JavaLocator.ParseMajor("1.8.0_402"));
        Assert.Equal(21, JavaLocator.ParseMajor("21.0.12"));
        Assert.Equal(25, JavaLocator.ParseMajor("25-ea"));
        return Task.CompletedTask;
    }

    private static Task TestOfflineUuidAsync()
    {
        var first = IdentityUtil.OfflineUuid("Notch");
        var second = IdentityUtil.OfflineUuid("Notch");
        Assert.Equal(first, second);
        Assert.Equal(32, first.Length);
        Assert.Equal('3', first[12]);
        return Task.CompletedTask;
    }

    private static Task TestMirrorAsync()
    {
        var library = MirrorUriFactory.Create(
            new Uri("https://libraries.minecraft.net/com/example/demo/1/demo-1.jar"),
            "https://mirror.example/base");
        Assert.Equal(
            "https://mirror.example/base/maven/com/example/demo/1/demo-1.jar",
            library!.ToString());
        var asset = MirrorUriFactory.Create(
            new Uri("https://resources.download.minecraft.net/aa/abcdef"),
            "https://mirror.example/");
        Assert.Equal("https://mirror.example/assets/aa/abcdef", asset!.ToString());
        return Task.CompletedTask;
    }

    private static async Task TestLaunchPlannerAsync()
    {
        using var fixture = new VersionFixture();
        var baseVersionDirectory = Path.Combine(fixture.Root, "versions", "1.20.1");
        File.WriteAllText(Path.Combine(baseVersionDirectory, "1.20.1.jar"), "placeholder");
        var libraryDirectory = Path.Combine(fixture.Root, "libraries", "example", "parent", "1.0");
        Directory.CreateDirectory(libraryDirectory);
        File.WriteAllText(Path.Combine(libraryDirectory, "parent-1.0.jar"), "placeholder");

        var planner = new LaunchPlanner(fixture.Resolver);
        var plan = await planner.BuildAsync(new LaunchRequest(
            new InstanceDescriptor(
                "1.20.1-Fabric test",
                "Fabric test",
                Path.Combine(fixture.Root, "versions", "1.20.1-Fabric test"),
                Path.Combine(fixture.Root, "versions", "1.20.1-Fabric test", "1.20.1-Fabric test.json"),
                "release",
                "1.20.1",
                LoaderKind.Fabric,
                17,
                DateTimeOffset.UtcNow),
            new AccountSession
            {
                Kind = AccountKind.Offline,
                Name = "TestPlayer",
                Uuid = IdentityUtil.OfflineUuid("TestPlayer"),
                AccessToken = "offline"
            },
            new JavaRuntime("java.exe", "C:/Java", 21, "21.0.0"),
            fixture.Root,
            Path.Combine(fixture.Root, "game"),
            2048,
            854,
            480,
            null,
            []));
        Assert.True(plan.Arguments.Contains("net.fabricmc.test.Main"));
        Assert.True(plan.Arguments.Contains("TestPlayer"));
        Assert.True(plan.Arguments.Contains("-Xmx2048m"));
        Assert.False(plan.RedactedCommandLine.Contains("offline", StringComparison.Ordinal));
    }

    private static async Task TestTextureServerAsync()
    {
        var temp = Path.Combine(Path.GetTempPath(), "PotatoLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var skin = Path.Combine(temp, "skin.png");
        await File.WriteAllBytesAsync(skin, [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3]);
        var id = Guid.NewGuid();
        var server = new LocalTextureServer(new TextureBundle("Test", id, skin, "classic", null));
        try
        {
            server.Start();
            using var client = new HttpClient();
            var metadata = await client.GetStringAsync(server.BaseUri);
            Assert.True(metadata.Contains("PotatoLauncher", StringComparison.Ordinal));
            var profile = await client.GetStringAsync(new Uri(server.BaseUri, $"sessionserver/session/minecraft/profile/{id:N}"));
            using var json = JsonDocument.Parse(profile);
            Assert.Equal("Test", json.RootElement.GetProperty("name").GetString()!);
            Assert.True(json.RootElement.GetProperty("properties")[0].TryGetProperty("signature", out _));
        }
        finally
        {
            await server.DisposeAsync();
            Directory.Delete(temp, recursive: true);
        }
    }

    private sealed class VersionFixture : IDisposable
    {
        public VersionFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "PotatoLauncherTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            var baseDir = Path.Combine(Root, "versions", "1.20.1");
            var childDir = Path.Combine(Root, "versions", "1.20.1-Fabric test");
            Directory.CreateDirectory(baseDir);
            Directory.CreateDirectory(childDir);
            var baseMetadata = new
            {
                id = "1.20.1",
                type = "release",
                mainClass = "net.minecraft.client.main.Main",
                assets = "5",
                assetIndex = new { id = "5", url = "https://example.invalid/5.json", sha1 = "00", size = 1 },
                downloads = new
                {
                    client = new { url = "https://example.invalid/client.jar", sha1 = "00", size = 1 }
                },
                javaVersion = new { component = "java-runtime-gamma", majorVersion = 17 },
                libraries = new object[]
                {
                    new { name = "example.parent:parent:1.0", downloads = new { artifact = new { path = "example/parent/1.0/parent-1.0.jar", url = "https://example.invalid/parent.jar", sha1 = "00", size = 1 } } }
                },
                arguments = new
                {
                    game = new object[] { "--username", "${auth_player_name}", "--version", "${version_name}", "--gameDir", "${game_directory}" },
                    jvm = new object[] { "-Djava.library.path=${natives_directory}", "-cp", "${classpath}" }
                }
            };
            var childMetadata = new
            {
                id = "1.20.1-Fabric test",
                inheritsFrom = "1.20.1",
                type = "release",
                mainClass = "net.fabricmc.test.Main",
                libraries = new object[]
                {
                    new { name = "net.fabricmc:fabric-loader:0.16.0", downloads = new { artifact = new { path = "net/fabricmc/fabric-loader/0.16.0/fabric-loader-0.16.0.jar", url = "https://example.invalid/fabric.jar", sha1 = "00", size = 1 } } }
                }
            };
            File.WriteAllText(
                Path.Combine(baseDir, "1.20.1.json"),
                JsonSerializer.Serialize(baseMetadata));
            File.WriteAllText(
                Path.Combine(childDir, "1.20.1-Fabric test.json"),
                JsonSerializer.Serialize(childMetadata));
            Resolver = new VersionResolver();
        }

        public string Root { get; }
        public VersionResolver Resolver { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private static class Assert
    {
        public static void True(bool condition)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Expected true.");
            }
        }

        public static void False(bool condition)
        {
            if (condition)
            {
                throw new InvalidOperationException("Expected false.");
            }
        }

        public static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
            }
        }

        public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
        {
            if (!expected.SequenceEqual(actual))
            {
                throw new InvalidOperationException(
                    $"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
            }
        }
    }
}
