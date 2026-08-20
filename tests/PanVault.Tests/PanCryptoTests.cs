using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using PanVault.Api.Crypto;

namespace PanVault.Tests;

public class PanCryptoTests
{
    private static PanCrypto CreateCrypto() =>
        new(Options.Create(new PanCryptoOptions
        {
            Dek = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }));

    [Fact]
    public void Encrypt_ThenDecrypt_ReturnsTheOriginalPan()
    {
        var crypto = CreateCrypto();
        const string pan = "4111111111111111";

        var ciphertext = crypto.Encrypt(pan);

        Assert.NotEqual(pan, ciphertext);
        Assert.Equal(pan, crypto.Decrypt(ciphertext));
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_Throws()
    {
        var crypto = CreateCrypto();
        var blob = Convert.FromBase64String(crypto.Encrypt("4111111111111111"));

        blob[^1] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(
            () => crypto.Decrypt(Convert.ToBase64String(blob)));
    }
}
