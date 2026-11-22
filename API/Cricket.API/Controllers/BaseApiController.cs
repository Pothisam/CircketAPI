using Cricket.API.Infrastructure;
using Cricket_Models.CommonModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace Cricket.API.Controllers;

/// <summary>
/// Base for all controllers: /api/v1, Firebase-token auth required unless an action says [AllowAnonymous].
/// The user id ALWAYS comes from the verified token, never from the request body.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1")]
public abstract class BaseApiController : ControllerBase
{
    protected UserContext Me => User.ToUserContext();
    protected string Uid => Me.Uid;

    /// <summary>Parses an ISO-8601 query value as UTC (the default model binder would convert to local time).</summary>
    protected static DateTime? ParseSince(string? value, string field = "updatedSince")
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d))
            return DateTime.SpecifyKind(d, DateTimeKind.Utc);
        throw ApiException.Validation($"{field} must be an ISO-8601 date", new List<ErrorDetail> { new(field, "invalid date") });
    }

    /// <summary>The route id wins; a conflicting id in the body is rejected. Ids are client-generated strings (max 64).</summary>
    protected static void ReconcileId(string? bodyId, string routeId, Action<string> setId)
    {
        if (string.IsNullOrWhiteSpace(routeId) || routeId.Length > 64)
            throw ApiException.Validation("id must be 1 to 64 characters", new List<ErrorDetail> { new("id", "invalid length") });
        if (!string.IsNullOrEmpty(bodyId) && bodyId != routeId)
            throw ApiException.Validation("id in the body does not match the URL", new List<ErrorDetail> { new("id", "mismatch") });
        setId(routeId);
    }
}
