import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { createJob } from "../api";

export default function StartCrawl() {
  const navigate = useNavigate();
  const [url, setUrl] = useState("");
  const [maxDepth, setMaxDepth] = useState(2);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      const job = await createJob(url.trim(), maxDepth);
      navigate(`/jobs/${job.id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to start crawl.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="page">
      <h1 className="page-title">🕷️ Web Crawler</h1>
      <p className="page-subtitle">Enter a URL to start a new crawl job.</p>

      <form className="card form" onSubmit={handleSubmit}>
        <div className="field">
          <label htmlFor="url">URL</label>
          <input
            id="url"
            type="url"
            placeholder="https://example.com"
            value={url}
            onChange={(e) => setUrl(e.target.value)}
            required
            disabled={loading}
          />
        </div>

        <div className="field">
          <label htmlFor="depth">
            Max Depth <span className="hint">(1–5, default 2)</span>
          </label>
          <input
            id="depth"
            type="number"
            min={1}
            max={5}
            value={maxDepth}
            onChange={(e) => setMaxDepth(Number(e.target.value))}
            disabled={loading}
          />
        </div>

        {error && <p className="error">{error}</p>}

        <div className="actions">
          <button type="submit" className="btn-primary" disabled={loading}>
            {loading ? <><span className="spinner-sm" /> Starting…</> : "Start Crawl"}
          </button>
          <button
            type="button"
            className="btn-ghost"
            onClick={() => navigate("/jobs")}
            disabled={loading}
          >
            View History
          </button>
        </div>
      </form>
    </div>
  );
}
