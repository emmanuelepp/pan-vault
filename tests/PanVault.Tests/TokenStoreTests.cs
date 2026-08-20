using PanVault.Api.Tokens;

namespace PanVault.Tests;

public class TokenStoreTests
{
    [Fact]
    public void Add_ThenGet_ReturnsTheStoredEntry()
    {
        var store = new TokenStore();

        var token = store.Add("cipher-text", "411111******1111", "visa");
        var entry = store.Get(token);

        Assert.NotNull(entry);
        Assert.Equal("cipher-text", entry.CipherText);
        Assert.Equal("411111******1111", entry.MaskedPan);
        Assert.Equal("visa", entry.Brand);

        Assert.Null(store.Get("tok_does_not_exist"));
    }

    [Fact]
    public void Add_ProducesUniqueTokensUnrelatedToTheCardData()
    {
        var store = new TokenStore();
        const string pan = "4111111111111111";

        var tokens = Enumerable.Range(0, 100)
            .Select(_ => store.Add("cipher-text", "411111******1111", "visa"))
            .ToList();

        Assert.Equal(100, tokens.Distinct().Count());
        Assert.All(tokens, t =>
        {
            Assert.StartsWith("tok_", t);
            Assert.Equal(36, t.Length);
            Assert.DoesNotContain(pan, t);
        });
    }
}
