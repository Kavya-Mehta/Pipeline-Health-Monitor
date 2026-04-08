# Pipeline Health Monitor API

> A production-grade REST API built with C# and ASP.NET Core 8 that tracks the health of data pipelines.

---

## Why this exists

Managing data pipelines manually means checking logs and piecing together whether something failed after the fact. This API centralizes that. Every pipeline run gets logged on open, closed with counts on completion, and is queryable by status, date, or pipeline. A background worker proactively detects stuck runs, stale pipelines, and volume anomalies automatically.

---

## Tech stack

| Layer           | Technology                           |
| --------------- | ------------------------------------ |
| Framework       | ASP.NET Core 8 Web API               |
| Language        | C# 12                                |
| ORM             | Entity Framework Core 8              |
| Database        | SQL Server                           |
| Architecture    | Repository Pattern + Unit of Work    |
| Background Jobs | BackgroundService (3 detection jobs) |
| Testing         | xUnit + Moq (8 unit tests)           |
| Logging         | Serilog (Console + Seq sinks)        |
| Docs            | Swagger / OpenAPI (auto-generated)   |
| Auth            | API key via X-Api-Key header         |

---

## Endpoints

### Pipeline definitions

| Method | Path            | Description                  |
| ------ | --------------- | ---------------------------- |
| POST   | /pipelines      | Create a pipeline definition |
| GET    | /pipelines      | List all pipelines           |
| GET    | /pipelines/{id} | Get a single pipeline        |

### Pipeline runs

| Method | Path                          | Description                       |
| ------ | ----------------------------- | --------------------------------- |
| POST   | /pipelines/runs               | Open a new run (status: RUNNING)  |
| PATCH  | /pipelines/runs/{id}/complete | Close a run with counts + errors  |
| GET    | /pipelines/runs               | List runs (filterable, paginated) |
| GET    | /pipelines/runs/{id}          | Full run detail with errors       |
| GET    | /pipelines/health             | Aggregated health snapshot        |

**GET /pipelines/runs query parameters:**

| Parameter  | Example        | Description             |
| ---------- | -------------- | ----------------------- |
| pipelineId | ?pipelineId=1  | Filter by pipeline ID   |
| status     | ?status=FAILED | Filter by status        |
| page       | ?page=2        | Page number (default 1) |
| pageSize   | ?pageSize=10   | Per page, max 100       |

**overallHealth** values in GET /pipelines/health:
- CRITICAL: any FAILED run in the last 24 hours
- DEGRADED: any PARTIAL run but no FAILED in the last 24 hours
- HEALTHY: no failures or partials in the last 24 hours

---

## Background worker

The PipelineMonitorWorker runs every 60 seconds and performs three detection jobs:

| Job                      | What it detects                         | Action                                       |
| ------------------------ | --------------------------------------- | -------------------------------------------- |
| Stuck run detection      | RUNNING runs past the timeout threshold | Auto-marks FAILED, inserts RUN_TIMEOUT error |
| Stale pipeline detection | Pipelines with no runs in X hours       | Logs STALE_PIPELINE warning                  |
| Volume anomaly detection | Failure rate spike vs last 5 runs       | Logs VOLUME_ANOMALY warning                  |

All thresholds are configurable in appsettings.json under the Monitor section.

---

## Structured logging (Serilog)

