# Pipeline Health Monitor API

> A production-grade REST API built with C# and ASP.NET Core 8 that tracks the health of data pipelines.

---

## What this project does

Data pipelines are automated jobs that move or process data — for example, a script that copies orders into a warehouse database every night, or a job that generates daily reports. When these jobs fail or get stuck, you often find out hours later from a complaint.

This API is the tracker. Every pipeline job reports its start, finish, and any errors here. A background worker runs every 60 seconds and automatically detects jobs that are stuck, pipelines that have gone silent, and failure rate spikes. Every log message and every database query is observable through Serilog and OpenTelemetry.

---

## Tech stack

| Layer | Technology |
|---|---|
| Framework | ASP.NET Core 8 Web API |
| Language | C# 12 |
| ORM | Entity Framework Core 8 |
| Database | SQL Server |
| Architecture | Repository Pattern + Unit of Work |
| Background jobs | BackgroundService (3 detection jobs) |
| Testing | xUnit + Moq (8 unit tests) |
| Structured logging | Serilog → Console + Seq |
| Distributed tracing | OpenTelemetry → Jaeger |
| Docs | Swagger / OpenAPI |
| Auth | API key via X-Api-Key header |
| Secrets | DotNetEnv (.env file) |

---

## How a real pipeline uses this API

```
Your data job (Python, Java, etc.)      Pipeline Health Monitor API
──────────────────────────────────      ────────────────────────────
Job starts               →   POST /pipelines/runs        → status = RUNNING
Job finishes OK          →   PATCH /runs/{id}/complete   → status = SUCCESS
Job crashes              →   PATCH /runs/{id}/complete   → status = FAILED + errors
Job gets stuck silently  →   (nothing)                   → worker auto-marks FAILED after 60 min
```

During development you can test with Postman, pretending to be the data job.

---

## API endpoints

### Pipeline definitions

| Method | Path | Description |
|---|---|---|
| POST | /pipelines | Create a pipeline definition |
| GET | /pipelines | List all pipelines |
| GET | /pipelines/{id} | Get a single pipeline |

### Pipeline runs

| Method | Path | Description |
|---|---|---|
| POST | /pipelines/runs | Open a new run (status: RUNNING) |
| PATCH | /pipelines/runs/{id}/complete | Close a run with counts and errors |
| GET | /pipelines/runs | List runs (filterable, paginated) |
| GET | /pipelines/runs/{id} | Full run detail with errors |
| GET | /pipelines/health | Aggregated health snapshot |

**GET /pipelines/runs query parameters:**

| Parameter | Example | Description |
|---|---|---|
| pipelineId | ?pipelineId=1 | Filter by pipeline |
| status | ?status=FAILED | Filter by status |
| page | ?page=2 | Page number (default 1) |
| pageSize | ?pageSize=10 | Per page, max 100 |

**overallHealth values in GET /pipelines/health:**
- `CRITICAL` — any FAILED run in the last 24 hours
- `DEGRADED` — any PARTIAL run but no FAILED in the last 24 hours
- `HEALTHY` — no failures or partials in the last 24 hours

---

## Architecture

### Repository Pattern + Unit of Work

Controllers never touch `AppDbContext` directly. Every database operation goes through a named interface:

```
IUnitOfWork
  ├── IPipelineRepository      → pipelines table
  ├── IPipelineRunRepository   → pipeline_runs table
  └── IRunErrorRepository      → run_errors table
```

`CommitAsync()` is the single transaction commit point. A controller calls it once at the end of a request. This makes unit testing possible — the test swaps in mock implementations with no database needed.

### Background worker

`PipelineMonitorWorker` runs every 60 seconds and performs three detection jobs:

| Job | What it detects | Action |
|---|---|---|
| Stuck run detection | RUNNING runs past the timeout | Auto-marks FAILED, inserts RUN_TIMEOUT error |
| Stale pipeline detection | Pipelines with no runs in X hours | Logs STALE_PIPELINE warning |
| Volume anomaly detection | Failure rate spike in last 5 runs | Logs VOLUME_ANOMALY warning |

The worker is a singleton. It uses `IServiceScopeFactory` to create a fresh scope each tick so the scoped `IUnitOfWork` (and its `DbContext`) is properly managed and disposed.

---

## Structured logging — Serilog

