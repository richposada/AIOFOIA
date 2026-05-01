import { useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { ApiValidationError, submitFoiaRequest } from "../api/client";
import type { SubmitFoiaRequest } from "../types";

const SUBJECT_MAX = 50;
const DESCRIPTION_MAX = 200;
const SHORT_TEXT_MAX = 100;

function isoDate(d: Date) {
    // YYYY-MM-DD in local time (matches the value an <input type="date"> uses).
    const yyyy = d.getFullYear();
    const mm = String(d.getMonth() + 1).padStart(2, "0");
    const dd = String(d.getDate()).padStart(2, "0");
    return `${yyyy}-${mm}-${dd}`;
}

function buildInitialForm(): SubmitFoiaRequest {
    const today = new Date();
    const thirtyDaysAgo = new Date();
    thirtyDaysAgo.setDate(today.getDate() - 30);
    return {
        subject: "",
        description: "",
        requestedStartDate: isoDate(thirtyDaysAgo),
        requestedEndDate: isoDate(today),
        requestorFullName: "",
        requestorOrganization: "",
        requestorEmail: "",
        requestorPhone: "",
        requestorMailingAddress: "",
    };
}

const labelClass = "block text-sm font-medium text-midnight-100";
const inputClass =
    "mt-1 block w-full rounded-md border border-midnight-700 bg-midnight-950/60 px-3 py-2 text-sm text-white shadow-sm placeholder:text-midnight-500 focus:border-indigo-400 focus:outline-none focus:ring-1 focus:ring-indigo-400";
const helperClass = "mt-1 flex justify-between text-xs text-midnight-400";
const errorClass = "mt-1 text-xs text-rose-300";

export default function SubmitRequestPage() {
    const initialForm = useMemo(buildInitialForm, []);
    const [form, setForm] = useState<SubmitFoiaRequest>(initialForm);
    const [errors, setErrors] = useState<Record<string, string[]>>({});
    const [submitting, setSubmitting] = useState(false);
    const navigate = useNavigate();

    function update<K extends keyof SubmitFoiaRequest>(key: K, value: SubmitFoiaRequest[K]) {
        setForm({ ...form, [key]: value });
    }

    async function onSubmit(e: React.FormEvent) {
        e.preventDefault();
        setSubmitting(true);
        setErrors({});
        try {
            const res = await submitFoiaRequest(form);
            navigate(`/requests/${res.id}`);
        } catch (err) {
            if (err instanceof ApiValidationError) {
                setErrors(err.problem.errors);
            } else {
                setErrors({ _: [(err as Error).message] });
            }
        } finally {
            setSubmitting(false);
        }
    }

    function fieldError(name: string) {
        const list = errors[name];
        return list && list.length > 0 ? <p className={errorClass}>{list.join(" ")}</p> : null;
    }

    function counter(value: string | undefined | null, max: number) {
        const len = value?.length ?? 0;
        const over = len > max;
        return (
            <span className={over ? "text-rose-300" : undefined}>
                {len}/{max}
            </span>
        );
    }

    return (
        <div>
            <div className="mb-8">
                <h1 className="text-3xl font-semibold text-white">Submit FOIA Request</h1>
                <p className="mt-1 text-sm text-midnight-300">
                    This page is to demo submitting a FOIA request through the UI.  The AI-Orchestrated FOIA process begins when a request is posted to the backend API, which then triggers the orchestration that handles document retrieval, redaction, review, and release packaging.
                </p>
            </div>

            {errors._ && (
                <div
                    role="alert"
                    className="mb-6 rounded-lg border border-rose-500/40 bg-rose-500/10 px-4 py-3 text-sm text-rose-200"
                >
                    {errors._.join(" ")}
                </div>
            )}

            <form
                onSubmit={onSubmit}
                noValidate
                className="space-y-6 rounded-2xl border border-slate-300/40 bg-slate-200/15 p-6 shadow-card ring-1 ring-slate-200/20"
            >
                <div>
                    <label htmlFor="subject" className={labelClass}>
                        Subject <span className="text-rose-300">*</span>
                    </label>
                    <input
                        id="subject"
                        type="text"
                        required
                        maxLength={SUBJECT_MAX}
                        value={form.subject}
                        onChange={(e) => update("subject", e.target.value)}
                        className={inputClass}
                    />
                    <div className={helperClass}>
                        <span>Short title for the records you are requesting.</span>
                        {counter(form.subject, SUBJECT_MAX)}
                    </div>
                    {fieldError("subject")}
                </div>

                <div>
                    <label htmlFor="description" className={labelClass}>
                        Description
                    </label>
                    <textarea
                        id="description"
                        rows={3}
                        maxLength={DESCRIPTION_MAX}
                        value={form.description ?? ""}
                        onChange={(e) => update("description", e.target.value)}
                        className={inputClass}
                    />
                    <div className={helperClass}>
                        <span>Optional. Add detail to help locate matching records.</span>
                        {counter(form.description, DESCRIPTION_MAX)}
                    </div>
                    {fieldError("description")}
                </div>

                <div className="grid gap-6 sm:grid-cols-2">
                    <div>
                        <label htmlFor="startDate" className={labelClass}>
                            Start date <span className="text-rose-300">*</span>
                        </label>
                        <input
                            id="startDate"
                            type="date"
                            required
                            value={form.requestedStartDate}
                            onChange={(e) => update("requestedStartDate", e.target.value)}
                            className={inputClass}
                        />
                        {fieldError("requestedStartDate")}
                    </div>
                    <div>
                        <label htmlFor="endDate" className={labelClass}>
                            End date <span className="text-rose-300">*</span>
                        </label>
                        <input
                            id="endDate"
                            type="date"
                            required
                            value={form.requestedEndDate}
                            onChange={(e) => update("requestedEndDate", e.target.value)}
                            className={inputClass}
                        />
                        {fieldError("requestedEndDate")}
                    </div>
                </div>

                <div className="grid gap-6 sm:grid-cols-2">
                    <div>
                        <label htmlFor="fullName" className={labelClass}>
                            Full name <span className="text-rose-300">*</span>
                        </label>
                        <input
                            id="fullName"
                            type="text"
                            required
                            maxLength={SHORT_TEXT_MAX}
                            value={form.requestorFullName}
                            onChange={(e) => update("requestorFullName", e.target.value)}
                            className={inputClass}
                        />
                        <div className={helperClass}>
                            <span />
                            {counter(form.requestorFullName, SHORT_TEXT_MAX)}
                        </div>
                        {fieldError("requestorFullName")}
                    </div>
                    <div>
                        <label htmlFor="organization" className={labelClass}>
                            Organization
                        </label>
                        <input
                            id="organization"
                            type="text"
                            maxLength={SHORT_TEXT_MAX}
                            value={form.requestorOrganization ?? ""}
                            onChange={(e) =>
                                update("requestorOrganization", e.target.value)
                            }
                            className={inputClass}
                        />
                        <div className={helperClass}>
                            <span />
                            {counter(form.requestorOrganization, SHORT_TEXT_MAX)}
                        </div>
                        {fieldError("requestorOrganization")}
                    </div>
                </div>

                <div className="grid gap-6 sm:grid-cols-2">
                    <div>
                        <label htmlFor="email" className={labelClass}>
                            Email <span className="text-rose-300">*</span>
                        </label>
                        <input
                            id="email"
                            type="email"
                            required
                            maxLength={SHORT_TEXT_MAX}
                            value={form.requestorEmail}
                            onChange={(e) => update("requestorEmail", e.target.value)}
                            className={inputClass}
                        />
                        <div className={helperClass}>
                            <span />
                            {counter(form.requestorEmail, SHORT_TEXT_MAX)}
                        </div>
                        {fieldError("requestorEmail")}
                    </div>
                    <div>
                        <label htmlFor="phone" className={labelClass}>
                            Phone
                        </label>
                        <input
                            id="phone"
                            type="tel"
                            maxLength={SHORT_TEXT_MAX}
                            value={form.requestorPhone ?? ""}
                            onChange={(e) => update("requestorPhone", e.target.value)}
                            className={inputClass}
                        />
                        <div className={helperClass}>
                            <span />
                            {counter(form.requestorPhone, SHORT_TEXT_MAX)}
                        </div>
                        {fieldError("requestorPhone")}
                    </div>
                </div>

                <div>
                    <label htmlFor="address" className={labelClass}>
                        Mailing address
                    </label>
                    <textarea
                        id="address"
                        rows={2}
                        maxLength={SHORT_TEXT_MAX}
                        value={form.requestorMailingAddress ?? ""}
                        onChange={(e) =>
                            update("requestorMailingAddress", e.target.value)
                        }
                        className={inputClass}
                    />
                    <div className={helperClass}>
                        <span />
                        {counter(form.requestorMailingAddress, SHORT_TEXT_MAX)}
                    </div>
                    {fieldError("requestorMailingAddress")}
                </div>

                <div className="flex items-center justify-end gap-3 border-t border-slate-300/30 pt-6">
                    <button
                        type="button"
                        onClick={() => navigate(-1)}
                        disabled={submitting}
                        className="rounded-md border border-midnight-700 bg-midnight-900/60 px-4 py-2 text-sm font-medium text-midnight-100 transition-colors hover:bg-midnight-800 disabled:cursor-not-allowed disabled:opacity-60"
                    >
                        Cancel
                    </button>
                    <button
                        type="submit"
                        disabled={submitting}
                        className="inline-flex items-center rounded-md bg-indigo-500 px-4 py-2 text-sm font-semibold text-white shadow-sm transition-colors hover:bg-indigo-400 disabled:cursor-not-allowed disabled:opacity-60"
                    >
                        {submitting ? "Submitting…" : "Submit request"}
                    </button>
                </div>
            </form>
        </div>
    );
}
