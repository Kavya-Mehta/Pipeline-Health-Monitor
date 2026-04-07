namespace PipelineHealthMonitor.Middleware;

/// <summary>
/// Middleware runs as a link in the HTTP request pipeline.
/// Every request passes through here before reaching any controller.
///
/// This middleware enforces API key authentication:
/// - Clients must send header:  X-Api-Key: {your-key}
/// - If missing or wrong → 401 Unauthorized
/// - Swagger UI paths are exempt so the docs still work without a key
/// </summary>
public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private const string ApiKeyHeaderName = "X-Api-Key";

    // RequestDelegate = "the next step in the pipeline"
    public ApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IConfiguration configuration)
    {
        // Allow Swagger UI through without authentication
        if (context.Request.Path.StartsWithSegments("/swagger"))
        {
            await _next(context);
            return;
        }

        var configuredKey = configuration["ApiKey"];

        // If no key is configured in appsettings.json, skip auth entirely
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            await _next(context);
            return;
        }

        // Check the header
        if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var providedKey)
            || providedKey != configuredKey)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                "{\"error\":\"Unauthorized. Provide a valid X-Api-Key header.\"}");
            return;
        }

        // Key is valid — pass the request on to the next middleware/controller
        await _next(context);
    }
}
