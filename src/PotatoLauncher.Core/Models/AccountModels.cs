namespace PotatoLauncher.Core.Models;

public enum AccountKind
{
    Offline,
    Microsoft
}

public sealed record MinecraftTexture(string Id, string Name, string Url, string? Alias = null);

public sealed class AccountSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public AccountKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string Uuid { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string? RefreshToken { get; set; }
    public string? Xuid { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? SkinPath { get; set; }
    public string SkinModel { get; set; } = "classic";
    public string? CapePath { get; set; }
    public List<MinecraftTexture> OfficialSkins { get; set; } = [];
    public List<MinecraftTexture> OfficialCapes { get; set; } = [];

    public string KindLabel => Kind == AccountKind.Microsoft ? "微软正版" : "离线";

    public bool HasUsableAccessToken =>
        Kind == AccountKind.Offline ||
        (!string.IsNullOrWhiteSpace(AccessToken) && ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2));

    public AccountSession Clone() => new()
    {
        Id = Id,
        Kind = Kind,
        Name = Name,
        Uuid = Uuid,
        AccessToken = AccessToken,
        RefreshToken = RefreshToken,
        Xuid = Xuid,
        ExpiresAt = ExpiresAt,
        SkinPath = SkinPath,
        SkinModel = SkinModel,
        CapePath = CapePath,
        OfficialSkins = OfficialSkins.ToList(),
        OfficialCapes = OfficialCapes.ToList()
    };
}

public sealed record MicrosoftAuthConfiguration(
    string ClientId,
    string RedirectUri = "http://localhost:53682/callback",
    string Scope = "XboxLive.signin offline_access");

public sealed record MicrosoftProfile(
    string Id,
    string Name,
    string? Xuid,
    IReadOnlyList<MinecraftTexture> Skins,
    IReadOnlyList<MinecraftTexture> Capes);
