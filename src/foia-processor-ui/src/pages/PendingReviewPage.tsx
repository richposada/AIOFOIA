import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { listPendingReviewRequests } from "../api/client";
import type { FoiaRequestSummary } from "../types";

const dateFmt = new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
});

export default function PendingReviewPage() {
    const [items, setItems] = useState<FoiaRequestSummary[] | null>(null);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        listPendingReviewRequests()
            .then(setItems)
            .catch((e) => setError((e as Error).message));
    }, []);

    return (
        <div>
            <div className="mb-8">
                <h1 className="text-3xl font-semibold text-white">
                    Pending Human Review
                </h1>
                <p className="mt-1 text-sm text-midnight-300">
                    FOIA requests waiting for an analyst to approve release.
                </p>
            </div>

            {error && (
                <div
                    role="alert"
                    className="mb-6 rounded-lg border border-rose-500/40 bg-rose-500/10 px-4 py-3 text-sm text-rose-200"
                >
                    Failed to load requests: {error}
                </div>
            )}

            {!items && !error && (
                <div
                    aria-hidden="true"
                    className="h-64 animate-pulse rounded-2xl border border-midnight-800 bg-midnight-900/40"
                />
            )}

            {items && items.length === 0 && (
                <div className="rounded-2xl border border-midnight-800 bg-midnight-900/60 p-10 text-center">
                    <h2 className="text-lg font-medium text-white">
                        Nothing waiting for review
                    </h2>
                    <p className="mt-2 text-sm text-midnight-300">
                        All caught up. New requests will appear here once they finish
                        automated processing.
                    </p>
                </div>
            )}

            {items && items.length > 0 && (
                <div className="overflow-hidden rounded-2xl border border-midnight-800 bg-midnight-900/60 shadow-card">
                    <div className="overflow-x-auto">
                        <table className="min-w-full divide-y divide-midnight-800 text-sm">
                            <thead className="bg-midnight-900 text-left text-xs uppercase tracking-wider text-midnight-300">
                                <tr>
                                    <th scope="col" className="px-6 py-3 font-semibold">
                                        Subject
                                    </th>
                                    <th scope="col" className="px-6 py-3 font-semibold">
                                        Requestor
                                    </th>
                                    <th scope="col" className="px-6 py-3 font-semibold">
                                        Request ID
                                    </th>
                                    <th scope="col" className="px-6 py-3 font-semibold">
                                        Submitted
                                    </th>
                                    <th scope="col" className="px-6 py-3 font-semibold">
                                        <span className="sr-only">Action</span>
                                    </th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-midnight-800">
                                {items.map((r, i) => (
                                    <tr
                                        key={r.id}
                                        className={
                                            i % 2 === 0
                                                ? "bg-midnight-900/40"
                                                : "bg-midnight-900/10"
                                        }
                                    >
                                        <td className="px-6 py-4 font-medium text-white">
                                            {r.subject}
                                        </td>
                                        <td className="px-6 py-4 text-midnight-100">
                                            {r.requestorFullName}
                                        </td>
                                        <td className="px-6 py-4 font-mono text-xs text-midnight-300">
                                            {r.id}
                                        </td>
                                        <td className="px-6 py-4 text-midnight-100">
                                            {dateFmt.format(new Date(r.submittedAt))}
                                        </td>
                                        <td className="px-6 py-4 text-right">
                                            <Link
                                                to={`/requests/${r.id}/review`}
                                                className="text-sm font-medium text-indigo-300 transition-colors hover:text-indigo-200"
                                                aria-label={`Review ${r.subject}`}
                                            >
                                                Review &rarr;
                                            </Link>
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                </div>
            )}
        </div>
    );
}
