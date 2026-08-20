using System.Text.Json;
using PanVault.Api.Validation;

namespace PanVault.Tests;

public class SadGuardTests
{
    private static bool ContainsSad(string json, out string field)
    {
        using var document = JsonDocument.Parse(json);
        return SadGuard.ContainsSad(document.RootElement, out field);
    }

    [Theory]
    [InlineData("""{"pan":"4111111111111111","cvv":"123"}""")]
    [InlineData("""{"pan":"4111111111111111","card":{"cvv":"123"}}""")]        
    [InlineData("""{"pan":"4111111111111111","items":[{"pin":"1234"}]}""")]    
    [InlineData("""{"cvv_2":"123"}""")]                                       
    public void ContainsSad_RejectsSensitiveAuthenticationData(string json)
    {
        Assert.True(ContainsSad(json, out var field));
        Assert.NotEmpty(field);
    }

    [Theory]
    [InlineData("""{"pan":"4111111111111111","expirationDate":"12/28"}""")]  
    [InlineData("""{"pan":"4111111111111111","card":{"brand":"visa"}}""")]
    [InlineData("""{"description":"payment with cvv included"}""")]          
    [InlineData("""{"pincode":"1234"}""")]                                   
    public void ContainsSad_AllowsCleanPayloads(string json)
    {
        Assert.False(ContainsSad(json, out var field));
        Assert.Empty(field);
    }
}
