using  System.ComponentModel.DataAnnotations;

namespace PanVault.Api.Crypto;


public sealed class PanCryptoOptions
{
    public const string SectionName = "PanCrypto";

    [Required(ErrorMessage = "The Dek property is required.")]
    public string Dek {get;init;} = string.Empty;
}