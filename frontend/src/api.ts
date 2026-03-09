import type { Job, PaginatedResult, TreeNode } from "./types";

const BASE = "http://localhost:5000/api/jobs";

async function handleResponse<T>(res: Response): Promise<T> {
  if (!res.ok) {
    const text = await res.text().catch(() => res.statusText);
    throw new Error(text || `HTTP ${res.status}`);
  }
  return res.json() as Promise<T>;
}

export async function fetchJobs(
  page = 1,
  pageSize = 20
): Promise<PaginatedResult<Job>> {
  const res = await fetch(`${BASE}?page=${page}&pageSize=${pageSize}`);
  return handleResponse<PaginatedResult<Job>>(res);
}

export async function fetchJob(id: string): Promise<Job> {
  const res = await fetch(`${BASE}/${id}`);
  return handleResponse<Job>(res);
}

export async function cancelJob(id: string): Promise<Job> {
  const res = await fetch(`${BASE}/${id}/cancel`, { method: "POST" });
  return handleResponse<Job>(res);
}

export async function fetchJobTree(id: string): Promise<TreeNode> {
  const res = await fetch(`${BASE}/${id}/tree`);
  return handleResponse<TreeNode>(res);
}

export async function createJob(url: string, maxDepth: number): Promise<Job> {
  const res = await fetch(BASE, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ url, maxDepth }),
  });
  return handleResponse<Job>(res);
}
