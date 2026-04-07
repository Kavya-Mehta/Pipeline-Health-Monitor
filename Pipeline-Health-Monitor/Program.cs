using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using PipelineHealthMonitor.Data;
using PipelineHealthMonitor.Middleware;

var builder = WebApplication.CreateBuilder(args);

// ── 1. Register Controllers ──────────────────────────────────────────────────
// Tells ASP.NET Core to scan for classes ending in "Controller" and wire up
// their [HttpGet], [HttpPost] etc. attributes as HTTP endpoints.
builder.Services.AddControllers();

// ── 2. Register EF Core with SQL Server ─────────────────────────────────────
// AddDbContext registers AppDbContext as a scoped service (one per HTTP request).
// UseSqlServer reads the connection string from appsettings.json.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// ── 3. Configure Swagger / OpenAPI ──────────────────────────────────────────
// Swagger auto-generates interactive API documentation from your controller code.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "Pipeline Health Monitor API",
        Version     = "v1",
        Description = "Monitor and track data pipeline runs, health, and errors."
    });

    // Tell Swagger about our API key auth so the UI shows an Authorize button
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Type        = SecuritySchemeType.ApiKey,
        In          = ParameterLocation.Header,
        Name        = "X-Api-Key",
        Description = "Enter your API key in the field below."
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id   = "ApiKey"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// ── 4. Swagger UI ────────────────────────────────────────────────────────────
// Available in all environments (including Production) for this project.
// Visit http://localhost:{port}/swagger to open the interactive docs.
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Pipeline Health Monitor API v1");
    c.RoutePrefix = "swagger";
});

// ── 5. API Key Middleware ────────────────────────────────────────────────────
// Runs before every controller action. Rejects requests missing X-Api-Key.
app.UseMiddleware<ApiKeyMiddleware>();

// ── 6. HTTPS Redirect ────────────────────────────────────────────────────────
app.UseHttpsRedirection();

// ── 7. Map Controllers ───────────────────────────────────────────────────────
// Activates the route table built from [Route] and [Http*] attributes.
app.MapControllers();

app.Run();
