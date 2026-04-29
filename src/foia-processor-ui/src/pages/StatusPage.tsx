import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { approveRelease, getFoiaRequestStatus } from "../api/client";
import type { FoiaRequestStatus } from "../types";

const TERMINAL_STATUSES = new Set(["ReleasePackageReady", "Rejected", "Error", "NoDocumentsFound"]);

export default function StatusPage() {
    const { id } = useParams<{ id: string }>();
    const [status, setStatus] = useState<FoiaRequestStatus | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);
    const [copied, setCopied] = useState(false);

    useEffect(() => {
        if (!id) return;
        let cancelled = false;
        let timer: number;

        async function poll() {
            try {
                const data = await getFoiaRequestStatus(id!);
                if (cancelled) return;
                setStatus(data);
                if (!TERMINAL_STATUSES.has(data.status)) {
                    timer = window.setTimeout(poll, 2000);
                }
            } catch (e) {
                if (!cancelled) setError((e as Error).message);
            }
        }

        poll();
        return () => {
            cancelled = true;
            if (timer) clearTimeout(timer);
        };
    }, [id]);

    async function onApproveRelease() {
        if (!id) return;
        setBusy(true);
        try {
            await approveRelease(id);
            const data = await getFoiaRequestStatus(id);
            setStatus(data);
        } catch (e) {
            setError((e as Error).message);
        } finally {
            setBusy(false);
        }
    }

    async function onCopySas() {
        if (!status?.release.sasUrl) return;
        await navigator.clipboard.writeText(status.release.sasUrl);
        setCopied(true);
        window.setTimeout(() => setCopied(false), 1500);
    }

    if (error) return <div className="page"><h1>Status</h1><p className="field-error">{error}</p></div>;
    if (!status) return <div className="page"><p>Loading…</p></div>;

    const allApproved =
        status.counts.documentsFound > 0 &&
        status.counts.documentsApproved + status.counts.documentsRejected === status.counts.documentsFound &&
        status.counts.documentsApproved > 0;
    const canApproveRelease = status.status === "PendingHumanReview" && allApproved;

    const sasExpired =
        status.release.status === "Ready" &&
        status.release.sasExpiresAt !== null &&
        new Date(status.release.sasExpiresAt) < new Date();

    return (
        <div className="page">
            <h1>Request {status.id}</h1>
            <p><strong>Subject:</strong> {status.subject}</p>
            <p><strong>Status:</strong> <span className="status-badge">{status.status}</span></p>

            <h2>Counts</h2>
            <ul>
                <li>Found: {status.counts.documentsFound}</li>
                <li>Pending: {status.counts.documentsPendingReview}</li>
                <li>Approved: {status.counts.documentsApproved}</li>
                <li>Rejected: {status.counts.documentsRejected}</li>
            </ul>

            {status.status === "PendingHumanReview" && (
                <p><Link to={`/requests/${status.id}/review`}>Review Documents →</Link></p>
            )}

            {canApproveRelease && (
                <p><button onClick={onApproveRelease} disabled={busy}>{busy ? "Working…" : "Approve Release"}</button></p>
            )}

            {status.release.status === "Ready" && status.release.sasUrl && (
                <div className="release-block">
                    <h2>Release Package</h2>
                    {!sasExpired ? (
                        <>
                            <p>
                                <a href={status.release.sasUrl} target="_blank" rel="noreferrer">
                                    Download Release Package
                                </a>
                            </p>
                            <p>
                                <button onClick={onCopySas}>Copy URL</button>
                                {copied && <span style={{ marginLeft: 8 }}>Copied!</span>}
                            </p>
                            <p><small>SAS expires: {status.release.sasExpiresAt}</small></p>
                        </>
                    ) : (
                        <p className="field-error">
                            The download link expired on {status.release.sasExpiresAt}. Please contact the administrator
                            to regenerate the release package.
                        </p>
                    )}
                </div>
            )}

            <h2>Audit timeline</h2>
            <ol className="audit-timeline">
                {status.auditEvents.map((e, i) => (
                    <li key={i}>
                        <div className="audit-time">{new Date(e.timestamp).toLocaleString()}</div>
                        <div className="audit-type">{e.eventType}</div>
                        <div className="audit-msg">{e.message}</div>
                        {e.relatedDocumentFileName && <div className="audit-doc">— {e.relatedDocumentFileName}</div>}
                    </li>
                ))}
            </ol>
        </div>
    );
}
