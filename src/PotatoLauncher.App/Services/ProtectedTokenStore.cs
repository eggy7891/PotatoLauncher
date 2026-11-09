using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PotatoLauncher.Core.Models;

namespace PotatoLauncher.App.Services;

public sealed class ProtectedTokenStore
{
    private readonly string _path;

    public ProtectedTokenStore(string path)
    {
        _path = path;
    }

    public async Task<Dictionary<string, StoredTokens>> LoadAsync()
    {
        if (!File.Exists(_path))
        {
            return new Dictionary<string, StoredTokens>(StringComparer.OrdinalIgnoreCase);
        }

        var encrypted = await File.ReadAllBytesAsync(_path);
        if (encrypted.Length == 0)
        {
            return new Dictionary<string, StoredTokens>(StringComparer.OrdinalIgnoreCase);
        }

        var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<Dictionary<string, StoredTokens>>(plain)
               ?? new Dictionary<string, StoredTokens>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task SaveAsync(IEnumerable<AccountSession> accounts)
    {
        var tokens = accounts
            .Where(account => !string.IsNullOrWhiteSpace(account.AccessToken) ||
                              !string.IsNullOrWhiteSpace(account.RefreshToken))
            .ToDictionary(
                account => account.Id,
                account => new StoredTokens(
                    account.AccessToken,
                    account.RefreshToken,
                    account.ExpiresAt,
                    account.Xuid),
                StringComparer.OrdinalIgnoreCase);
        var plain = JsonSerializer.SerializeToUtf8Bytes(tokens);
        var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        await File.WriteAllBytesAsync(temp, encrypted);
        File.Move(temp, _path, overwrite: true);
    }
}

public sealed record StoredTokens(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    string? Xuid);
