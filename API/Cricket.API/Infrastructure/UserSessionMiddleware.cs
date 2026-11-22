using Cricket_Repository.UserRepository;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;

namespace Cricket.API.Infrastructure;

/// <summary>
/// Every data row belongs to a users row, so a valid Firebase token alone isn't enough: the user must have
/// called POST /auth/session first. Result is cached briefly to avoid a lookup per request.
/// </summary>
public class UserSessionMiddleware
{
    public const string SessionPath = "/api/v1/auth/session";
    public static string CacheKey(string uid) => $"session:{uid}";

    private readonly RequestDelegate _next;

    public UserSessionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context, IUserRepo users, IMemoryCache cache)
    {
        var endpoint = context.GetEndpoint();
        var needsSession = context.User.Identity?.IsAuthenticated == true
                           && endpoint?.Metadata.GetMetadata<IAllowAnonymous>() == null
                           && !context.Request.Path.Equals(SessionPath, StringComparison.OrdinalIgnoreCase);

        if (needsSession)
        {
            var uid = context.User.ToUserContext().Uid;
            if (!cache.TryGetValue(CacheKey(uid), out bool _))
            {
                var active = await users.GetActiveFlagAsync(uid);
                if (active == null)
                {
                    await ApiErrorWriter.WriteAsync(context, 401, "UNAUTHENTICATED", "No session for this user. Call POST /api/v1/auth/session first.");
                    return;
                }
                if (active == false)
                {
                    await ApiErrorWriter.WriteAsync(context, 403, "FORBIDDEN", "This account is disabled.");
                    return;
                }
                cache.Set(CacheKey(uid), true, TimeSpan.FromSeconds(60));
            }
        }

        await _next(context);
    }
}
