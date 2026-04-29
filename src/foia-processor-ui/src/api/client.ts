import type {
    DocumentReview,
    DocumentsList,
    FoiaRequestStatus,
    ReleasePackage,
    SubmitFoiaRequest,
    SubmitFoiaResponse,
    ValidationProblem,
} from "../types";

const BASE = (import.meta.env.VITE_API_BASE_URL ?? "").replace(/\/$/, "");

export class ApiValidationError extends Error {
    constructor(public problem: ValidationProblem) {
        super(problem.title);
    }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
    const res = await fetch(`${BASE}${path}`, {
        headers: { "Content-Type": "application/json" },
        ...init,
    });
    if (res.status === 400) {
        const problem = (await res.json()) as ValidationProblem;
        throw new ApiValidationError(problem);
    }
    if (!res.ok) {
        const text = await res.text();
        throw new Error(`HTTP ${res.status}: ${text}`);
    }
    if (res.status === 204) return undefined as T;
    return (await res.json()) as T;
}

export const submitFoiaRequest = (dto: SubmitFoiaRequest) =>
    request<SubmitFoiaResponse>("/api/foiarequests", { method: "POST", body: JSON.stringify(dto) });

export const getFoiaRequestStatus = (id: string) =>
    request<FoiaRequestStatus>(`/api/foiarequests/${id}`);

export const getDocuments = (id: string) =>
    request<DocumentsList>(`/api/foiarequests/${id}/documents`);

export const getDocumentReview = (id: string) =>
    request<DocumentReview>(`/api/documents/${id}/review`);

export const approveDocument = (id: string, comments?: string) =>
    request<{ id: string; reviewStatus: string; approvedAt: string }>(`/api/documents/${id}/approve`, {
        method: "POST",
        body: JSON.stringify({ comments: comments ?? null }),
    });

export const rejectDocument = (id: string, comments: string) =>
    request<{ id: string; reviewStatus: string; rejectedAt: string }>(`/api/documents/${id}/reject`, {
        method: "POST",
        body: JSON.stringify({ comments }),
    });

export const approveRelease = (id: string) =>
    request<{ id: string; status: string; approvedAt: string }>(
        `/api/foiarequests/${id}/approve-release`,
        { method: "POST" }
    );

export const getRelease = (id: string) =>
    request<ReleasePackage>(`/api/foiarequests/${id}/release`);
