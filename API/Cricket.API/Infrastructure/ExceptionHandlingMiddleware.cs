using Cricket_Models.CommonModels;

namespace Cricket.API.Infrastructure;

/// <summary>Turns ApiException into the standard error body; logs anything else and returns a generic 500.</summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ApiException ex)
        {
            if (context.Response.HasStarted) throw;
            await ApiErrorWriter.WriteAsync(context, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            if (context.Response.HasStarted) throw;
            await ApiErrorWriter.WriteAsync(context, 500, "SERVER_ERROR", "An unexpected error occurred.");
        }
    }
}
