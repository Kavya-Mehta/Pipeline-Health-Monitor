using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PipelineHealthMonitor.Data;
using PipelineHealthMonitor.Interfaces;
using PipelineHealthMonitor.Middleware;
using PipelineHealthMonitor.Repositories;
using PipelineHealthMonitor.Services;
using Serilog;
using Serilog.Events;

// ── Bootstrap logger ─────────────────────────────────────────────────────────
// This catches startup errors before the full Serilog config is loaded.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    // Load .env file into environment variables before anything else.
    // ApiKey and other secrets live in .env — never committed to git.
    Env.Load();

    var builder = WebApplication.CreateBuilder(args);

    // ── 1. Serilog ───────────────────────────────────────────────────────────
    // Replaces the default .NET logger with Serilog.
    // ReadFrom.Configuration reads the Serilog section from appsettings.json.
    // ReadFrom.Services allows Serilog sinks that need DI services.
    // Enrich.WithProperty adds "Application" to every log event.
    builder.Host.UseSerilog((ctx, services, config) =>
        config
            .ReadFrom.Configuration(ctx.Configuration)
            .ReadFrom.Services(services)
            .Enrich.WithProperty("Application", "PipelineHealthMonitor"));

    // ── 2. Controllers ───────────────────────────────────────────────────────
    builder.Services.AddControllers();

    // ── 3. EF Core ───────────────────────────────────────────────────────────
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlServer(
            builder.Configuration.GetConnectionString("DefaultConnection")));

    // ── 4. Repository + Unit of Work ─────────────────────────────────────────
    // Scoped = one UnitOfWork (and its underlying DbContext) per HTTP request.
    builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

    // ── 5. Background Worker ─────────────────────────────────────────────────
    // Registered as a hosted service — starts when the app starts, runs until shutdown.
    builder.Services.AddHostedService<PipelineMonitorWorker>();

    // ── 6. OpenTelemetry Distributed Tracing ─────────────────────────────────
    // A "trace" = the full story of one request, from HTTP in to DB out.
    // A "span"  = one timed step inside that trace (e.g. one SQL query).
    // Jaeger    = the browser UI where you search and inspect traces.
    //
    // AddAspNetCoreInstrumentation — automatically wraps every HTTP request in a span.
    // AddSqlClientInstrumentation  — automatically wraps every SQL query in a span,
    //                                and optionally includes the SQL text so you can
    //                                see the actual queries in Jaeger.
    // AddSource("...Worker")        — allows the background worker's manual spans
    //                                to be captured (they use a custom ActivitySource).
    // AddOtlpExporter              — ships traces to Jaeger over OTLP gRPC (port 4317).
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService(
            serviceName: builder.Configuration["OpenTelemetry:ServiceName"]
                         ?? "PipelineHealthMonitor",
            serviceVersion: "1.0.0"))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation(opts => opts.RecordException = true)
            .AddSqlClientInstrumentation(opts => opts.RecordException = true)
            .AddSource("PipelineHealthMonitor.Worker")
            .AddOtlpExporter(opts =>
                opts.Endpoint = new Uri(
                    builder.Configuration["OpenTelemetry:OtlpEndpoint"]
                    ?? "http://localhost:4317")));

    // ── 7. Swagger ───────────────────────────────────────────────────────────
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
            Description = "Enter your API key."
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

    // ── Serilog request logging ───────────────────────────────────────────────
    // Logs one structured event per HTTP request with method, path, status, and timing.
    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
    });

    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Pipeline Health Monitor API v1");
        c.RoutePrefix = "swagger";
    });

    app.UseMiddleware<ApiKeyMiddleware>();
    app.UseHttpsRedirection();
    app.MapControllers();

    // Create DB tables if they don't exist yet.
    // EnsureCreated() is a no-op when the DB already has the tables.
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
    }

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