All log output goes through [Serilog](https://serilog.net/). Serilog writes structured JSON events rather than flat strings — every log entry carries named properties you can filter and query.

### Sinks

| Sink | Output |
|---|---|
| Console | Formatted text to terminal during development |
| Seq | Structured events to http://localhost:5341 |

### What gets logged automatically

- Every HTTP request: method, path, status code, elapsed ms
- Worker events: stuck runs, stale pipelines, volume anomalies
- Startup and shutdown
- All entries carry: `Application`, `MachineName`, `ThreadId`, request context

### Searching in Seq

Open `http://localhost:5341` and use filter expressions like:
- `@Level = 'Warning'` — all warnings
- `PipelineId = 3` — everything about pipeline 3
- `@Message like '%STALE%'` — stale pipeline detections

---

## Distributed tracing — OpenTelemetry + Jaeger

Every HTTP request generates a **trace** — a timeline showing how long each step took. A **span** is one timed operation within that trace (e.g. one SQL query).

### What gets traced automatically

| Instrumentation | What it captures |
|---|---|
| ASP.NET Core | Every incoming HTTP request as a root span |
| SQL Client | Every SQL query EF Core executes as a child span |
| Worker ActivitySource | Each of the 3 detection jobs as its own named span |

### Viewing traces in Jaeger

1. Open `http://localhost:16686`
2. Select `PipelineHealthMonitor` in the Service dropdown
3. Click **Find Traces**
4. Click any trace to see the full span timeline

This tells you not just that `GET /pipelines/health` was slow — it tells you *which of the 10 SQL queries* was the slow one.

### Custom span tags on worker spans

Each background job span carries metadata visible in Jaeger:

| Span | Tags |
|---|---|
| DetectStuckRuns | `timeout.minutes`, `stuck.runs.found` |
| DetectStalePipelines | `stale.threshold.hours` |
| DetectVolumeAnomalies | `anomaly.threshold` |

---

## Database schema

### pipelines

| Column | Type | Notes |
|---|---|---|
| id | int | PK, identity |
| name | varchar(200) | unique, not null |
| description | varchar(500) | nullable |
| source_system | varchar(200) | not null |
| sink_system | varchar(200) | not null |
| created_at | datetime2 | set on insert |

### pipeline_runs

| Column | Type | Notes |
|---|---|---|
| id | int | PK, identity |
| pipeline_id | int | FK to pipelines |
| status | varchar(20) | RUNNING / SUCCESS / FAILED / PARTIAL |
| triggered_by | varchar(200) | nullable |
| started_at | datetime2 | |
| completed_at | datetime2 | nullable |
| records_read | int | nullable |
| records_written | int | nullable |
| records_failed | int | nullable |

### run_errors

| Column | Type | Notes |
|---|---|---|
| id | int | PK, identity |
| run_id | int | FK to pipeline_runs |
| error_code | varchar(100) | e.g. RUN_TIMEOUT |
| message | varchar(2000) | |
| occurred_at | datetime2 | |

`error_rate` is computed in C# from `records_failed / records_read` — not stored, to avoid consistency risk.

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
  },
  "OpenTelemetry": {
    "ServiceName": "PipelineHealthMonitor",
    "OtlpEndpoint": "http://localhost:4317"
  }
}
```

**API key** in `.env` (never committed):

```
ApiKey=your-secret-key-here
```

---

## Authentication

All endpoints require the `X-Api-Key` header:

```
X-Api-Key: your-secret-key-here
```

The key is loaded from `.env` at startup. In Swagger UI, click **Authorize** and enter your key.

---

## Running locally

### Prerequisites

- .NET 8 SDK
- SQL Server Express (or LocalDB)
- [Seq](https://datalust.co/seq) for log viewing (free single-user)
- [Jaeger](https://www.jaegertracing.io/download/) for trace viewing

### Setup

```bash
# 1. Restore packages
dotnet restore

# 2. Start Seq (download from https://datalust.co/seq)

# 3. Start Jaeger
.\jaeger.exe

# 4. Run the API
dotnet run
```

Open `http://localhost:5288/swagger` to use the interactive API docs.

### Run the tests

```bash
cd PipelineHealthMonitor.Tests
dotnet test
```

Expected: 8 tests, all passing.

---

## Design decisions

**Repository Pattern + Unit of Work** — controllers depend on interfaces, not EF Core. This decouples HTTP logic from database logic and makes unit testing straightforward.

**BackgroundService uses IServiceScopeFactory** — the worker is a singleton but `IUnitOfWork` is scoped. Injecting scoped into singleton leaks DB connections. The factory creates a fresh scope per tick.

**Serilog replaces the default .NET logger** — default logging writes unstructured strings. Serilog writes structured events with named properties, making logs queryable in Seq.

**OpenTelemetry with OTLP exporter** — vendor-neutral tracing standard. Traces export to Jaeger via OTLP gRPC. Switching to a different backend (Zipkin, Tempo) requires changing one config value.

**status is varchar with no enum** — more portable across databases and extensible without schema migrations.

**error_rate computed in C#** — derived from counts already stored. Storing it would create a consistency risk.

---

## License

MIT
