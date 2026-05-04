import { useEffect, useRef, useState } from "react";
import { getSystemHealth } from "../api/client";
import type { ComponentHealth, SystemHealthReport } from "../types";
import HealthBadge from "../components/HealthBadge";

const dateFmt = new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "medium",
});

const categoryOrder = [
    "Infrastructure",
    "AI",
    "Data",
    "Application",
    "AgentRuntime",
    "McpTool",
];

const categoryLabels: Record<string, string> = {
    Infrastructure: "Infrastructure",
    AI: "AI Services",
    Data: "Data",
    Application: "Application",
    AgentRuntime: "Agent Runtime",
    McpTool: "MCP Tool Servers",
};

export default function SystemHealthPage() {
    const [report, setReport] = useState<SystemHealthReport | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [loading, setLoading] = useState(false);
    const [autoRefresh, setAutoRefresh] = useState(true);
    const autoRefreshRef = useRef(autoRefresh);
    autoRefreshRef.current = autoRefresh;

    useEffect(() => {
        let cancelled = false;
        let timer: ReturnType<typeof setTimeout> | null = null;

        async function tick() {
            setLoading(true);
            try {
                const data = await getSystemHealth();
                if (cancelled) return;
                setReport(data);
                setError(null);
                setLoading(false);
                if (!autoRefreshRef.current) return;
                const delay = data.status === "Healthy" ? 10000 : 5000;
                timer = setTimeout(tick, delay);
            } catch (e) {
                if (cancelled) return;
                setError((e as Error).message);
                setLoading(false);
                if (!autoRefreshRef.current) return;
                timer = setTimeout(tick, 5000);
            }
        }

        tick();
        return () => {
            cancelled = true;
            if (timer) clearTimeout(timer);
        };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [autoRefresh]);

    async function refreshNow() {
        setLoading(true);
        try {
            const data = await getSystemHealth();
            setReport(data);
            setError(null);
        } catch (e) {
            setError((e as Error).message);
        } finally {
            setLoading(false);
        }
    }

    const grouped = groupByCategory(report?.components ?? []);
    const visibleCategories = categoryOrder.filter((c) => grouped[c]?.length);

    return (
        <div className="space-y-4">
            <header className="rounded-2xl border border-midnight-800 bg-midnight-900/60 px-5 py-3 shadow-card">
                <div className="flex flex-wrap items-center justify-between gap-3">
                    <div className="flex items-center gap-3 pl-2">
                        <h1 className="text-lg font-semibold text-white" style={{ marginLeft: 12 }}>System Health</h1>
                        {report && <HealthBadge status={report.status} size="md" />}
                        {report && (
                            <span className="text-xs text-midnight-400">
                                Last checked {dateFmt.format(new Date(report.timestamp))}
                            </span>
                        )}
                    </div>
                    <div className="flex items-center gap-4">
                        <ToggleSwitch
                            checked={autoRefresh}
                            onChange={setAutoRefresh}
                            label="Auto-refresh"
                        />
                        <button
                            type="button"
                            onClick={refreshNow}
                            disabled={loading}
                            className="rounded-md bg-indigo-500 px-3 py-1.5 text-sm font-semibold text-white hover:bg-indigo-400 disabled:opacity-50"
                        >
                            {loading ? "Refreshing…" : "Refresh now"}
                        </button>
                    </div>
                </div>
            </header>

            {error && (
                <div
                    role="alert"
                    className="rounded-lg border border-rose-500/40 bg-rose-500/10 px-4 py-2 text-sm text-rose-200"
                >
                    Failed to fetch system health: {error}
                    <button
                        type="button"
                        onClick={refreshNow}
                        className="ml-3 underline hover:text-rose-100"
                    >
                        Retry
                    </button>
                </div>
            )}

            {!report && !error && <SkeletonGrid />}

            {report && (
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-4">
                    {visibleCategories.map((cat) => (
                        <CategoryCard
                            key={cat}
                            label={categoryLabels[cat] ?? cat}
                            items={grouped[cat]}
                        />
                    ))}
                </div>
            )}
        </div>
    );
}

function groupByCategory(components: ComponentHealth[]) {
    const out: Record<string, ComponentHealth[]> = {};
    for (const c of components) {
        (out[c.category] ??= []).push(c);
    }
    return out;
}

function CategoryCard({
    label,
    items,
}: {
    label: string;
    items: ComponentHealth[];
}) {
    const worst = worstStatus(items);
    return (
        <section className="flex flex-col rounded-xl border border-midnight-800 bg-midnight-900/60 shadow-card">
            <header className="flex items-center justify-between border-b border-midnight-800 px-3 py-2">
                <h2 className="text-xs font-semibold uppercase tracking-wide text-midnight-300">
                    {label}
                </h2>
                <div className="flex items-center gap-2">
                    <span className="text-[10px] text-midnight-500">{items.length}</span>
                    <HealthBadge status={worst} size="sm" />
                </div>
            </header>
            <ul className="divide-y divide-midnight-800/70">
                {items.map((c) => (
                    <ComponentRow key={`${c.category}:${c.name}`} component={c} />
                ))}
            </ul>
        </section>
    );
}

function worstStatus(items: ComponentHealth[]) {
    if (items.some((i) => i.status === "Unhealthy")) return "Unhealthy" as const;
    if (items.some((i) => i.status === "Degraded")) return "Degraded" as const;
    return "Healthy" as const;
}

function ComponentRow({ component }: { component: ComponentHealth }) {
    const dataKeys = component.data ? Object.keys(component.data) : [];
    return (
        <li className="px-3 py-2">
            <div className="flex items-center justify-between gap-2">
                <div className="flex min-w-0 items-center gap-2">
                    <HealthBadge status={component.status} size="sm" />
                    <span
                        className="truncate text-sm font-medium text-white"
                        title={component.name}
                    >
                        {component.name}
                    </span>
                </div>
                <span className="shrink-0 text-[10px] text-midnight-500">
                    {component.durationMs}ms
                </span>
            </div>
            {component.error && (
                <p
                    className="mt-1 line-clamp-2 text-xs text-rose-300"
                    title={component.error}
                >
                    {component.error}
                </p>
            )}
            {!component.error && component.description && (
                <p
                    className="mt-1 line-clamp-2 text-xs text-midnight-300"
                    title={component.description}
                >
                    {component.description}
                </p>
            )}
            {dataKeys.length > 0 && (
                <details className="mt-1 group">
                    <summary className="cursor-pointer list-none text-[10px] uppercase tracking-wide text-midnight-500 hover:text-midnight-200">
                        Details · {dataKeys.length}
                    </summary>
                    <dl className="mt-1 space-y-0.5 rounded-md bg-midnight-950/60 p-2 text-[11px]">
                        {dataKeys.map((k) => (
                            <div key={k} className="flex justify-between gap-2">
                                <dt className="text-midnight-400">{k}</dt>
                                <dd
                                    className="truncate text-right font-mono text-midnight-100"
                                    title={formatValue(component.data?.[k])}
                                >
                                    {formatValue(component.data?.[k])}
                                </dd>
                            </div>
                        ))}
                    </dl>
                </details>
            )}
        </li>
    );
}

function formatValue(v: unknown): string {
    if (v === null || v === undefined) return "—";
    if (typeof v === "string") return v;
    if (typeof v === "number" || typeof v === "boolean") return String(v);
    try {
        return JSON.stringify(v);
    } catch {
        return String(v);
    }
}

function ToggleSwitch({
    checked,
    onChange,
    label,
}: {
    checked: boolean;
    onChange: (next: boolean) => void;
    label: string;
}) {
    return (
        <label className="flex cursor-pointer items-center gap-3 text-sm text-midnight-200 select-none">
            <span>{label}</span>
            <button
                type="button"
                role="switch"
                aria-checked={checked}
                onClick={() => onChange(!checked)}
                style={{
                    width: 52,
                    height: 28,
                    padding: 0,
                    margin: 0,
                    borderRadius: 9999,
                    backgroundColor: checked ? "#6366f1" : "#475569",
                    border: `1px solid ${checked ? "#818cf8" : "#64748b"}`,
                    position: "relative",
                    display: "inline-block",
                    cursor: "pointer",
                    transition: "background-color 150ms ease",
                    flexShrink: 0,
                }}
                className="focus:outline-none focus:ring-2 focus:ring-indigo-400 focus:ring-offset-2 focus:ring-offset-midnight-900"
            >
                <span
                    style={{
                        position: "absolute",
                        top: 2,
                        left: checked ? 26 : 2,
                        width: 22,
                        height: 22,
                        borderRadius: "50%",
                        background: "#ffffff",
                        boxShadow: "0 1px 2px rgba(0,0,0,0.35)",
                        transition: "left 150ms ease",
                        display: "block",
                    }}
                />
            </button>
        </label>
    );
}

function SkeletonGrid() {
    return (
        <div
            className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-4"
            aria-hidden="true"
        >
            {Array.from({ length: 6 }).map((_, i) => (
                <div
                    key={i}
                    className="h-40 animate-pulse rounded-xl border border-midnight-800 bg-midnight-900/40"
                />
            ))}
        </div>
    );
}
