using Cricket_Models.CommonModels;
using System.Text.Json;

namespace Cricket.API.Infrastructure;

/// <summary>Writes the spec's standard error body: { "error": { "code", "message", "details" } }.</summary>
public static class ApiErrorWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, int status, string code, string message,
        IEnumerable<ErrorDetail>? details = null, Dictionary<string, object?>? extra = null)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";

        var error = new Dictionary<string, object?> { ["code"] = code, ["message"] = message };
        var detailList = details?.ToList();
        if (detailList is { Count: > 0 }) error["details"] = detailList;

        var body = new Dictionary<string, object?> { ["error"] = error };
        if (extra != null)
        {
            foreach (var kv in extra) body[kv.Key] = kv.Value;
        }
        return context.Response.WriteAsync(JsonSerializer.Serialize(body, Json));
    }

    public static Task WriteAsync(HttpContext context, ApiException ex) =>
        WriteAsync(context, ex.StatusCode, ex.Code, ex.Message, ex.Details, ex.Extra);
}

/// <summary>400 VALIDATION_ERROR result used for model-binding / DataAnnotations failures.</summary>
public class ApiErrorResult : Microsoft.AspNetCore.Mvc.IActionResult
{
    private readonly string _message;
    private readonly List<ErrorDetail> _details;

    public ApiErrorResult(string message, List<ErrorDetail> details)
    {
        _message = message;
        _details = details;
    }

    public Task ExecuteResultAsync(Microsoft.AspNetCore.Mvc.ActionContext context) =>
        ApiErrorWriter.WriteAsync(context.HttpContext, 400, "VALIDATION_ERROR", _message, _details);
}
