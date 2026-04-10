namespace PipelineHealthMonitor.Middleware;

// Middleware runs in the ASP.NET Core request pipeline before controllers.
// This one rejects any request that doesn't carry the correct X-Api-Key header.
public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _apiKey;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        // Reads from environment variables (loaded from .env by DotNetEnv) or appsettings.json.
        _apiKey = configuration["ApiKey"]
            ?? throw new InvalidOperationException("ApiKey is not configured.");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Let Swagger UI through without authentication so the docs are always accessible.
        if (context.Request.Path.StartsWithSegments("/swagger"))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var key) || key != _apiKey)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized: missing or invalid X-Api-Key header.");
            return;
        }

        await _next(context);
    }
}
