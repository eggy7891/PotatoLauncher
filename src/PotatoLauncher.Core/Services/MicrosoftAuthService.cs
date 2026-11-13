using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PotatoLauncher.Core.Models;

namespace PotatoLauncher.Core.Services;

public interface IMicrosoftAuthService
{
    Task<AccountSession> SignInAsync(
        MicrosoftAuthConfiguration configuration,
        Action<string>? status = null,
        CancellationToken cancellationToken = default);

    Task<AccountSession> RefreshAsync(
        AccountSession account,
        MicrosoftAuthConfiguration configuration,
        CancellationToken cancellationToken = default);
}

public sealed class MicrosoftAuthService : IMicrosoftAuthService
{
    private readonly HttpClient _httpClient;

    public MicrosoftAuthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PotatoLauncher/1.0.2 (+https://github.com/eggy7891/PotatoLauncher)");
    }

    public async Task<AccountSession> SignInAsync(
        MicrosoftAuthConfiguration configuration,
        Action<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configuration.ClientId))
        {
            throw new InvalidOperationException("尚未配置 Microsoft OAuth Client ID。请先在设置中填写。");
        }
        if (!Uri.TryCreate(configuration.RedirectUri, UriKind.Absolute, out var redirect) ||
            redirect.Scheme != Uri.UriSchemeHttp ||
            !(redirect.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
              redirect.Host == "127.0.0.1"))
        {
            throw new InvalidOperationException("回调地址必须是 localhost 或 127.0.0.1 的 HTTP 地址。");
        }

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(24));
        var authorizeUrl =
            "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize" +
            $"?client_id={Uri.EscapeDataString(configuration.ClientId)}" +
            "&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(configuration.RedirectUri)}" +
            $"&scope={Uri.EscapeDataString(configuration.Scope)}" +
            $"&state={Uri.EscapeDataString(state)}" +
            $"&code_challenge={challenge}&code_challenge_method=S256&prompt=select_account";

        using var listener = new TcpListener(IPAddress.Loopback, redirect.Port);
        listener.Start();
        status?.Invoke("已在浏览器中打开微软登录，请完成授权。");
        Process.Start(new ProcessStartInfo(authorizeUrl) { UseShellExecute = true });

        var callback = await WaitForCallbackAsync(listener, state, redirect.AbsolutePath, cancellationToken)
            .ConfigureAwait(false);
        var code = callback.TryGetValue("code", out var codeValue)
            ? codeValue
            : throw new InvalidOperationException("授权回调中没有 code。");

        var token = await RequestTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = configuration.ClientId,
            ["scope"] = configuration.Scope,
            ["code"] = code,
            ["redirect_uri"] = configuration.RedirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = verifier
        }, cancellationToken).ConfigureAwait(false);
        return await CompleteMinecraftLoginAsync(token, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AccountSession> RefreshAsync(
        AccountSession account,
        MicrosoftAuthConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(account.RefreshToken))
        {
            throw new InvalidOperationException("微软账号没有可用的刷新令牌，请重新登录。");
        }
        var token = await RequestTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = configuration.ClientId,
            ["scope"] = configuration.Scope,
            ["refresh_token"] = account.RefreshToken,
            ["grant_type"] = "refresh_token"
        }, cancellationToken).ConfigureAwait(false);
        return await CompleteMinecraftLoginAsync(token, account.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AccountSession> CompleteMinecraftLoginAsync(
        MsTokenResponse token,
        string? existingId,
        CancellationToken cancellationToken)
    {
        var xbl = await PostJsonAsync<XboxResponse>(
            "https://user.auth.xboxlive.com/user/authenticate",
            new
            {
                Properties = new
                {
                    AuthMethod = "RPS",
                    SiteName = "user.auth.xboxlive.com",
                    RpsTicket = token.AccessToken.StartsWith("d=", StringComparison.Ordinal)
                        ? token.AccessToken
                        : "d=" + token.AccessToken
                },
                RelyingParty = "http://auth.xboxlive.com",
                TokenType = "JWT"
            }, cancellationToken).ConfigureAwait(false);

        XboxResponse xsts;
        try
        {
            xsts = await PostJsonAsync<XboxResponse>(
                "https://xsts.auth.xboxlive.com/xsts/authorize",
                new
                {
                    Properties = new { SandboxId = "RETAIL", UserTokens = new[] { xbl.Token } },
                    RelyingParty = "rp://api.minecraftservices.com/",
                    TokenType = "JWT"
                }, cancellationToken).ConfigureAwait(false);
        }
        catch (MicrosoftAuthException ex) when (ex.Xerr is not null)
        {
            throw new MicrosoftAuthException(TranslateXstsError(ex.Xerr), ex.Xerr, ex);
        }

        var userHash = xsts.DisplayClaims?.Xui.FirstOrDefault()?.UserHash
                       ?? xbl.DisplayClaims?.Xui.FirstOrDefault()?.UserHash
                       ?? throw new InvalidOperationException("Xbox 响应缺少用户哈希。");
        var xuid = xsts.DisplayClaims?.Xui.FirstOrDefault()?.Xid
                   ?? xbl.DisplayClaims?.Xui.FirstOrDefault()?.Xid
                   ?? userHash;
        var minecraft = await PostJsonAsync<MinecraftTokenResponse>(
            "https://api.minecraftservices.com/authentication/login_with_xbox",
            new { identityToken = $"XBL3.0 x={userHash};{xsts.Token}" },
            cancellationToken).ConfigureAwait(false);

        using var profileRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
        profileRequest.Headers.Authorization = new("Bearer", minecraft.AccessToken);
        using var profileResponse = await _httpClient.SendAsync(profileRequest, cancellationToken).ConfigureAwait(false);
        if (!profileResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                profileResponse.StatusCode == HttpStatusCode.NotFound
                    ? "该微软账号尚未拥有 Minecraft Java Edition。"
                    : $"获取 Minecraft 档案失败：{(int)profileResponse.StatusCode}");
        }
        var profile = await profileResponse.Content.ReadFromJsonAsync<MinecraftProfileResponse>(
            cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Minecraft 档案响应为空。");

        return new AccountSession
        {
            Id = existingId ?? Guid.NewGuid().ToString("N"),
            Kind = AccountKind.Microsoft,
            Name = profile.Name,
            Uuid = profile.Id,
            AccessToken = minecraft.AccessToken,
            RefreshToken = token.RefreshToken,
            Xuid = xuid,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, minecraft.ExpiresIn)),
            OfficialSkins = profile.Skins.Select(item => new MinecraftTexture(
                item.Id, item.State ?? "官方皮肤", item.Url, item.Alias)).ToList(),
            OfficialCapes = profile.Capes.Select(item => new MinecraftTexture(
                item.Id, item.Alias ?? item.State ?? "披风", item.Url, item.Alias)).ToList()
        };
    }

    private async Task<MsTokenResponse> RequestTokenAsync(
        IReadOnlyDictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(
            "https://login.microsoftonline.com/consumers/oauth2/v2.0/token",
            content,
            cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"微软令牌请求失败：{(int)response.StatusCode} {body}");
        }
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        return new MsTokenResponse(
            root.GetProperty("access_token").GetString() ?? "",
            root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null,
            root.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 3600);
    }

    private async Task<T> PostJsonAsync<T>(string url, object body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(url, body, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string? xerr = null;
            try
            {
                using var document = JsonDocument.Parse(text);
                if (document.RootElement.TryGetProperty("XErr", out var xerrElement))
                {
                    xerr = xerrElement.GetInt64().ToString();
                }
            }
            catch (JsonException)
            {
                // Keep the raw body in the user-facing error.
            }
            throw new MicrosoftAuthException($"Xbox 服务返回错误：{(int)response.StatusCode} {text}", xerr);
        }
        return JsonSerializer.Deserialize<T>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidDataException("Xbox 服务返回空响应。");
    }

    private static async Task<Dictionary<string, string>> WaitForCallbackAsync(
        TcpListener listener,
        string expectedState,
        string expectedPath,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            var stream = client.GetStream();
            var target = await ReadRequestTargetAsync(stream, cancellationToken).ConfigureAwait(false);
            if (target is null)
            {
                continue;
            }
            var queryStart = target.IndexOf('?');
            var requestPath = queryStart >= 0 ? target[..queryStart] : target;
            if (!requestPath.Equals(expectedPath, StringComparison.OrdinalIgnoreCase))
            {
                await RespondBrowserAsync(stream, false, "404 Not Found").ConfigureAwait(false);
                continue;
            }
            var query = ParseQuery(queryStart >= 0 ? target[(queryStart + 1)..] : "");

            if (query.TryGetValue("error", out var error) && !string.IsNullOrWhiteSpace(error))
            {
                await RespondBrowserAsync(stream, false).ConfigureAwait(false);
                throw new InvalidOperationException($"微软登录被取消或失败：{error}");
            }
            if (!query.TryGetValue("state", out var state) ||
                !string.Equals(state, expectedState, StringComparison.Ordinal))
            {
                await RespondBrowserAsync(stream, false).ConfigureAwait(false);
                throw new InvalidOperationException("微软登录 state 校验失败，请重试。");
            }
            await RespondBrowserAsync(stream, true).ConfigureAwait(false);
            return query;
        }
    }

    private static async Task<string?> ReadRequestTargetAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var one = new byte[1];
        uint tail = 0;
        while (buffer.Length < 32 * 1024)
        {
            var read = await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            buffer.WriteByte(one[0]);
            tail = (tail << 8) | one[0];
            if (tail == 0x0D0A0D0A)
            {
                break;
            }
        }
        if (buffer.Length == 0)
        {
            return null;
        }
        var firstLine = Encoding.ASCII.GetString(buffer.ToArray()).Split("\r\n", 2, StringSplitOptions.None)[0];
        var firstSpace = firstLine.IndexOf(' ');
        var lastSpace = firstLine.LastIndexOf(' ');
        return firstSpace >= 0 && lastSpace > firstSpace ? firstLine[(firstSpace + 1)..lastSpace] : null;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator >= 0 ? pair[..separator] : pair;
            var value = separator >= 0 ? pair[(separator + 1)..] : "";
            result[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value.Replace('+', ' '));
        }
        return result;
    }

    private static async Task RespondBrowserAsync(
        Stream stream,
        bool success,
        string status = "200 OK")
    {
        var html = success
            ? "<!doctype html><meta charset=utf-8><title>PotatoLauncher</title><body style='font-family:Segoe UI;background:#17130e;color:#f2e5d6;padding:48px'><h2>登录成功</h2><p>可以关闭此窗口并返回 PotatoLauncher。</p></body>"
            : "<!doctype html><meta charset=utf-8><title>PotatoLauncher</title><body style='font-family:Segoe UI;background:#17130e;color:#f2e5d6;padding:48px'><h2>登录未完成</h2><p>请返回 PotatoLauncher 查看错误信息。</p></body>";
        var body = Encoding.UTF8.GetBytes(html);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header).ConfigureAwait(false);
        await stream.WriteAsync(body).ConfigureAwait(false);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string TranslateXstsError(string xerr) => xerr switch
    {
        "2148916233" => "该微软账号尚未创建 Xbox 档案。请登录 xbox.com 完成创建。",
        "2148916235" => "该 Xbox 账号所在地区不可用。",
        "2148916236" => "该账号需要在 Xbox 网页完成成人验证。",
        "2148916237" => "该账号所在地区不支持 Xbox Live。",
        "2148916238" => "未成年人账号需要由家庭组管理员允许登录。",
        _ => $"Xbox 登录失败（XErr {xerr}）。"
    };

    private sealed record MsTokenResponse(string AccessToken, string? RefreshToken, int ExpiresIn);

    private sealed class XboxResponse
    {
        [JsonPropertyName("Token")]
        public string Token { get; set; } = "";

        [JsonPropertyName("DisplayClaims")]
        public DisplayClaims? DisplayClaims { get; set; }
    }

    private sealed class DisplayClaims
    {
        [JsonPropertyName("xui")]
        public List<XuiClaim> Xui { get; set; } = [];
    }

    private sealed class XuiClaim
    {
        [JsonPropertyName("uhs")]
        public string UserHash { get; set; } = "";

        [JsonPropertyName("xid")]
        public string? Xid { get; set; }
    }

    private sealed class MinecraftTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = "";

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class MinecraftProfileResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("skins")]
        public List<ProfileTexture> Skins { get; set; } = [];

        [JsonPropertyName("capes")]
        public List<ProfileTexture> Capes { get; set; } = [];
    }

    private sealed class ProfileTexture
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("alias")]
        public string? Alias { get; set; }
    }
}

public sealed class MicrosoftAuthException : Exception
{
    public MicrosoftAuthException(string message, string? xerr = null, Exception? inner = null)
        : base(message, inner)
    {
        Xerr = xerr;
    }

    public string? Xerr { get; }
}
