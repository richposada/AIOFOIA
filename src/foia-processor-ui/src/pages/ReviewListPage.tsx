import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { getDocuments } from "../api/client";
import type { DocumentsList } from "../types";

export default function ReviewListPage() {
    const { id } = useParams<{ id: string }>();
    const [data, setData] = useState<DocumentsList | null>(null);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        if (!id) return;
        getDocuments(id).then(setData).catch((e) => setError((e as Error).message));
    }, [id]);

    if (error) return <div className="page"><p className="field-error">{error}</p></div>;
    if (!data) return <div className="page"><p>Loading…</p></div>;

    return (
        <div className="page">
            <h1>Review documents</h1>
            <p><Link to={`/requests/${id}`}>← Back to status</Link></p>
            {data.documents.length === 0 ? (
                <p>No documents yet.</p>
            ) : (
                <table className="docs">
                    <thead>
                        <tr>
                            <th>File</th>
                            <th>Type</th>
                            <th>Redactions</th>
                            <th>Redaction status</th>
                            <th>Review status</th>
                            <th />
                        </tr>
                    </thead>
                    <tbody>
                        {data.documents.map((d) => (
                            <tr key={d.id}>
                                <td>{d.fileName}</td>
                                <td>{d.fileType}</td>
                                <td>{d.redactionCount}</td>
                                <td>{d.redactionStatus}</td>
                                <td>{d.reviewStatus}</td>
                                <td><Link to={`/requests/${id}/documents/${d.id}`}>Open →</Link></td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            )}
        </div>
    );
}
