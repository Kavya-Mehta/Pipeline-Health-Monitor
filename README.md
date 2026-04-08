# Pipeline Health Monitor API

> A REST API built with C# and ASP.NET Core 8 that tracks the health of data pipelines — logs every run, monitors record throughput, surfaces error rates, and exposes queryable endpoints for downstream consumers and monitoring dashboards.


## Why this exists

Managing data pipelines manually means SSHing into servers, querying run tables by hand, and piecing together whether something failed — usually after a downstream team has already noticed. There's no single place to ask "did the orders pipeline succeed last night, how many records did it process, and if it failed, why?"

This API centralizes that. Every pipeline run gets logged on open with a unique run ID, closed with exact record counts on completion, and is immediately queryable by status, date range, or pipeline name. Errors are captured with codes and severity levels — not just a generic failure flag. A single `/health` endpoint aggregates success rates, average throughput, and the last known error across all pipelines, giving operators an instant snapshot without touching the database directly.

Built as a lightweight observability layer that sits on top of any existing pipeline infrastructure — no changes to the pipelines themselves required.


## Tech stack

| Layer | Technology |
| --------- | ---------------------------------- |
| Framework | ASP.NET Core 8 Web API |
| Language | C# 12 |
| ORM | Entity Framework Core 8 |
| Database | SQL Server |
| Docs | Swagger / OpenAPI (auto-generated) |
| Auth | API key via `X-Api-Key` header |


## Endpoints

### Pipeline definitions

Opens a new pipeline run and immediately returns a run ID. The caller holds onto this ID and uses it to close the run once execution finishes. This two-step design (open → complete) mirrors how real async pipeline agents work — the process that starts a run isn't always the one that ends it.

### Pipeline runs

```json
{
  "pipelineId": 1,
  "triggeredBy": "scheduler"
}
```

**Response `201`:**

```json
{
  "id": 42,
  "status": "RUNNING",
  "startedAt": "2025-01-15T10:00:00Z"
}
```


**GET /pipelines/runs query parameters:**

Closes an open run with final record counts and any errors that occurred during execution. Accepts multiple errors per run — each with a typed error code and severity level so they can be aggregated and queried independently. Returns `409 Conflict` if the run is already closed, preventing double-writes.

**overallHealth** values in GET /pipelines/health:
- CRITICAL: any FAILED run in the last 24 hours
- DEGRADED: any PARTIAL run but no FAILED in the last 24 hours
- HEALTHY: no failures or partials in the last 24 hours


### `GET /pipelines/runs`

Lists all runs in reverse chronological order. Supports chained query filters so operators can slice by exactly what they need — failed runs from last week, all runs for a specific pipeline, runs within a date window. Returns error rate pre-computed per run so callers don't have to calculate it.

| Parameter | Example | Description |
| -------------- | ---------------------- | ----------------------------- |
| `status` | `?status=FAILED` | Filter by run status |
| `pipelineName` | `?pipelineName=orders` | Filter by pipeline name |
| `from` | `?from=2025-01-01` | Runs started after this date |
| `to` | `?to=2025-01-31` | Runs started before this date |


The PipelineMonitorWorker runs every 60 seconds and performs three detection jobs:

Full detail for a single run including every associated error. Includes computed `errorRate` (failed / read × 100) and `durationSeconds` derived from start and end timestamps. Useful for post-mortems and incident reviews where you need the complete picture for one specific execution.

All thresholds are configurable in appsettings.json under the Monitor section.


## Structured logging (Serilog)

Aggregated health summary computed across all completed runs. Designed to power a monitoring dashboard or alert system — gives you overall success rate, average record throughput, average error rate, and the most recent error with full context. Only completed runs are included; in-progress `RUNNING` runs are excluded from all aggregations.

### Sinks

| Sink    | Output                                    |
| ------- | ----------------------------------------- |
| Console | Formatted text to terminal during dev     |
| Seq     | Structured events to http://localhost:5341 |


- Every HTTP request: method, path, status code, and elapsed ms via `UseSerilogRequestLogging`
- Worker events: stuck runs detected, stale pipelines, volume anomalies
- Startup and shutdown events
- All log entries carry: `Application`, `MachineName`, `ThreadId`, and request context fields

