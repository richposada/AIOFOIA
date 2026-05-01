import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { deleteFoiaRequest, listAllFoiaRequests } from "../api/client";
import StatusBadge from "../components/StatusBadge";
import type { FoiaRequestSummary } from "../types";

const dateFmt = new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
});

export default function AdminPage() {
    const [items, setItems] = useState<FoiaRequestSummary[] | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [deletingId, setDeletingId] = useState<string | null>(null);

    function load() {
        setError(null);
        setItems(null);
        listAllFoiaRequests()
            .then(setItems)
            .catch((e) => setError((e as Error).message));
    }

    useEffect(() => {
        load();
    }, []);

    async function handleDelete(req: FoiaRequestSummary) {
        const ok = window.confirm(
            `Delete FOIA request "${req.subject}" (${req.id})?\n\n` +
            "This permanently removes the request and all associated documents, " +
            "redactions, review tasks, release package, and audit events. " +
            "This cannot be undone."
        );
        if (!ok) return;

        setDeletingId(req.id);
        setError(null);
        try {
            await deleteFoiaRequest(req.id);
            setItems((prev) => (prev ? prev.filter((r) => r.id !== req.id) : prev));
        } catch (e) {
            setError(`Failed to delete ${req.id}: ${(e as Error).message}`);
        } finally {
            setDeletingId(null);
        }
    }

    return (
        <div>
            <div className="mb-8 flex items-end justify-between gap-4">
                <div>
                    <h1 className="text-3xl font-semibold text-white">Admin</h1>
                    <p className="mt-1 text-sm text-midnight-300">
                        Manage all FOIA requests in the system. Deleting a request also
                        removes its documents, redactions, review tasks, release package,
                        and audit events.
                    </p>
                </div>
                <button
                    type="button"
                    onClick={load}
                    className="rounded-md border border-midnight-700 bg-midnight-900/60 px-3 py-2 text-sm font-medium text-midnight-100 transition-colors hover:bg-midnight-800"
                >
                    Refresh
                </button>
            </div>

            {error && (
                <div
                    role="alert"
                    className="mb-6 rounded-lg border border-rose-500/40 bg-rose-500/10 px-4 py-3 text-sm text-rose-200"
                >
                    {error}
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
                    <h2 className="text-lg font-medium text-white">No requests</h2>
                    <p className="mt-2 text-sm text-midnight-300">
                        There are no FOIA requests in the system.
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
                                        Status
                                    </th>
                                    <th scope="col" className="px-6 py-3 font-semibold">
                                        Submitted
                                    </th>
                                    <th scope="col" className="px-6 py-3 font-semibold text-right">
                                        Actions
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
                                            <Link
                                                to={`/requests/${r.id}/status`}
                                                className="hover:text-indigo-300"
                                            >
                                                {r.subject}
                                            </Link>
                                        </td>
                                        <td className="px-6 py-4 text-midnight-100">
                                            {r.requestorFullName}
                                        </td>
                                        <td className="px-6 py-4 font-mono text-xs text-midnight-300">
                                            {r.id}
                                        </td>
                                        <td className="px-6 py-4">
                                            <StatusBadge status={r.status} />
                                        </td>
                                        <td className="px-6 py-4 text-midnight-100">
                                            {dateFmt.format(new Date(r.submittedAt))}
                                        </td>
                                        <td className="px-6 py-4 text-right">
                                            <button
                                                type="button"
                                                onClick={() => handleDelete(r)}
                                                disabled={deletingId === r.id}
                                                className="inline-flex items-center rounded-md bg-rose-600/90 px-3 py-1.5 text-xs font-semibold text-white shadow-sm transition-colors hover:bg-rose-600 disabled:cursor-not-allowed disabled:opacity-60"
                                                aria-label={`Delete ${r.subject}`}
                                            >
                                                {deletingId === r.id ? "Deleting…" : "Delete"}
                                            </button>
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
