import { useEffect, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { cancelJob, fetchJob, fetchJobTree } from "../api";
import type { Job, TreeNode } from "../types";
import { STATUS_COLOR } from "../types";

// ── Tree view ─────────────────────────────────────────────────────────────────
// Renders the server-built tree returned by GET /api/jobs/{id}/tree.
// Each node carries the full URL; non-root nodes display only their path
// segment for readability.

function TreeNodeView({ node, depth = 0 }: { node: TreeNode; depth?: number }) {
  const [open, setOpen] = useState(true);
  const hasChildren = node.children.length > 0;
  const indent = depth * 20;

  let label = node.url;
  if (depth > 0) {
    try {
      const pathname = new URL(node.url).pathname;
      label = pathname || "/";
    } catch {
      label = node.url;
    }
  }

  return (
    <div className="tree-node">
      <div
        className={`tree-row ${hasChildren ? "clickable" : ""}`}
        style={{ paddingLeft: indent }}
        onClick={() => hasChildren && setOpen((o) => !o)}
      >
        {hasChildren && (
          <span className="tree-toggle">{open ? "▾" : "▸"}</span>
        )}
        {!hasChildren && <span className="tree-leaf">•</span>}
        <span className="tree-url" title={node.url}>
          {label}
        </span>
        {node.domainLinkRatio !== null && (
          <span className="tree-ratio">
            {(node.domainLinkRatio * 100).toFixed(1)}%
          </span>
        )}
      </div>
      {open &&
        node.children.map((child) => (
          <TreeNodeView key={child.url} node={child} depth={depth + 1} />
        ))}
    </div>
  );
}

// ── Elapsed timer ─────────────────────────────────────────────────────────────

function ElapsedTimer({ startedAt }: { startedAt: string }) {
  const [elapsed, setElapsed] = useState(0);

  useEffect(() => {
    const start = new Date(startedAt).getTime();
    const id = setInterval(() => {
      setElapsed(Math.floor((Date.now() - start) / 1000));
    }, 1000);
    return () => clearInterval(id);
  }, [startedAt]);

  const mins = Math.floor(elapsed / 60);
  const secs = elapsed % 60;
  return (
    <span>
      {mins > 0 ? `${mins}m ` : ""}
      {secs}s elapsed
    </span>
  );
}

// ── Main component ────────────────────────────────────────────────────────────

export default function JobDetails() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [job, setJob] = useState<Job | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [tree, setTree] = useState<TreeNode | null>(null);
  const [treeError, setTreeError] = useState<string | null>(null);
  const [canceling, setCanceling] = useState(false);
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const load = async () => {
    try {
      const data = await fetchJob(id!);
      setJob(data);
      if (data.status === "Completed" || data.status === "Failed") {
        if (pollRef.current) clearInterval(pollRef.current);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load job.");
      if (pollRef.current) clearInterval(pollRef.current);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
    pollRef.current = setInterval(load, 3000);
    return () => {
      if (pollRef.current) clearInterval(pollRef.current);
    };
  }, [id]);

  const handleCancel = async () => {
    if (!id || canceling) return;
    setCanceling(true);
    try {
      const updated = await cancelJob(id);
      setJob(updated);
      if (pollRef.current) clearInterval(pollRef.current);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to cancel job.");
    } finally {
      setCanceling(false);
    }
  };

  // Fetch the server-built tree once the job reaches Completed status.
  useEffect(() => {
    if (job?.status !== "Completed" || tree) return;
    fetchJobTree(id!)
      .then(setTree)
      .catch((err) =>
        setTreeError(err instanceof Error ? err.message : "Failed to load tree.")
      );
  }, [job?.status]);

  return (
    <div className="page">
      <button className="btn-ghost back-btn" onClick={() => navigate("/jobs")}>
        ← Back to History
      </button>

      {loading && (
        <div className="centered">
          <span className="spinner" />
          <p>Loading job…</p>
        </div>
      )}

      {error && <p className="error">{error}</p>}

      {job && (
        <>
          {/* ── Header card ── */}
          <div className="card detail-header">
            <div className="detail-url">{job.url}</div>
            <span
              className="badge badge-lg"
              style={{ background: STATUS_COLOR[job.status] }}
            >
              {job.status}
            </span>
          </div>

          {/* ── Timestamps card ── */}
          <div className="card timestamps">
            <div className="ts-row">
              <span className="ts-label">Created</span>
              <span>{new Date(job.createdAt).toLocaleString()}</span>
            </div>
            {job.startedAt && (
              <div className="ts-row">
                <span className="ts-label">Started</span>
                <span>{new Date(job.startedAt).toLocaleString()}</span>
              </div>
            )}
            {job.completedAt && (
              <div className="ts-row">
                <span className="ts-label">Completed</span>
                <span>{new Date(job.completedAt).toLocaleString()}</span>
              </div>
            )}
            {job.status === "Completed" && job.startedAt && job.completedAt && (
              <div className="ts-row">
                <span className="ts-label">Duration</span>
                <span>
                  {(
                    (new Date(job.completedAt).getTime() -
                      new Date(job.startedAt).getTime()) /
                    1000
                  ).toFixed(1)}
                  s
                </span>
              </div>
            )}
          </div>

          {/* ── Progress ── */}
          {(job.status === "Pending" || job.status === "Running") && (
            <div className="card progress-card">
              <div className="progress-header">
                <span className="spinner" />
                <span>
                  {job.status === "Running" ? (
                    <>
                      Crawling…{" "}
                      {job.startedAt && (
                        <ElapsedTimer startedAt={job.startedAt} />
                      )}
                    </>
                  ) : (
                    "Waiting to start…"
                  )}
                </span>
                {job.status === "Running" && job.pageCount > 0 && (
                  <span className="progress-count">
                    {job.pageCount} page{job.pageCount !== 1 ? "s" : ""} crawled
                  </span>
                )}
                <button
                  className="btn-danger"
                  onClick={handleCancel}
                  disabled={canceling}
                >
                  {canceling ? "Canceling…" : "Cancel"}
                </button>
              </div>
              <div className="progress-bar">
                <div
                  className="progress-fill"
                  style={
                    job.status === "Running" && job.pageCount > 0
                      ? { width: `${Math.min((job.pageCount / 200) * 100, 100)}%`, animation: "none" }
                      : undefined
                  }
                />
              </div>
            </div>
          )}

          {/* ── Failed ── */}
          {job.status === "Failed" && (
            <div className="card error-card">
              <strong>❌ Crawl failed.</strong>
              {job.failureReason ? (
                <p className="failure-reason"><code>{job.failureReason}</code></p>
              ) : (
                <p>The job encountered an error. Check the Worker logs for details.</p>
              )}
              <button className="btn-primary" onClick={() => navigate("/")}>
                Try Again
              </button>
            </div>
          )}

          {/* ── Canceled ── */}
          {job.status === "Canceled" && (
            <div className="card canceled-card">
              <strong>🚫 Crawl canceled.</strong>
              <button className="btn-ghost" onClick={() => navigate("/")}>
                Start New Crawl
              </button>
            </div>
          )}

          {/* ── Completed: tree view ── */}
          {job.status === "Completed" && (
            <div className="card">
              <h2 className="section-title">
                Pages crawled ({job.pageCount ?? 0})
              </h2>
              {treeError && <p className="error">{treeError}</p>}
              {!treeError && !tree && (
                <div className="centered">
                  <span className="spinner" />
                  <p>Building tree…</p>
                </div>
              )}
              {tree && tree.children.length === 0 && job.pageCount === 0 && (
                <p className="muted">No pages were crawled (site may be JS-rendered).</p>
              )}
              {tree && (tree.children.length > 0 || job.pageCount > 0) && (
                <div className="tree-view">
                  <div className="tree-legend">
                    <span>URL</span>
                    <span>Domain Link Ratio</span>
                  </div>
                  <TreeNodeView node={tree} />
                </div>
              )}
            </div>
          )}
        </>
      )}
    </div>
  );
}
