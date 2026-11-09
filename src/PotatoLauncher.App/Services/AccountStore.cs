using System.IO;
using System.Text.Json;
using PotatoLauncher.Core.Models;

namespace PotatoLauncher.App.Services;

public sealed class AccountStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;
    private readonly ProtectedTokenStore _tokenStore;

    public AccountStore(AppPaths paths)
    {
        _paths = paths;
        _tokenStore = new ProtectedTokenStore(paths.TokenFile);
    }

    public LauncherState State { get; private set; } = new();

    public async Task LoadAsync()
    {
        if (File.Exists(_paths.AccountsFile))
        {
            try
            {
                State = JsonSerializer.Deserialize<LauncherState>(
                    await File.ReadAllTextAsync(_paths.AccountsFile), JsonOptions) ?? new LauncherState();
            }
            catch (JsonException)
            {
                State = new LauncherState();
            }
        }

        var tokens = await _tokenStore.LoadAsync();
        foreach (var account in State.Accounts)
        {
            if (tokens.TryGetValue(account.Id, out var token))
            {
                account.AccessToken = token.AccessToken;
                account.RefreshToken = token.RefreshToken;
                account.ExpiresAt = token.ExpiresAt;
                account.Xuid = token.Xuid;
            }
        }
        if (State.Accounts.Count == 0)
        {
            var offline = new AccountSession
            {
                Kind = AccountKind.Offline,
                Name = "Player",
                Uuid = Core.Services.IdentityUtil.OfflineUuid("Player")
            };
            State.Accounts.Add(offline);
            State.SelectedAccountId = offline.Id;
            await SaveAsync();
        }
    }

    public async Task SaveAsync()
    {
        var originalAccounts = State.Accounts.Select(account => account.Clone()).ToArray();
        var safeAccounts = originalAccounts.Select(account =>
        {
            var safe = account.Clone();
            safe.AccessToken = "";
            safe.RefreshToken = null;
            return safe;
        }).ToList();
        var safeState = new LauncherState
        {
            SchemaVersion = State.SchemaVersion,
            Accounts = safeAccounts,
            SelectedAccountId = State.SelectedAccountId,
            RecentInstanceIds = State.RecentInstanceIds.ToList()
        };
        var temp = _paths.AccountsFile + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(safeState, JsonOptions));
        File.Move(temp, _paths.AccountsFile, overwrite: true);
        await _tokenStore.SaveAsync(originalAccounts);
    }

}
