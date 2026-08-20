namespace PanVault.Api.Validation;

public static class LuhnValidator
{
    public static bool IsValid(string pan)
    {
        if (string.IsNullOrWhiteSpace(pan) || pan.Length is < 12 or > 19)
            return false;

        if (!pan.All(char.IsAsciiDigit))
            return false;

        var sum = 0;
        var doubleIt = false;

        for (var i = pan.Length - 1; i >= 0; i--)
        {
            var digit = pan[i] - '0';

            if (doubleIt)
            {
                digit *= 2;
                if (digit > 9) digit -= 9;
            }

            sum += digit;
            doubleIt = !doubleIt;
        }

        return sum % 10 == 0;
    }
}