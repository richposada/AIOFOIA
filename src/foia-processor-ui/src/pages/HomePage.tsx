import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { listFoiaRequests } from "../api/client";
import type { FoiaRequestSummary } from "../types";
import StatusBadge from "../components/StatusBadge";

const dateFmt = new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
});

const activeStatuses = new Set<string>([
    "Submitted",
    "Validated",
    "Searching",
    "DocumentsFound",
    "Redacting",
    "Packaging",
    "InProgress",
]);

export default function HomePage() {
    const [items, setItems] = useState<FoiaRequestSummary[] | null>(null);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        let cancelled = false;
        let timer: ReturnType<typeof setTimeout> | null = null;

        async function tick() {
            try {
                const data = await listFoiaRequests(10);
                if (cancelled) return;
                setItems(data);
                setError(null);
                const hasActive = data.some((r) => activeStatuses.has(r.status));
                timer = setTimeout(tick, hasActive ? 3000 : 15000);
            } catch (e) {
                if (cancelled) return;
                setError((e as Error).message);
                timer = setTimeout(tick, 5000);
            }
        }

        tick();
        return () => {
            cancelled = true;
            if (timer) clearTimeout(timer);
        };
    }, []);

    return (
        <div>
            <div className="mb-8 flex flex-wrap items-end justify-between gap-4">
                <div>
                    <h1 className="text-3xl font-semibold text-white">
                        Recent FOIA Requests
                    </h1>
                    <p className="mt-1 text-sm text-midnight-300">
                        The 10 most recently submitted requests for analyst review.
                    </p>
                </div>
                <Link
                    to="/submit"
                    className="inline-flex items-center rounded-md bg-indigo-500 px-4 py-2 text-sm font-semibold text-white shadow-sm transition-colors hover:bg-indigo-400 focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-300"
                >
                    + New Request
                </Link>
            </div>

            {error && (
                <div
                    role="alert"
                    className="mb-6 rounded-lg border border-rose-500/40 bg-rose-500/10 px-4 py-3 text-sm text-rose-200"
                >
                    Failed to load requests: {error}
                </div>
            )}

            {!items && !error && <SkeletonGrid />}

            {items && items.length === 0 && (
                <div className="rounded-2xl border border-midnight-800 bg-midnight-900/60 p-10 text-center">
                    <h2 className="text-lg font-medium text-white">
                        No FOIA requests yet
                    </h2>
                    <p className="mt-2 text-sm text-midnight-300">
                        Submit a new request to get started.
                    </p>
                    <Link
                        to="/submit"
                        className="mt-4 inline-flex items-center rounded-md bg-indigo-500 px-4 py-2 text-sm font-semibold text-white hover:bg-indigo-400"
                    >
                        Submit a request
                    </Link>
                </div>
            )}

            {items && items.length > 0 && (
                <ul className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3">
                    {items.map((r) => (
                        <li key={r.id}>
                            <article className="group flex h-full flex-col rounded-2xl border border-midnight-800 bg-midnight-900/60 p-6 shadow-card transition-colors hover:border-midnight-600">
                                <header className="mb-4 flex items-start justify-between gap-3">
                                    <h2 className="line-clamp-2 text-base font-semibold text-white">
                                        {r.subject}
                                    </h2>
                                    <StatusBadge status={r.status} />
                                </header>
                                <dl className="flex-1 space-y-2 text-sm">
                                    <div className="flex justify-between gap-4">
                                        <dt className="text-midnight-400">Requestor</dt>
                                        <dd className="text-right text-midnight-100">
                                            {r.requestorFullName}
                                        </dd>
                                    </div>
                                    <div className="flex justify-between gap-4">
                                        <dt className="text-midnight-400">Submitted</dt>
                                        <dd className="text-right text-midnight-100">
                                            {dateFmt.format(new Date(r.submittedAt))}
                                        </dd>
                                    </div>
                                </dl>
                                <footer className="mt-6 border-t border-midnight-800 pt-4">
                                    <Link
                                        to={`/requests/${r.id}`}
                                        className="text-sm font-medium text-indigo-300 transition-colors hover:text-indigo-200"
                                        aria-label={`View details for ${r.subject}`}
                                    >
                                        View details &rarr;
                                    </Link>
                                </footer>
                            </article>
                        </li>
                    ))}
                </ul>
            )}
        </div>
    );
}

function SkeletonGrid() {
    return (
        <ul
            aria-hidden="true"
            className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3"
        >
            {Array.from({ length: 6 }).map((_, i) => (
                <li
                    key={i}
                    className="h-48 animate-pulse rounded-2xl border border-midnight-800 bg-midnight-900/40"
                />
            ))}
        </ul>
    );
}
