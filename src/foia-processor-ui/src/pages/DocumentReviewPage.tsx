import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { approveDocument, getDocumentReview, rejectDocument } from "../api/client";
import type { DocumentReview } from "../types";

function highlight(text: string, ranges: { start: number; end: number; piiType: string }[]) {
    if (!ranges.length) return [text];
    const sorted = [...ranges].sort((a, b) => a.start - b.start);
    const out: (string | JSX.Element)[] = [];
    let cursor = 0;
    sorted.forEach((r, i) => {
        if (r.start > cursor) out.push(text.slice(cursor, r.start));
        out.push(<mark key={i} title={r.piiType}>{text.slice(r.start, r.end)}</mark>);
        cursor = r.end;
    });
    if (cursor < text.length) out.push(text.slice(cursor));
    return out;
}

export default function DocumentReviewPage() {
    const { id, documentId } = useParams<{ id: string; documentId: string }>();
    const [doc, setDoc] = useState<DocumentReview | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [comments, setComments] = useState("");
    const [busy, setBusy] = useState(false);
    const navigate = useNavigate();

    useEffect(() => {
        if (!documentId) return;
        getDocumentReview(documentId).then(setDoc).catch((e) => setError((e as Error).message));
    }, [documentId]);

    async function onApprove() {
        if (!documentId) return;
        setBusy(true);
        try {
            await approveDocument(documentId, comments || undefined);
            navigate(`/requests/${id}/review`);
        } catch (e) {
            setError((e as Error).message);
        } finally {
            setBusy(false);
        }
    }

    async function onReject() {
        if (!documentId) return;
        if (!comments.trim()) {
            setError("Comments are required when rejecting a document.");
            return;
        }
        setBusy(true);
        try {
            await rejectDocument(documentId, comments);
            navigate(`/requests/${id}/review`);
        } catch (e) {
            setError((e as Error).message);
        } finally {
            setBusy(false);
        }
    }

    if (error) return <div className="page"><p className="field-error">{error}</p></div>;
    if (!doc) return <div className="page"><p>Loading…</p></div>;

    const originalRanges = doc.redactions
        .filter((r) => r.startOffset !== null && r.endOffset !== null)
        .map((r) => ({ start: r.startOffset!, end: r.endOffset!, piiType: r.piiType }));

    return (
        <div className="page">
            <h1>{doc.fileName}</h1>
            <p>
                <Link to={`/requests/${id}/review`}>← Back to review list</Link>
                {" · "}Status: <strong>{doc.reviewStatus}</strong>
                {" · "}Redactions: <strong>{doc.redactions.length}</strong>
            </p>

            <div className="review-grid">
                <div>
                    <h2>Original (highlighted)</h2>
                    <div className="review-pane">{highlight(doc.originalContent, originalRanges)}</div>
                </div>
                <div>
                    <h2>Redacted</h2>
                    <div className="review-pane">{doc.redactedContent ?? "(not yet generated)"}</div>
                </div>
            </div>

            <h2>Findings</h2>
            <table className="docs">
                <thead><tr><th>Type</th><th>Original</th><th>Replacement</th><th>Source</th><th>Confidence</th></tr></thead>
                <tbody>
                    {doc.redactions.map((r) => (
                        <tr key={r.id}>
                            <td>{r.piiType}</td>
                            <td><code>{r.originalText}</code></td>
                            <td><code>{r.replacementText}</code></td>
                            <td>{r.detectionSource}</td>
                            <td>{r.confidence?.toFixed(2) ?? "—"}</td>
                        </tr>
                    ))}
                </tbody>
            </table>

            <h2>Decision</h2>
            <label>
                Comments {doc.reviewStatus === "Approved" ? "(optional)" : "(required if rejecting)"}
                <textarea value={comments} onChange={(e) => setComments(e.target.value)} />
            </label>
            <p>
                <button onClick={onApprove} disabled={busy} style={{ marginRight: 8 }}>Approve</button>
                <button onClick={onReject} disabled={busy}>Reject</button>
            </p>
        </div>
    );
}
