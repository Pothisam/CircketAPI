using Cricket_Models.CommonModels;
using System.Security.Claims;
using System.Text.Json;

namespace Cricket.API.Infrastructure;

public static class ClaimsExtensions
{
    /// <summary>Reads the verified Firebase ID token claims (inbound claim mapping is disabled, so names are raw).</summary>
    public static UserContext ToUserContext(this ClaimsPrincipal user)
    {
        string? Get(string type) => user.FindFirst(type)?.Value;

        var provider = "unknown";
        var firebase = Get("firebase");              // JSON object: { "sign_in_provider": "google.com", ... }
        if (!string.IsNullOrEmpty(firebase))
        {
            try
            {
                using var doc = JsonDocument.Parse(firebase);
                if (doc.RootElement.TryGetProperty("sign_in_provider", out var p) && p.GetString() is { Length: > 0 } s)
                    provider = s;
            }
            catch (JsonException) { /* keep "unknown" */ }
        }

        return new UserContext
        {
            Uid = Get("user_id") ?? Get("sub") ?? "",
            Email = Get("email"),
            PhoneNumber = Get("phone_number"),
            DisplayName = Get("name"),
            PhotoUrl = Get("picture"),
            Provider = provider,
            EmailVerified = string.Equals(Get("email_verified"), "true", StringComparison.OrdinalIgnoreCase),
            IsAdmin = string.Equals(Get("admin"), "true", StringComparison.OrdinalIgnoreCase)
        };
    }
}
