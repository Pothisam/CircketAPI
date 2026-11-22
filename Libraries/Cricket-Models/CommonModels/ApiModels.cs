namespace Cricket_Models.CommonModels;

public record ErrorDetail(string Field, string Issue);

/// <summary>Throw from services; the API's exception middleware turns it into the standard error body.</summary>
public class ApiException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }
    public List<ErrorDetail>? Details { get; }
    /// <summary>Extra top-level properties merged into the error body (e.g. "current", "expectedSeq").</summary>
    public Dictionary<string, object?>? Extra { get; init; }

    public ApiException(int statusCode, string code, string message, List<ErrorDetail>? details = null) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Details = details;
    }

    public static ApiException Validation(string message, List<ErrorDetail>? details = null) =>
        new(400, "VALIDATION_ERROR", message, details);
    public static ApiException Unauthenticated(string message) => new(401, "UNAUTHENTICATED", message);
    public static ApiException Forbidden(string message) => new(403, "FORBIDDEN", message);
    public static ApiException NotFound(string what) => new(404, "NOT_FOUND", $"{what} not found");
    public static ApiException Conflict(string message, Dictionary<string, object?>? extra = null) =>
        new(409, "CONFLICT", message) { Extra = extra };
}

/// <summary>Collects field errors, then throws one VALIDATION_ERROR.</summary>
public class ValidationBuilder
{
    private readonly List<ErrorDetail> _details = new();

    public bool HasErrors => _details.Count > 0;

    public ValidationBuilder Check(bool ok, string field, string issue)
    {
        if (!ok) Add(field, issue);
        return this;
    }

    // A 150-ball match with a systematic problem would otherwise produce thousands of entries.
    private void Add(string field, string issue)
    {
        if (_details.Count < 50) _details.Add(new ErrorDetail(field, issue));
    }

    public ValidationBuilder OneOf(string? value, HashSet<string> allowed, string field, bool optional = false)
    {
        if (value == null && optional) return this;
        if (value == null || !allowed.Contains(value))
            Add(field, $"must be one of: {string.Join(", ", allowed)}");
        return this;
    }

    public void ThrowIfAny()
    {
        if (_details.Count == 0) return;
        throw ApiException.Validation($"{_details[0].Field}: {_details[0].Issue}", _details);
    }
}

public class ListResponse<T>
{
    public List<T> Items { get; set; } = new();
    public string? NextCursor { get; set; }
}

/// <summary>Caller identity taken from the verified Firebase ID token.</summary>
public class UserContext
{
    public string Uid { get; set; } = "";
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? DisplayName { get; set; }
    public string? PhotoUrl { get; set; }
    public string Provider { get; set; } = "unknown";
    public bool EmailVerified { get; set; }
    public bool IsAdmin { get; set; }
}

/// <summary>limit/cursor handling. The cursor is an opaque base64 offset into a stably-ordered list.</summary>
public static class Paging
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public static (int Limit, int Offset) Parse(int? limit, string? cursor)
    {
        var l = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var offset = 0;
        if (!string.IsNullOrEmpty(cursor))
        {
            try
            {
                offset = int.Parse(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor)));
                if (offset < 0) throw new FormatException();
            }
            catch (Exception)
            {
                throw ApiException.Validation("cursor is invalid", new List<ErrorDetail> { new("cursor", "invalid") });
            }
        }
        return (l, offset);
    }

    /// <summary>Pass the rows fetched with Take(limit + 1); trims the extra row and sets NextCursor.</summary>
    public static ListResponse<T> Build<T>(List<T> fetched, int limit, int offset)
    {
        var hasMore = fetched.Count > limit;
        return new ListResponse<T>
        {
            Items = hasMore ? fetched.Take(limit).ToList() : fetched,
            NextCursor = hasMore ? Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes((offset + limit).ToString())) : null
        };
    }
}

public static class Clock
{
    /// <summary>UTC now truncated to milliseconds (the DB column precision) so timestamps round-trip exactly.</summary>
    public static DateTime UtcNow()
    {
        var t = DateTime.UtcNow;
        return new DateTime(t.Ticks - t.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }
}
