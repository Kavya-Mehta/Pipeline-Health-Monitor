using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using PipelineHealthMonitor.Data;
using PipelineHealthMonitor.Interfaces;
using PipelineHealthMonitor.Middleware;
using PipelineHealthMonitor.Repositories;
using PipelineHealthMonitor.Services;
using Serilog;

// ── Bootstrap logger ─────────────────────────────────────────────────────────
// This is a minimal logger used ONLY during startup, before the full Serilog
// pipeline is configured. If Program.cs itself throws, you still see the error.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Pipeline Health Monitor API...");

    DotNetEnv.Env.Load();

    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog ───────────────────────────────────────────────────────────────
    // UseSerilog() replaces the default .NET logging entirely.
    // ReadFrom.Configuration() reads all Serilog settings from appsettings.json.
    // This means the Console sink, Seq sink, enrichers, and minimum levels
    // are all controlled from config -- no hardcoding in code.
    //
    // WithProperty("Application", ...) attaches the app name to EVERY log entry.
    // In Seq you can then filter by Application to separate logs from multiple services.
    builder.Host.UseSerilog((ctx, services, config) =>
        config
            .ReadFrom.Configuration(ctx.Configuration)
            .ReadFrom.Services(services)
            .Enrich.WithProperty("Application", "PipelineHealthMonitor"));

    // ── Controllers ───────────────────────────────────────────────────────────
    builder.Services.AddControllers();

    // ── EF Core ───────────────────────────────────────────────────────────────
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlServer(
            builder.Configuration.GetConnectionString("DefaultConnection")));

    // ── Repository Pattern + Unit of Work ─────────────────────────────────────
    builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

    // ── Background Worker ─────────────────────────────────────────────────────
    builder.Services.AddHostedService<PipelineMonitorWorker>();

    // ── Swagger / OpenAPI ─────────────────────────────────────────────────────
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title       = "Pipeline Health Monitor API",
            Version     = "v1",
            Description = "Monitor and track data pipeline runs, health, and errors."
        });

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

    // ── Serilog request logging ────────────────────────────────────────────────
    // Logs one structured entry per HTTP request with method, path, status code,
    // and duration. Much richer than the default ASP.NET Core request logging.
    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
    });

    // ── Swagger UI ────────────────────────────────────────────────────────────
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Pipeline Health Monitor API v1");
        c.RoutePrefix = "swagger";
    });

    // ── API Key Middleware ─────────────────────────────────────────────────────
    app.UseMiddleware<ApiKeyMiddleware>();

    // ── HTTPS Redirect ────────────────────────────────────────────────────────
    app.UseHttpsRedirection();

    // ── Map Controllers ───────────────────────────────────────────────────────
    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    // If startup itself crashes, the bootstrap logger captures it before anything
    // is set up, so you see the exception rather than a silent failure.
    Log.Fatal(ex, "Pipeline Health Monitor API failed to start.");
}
finally
{
    // Flush and close all Serilog sinks before the process exits.
    // Without this, the last few log entries might not reach Seq.
    Log.CloseAndFlush();
}
