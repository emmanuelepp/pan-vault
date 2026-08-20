namespace PanVault.Api.Tokens;

public sealed record TokenizeRequest(string Pan, string? Expiry);

public sealed record TokenizeResponse(string Token, string Last4, string Brand);

public sealed record TokenDetails(string Token, string MaskedPan, string Brand, DateTimeOffset CreatedAt);

public sealed record VaultEntry(string CipherText, string MaskedPan, string Brand, DateTimeOffset CreatedAt);