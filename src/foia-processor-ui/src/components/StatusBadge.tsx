type Props = { status: string };

const palette: Record<string, string> = {
    Submitted: "bg-sky-500/15 text-sky-300 ring-sky-500/30",
    InProgress: "bg-amber-500/15 text-amber-300 ring-amber-500/30",
    PendingReview: "bg-amber-500/15 text-amber-300 ring-amber-500/30",
    AwaitingReview: "bg-amber-500/15 text-amber-300 ring-amber-500/30",
    PendingHumanReview: "bg-amber-500/15 text-amber-300 ring-amber-500/30",
    ApprovedForRelease: "bg-emerald-500/15 text-emerald-300 ring-emerald-500/30",
    Released: "bg-emerald-500/15 text-emerald-300 ring-emerald-500/30",
    ReleasePackageReady: "bg-emerald-500/15 text-emerald-300 ring-emerald-500/30",
    NoDocumentsFound: "bg-midnight-700/60 text-midnight-100 ring-midnight-600",
    Rejected: "bg-rose-500/15 text-rose-300 ring-rose-500/30",
    Failed: "bg-rose-500/15 text-rose-300 ring-rose-500/30",
    Error: "bg-rose-500/15 text-rose-300 ring-rose-500/30",
};

// Statuses that represent active automated processing (not terminal,
// not awaiting human action). Badges for these statuses pulse to
// indicate live activity.
const activeStatuses = new Set<string>([
    "Submitted",
    "Validated",
    "Searching",
    "DocumentsFound",
    "Redacting",
    "Packaging",
    "InProgress",
]);

function format(status: string) {
    return status.replace(/([a-z])([A-Z])/g, "$1 $2");
}

export default function StatusBadge({ status }: Props) {
    const isActive = activeStatuses.has(status);
    const cls = isActive
        ? "bg-emerald-500/15 text-emerald-200 ring-emerald-400/40 animate-status-pulse"
        : palette[status] ?? "bg-midnight-700/60 text-midnight-100 ring-midnight-600";
    return (
        <span
            className={`inline-flex shrink-0 items-center whitespace-nowrap rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset ${cls}`}
            title={isActive ? "Actively processing" : undefined}
        >
            {isActive && (
                <span
                    aria-hidden="true"
                    className="mr-1.5 inline-block h-1.5 w-1.5 rounded-full bg-emerald-400 animate-status-dot"
                />
            )}
            {format(status)}
        </span>
    );
}

