-- =============================================================================
-- Web Crawler Schema
-- Kept in sync with EF Core migrations in Crawl.Api/Migrations/.
-- The authoritative source of truth is the migrations; this file is provided
-- as a human-readable reference and for manual inspection / ad-hoc tooling.
-- =============================================================================

CREATE TABLE IF NOT EXISTS "Jobs" (
    "Id"            UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    "Url"           VARCHAR(2048) NOT NULL,
    "Status"        VARCHAR(20)   NOT NULL DEFAULT 'Pending'
                                  CHECK ("Status" IN ('Pending','Running','Completed','Failed','Canceled')),
    "CreatedAt"     TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    "StartedAt"     TIMESTAMPTZ,
    "CompletedAt"   TIMESTAMPTZ,
    "FailureReason" VARCHAR(2048)
);

CREATE TABLE IF NOT EXISTS "Pages" (
    "Id"              UUID             PRIMARY KEY DEFAULT gen_random_uuid(),
    "JobId"           UUID             NOT NULL REFERENCES "Jobs"("Id") ON DELETE CASCADE,
    "Url"             VARCHAR(2048)    NOT NULL,
    "DomainLinkRatio" DOUBLE PRECISION NOT NULL DEFAULT 0,
    "OutgoingLinks"   TEXT[]           NOT NULL DEFAULT '{}'::TEXT[],
    "CrawledAt"       TIMESTAMPTZ      NOT NULL DEFAULT NOW()
);

-- Directed link graph: each row is a parent → child relationship discovered during a crawl.
CREATE TABLE IF NOT EXISTS "Edges" (
    "Id"        UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    "JobId"     UUID          NOT NULL REFERENCES "Jobs"("Id") ON DELETE CASCADE,
    "ParentUrl" VARCHAR(2048) NOT NULL,
    "ChildUrl"  VARCHAR(2048) NOT NULL
);

-- ── Indexes ───────────────────────────────────────────────────────────────────
-- Performance: speeds up "all pages / edges for a job" lookups.
CREATE INDEX IF NOT EXISTS idx_pages_jobid ON "Pages"("JobId");
CREATE INDEX IF NOT EXISTS idx_edges_jobid ON "Edges"("JobId");
-- Performance: supports efficient filtering by job status.
CREATE INDEX IF NOT EXISTS idx_jobs_status ON "Jobs"("Status");

-- ── Idempotency constraints ───────────────────────────────────────────────────
-- Prevent duplicate rows if a StartCrawlJob message is redelivered mid-run.
-- The Worker uses INSERT … ON CONFLICT DO NOTHING against these constraints.
CREATE UNIQUE INDEX IF NOT EXISTS "UX_Pages_JobId_Url"
    ON "Pages"("JobId", "Url");

CREATE UNIQUE INDEX IF NOT EXISTS "UX_Edges_JobId_ParentUrl_ChildUrl"
    ON "Edges"("JobId", "ParentUrl", "ChildUrl");
