# Web Crawler System

A job-based web crawler built with .NET 8, React, RabbitMQ, and PostgreSQL. Users submit a URL, the system crawls it asynchronously up to a configurable depth, and the results are presented in a tree view with per-page Domain Link Ratios.

---

## Table of Contents

- [How to Run Locally](#how-to-run-locally)
- [Architecture Overview](#architecture-overview)
- [API Reference](#api-reference)
- [Message Schema](#message-schema)
- [Event-Driven Design](#event-driven-design)
  - [Idempotency Strategy](#idempotency-strategy)
  - [Retry Policy](#retry-policy)
  - [Dead-Letter Queue](#dead-letter-queue)
- [SQL Model & Performance](#sql-model--performance)
- [What Was Implemented First (and Why)](#what-was-implemented-first-and-why)
- [What Was Cut (and Why)](#what-was-cut-and-why)
- [What Would Come Next](#what-would-come-next)
- [Known Limitations & Trade-offs](#known-limitations--trade-offs)

---

## How to Run Locally

### Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (v24+) — that's it

### 1. Start the full stack

```bash
docker compose up --build
```

This starts five services:

| Container | Port | Description |
|---|---|---|
| `crawl_postgres` | 5433 | PostgreSQL database |
| `crawl_rabbitmq` | 5672 / 15672 | RabbitMQ (AMQP / Management UI) |
| `crawl_api` | 5000 | Crawl API (REST) |
| `crawl_worker` | — | Crawl Worker |
| `crawl_frontend` | 5173 | React UI (nginx) |

The API auto-applies EF Core migrations on startup — no manual database setup needed.

Open [http://localhost:5173](http://localhost:5173) in your browser.

To run multiple worker replicas:
```bash
docker compose up --build --scale worker=3
```

RabbitMQ Management UI: [http://localhost:15672](http://localhost:15672) (guest / guest)

Swagger UI: [http://localhost:5000/swagger](http://localhost:5000/swagger)

### 2. (Optional) Run the frontend in dev mode

If you have [Node.js](https://nodejs.org/) (v20+) installed and want hot-reload during development:

```bash
cd frontend
npm install
npm run dev
```

### 3. Run the tests

```bash
cd backend
dotnet test Crawl.sln
```

### 4. Quick smoke test

1. Open the UI, enter `https://example.com`, click **Start Crawl**.
2. You are redirected to the Job Details page — status shows **Running** with a live page counter.
3. After a few seconds the status changes to **Completed** and a tree of discovered pages appears with their Domain Link Ratios.
4. Click **Back to History** to see all previous jobs.
5. While a job is running, click **Cancel** to stop it mid-crawl.

---

## Architecture Overview

```
┌─────────────┐   POST /api/jobs    ┌──────────────────┐
│   React UI  │ ─────────────────── │   Crawl API       │
│  (Vite/TS)  │ ← polling (3s)      │  (ASP.NET Core 8) │
└─────────────┘                     └────────┬─────────┘
                                             │  StartCrawlJob (RabbitMQ)
                                             ▼
                                    ┌──────────────────┐
                                    │   Crawl Worker   │
                                    │  (.NET 8 Worker) │
                                    └────────┬─────────┘
                                             │  read/write
                                             ▼
                                    ┌──────────────────┐
                                    │   PostgreSQL      │
                                    │  Jobs/Pages/Edges │
                                    └──────────────────┘
```

**Three .NET projects share one solution (`Crawl.sln`):**

| Project | Role |
|---|---|
| `Crawl.Core` | Domain models (`Job`, `Page`, `Edge`), interfaces (`IJobRepository`), MassTransit message contracts (`StartCrawlJob`, `CrawlJobCompleted`), and `LinkExtractor` service |
| `Crawl.Api` | ASP.NET Core REST API — creates/cancels jobs, serves job status, history, and the hierarchical tree result |
| `Crawl.Worker` | .NET Worker Service — consumes `StartCrawlJob`, runs concurrent BFS crawler, persists results, publishes `CrawlJobCompleted` |
| `Crawl.Tests` | xUnit test project — unit tests for `LinkExtractor` + integration tests using local HTML fixtures |

**Key technology choices:**

- **MassTransit** over raw RabbitMQ client — built-in message correlation, retry middleware, error-queue routing, and a clean consumer abstraction.
- **HtmlAgilityPack** for HTML parsing — battle-tested, handles malformed HTML gracefully.
- **Polly** for HTTP resilience — exponential back-off retries decoupled from crawl logic.
- **EF Core + raw Npgsql** — EF Core for schema management (migrations, LINQ queries); raw Npgsql `NpgsqlBinaryImporter`-style batch inserts with `ON CONFLICT DO NOTHING` for idempotent high-throughput writes during BFS.

---

## API Reference

All endpoints are under `http://localhost:5000/api/jobs`.

### `POST /api/jobs` — Create a job

**Request body:**
```json
{ "url": "https://example.com", "maxDepth": 2 }
```
`maxDepth` is optional (default `2`, clamped to 1–5).

**Response `201 Created`:**
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "url": "https://example.com",
  "status": "Pending",
  "createdAt": "2026-03-05T22:18:37Z",
  "startedAt": null,
  "completedAt": null,
  "failureReason": null,
  "pages": [],
  "edges": [],
  "pageCount": 0
}
```

### `GET /api/jobs/{id}` — Job status, summary, and results

Returns the job with all crawled pages and edges embedded.

### `GET /api/jobs/{id}/tree` — Hierarchical tree result

Returns the crawl result as a recursive tree built server-side from the Edges table. Only available for `Completed` jobs; returns `400` for other statuses.

**Response `200 OK`:**
```json
{
  "url": "https://example.com",
  "domainLinkRatio": 0.875,
  "children": [
    {
      "url": "https://example.com/about",
      "domainLinkRatio": 1.0,
      "children": []
    }
  ]
}
```

Only crawled pages appear as tree nodes — frontier URLs (links beyond the crawl depth) are excluded so every visible node has a Domain Link Ratio.

### `GET /api/jobs?page=1&pageSize=20` — Job history

Returns all jobs ordered by `createdAt` descending (most recent first). Paginated; `pageSize` clamped to 1–100.

**Response envelope:**
```json
{
  "items": [...],
  "totalCount": 42,
  "page": 1,
  "pageSize": 20,
  "totalPages": 3
}
```

### `POST /api/jobs/{id}/cancel` — Cancel a job

Transitions a `Pending` or `Running` job to `Canceled`. Returns `409 Conflict` for terminal states.

### Health endpoints

| Endpoint | Check |
|---|---|
| `GET /health` (API, port 5000) | Liveness — self only |
| `GET /health/ready` (API, port 5000) | Readiness — self + database + RabbitMQ bus |
| `GET /health` (Worker, port 8081) | Liveness — self only |
| `GET /health/ready` (Worker, port 8081) | Readiness — self + database |

---

## Message Schema

### `StartCrawlJob` (API → Worker)

Published by the API when a new job is created. Consumed by the Worker.

```json
{
  "jobId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "url": "https://example.com",
  "maxDepth": 2
}
```

| Field | Type | Description |
|---|---|---|
| `jobId` | GUID | Unique job identifier; used as idempotency key |
| `url` | string | Seed URL to start crawling from |
| `maxDepth` | int | Maximum BFS depth (1–5) |

### `CrawlJobCompleted` (Worker → any subscriber)

Published by the Worker when a job finishes (successfully or not).

```json
{
  "jobId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "success": true,
  "errorMessage": null
}
```

| Field | Type | Description |
|---|---|---|
| `jobId` | GUID | The job that finished |
| `success` | bool | `true` if completed, `false` if failed |
| `errorMessage` | string? | Exception message when `success` is `false` |

---

## Event-Driven Design

### Idempotency Strategy

The Worker consumer is safe under at-least-once delivery at every layer:

1. **Pre-flight status check** — On receiving `StartCrawlJob`, the consumer looks up the job by `jobId`. If not found, it skips. If the status is already `Canceled` (job was canceled before the worker picked it up), it skips without doing any work.
2. **In-process URL deduplication** — A `HashSet<string>` of visited URLs is maintained throughout the BFS run. The same normalised URL is never fetched or stored twice within a single execution.
3. **DB-level unique constraints + `ON CONFLICT DO NOTHING`** — `Pages` has a unique index on `(JobId, Url)` and `Edges` has a unique index on `(JobId, ParentUrl, ChildUrl)`. All batch inserts are written as `INSERT … ON CONFLICT DO NOTHING`. If the same `StartCrawlJob` message is redelivered after a crash mid-run, re-crawled pages and edges are silently ignored by the database rather than creating duplicates.

### Retry Policy

**HTTP-level retries (Polly):**
- Policy: `WaitAndRetryAsync` — 3 retries with exponential back-off (2 s, 4 s, 8 s)
- Triggers: `HttpRequestException`, 5xx responses, 408 Request Timeout
- Does **not** retry: 4xx client errors (bad URL, 404, 403) — permanent failures
- Timeout per request: 30 seconds

**Message-level retries (MassTransit `StartCrawlJobConsumerDefinition`):**
- Attempt 1: immediate
- Attempt 2: 5 s delay
- Attempt 3: 30 s delay
- After all retries are exhausted, the message is routed to the DLQ

**What qualifies as transient:**
- Network timeouts / connection resets
- 429 Too Many Requests
- 503 / 502 / 504 gateway errors
- Any `HttpRequestException`

**What is treated as permanent:**
- 4xx errors (except 408 / 429) — logged and URL skipped, BFS continues
- Malformed or non-HTML URLs — skipped silently

### Dead-Letter Queue

MassTransit automatically creates a `StartCrawlJob_error` queue in RabbitMQ for messages that exhaust all retry attempts (poison messages).

**What gets routed there:**
- Messages where the consumer throws an unhandled exception on every retry attempt (e.g., a corrupt `jobId` that always triggers a database exception)
- Deserialization failures (message body does not match the contract)

**What to do with DLQ messages:**
- Inspect via the RabbitMQ Management UI at [http://localhost:15672](http://localhost:15672) → Queues → `StartCrawlJob_error`
- Messages retain the original body and MassTransit correlation headers for debugging
- Requeue manually once the underlying issue is fixed, or purge if unrecoverable

---

## SQL Model & Performance

```
Jobs
  Id            UUID          PK
  Url           VARCHAR(2048) NOT NULL
  Status        VARCHAR(20)   NOT NULL  -- 'Pending'|'Running'|'Completed'|'Failed'|'Canceled'
  CreatedAt     TIMESTAMPTZ   NOT NULL
  StartedAt     TIMESTAMPTZ   nullable
  CompletedAt   TIMESTAMPTZ   nullable
  FailureReason VARCHAR(2048) nullable

Pages
  Id              UUID             PK
  JobId           UUID             FK → Jobs.Id  ON DELETE CASCADE
  Url             VARCHAR(2048)    NOT NULL
  DomainLinkRatio DOUBLE PRECISION NOT NULL
  OutgoingLinks   TEXT[]           NOT NULL      -- all valid http/https links on the page
  CrawledAt       TIMESTAMPTZ      NOT NULL

Edges
  Id        UUID          PK
  JobId     UUID          FK → Jobs.Id  ON DELETE CASCADE
  ParentUrl VARCHAR(2048) NOT NULL
  ChildUrl  VARCHAR(2048) NOT NULL      -- internal (same-domain) links only

Indexes
  idx_pages_jobid              ON Pages(JobId)                          -- "all pages for a job"
  idx_edges_jobid              ON Edges(JobId)                          -- "all edges for a job"
  idx_jobs_status              ON Jobs(Status)                          -- filter by status
  UX_Pages_JobId_Url           UNIQUE ON Pages(JobId, Url)              -- idempotency
  UX_Edges_JobId_ParentUrl_ChildUrl  UNIQUE ON Edges(JobId, ParentUrl, ChildUrl)  -- idempotency
```

**Performance considerations:**

- `Pages(JobId)` and `Edges(JobId)` indexes make the most common queries (fetching all pages/edges for a job) index scans instead of full table scans.
- `Jobs(Status)` supports efficient status-based filtering.
- The `GET /api/jobs` list endpoint uses a lightweight LINQ projection (no JOIN to Pages or Edges) so the history page is fast regardless of how many pages a job has crawled.
- `GET /api/jobs/{id}` uses `AsSplitQuery()` to avoid a Cartesian product when loading both the Pages and Edges collections.
- `ON DELETE CASCADE` on both Pages and Edges ensures orphan cleanup is handled by the database automatically.
- Worker batch inserts use raw `Npgsql` with `ON CONFLICT DO NOTHING` rather than EF Core `SaveChanges` to avoid N+1 round-trips during BFS.

---

## What Was Implemented First (and Why)

1. **Core data model and database schema** — Everything else depends on knowing what a `Job`, `Page`, and `Edge` look like. Getting this right first prevented rework.
2. **Message contracts in `Crawl.Core`** — Defined `StartCrawlJob` and `CrawlJobCompleted` as the API boundary between services before writing either service, keeping them decoupled.
3. **Crawl API (job creation + status endpoints)** — The UI and worker both depend on this. Getting the REST surface working early enabled end-to-end testing quickly.
4. **BFS crawler in the Worker** — The most algorithmically interesting piece: URL normalisation, HTML parsing, deduplication, depth limiting, concurrent batch fetching, and Domain Link Ratio calculation.
5. **Docker Compose** — Set up early so the full stack could be tested realistically rather than against mocked dependencies.
6. **React frontend** — Built against the already-working API, with loading/error states, progress indicator, tree view, and pagination.
7. **Idempotency constraints** — Unique indexes on `Pages(JobId, Url)` and `Edges(JobId, ParentUrl, ChildUrl)` combined with `ON CONFLICT DO NOTHING` batch inserts.
8. **Cancel endpoint + worker-side cancellation** — Cancel writes `Canceled` directly to the DB; the worker polls status before each BFS batch and aborts if it sees `Canceled`.
9. **Hierarchical tree API endpoint** — `GET /api/jobs/{id}/tree` builds the tree server-side from the Edges table, returning only crawled pages (nodes with Domain Link Ratios).
10. **CI pipeline** — GitHub Actions workflow: backend build + test, frontend type-check + build, Docker Compose image build.

---

## What Was Cut (and Why)

| Feature | Reason |
|---|---|
| **`IPageRepository` implementation** | The interface exists in `Crawl.Core` as scaffolding for page-specific queries, but the Worker writes pages via raw Npgsql (for batch insert performance) and the API reads them directly via `AppDbContext`. A concrete implementation was never needed. |
| **SSE / SignalR real-time updates** | Polling every 3 seconds is sufficient for the demo and far simpler to implement and maintain. |
| **Auth** | All endpoints are public. Acceptable for a demo; would add JWT or API-key auth for production. |
| **JavaScript-rendered pages** | The crawler fetches raw HTML via `HttpClient`. SPAs that render content client-side return little crawlable content. A headless browser (Playwright) would be needed for those. |
| **Cursor-based pagination** | The history endpoint uses `LIMIT`/`OFFSET`. For the demo dataset this is fine; a production system with millions of jobs would use keyset pagination on `CreatedAt DESC`. |

---

## What Would Come Next

1. **Remove dead code** — `IPageRepository` (never implemented) and the unused `GetAllAsync` / `GetPagedAsync` methods in `JobRepository` should be deleted.
2. **Auth** — JWT or API-key middleware on all write endpoints.
3. **SSE / SignalR** — Replace the 3-second polling loop with a push model for real-time progress.
4. **Headless browser support** — Plug in Playwright for JS-rendered sites.
5. **Cursor-based pagination** — Replace `OFFSET` with keyset pagination for the history endpoint at scale.
6. **Rate limiting / robots.txt** — Respect `robots.txt` and add per-domain request throttling.
7. **Worker crash recovery** — Persist BFS state (queue + visited set) to Redis or the DB so a worker restart mid-job can resume rather than restart.

---

## Known Limitations & Trade-offs

- **`deploy.replicas` ignored outside Swarm** — The `docker-compose.yml` has `deploy.replicas: 3` under the `worker` service. This is a Docker Swarm directive and is silently ignored by plain `docker compose up`. Use `--scale worker=3` for multiple replicas in Compose mode.
- **In-memory BFS state** — The visited set and BFS queue live in the worker process. A crash mid-job loses this state. The DB-level `ON CONFLICT DO NOTHING` prevents duplicate rows on restart, but pages may be re-fetched. A durable queue or Redis set would fix this for long-running jobs.
- **No auth** — All endpoints are public. Acceptable for a demo.
- **Content-type sniffing** — The crawler skips non-HTML responses based on the `Content-Type` response header. Sites that serve HTML with a wrong content type (rare but real) would be skipped.
- **JavaScript-rendered pages** — Raw `HttpClient` fetches won't capture content rendered client-side.
- **Seed URL normalisation** — The seed URL is stored as-is from the user's input (not normalised). Internal links extracted from pages ARE normalised (trailing slash stripped, fragment removed). If the seed URL has a trailing slash that other pages then link back to without the slash, the root node in the tree may show as a separate entry from those back-links. Normalising the seed URL on creation would fix this.
