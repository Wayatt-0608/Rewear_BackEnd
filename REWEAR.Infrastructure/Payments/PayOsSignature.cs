using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace REWEAR.Infrastructure.Payments;

public static class PayOsSignature
{
    public static bool Verify(JsonElement data, string? signature, string checksumKey)
    {
        if (string.IsNullOrWhiteSpace(checksumKey) || signature?.Length != 64 || data.ValueKind != JsonValueKind.Object) return false;
        try
        {
            var fields = data.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p =>
            {
                var value = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString() ?? "",
                    JsonValueKind.Null => "",
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => p.Value.GetRawText(),
                    _ => throw new FormatException("Unsupported payment signature value.")
                };
                return p.Name + "=" + value;
            });
            var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(checksumKey), Encoding.UTF8.GetBytes(string.Join("&", fields)));
            return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(signature));
        }
        catch (FormatException) { return false; }
    }
}
