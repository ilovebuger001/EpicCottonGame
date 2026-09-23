using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;

namespace EpicCottonGame.Services;

/// <summary>
/// Save storage that uses localStorage for all accounts and additionally
/// syncs named accounts (non-EC-XXXX) with Firebase Realtime Database
/// so progress is shared across devices.
/// </summary>
public sealed class LocalSaveService(IJSRuntime js, HttpClient http)
{
    const string SaveKey    = "epic-cotton-game.save.v5";
    const string AccountKey = "epic-cotton-game.account.v1";
    const string SecretKey  = "epic-cotton-game.integrity.v5";
    const string FirebaseUrl = "https://epiccottongame-default-rtdb.firebaseio.com";

    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    static bool IsCloudAccount(string id) =>
        !string.IsNullOrWhiteSpace(id) && !id.StartsWith("EC-", StringComparison.OrdinalIgnoreCase);

    static string FirebaseKey(string accountId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accountId.ToLowerInvariant())));

    // --- Account identity ---

    public async Task SetAccountIdAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        await js.InvokeVoidAsync("localStorage.setItem", AccountKey, id.Trim());
    }

    public async Task<string> GetAccountIdAsync()
    {
        var id = await js.InvokeAsync<string?>("localStorage.getItem", AccountKey);
        if (!string.IsNullOrWhiteSpace(id)) return id.Trim();

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

    // --- Password management ---

    string PwdLocalKey(string accountId) =>
        "epic-cotton-game.pwd." + FirebaseKey(accountId);

    public async Task<bool> AccountHasPasswordAsync(string accountId)
    {
        if (IsCloudAccount(accountId))
        {
            try
            {
                var hash = await http.GetFromJsonAsync<string?>($"{FirebaseUrl}/passwords/{FirebaseKey(accountId)}.json");
                if (!string.IsNullOrWhiteSpace(hash)) return true;
            }
            catch { }
        }
        var stored = await js.InvokeAsync<string?>("localStorage.getItem", PwdLocalKey(accountId));
        return !string.IsNullOrWhiteSpace(stored);
    }

    public async Task<bool> CheckAccountPasswordAsync(string accountId, string password)
    {
        string? stored = null;
        if (IsCloudAccount(accountId))
        {
            try { stored = await http.GetFromJsonAsync<string?>($"{FirebaseUrl}/passwords/{FirebaseKey(accountId)}.json"); }
            catch { }
        }
        if (string.IsNullOrWhiteSpace(stored))
            stored = await js.InvokeAsync<string?>("localStorage.getItem", PwdLocalKey(accountId));
        if (string.IsNullOrWhiteSpace(stored)) return true;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password ?? "")));
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(stored), Encoding.UTF8.GetBytes(hash));
    }

    public async Task SetAccountPasswordAsync(string accountId, string password)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
        await js.InvokeVoidAsync("localStorage.setItem", PwdLocalKey(accountId), hash);
        if (IsCloudAccount(accountId))
        {
            try { await http.PutAsJsonAsync($"{FirebaseUrl}/passwords/{FirebaseKey(accountId)}.json", hash); }
            catch { }
        }
    }

    public async Task RemoveAccountPasswordAsync(string accountId)
    {
        await js.InvokeVoidAsync("localStorage.removeItem", PwdLocalKey(accountId));
        if (IsCloudAccount(accountId))
        {
            try { await http.DeleteAsync($"{FirebaseUrl}/passwords/{FirebaseKey(accountId)}.json"); }
            catch { }
        }
    }

    // --- Load ---

    public async Task<SaveLoadResult<T>> LoadAsync<T>(string accountId)
    {
        var localEnv = await LoadLocalEnvelopeAsync(accountId);
        SaveEnvelope? cloudEnv = null;
        if (IsCloudAccount(accountId)) cloudEnv = await LoadCloudEnvelopeAsync(accountId);

        var best = PickNewer(localEnv, cloudEnv);
        if (best == null) return new(false, true, default);

        if (cloudEnv != null && best == cloudEnv && best != localEnv)
        {
            try
            {
                var raw = JsonSerializer.Serialize(best, JsonOptions);
                await js.InvokeVoidAsync("localStorage.setItem", SaveKey, raw);
            }
            catch { }
        }

        try
        {
            var data = JsonSerializer.Deserialize<T>(best.Data, JsonOptions);
            return new(true, data != null, data);
        }
        catch { return new(true, false, default); }
    }

    // --- Save ---

    public async Task SaveAsync<T>(string accountId, T data)
    {
        try
        {
            var serialized = JsonSerializer.Serialize(data, JsonOptions);
            var secret     = await GetOrCreateSecretAsync();
            var envelope   = new SaveEnvelope
            {
                Version    = 5,
                AccountId  = accountId,
                SavedAtUtc = DateTime.UtcNow,
                Data       = serialized,
                Signature  = Sign(accountId, serialized, secret)
            };
            var raw = JsonSerializer.Serialize(envelope, JsonOptions);
            await js.InvokeVoidAsync("localStorage.setItem", SaveKey, raw);
            if (IsCloudAccount(accountId)) _ = PushToCloudAsync(accountId, envelope);
        }
        catch { }
    }

    // --- Private helpers ---

    async Task<SaveEnvelope?> LoadLocalEnvelopeAsync(string accountId)
    {
        try
        {
            var raw = await js.InvokeAsync<string?>("localStorage.getItem", SaveKey);
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var env = JsonSerializer.Deserialize<SaveEnvelope>(raw, JsonOptions);
            if (env == null || env.Version != 5) return null;
            if (!string.Equals(env.AccountId, accountId, StringComparison.OrdinalIgnoreCase)) return null;
            var secret   = await GetOrCreateSecretAsync();
            var expected = Sign(env.AccountId, env.Data, secret);
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(env.Signature))) return null;
            return env;
        }
        catch { return null; }
    }

    async Task<SaveEnvelope?> LoadCloudEnvelopeAsync(string accountId)
    {
        try
        {
            var env = await http.GetFromJsonAsync<SaveEnvelope?>($"{FirebaseUrl}/saves/{FirebaseKey(accountId)}.json", JsonOptions);
            if (env == null || env.Version != 5) return null;
            return env;
        }
        catch { return null; }
    }

    async Task PushToCloudAsync(string accountId, SaveEnvelope envelope)
    {
        try
        {
            var cloudEnv = new SaveEnvelope
            {
                Version    = envelope.Version,
                AccountId  = envelope.AccountId,
                SavedAtUtc = envelope.SavedAtUtc,
                Data       = envelope.Data,
                Signature  = ""
            };
            await http.PutAsJsonAsync($"{FirebaseUrl}/saves/{FirebaseKey(accountId)}.json", cloudEnv, JsonOptions);
        }
        catch { }
    }

    static SaveEnvelope? PickNewer(SaveEnvelope? a, SaveEnvelope? b)
    {
        if (a == null) return b;
        if (b == null) return a;
        return b.SavedAtUtc > a.SavedAtUtc ? b : a;
    }

    async Task<string> GetOrCreateSecretAsync()
    {
        var secret = await js.InvokeAsync<string?>("localStorage.getItem", SecretKey);
        if (!string.IsNullOrWhiteSpace(secret)) return secret;
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
        public int      Version    { get; set; }
        public string   AccountId  { get; set; } = "";
        public DateTime SavedAtUtc { get; set; }
        public string   Data       { get; set; } = "";
        public string   Signature  { get; set; } = "";
    }

    public readonly record struct SaveLoadResult<T>(bool Found, bool Valid, T? Data);
}
