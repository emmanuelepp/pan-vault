using PanVault.Api.Validation;

namespace PanVault.Tests;

public class PanMaskerTests
{
    [Theory]
    [InlineData("4111111111111111", "411111******1111")]
    [InlineData("378282246310005", "378282*****0005")]
    public void Mask_KeepsOnlyFirstSixAndLastFour(string pan, string expected) =>
        Assert.Equal(expected, PanMasker.Mask(pan));

    [Theory]
    [InlineData("4111111111")]   
    [InlineData("41111111111")]
    [InlineData("4111")]
    public void Mask_InputTooShortToBeAPan_MasksEverything(string input)
    {
        var masked = PanMasker.Mask(input);

        Assert.Equal(new string('*', input.Length), masked);
        Assert.DoesNotContain(input, masked);
    }

    [Theory]
    [InlineData("2223003122003222", "mastercard")]  
    [InlineData("3528000700000000", "jcb")]         
    [InlineData("6212345678901232", "unionpay")] 
    public void Brand_UsesIinRangesNotJustTheFirstDigit(string pan, string expected) =>
        Assert.Equal(expected, PanMasker.Brand(pan));
}
