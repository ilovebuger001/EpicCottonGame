using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;

namespace EpicCottonGame.Services;

/// <summary>Browser-local save storage with a lightweight integrity check for classroom use.</summary>
public sealed class LocalSaveService(IJSRuntime js)
{
    const string SaveKey = "epic-cotton-game.save.v5";
    const string AccountKey = "epic-cotton-game.account.v1";
    const string SecretKey = "epic-cotton-game.integrity.v5";
    const string PwdKeyPrefix = "epic-cotton-game.pwd.";

    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // --- Account identity ---

    public async Task SetAccountIdAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        await js.InvokeVoidAsync("localStorage.setItem", AccountKey, id.Trim());
    }

    public async Task<string> GetAccountIdAsync()
    {
        var id = await js.InvokeAsync<string?>("localStorage.getItem", AccountKey);
        if (!string.IsNullOrWhiteSpace(id))
            return id.Trim();

        // Fall back to a URL-passed email stored by JS
        var email = await js.InvokeAsync<string?>("localStorage.getItem", "email");
        if (!string.IsNullOrWhiteSpace(email))
        {
            await js.InvokeVoidAsync("localStorage.setItem", AccountKey, email.Trim());
            return email.Trim();
        }

        id = $"EC-{Convert.ToHexString(RandomNumberGenerator.GetBytes(4))}";
        await js.InvokeVoidAsync("localStorage.setItem", AccountKey, id);
        return id;
    }

    // --- Per-account password (SHA-256 hash stored in localStorage) ---

    static string PwdKey(string accountId) =>
        PwdKeyPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accountId.ToLowerInvariant())));

    public async Task<bool> AccountHasPasswordAsync(string accountId)
    {
        var stored = await js.InvokeAsync<string?>("localStorage.getItem", PwdKey(accountId));
        return !string.IsNullOrWhiteSpace(stored);
    }

    public async Task<bool> CheckAccountPasswordAsync(string accountId, string password)
    {
        var stored = await js.InvokeAsync<string?>("localStorage.getItem", PwdKey(accountId));
        if (string.IsNullOrWhiteSpace(stored)) return true; // no password set
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password ?? "")));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(stored),
            Encoding.UTF8.GetBytes(hash));
    }

    public async Task SetAccountPasswordAsync(string accountId, string password)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
        await js.InvokeVoidAsync("localStorage.setItem", PwdKey(accountId), hash);
    }

    public async Task RemoveAccountPasswordAsync(string accountId)
    {
        await js.InvokeVoidAsync("localStorage.removeItem", PwdKey(accountId));
    }

    public async Task<SaveLoadResult<T>> LoadAsync<T>(string accountId)
    {
        var raw = await js.InvokeAsync<string?>("localStorage.getItem", SaveKey);
        if (string.IsNullOrWhiteSpace(raw))
            return new(false, true, default);

        try
        {
            var envelope = JsonSerializer.Deserialize<SaveEnvelope>(raw, JsonOptions);
            if (envelope == null || envelope.AccountId != accountId || envelope.Version != 5)
                return new(true, false, default);

            var secret = await GetOrCreateSecretAsync();
            var expected = Sign(accountId, envelope.Data, secret);
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(envelope.Signature)))
                return new(true, false, default);

            var data = JsonSerializer.Deserialize<T>(envelope.Data, JsonOptions);
            return new(true, data != null, data);
        }
        catch
        {
            return new(true, false, default);
        }
    }

    public async Task SaveAsync<T>(string accountId, T data)
    {
        try
        {
            var serialized = JsonSerializer.Serialize(data, JsonOptions);
            var secret = await GetOrCreateSecretAsync();
            var envelope = new SaveEnvelope
            {
                Version = 5,
                AccountId = accountId,
                SavedAtUtc = DateTime.UtcNow,
                Data = serialized,
                Signature = Sign(accountId, serialized, secret)
            };

            var raw = JsonSerializer.Serialize(envelope, JsonOptions);
            await js.InvokeVoidAsync("localStorage.setItem", SaveKey, raw);
        }
        catch
        {
            // Browser storage can be unavailable or full; the running game remains playable.
        }
    }

    async Task<string> GetOrCreateSecretAsync()
    {
        var secret = await js.InvokeAsync<string?>("localStorage.getItem", SecretKey);
        if (!string.IsNullOrWhiteSpace(secret))
            return secret;

        secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await js.InvokeVoidAsync("localStorage.setItem", SecretKey, secret);
        return secret;
    }

    static string Sign(string accountId, string data, string secret)
    {
        var bytes = Encoding.UTF8.GetBytes($"{accountId}|{secret}|{data}");
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    sealed class SaveEnvelope
    {
        public int Version { get; set; }
        public string AccountId { get; set; } = "";
        public DateTime SavedAtUtc { get; set; }
        public string Data { get; set; } = "";
        public string Signature { get; set; } = "";
    }

    public readonly record struct SaveLoadResult<T>(bool Found, bool Valid, T? Data);
}