All log output goes through [Serilog](https://serilog.net/) instead of the default .NET logger. Serilog writes structured JSON events rather than flat strings, so every log entry carries named properties you can filter and query.

### Sinks

| Sink    | Output                                    |
| ------- | ----------------------------------------- |
| Console | Formatted text to terminal during dev     |
| Seq     | Structured events to http://localhost:5341 |

### What gets logged automatically

- Every HTTP request: method, path, status code, and elapsed ms via `UseSerilogRequestLogging`
- Worker events: stuck runs detected, stale pipelines, volume anomalies
- Startup and shutdown events
- All log entries carry: `Application`, `MachineName`, `ThreadId`, and request context fields

### Running Seq locally

Download from https://datalust.co/seq — free for single-user dev. After install, open http://localhost:5341 to search and filter structured logs with full-text queries like `@Level = 'Warning'` or `PipelineId = 3`.

### Configuration

Serilog is configured in `appsettings.json` under the `Serilog` key. Minimum levels, sinks, and enrichers are all set there — no code changes needed to adjust log verbosity.

---

## Database schema

### pipelines

| Column     | Type         | Notes              |
| ---------- | ------------ | ------------------ |
| id         | int          | PK, identity       |
| name       | varchar(200) | unique, not null   |
| created_at | datetime2    | set on insert      |

### pipeline_runs

| Column          | Type         | Notes                            |
| --------------- | ------------ | -------------------------------- |
| id              | int          | PK, identity                     |
| pipeline_id     | int          | FK to pipelines                  |
| status          | varchar(20)  | CHECK: RUNNING/SUCCESS/FAILED/PARTIAL |
| started_at      | datetime2    |                                  |
| completed_at    | datetime2    | nullable                         |
| records_read    | int          | nullable                         |
| records_written | int          | nullable                         |
| records_failed  | int          | nullable                         |
| error_rate      | computed     | records_failed / records_read (C#) |

### run_errors

| Column     | Type         | Notes              |
| ---------- | ------------ | ------------------ |
| id         | int          | PK, identity       |
| run_id     | int          | FK to pipeline_runs |
| error_code | varchar(100) |                    |
| message    | varchar(2000)|                    |
| occurred_at| datetime2    |                    |

---

## Configuration

**appsettings.json structure:**

```json
{
  "Serilog": {
    "MinimumLevel": { "Default": "Information" },
    "WriteTo": [
      { "Name": "Console" },
      { "Name": "Seq", "Args": { "serverUrl": "http://localhost:5341" } }
    ]
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost\\SQLEXPRESS;Database=PipelineHealthMonitorDb;Trusted_Connection=True;"
  },
  "Monitor": {
    "IntervalSeconds": 60,
    "StuckRunTimeoutMinutes": 60,
    "StalePipelineHours": 24,
    "VolumeAnomalyThreshold": 0.5
  }
}
```

**API key** in `.env` (never commit this):

```
API_KEY=your-secret-key-here
```

---

## Authentication

All endpoints require the `X-Api-Key` header:

```
X-Api-Key: your-secret-key-here
```

The key is loaded from `.env` at startup via DotNetEnv. In production use an environment variable or secrets manager.
In Swagger UI, click Authorize and enter your key to authenticate all requests.

---

## Design decisions

**Repository Pattern + Unit of Work**
Controllers depend on IUnitOfWork, not AppDbContext directly. This decouples HTTP logic from database logic, makes every data access method named and intentional, and enables unit testing with mocks. CommitAsync() is the single transaction commit point per request.

**BackgroundService uses IServiceScopeFactory**
The worker is a singleton. IUnitOfWork is scoped. Injecting a scoped service into a singleton would exhaust the DB connection pool. The factory creates a fresh scope per tick, resolves IUnitOfWork, does the work, and disposes the scope.

**Serilog replaces the default .NET logger**
Default .NET logging writes unstructured strings. Serilog writes structured events with named properties. This means logs are queryable — you can filter by PipelineId or RunId in Seq rather than grepping strings.

**status is a varchar with a CHECK constraint, not a DB enum**
CHECK constraints are more portable across SQL Server and PostgreSQL and can be extended without a schema migration.

**run_errors is a separate table, not a JSON column**
Separate rows allow clean indexed queries. JSON inside a WHERE clause is unindexable.

**error_rate is computed in C#, not stored**
Derived from records_failed / records_read. Storing it creates a consistency risk if counts are updated.

**POST to open, PATCH to complete**
Real pipelines run asynchronously. The process that opens a run is not always the one that closes it.

---

## License

MIT
