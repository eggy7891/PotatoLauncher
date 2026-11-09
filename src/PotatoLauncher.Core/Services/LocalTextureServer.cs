using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using PotatoLauncher.Core.Models;

namespace PotatoLauncher.Core.Services;

public sealed class LocalTextureServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly TextureBundle _bundle;
    private readonly Dictionary<string, (string Path, string ContentType)> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _publicKeyPem;
    private Task? _serverTask;
    private bool _started;

    public LocalTextureServer(TextureBundle bundle)
    {
        _bundle = bundle;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var endpoint = (IPEndPoint)_listener.LocalEndpoint;
        BaseUri = new Uri($"http://localhost:{endpoint.Port}/");
        _publicKeyPem = _rsa.ExportSubjectPublicKeyInfoPem();
        RegisterTexture(bundle.SkinPath, "image/png");
        RegisterTexture(bundle.CapePath, "image/png");
    }

    public Uri BaseUri { get; }

    public void Start()
    {
        if (_started)
        {
            return;
        }
        _started = true;
        _serverTask = ServeAsync(_shutdown.Token);
    }

    public static string FindBundledInjector()
    {
        var appBase = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(appBase, "dependencies", "authlib-injector-1.2.8.jar"),
            Path.Combine(appBase, "authlib-injector-1.2.8.jar"),
            Path.Combine(Environment.CurrentDirectory, "dependencies", "authlib-injector-1.2.8.jar")
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _shutdown.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        try
        {
            _listener.Stop();
        }
        catch (SocketException)
        {
            // Listener shutdown is idempotent.
        }
        if (_serverTask is not null)
        {
            try
            {
                await _serverTask.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is ObjectDisposedException or SocketException or OperationCanceledException)
            {
                // Expected when the game exits.
            }
        }
        _rsa.Dispose();
        _shutdown.Dispose();
    }

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                break;
            }
            _ = HandleClientAsync(client, cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            try
            {
                var requestLine = await ReadRequestLineAsync(stream, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(requestLine))
                {
                    await WriteResponseAsync(stream, 400, "text/plain", "Bad Request", cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                var firstSpace = requestLine.IndexOf(' ');
                var lastSpace = requestLine.LastIndexOf(' ');
                if (firstSpace < 0 || lastSpace <= firstSpace)
                {
                    await WriteResponseAsync(stream, 400, "text/plain", "Bad Request", cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }
                var method = requestLine[..firstSpace];
                var target = requestLine[(firstSpace + 1)..lastSpace];
                if (!method.Equals("GET", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteResponseAsync(stream, 405, "text/plain", "Method Not Allowed", cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                var path = target.Split('?', 2)[0];
                if (path == "/")
                {
                    await WriteJsonAsync(stream, BuildMetadata(), cancellationToken).ConfigureAwait(false);
                    return;
                }

                var profilePrefix = "/sessionserver/session/minecraft/profile/";
                if (path.StartsWith(profilePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var id = path[profilePrefix.Length..];
                    if (!Guid.TryParseExact(id, "N", out var uuid) || uuid != _bundle.ProfileId)
                    {
                        await WriteResponseAsync(stream, 404, "text/plain", "Not Found", cancellationToken)
                            .ConfigureAwait(false);
                        return;
                    }
                    await WriteJsonAsync(stream, BuildProfile(), cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (path.StartsWith("/textures/", StringComparison.OrdinalIgnoreCase) &&
                    _textures.TryGetValue(path["/textures/".Length..], out var texture) &&
                    File.Exists(texture.Path))
                {
                    var bytes = await File.ReadAllBytesAsync(texture.Path, cancellationToken).ConfigureAwait(false);
                    await WriteResponseAsync(stream, 200, texture.ContentType, bytes, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                await WriteResponseAsync(stream, 404, "text/plain", "Not Found", cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
            {
                // The game may close a texture connection early.
            }
        }
    }

    private JsonObject BuildMetadata()
    {
        return new JsonObject
        {
            ["signaturePublickey"] = _publicKeyPem,
            ["skinDomains"] = new JsonArray("127.0.0.1", "localhost"),
            ["meta"] = new JsonObject
            {
                ["serverName"] = "PotatoLauncher",
                ["implementationName"] = "PotatoLauncher Local Textures",
                ["implementationVersion"] = "1.0.1",
                ["feature.non_email_login"] = true
            }
        };
    }

    private JsonObject BuildProfile()
    {
        var textures = new JsonObject();
        if (FindTexture("skin", out var skinUrl))
        {
            var skin = new JsonObject { ["url"] = skinUrl };
            if (_bundle.SkinModel.Equals("slim", StringComparison.OrdinalIgnoreCase))
            {
                skin["metadata"] = new JsonObject { ["model"] = "slim" };
            }
            textures["SKIN"] = skin;
        }
        if (FindTexture("cape", out var capeUrl))
        {
            textures["CAPE"] = new JsonObject { ["url"] = capeUrl };
        }

        var payload = new JsonObject
        {
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["profileId"] = _bundle.ProfileId.ToString("N"),
            ["profileName"] = _bundle.ProfileName,
            ["textures"] = textures
        };
        var valueBytes = Encoding.UTF8.GetBytes(payload.ToJsonString());
        var signature = _rsa.SignData(valueBytes, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        return new JsonObject
        {
            ["id"] = _bundle.ProfileId.ToString("N"),
            ["name"] = _bundle.ProfileName,
            ["properties"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "textures",
                    ["value"] = Convert.ToBase64String(valueBytes),
                    ["signature"] = Convert.ToBase64String(signature)
                }
            }
        };
    }

    private bool FindTexture(string kind, out string url)
    {
        var path = kind == "skin" ? _bundle.SkinPath : _bundle.CapePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            url = "";
            return false;
        }
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        url = new Uri(BaseUri, "textures/" + hash).ToString();
        return true;
    }

    private void RegisterTexture(string? path, string contentType)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        _textures[hash] = (path, contentType);
    }

    private static async Task<string?> ReadRequestLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var one = new byte[1];
        uint tail = 0;
        while (buffer.Length < 16 * 1024)
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
        var request = Encoding.ASCII.GetString(buffer.ToArray());
        return request.Split("\r\n", 2, StringSplitOptions.None)[0];
    }

    private static Task WriteJsonAsync(
        Stream stream,
        JsonNode json,
        CancellationToken cancellationToken)
    {
        return WriteResponseAsync(
            stream,
            200,
            "application/json; charset=utf-8",
            Encoding.UTF8.GetBytes(json.ToJsonString()),
            cancellationToken);
    }

    private static async Task WriteResponseAsync(
        Stream stream,
        int status,
        string contentType,
        byte[] body,
        CancellationToken cancellationToken)
    {
        var reason = status switch
        {
            200 => "OK",
            400 => "Bad Request",
            404 => "Not Found",
            405 => "Method Not Allowed",
            _ => "Error"
        };
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
    }

    private static Task WriteResponseAsync(
        Stream stream,
        int status,
        string contentType,
        string text,
        CancellationToken cancellationToken)
    {
        return WriteResponseAsync(stream, status, contentType, Encoding.UTF8.GetBytes(text), cancellationToken);
    }
}
