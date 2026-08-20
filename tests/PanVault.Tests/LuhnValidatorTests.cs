using PanVault.Api.Validation;

namespace PanVault.Tests;

public class LuhnValidatorTests
{
    [Theory]
    [InlineData("4111111111111111", true)]   // Visa
    [InlineData("378282246310005", true)]    // Amex, 15 digits
    [InlineData("2223003122003222", true)]   // Mastercard series 2
    [InlineData("4111111111111112", false)]  // check digit is wrong
    [InlineData("4111111111", false)]        // shorter than 12
    [InlineData("4111-1111-1111-1111", false)] // separators are not digits
    [InlineData("", false)]
    public void IsValid_ReturnsExpectedResult(string pan, bool expected) =>
        Assert.Equal(expected, LuhnValidator.IsValid(pan));
}
