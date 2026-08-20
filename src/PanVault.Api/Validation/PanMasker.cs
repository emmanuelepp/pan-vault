namespace PanVault.Api.Validation;

public static class PanMasker
{
    private const int MinPanLength = 12; // same floor as LuhnValidator

    public static string Mask(string pan)
    {
        if (string.IsNullOrEmpty(pan)) return string.Empty;
        if (pan.Length < MinPanLength) return new string('*', pan.Length);

        var bin = pan[..6];
        var last4 = pan[^4..];
        var masked = new string('*', pan.Length - 10);

        return $"{bin}{masked}{last4}";
    }

    public static string Last4(string pan) =>
        pan.Length < 4
            ? throw new ArgumentOutOfRangeException(nameof(pan), "PAN must be at least 4 characters long.")
            : pan[^4..];

    public static string Brand(string pan)
    {
        if (pan.Length < 4 || !pan.All(char.IsAsciiDigit)) return "unknown";

        int Prefix(int length) => int.Parse(pan.AsSpan(0, length));

        if (Prefix(1) == 4) return "visa";
        if (Prefix(2) is >= 51 and <= 55 || Prefix(4) is >= 2221 and <= 2720) return "mastercard";
        if (Prefix(2) is 34 or 37) return "amex";
        if (Prefix(4) is >= 3528 and <= 3589) return "jcb";
        if (Prefix(2) is 36 or 38 or 39 || Prefix(3) is >= 300 and <= 305) return "diners";
        if (Prefix(2) == 62) return "unionpay";
        if (Prefix(4) == 6011 || Prefix(2) == 65 || Prefix(3) is >= 644 and <= 649) return "discover";

        return "unknown";
    }
}
