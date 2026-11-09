using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using PotatoLauncher.Core.Models;

namespace PotatoLauncher.App.Services;

public sealed class AuthlibInjectorManager
{
    private const string DownloadUrl =
        "https://github.com/yushijinhun/authlib-injector/releases/download/v1.2.8/authlib-injector-1.2.8.jar";
    private const string ExpectedSha256 =
        "9c7f4343e6c82034958ffb48c14a2cb0c85928be7283103ce17da00c6d5a7b10";
    private readonly AppPaths _paths;
    private readonly HttpClient _httpClient;

    public AuthlibInjectorManager(AppPaths paths, HttpClient httpClient)
    {
        _paths = paths;
        _httpClient = httpClient;
    }

    public async Task<string> EnsureAsync(Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        var bundled = FindBundled();
        if (bundled is not null)
        {
            return bundled;
        }
        if (File.Exists(_paths.AuthlibInjectorFile) && new FileInfo(_paths.AuthlibInjectorFile).Length > 100_000)
        {
            return _paths.AuthlibInjectorFile;
        }

        log?.Invoke("正在获取 authlib-injector 1.2.8（仅离线自定义外观需要）。");
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.AuthlibInjectorFile)!);
        var temp = _paths.AuthlibInjectorFile + ".part";
        using var response = await _httpClient.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var output = File.Create(temp))
        {
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }
        if (new FileInfo(temp).Length < 100_000)
        {
            File.Delete(temp);
            throw new InvalidDataException("authlib-injector 下载结果不是有效的 JAR。");
        }
        await using (var stream = File.OpenRead(temp))
        {
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!hash.Equals(ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temp);
                throw new InvalidDataException("authlib-injector SHA-256 校验失败。");
            }
        }
        File.Move(temp, _paths.AuthlibInjectorFile, overwrite: true);
        log?.Invoke("authlib-injector 已就绪。");
        return _paths.AuthlibInjectorFile;
    }

    private static string? FindBundled()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "dependencies", "authlib-injector-1.2.8.jar"),
            Path.Combine(AppContext.BaseDirectory, "authlib-injector-1.2.8.jar")
        };
        foreach (var candidate in candidates.Where(File.Exists))
        {
            using var stream = File.OpenRead(candidate);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            if (hash.Equals(ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }
        return null;
    }
}
