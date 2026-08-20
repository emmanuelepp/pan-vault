using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace PanVault.Api.Crypto;

public sealed class PanCrypto
{
    private const int NonceSize = 12; // AES-GCM standard nonce size
    private const int TagSize = 16; // AES-GCM standard tag size
    private const int VersionSize = 1;
    private const byte Version1 = 1;
    private const int HeaderSize = VersionSize + NonceSize + TagSize;
    private readonly byte[] _key;
    public PanCrypto(IOptions<PanCryptoOptions> options)
    {
        _key = Convert.FromBase64String(options.Value.Dek);

        if (_key.Length != 32) throw new InvalidOperationException("The Dek must be a 32-byte key.");

    }

    public string Encrypt(string plaintext)
    {
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);

        var output = new byte[HeaderSize + plain.Length];
        output[0] = Version1;
        nonce.CopyTo(output, VersionSize);

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(
            nonce,
            plain,
            output.AsSpan(HeaderSize),
            output.AsSpan(VersionSize + NonceSize, TagSize),
            output.AsSpan(0, VersionSize)); // the version byte is authenticated

        return Convert.ToBase64String(output);
    }

    public string Decrypt(string ciphertext)
    {
        var input = Convert.FromBase64String(ciphertext);
        if (input.Length < HeaderSize)
            throw new InvalidOperationException("The ciphertext is too short.");

        if (input[0] != Version1)
            throw new InvalidOperationException($"Unsupported ciphertext version {input[0]}.");

        var plaintext = new byte[input.Length - HeaderSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(
            input.AsSpan(VersionSize, NonceSize),
            input.AsSpan(HeaderSize),
            input.AsSpan(VersionSize + NonceSize, TagSize),
            plaintext,
            input.AsSpan(0, VersionSize));

        return Encoding.UTF8.GetString(plaintext);
    }

}
