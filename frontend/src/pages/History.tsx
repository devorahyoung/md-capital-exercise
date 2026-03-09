import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { fetchJobs } from "../api";
import type { Job } from "../types";
import { STATUS_COLOR } from "../types";

const PAGE_SIZE = 20;

export default function History() {
  const navigate = useNavigate();
  const [jobs, setJobs] = useState<Job[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [totalCount, setTotalCount] = useState(0);

  useEffect(() => {
    setLoading(true);
    setError(null);
    fetchJobs(page, PAGE_SIZE)
      .then((result) => {
        setJobs(result.items);
        setTotalPages(result.totalPages);
        setTotalCount(result.totalCount);
      })
      .catch((err) =>
        setError(err instanceof Error ? err.message : "Failed to load jobs.")
      )
      .finally(() => setLoading(false));
  }, [page]);

  return (
    <div className="page">
      <div className="page-header">
        <h1 className="page-title">Job History</h1>
        <button className="btn-primary" onClick={() => navigate("/")}>
          + New Crawl
        </button>
      </div>

      {loading && (
        <div className="centered">
          <span className="spinner" />
          <p>Loading jobs…</p>
        </div>
      )}

      {error && <p className="error">{error}</p>}

      {!loading && !error && jobs.length === 0 && (
        <div className="card centered">
          <p className="muted">No jobs yet. Start your first crawl!</p>
          <button className="btn-primary" onClick={() => navigate("/")}>
            Start Crawl
          </button>
        </div>
      )}

      {!loading && jobs.length > 0 && (
        <>
          <div className="job-table card">
            <table>
              <thead>
                <tr>
                  <th>URL</th>
                  <th>Status</th>
                  <th>Pages</th>
                  <th>Created</th>
                </tr>
              </thead>
              <tbody>
                {jobs.map((job) => (
                  <tr
                    key={job.id}
                    className="clickable"
                    onClick={() => navigate(`/jobs/${job.id}`)}
                  >
                    <td className="url-cell">{job.url}</td>
                    <td>
                      <span
                        className="badge"
                        style={{ background: STATUS_COLOR[job.status] }}
                      >
                        {job.status}
                      </span>
                    </td>
                    <td>{job.pageCount ?? job.pages?.length ?? "—"}</td>
                    <td className="muted">
                      {new Date(job.createdAt).toLocaleString()}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="pagination">
            <span className="pagination-info">
              {totalCount} job{totalCount !== 1 ? "s" : ""} total
            </span>
            <div className="pagination-controls">
              <button
                className="btn-secondary"
                disabled={page <= 1}
                onClick={() => setPage((p) => p - 1)}
              >
                ← Prev
              </button>
              <span className="pagination-page">
                Page {page} of {totalPages}
              </span>
              <button
                className="btn-secondary"
                disabled={page >= totalPages}
                onClick={() => setPage((p) => p + 1)}
              >
                Next →
              </button>
            </div>
          </div>
        </>
      )}
    </div>
  );
}