Three tables. `pipelines` is the master registry — you register a pipeline once. `pipeline_runs` gets a new row every time a pipeline executes. `run_errors` captures individual errors per run so they can be queried, grouped, and aggregated by code or severity independently.

```
pipelines
─────────────────────────────
id              INT PK
name            VARCHAR(200) UNIQUE
source_system   VARCHAR(100)
sink_system     VARCHAR(100)
description     VARCHAR (nullable)
created_at      DATETIME


pipeline_runs
─────────────────────────────
id              INT PK
pipeline_id     INT FK → pipelines.id
started_at      DATETIME
ended_at        DATETIME (nullable)
status          VARCHAR  CHECK: RUNNING | SUCCESS | FAILED | PARTIAL
records_read    INT
records_written INT
records_failed  INT
triggered_by    VARCHAR (nullable)


run_errors
─────────────────────────────
id              INT PK
run_id          INT FK → pipeline_runs.id
error_code      VARCHAR(100)
error_message   TEXT
severity        VARCHAR  CHECK: INFO | WARNING | CRITICAL
occurred_at     DATETIME
```


### Configuration

Serilog is configured in `appsettings.json` under the `Serilog` key. Minimum levels, sinks, and enrichers are all set there — no code changes needed to adjust log verbosity.


## Database schema


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

**API key** — set in `PipelineHealthMonitor/.env` (never in `appsettings.json`):

```
ApiKey=your-secret-api-key-here
```

Copy `.env.example` to `.env` and replace the placeholder. The `.env` file is git-ignored and will never be committed.


## Authentication

All endpoints require the `X-Api-Key` header:

```
X-Api-Key: your-secret-key-here
```

The key is loaded from `.env` at startup via DotNetEnv. In production use an environment variable or secrets manager.
In Swagger UI, click Authorize and enter your key to authenticate all requests.


## Design decisions

**`status` is a varchar with a CHECK constraint, not a DB enum**
CHECK constraints are more portable across SQL Server and PostgreSQL and can be extended without a schema migration that alters an enum type definition. Adding a new status value like `CANCELLED` is a single-line SQL change rather than a DDL type alteration. A common trade-off in production systems that need to stay schema-flexible.

**`run_errors` is a separate table, not a JSON column**
Separate rows let you query errors independently — "show all runs with SCHEMA_MISMATCH errors in the last 7 days" is a clean indexed query with a simple WHERE clause. Storing errors as JSON would make that query require parsing inside the predicate, which is unindexable and slow at scale. The separate table also lets you aggregate by error code across all runs to find the most frequent failure types.

**`error_rate` is computed in C#, not stored**
Derived from `records_failed / records_read`. Storing it would create a consistency risk — if either count were ever updated, the stored rate would silently be wrong. Computed properties calculated at read time are always accurate, and the denominator guard (`RecordsRead > 0`) lives in exactly one place.

**POST to open, PATCH to complete**
Real pipelines run asynchronously — the process that starts a run isn't always the one that ends it. A scheduler might open the run, hand off to a worker process, and the worker closes it with counts when done. Splitting the lifecycle into two calls mirrors how monitoring agents actually work and avoids requiring the caller to know the final record counts at the moment the run starts.

**Indexes on `pipeline_id`, `status`, and `started_at`**
These are the three columns that every real query filters on. Without them, listing failed runs or filtering by pipeline would be full table scans. The indexes are defined explicitly in `AppDbContext.OnModelCreating` rather than relying on EF Core conventions, making the intent clear and reviewable.


**error_rate is computed in C#, not stored**
Derived from records_failed / records_read. Storing it creates a consistency risk if counts are updated.

This project is a direct extension of pipeline monitoring done manually in a previous data engineering role — tracking run status, record counts, and failure rates across ETL pipelines loading inventory and order data into a warehouse. In that role, checking whether a pipeline succeeded meant querying the run table directly and cross-referencing logs. This API formalizes that observability layer into a documented, authenticated service with structured error capture and aggregated health metrics — the kind of tooling that would have made incident response significantly faster.


## License

MIT
