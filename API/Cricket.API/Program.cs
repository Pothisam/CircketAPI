using Cricket.API.Infrastructure;
using Cricket_Models.CommonModels;
using Cricket_Models.ConfigurationModels;
using Cricket_Services.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// ---- Logging (Serilog, from appsettings). Tokens are never logged. ----
var logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .Enrich.FromLogContext()
    .CreateLogger();
builder.Logging.ClearProviders();
builder.Logging.AddSerilog(logger);

try
{
    // ---- Configuration ----
    builder.Services.Configure<FirebaseConfig>(configuration.GetSection("FirebaseConfig"));
    builder.Services.Configure<AppConfig>(configuration.GetSection("AppConfig"));
    var firebaseProjectId = configuration["FirebaseConfig:ProjectId"] ?? "cricket-run-notification";

    // ---- Services / repositories / DbContext / Firebase Admin ----
    builder.Services.ServicesDependencyInjection(configuration);

    // ---- Authentication: verify Firebase ID tokens (signature via Google's public keys, issuer, audience, expiry) ----
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = $"https://securetoken.google.com/{firebaseProjectId}";
            options.MapInboundClaims = false;      // keep Firebase claim names (user_id, email, phone_number, ...)
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = $"https://securetoken.google.com/{firebaseProjectId}",
                ValidateAudience = true,
                ValidAudience = firebaseProjectId,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = "user_id"
            };
            options.Events = new JwtBearerEvents
            {
                OnChallenge = context =>
                {
                    context.HandleResponse();
                    return ApiErrorWriter.WriteAsync(context.HttpContext, 401, "UNAUTHENTICATED", "Missing, invalid or expired token.");
                },
                OnForbidden = context =>
                    ApiErrorWriter.WriteAsync(context.HttpContext, 403, "FORBIDDEN", "You do not have access to this resource.")
            };
        });
    // Admin = a Firebase custom claim { "admin": true } set with the Admin SDK.
    builder.Services.AddAuthorization(o => o.AddPolicy("Admin", p => p.RequireClaim("admin", "true")));

    builder.Services.AddControllers()
        .ConfigureApiBehaviorOptions(options =>
        {
            // Model-binding / DataAnnotations failures -> the standard VALIDATION_ERROR body.
            options.InvalidModelStateResponseFactory = ctx =>
            {
                var details = ctx.ModelState
                    .Where(kv => kv.Value is { Errors.Count: > 0 })
                    .SelectMany(kv => kv.Value!.Errors.Select(e => new ErrorDetail(
                        ToCamel(kv.Key.TrimStart('$', '.')),
                        string.IsNullOrEmpty(e.ErrorMessage) ? "invalid value" : e.ErrorMessage)))
                    .Take(50).ToList();
                var message = details.Count > 0 ? $"{details[0].Field}: {details[0].Issue}" : "Validation failed";
                return new ApiErrorResult(message, details);
            };
        });

    // ---- Swagger with Bearer support ----
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
            Scheme = "Bearer",
            BearerFormat = "JWT",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Description = "Paste a Firebase ID token. No need to type \"Bearer \"."
        });
        options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            {
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    {
                        Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    });

    // ---- Rate limiting: ~60 req/min per user, more headroom for the live-scoring ball endpoints ----
    builder.Services.AddRateLimiter(options =>
    {
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        {
            var key = ctx.User.FindFirst("user_id")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
            var scoring = ctx.Request.Path.Value?.Contains("/balls", StringComparison.OrdinalIgnoreCase) == true;
            return RateLimitPartition.GetFixedWindowLimiter($"{key}|{(scoring ? "scoring" : "default")}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = scoring ? 600 : 60,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
        });
        options.OnRejected = async (context, token) =>
        {
            context.HttpContext.Response.Headers.RetryAfter = "60";
            await ApiErrorWriter.WriteAsync(context.HttpContext, 429, "RATE_LIMITED", "Too many requests. Try again shortly.");
        };
    });

    // ---- CORS: configured origins + any localhost port ----
    const string AllowedOriginsPolicy = "AllowedOrigins";
    var allowedOrigins = new HashSet<string>(
        configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>(),
        StringComparer.OrdinalIgnoreCase);
    builder.Services.AddCors(options =>
    {
        options.AddPolicy(AllowedOriginsPolicy, policy =>
        {
            policy.SetIsOriginAllowed(origin =>
                      allowedOrigins.Contains(origin) ||
                      (Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                       (uri.Host == "localhost" || uri.Host == "127.0.0.1")))
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
    });

    var app = builder.Build();

    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseCors(AllowedOriginsPolicy);

    // Swagger is intentionally enabled in every environment (including production) at /swagger.
    app.UseSwagger();
    app.UseSwaggerUI(options => options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None));

    var apiVersion = configuration["ApiInfo:Version"];
    app.Use(async (context, next) =>
    {
        context.Response.Headers["X-API-Version"] = apiVersion;
        await next();
    });

    app.UseHttpsRedirection();
    app.UseAuthentication();
    app.UseRateLimiter();                       // after authentication so it can partition by user
    app.UseMiddleware<UserSessionMiddleware>(); // 401 until POST /auth/session has created the user row
    app.UseAuthorization();
    app.MapControllers();

    app.Run();
}
catch (Exception ex) when (ex is not Microsoft.Extensions.Hosting.HostAbortedException)   // EF tooling / test hosts abort the host on purpose
{
    logger.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

static string ToCamel(string s) => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s[1..];

// Lets integration tests (WebApplicationFactory<Program>) see the entry point.
public partial class Program { }
