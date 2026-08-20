using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace PanVault.Api.Tokens;

public sealed class TokenStore
{
    private readonly ConcurrentDictionary<string, VaultEntry> _entries = new();

    public string Add(string cipherText, string maskedPan, string brand)
    {
        var token = NewToken();
        _entries[token] = new VaultEntry(cipherText, maskedPan, brand, DateTimeOffset.UtcNow);
        return token;
    }

    public VaultEntry? Get(string token) =>
        _entries.TryGetValue(token, out var entry) ? entry : null;

    private static string NewToken() =>
        "tok_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}