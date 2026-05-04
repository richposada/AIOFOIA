import type { HealthStatus } from "../types";

type Props = { status: HealthStatus; size?: "sm" | "md" };

const palette: Record<HealthStatus, { pill: string; dot: string }> = {
    Healthy: {
        pill: "bg-emerald-500/15 text-emerald-200 ring-emerald-400/40",
        dot: "bg-emerald-400",
    },
    Degraded: {
        pill: "bg-amber-500/15 text-amber-200 ring-amber-400/40",
        dot: "bg-amber-400",
    },
    Unhealthy: {
        pill: "bg-rose-500/15 text-rose-200 ring-rose-400/40",
        dot: "bg-rose-400",
    },
};

export default function HealthBadge({ status, size = "sm" }: Props) {
    const p = palette[status];
    const sz =
        size === "md"
            ? "px-3 py-1 text-sm"
            : "px-2.5 py-0.5 text-xs";
    return (
        <span
            className={`inline-flex shrink-0 items-center whitespace-nowrap rounded-full font-medium ring-1 ring-inset ${p.pill} ${sz}`}
        >
            <span
                aria-hidden="true"
                className={`mr-1.5 inline-block h-1.5 w-1.5 rounded-full ${p.dot}`}
            />
            {status}
        </span>
    );
}
