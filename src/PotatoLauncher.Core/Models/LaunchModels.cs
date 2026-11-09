using PotatoLauncher.Core.Services;

namespace PotatoLauncher.Core.Models;

public sealed record JavaRuntime(
    string ExecutablePath,
    string HomePath,
    int MajorVersion,
    string VersionText,
    string Vendor = "Unknown");

public sealed record LaunchRequest(
    InstanceDescriptor Instance,
    AccountSession Account,
    JavaRuntime Java,
    string MinecraftDirectory,
    string GameDirectory,
    int MemoryMb,
    int Width,
    int Height,
    string? ServerAddress,
    IReadOnlyList<string> ExtraJvmArguments,
    TextureBundle? Textures = null,
    LocalTextureServer? TextureServer = null)
{
    public string? AuthlibInjectorPath { get; init; }
    public string? MicrosoftClientId { get; init; }
}

public sealed record LaunchPlan(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    string MainClass,
    string Classpath,
    string? NativeDirectory,
    string RedactedCommandLine);

public sealed record LaunchProcessResult(int ExitCode, string? FailureHint)
{
    public bool Succeeded => ExitCode == 0;
}

public sealed record LaunchProgress(string Stage, string Detail, double? Percent = null);

public sealed record TextureBundle(
    string ProfileName,
    Guid ProfileId,
    string? SkinPath,
    string SkinModel,
    string? CapePath);
