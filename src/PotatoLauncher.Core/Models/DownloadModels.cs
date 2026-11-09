namespace PotatoLauncher.Core.Models;

public enum DownloadKind
{
    VersionJson,
    Client,
    Library,
    AssetIndex,
    Asset,
    Logging,
    Loader,
    Mod,
    ResourcePack,
    Shader
}

public sealed record DownloadItem(
    string Name,
    Uri PrimaryUri,
    string DestinationPath,
    string? Sha1 = null,
    string? Sha256 = null,
    string? Sha512 = null,
    long? Size = null,
    Uri? MirrorUri = null,
    DownloadKind Kind = DownloadKind.Library);

public sealed record DownloadPlan(
    string Name,
    string InstanceId,
    IReadOnlyList<DownloadItem> Items);

public sealed record DownloadProgress(
    string CurrentFile,
    int Completed,
    int Total,
    long BytesDownloaded,
    long? TotalBytes,
    double OverallPercent);

public sealed class DownloadFailureException : Exception
{
    public DownloadFailureException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

public sealed class MirrorDecision
{
    public required string Url { get; init; }
    public required string Reason { get; init; }
}
