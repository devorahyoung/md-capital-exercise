export type JobStatus = "Pending" | "Running" | "Completed" | "Failed" | "Canceled";

export interface TreeNode {
  url: string;
  domainLinkRatio: number | null;
  children: TreeNode[];
}

export interface Page {
  id: string;
  url: string;
  domainLinkRatio: number;
  crawledAt: string;
}

export interface Edge {
  id: string;
  parentUrl: string;
  childUrl: string;
}

export interface Job {
  id: string;
  url: string;
  status: JobStatus;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  failureReason: string | null;
  pages: Page[];
  edges: Edge[];
  pageCount: number;
}

export interface PaginatedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export const STATUS_COLOR: Record<JobStatus, string> = {
  Pending:   "#f59e0b",
  Running:   "#3b82f6",
  Completed: "#10b981",
  Failed:    "#ef4444",
  Canceled:  "#6b7280",
};
