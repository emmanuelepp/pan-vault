using System.Text.Json;

namespace PanVault.Api.Validation;

public static class SadGuard
{
    private static readonly HashSet<string> ForbiddenFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "cvv", "cvv2", "cvc", "cvc2", "cav2", "cid", "csc", "cvn", "securitycode",
        "pin", "pinblock", "track", "track1", "track2", "trackdata", "magstripe"
    };

    public static bool ContainsSad(JsonElement payload, out string field)
    {
        field = string.Empty;

        if (payload.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in payload.EnumerateArray())
                if (ContainsSad(item, out field)) return true;
        }
        else if (payload.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in payload.EnumerateObject())
            {
                if (ForbiddenFields.Contains(Normalize(property.Name)))
                {
                    field = property.Name;
                    return true;
                }

                if (ContainsSad(property.Value, out field)) return true;
            }
        }

        return false;
    }

    private static string Normalize(string key) => new(key.Where(char.IsLetterOrDigit).ToArray());
}
