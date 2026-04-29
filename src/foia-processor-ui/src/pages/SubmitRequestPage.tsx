import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { ApiValidationError, submitFoiaRequest } from "../api/client";
import type { SubmitFoiaRequest } from "../types";

const empty: SubmitFoiaRequest = {
    subject: "",
    description: "",
    requestedStartDate: "",
    requestedEndDate: "",
    requestorFullName: "",
    requestorOrganization: "",
    requestorEmail: "",
    requestorPhone: "",
    requestorMailingAddress: "",
};

export default function SubmitRequestPage() {
    const [form, setForm] = useState<SubmitFoiaRequest>(empty);
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
        return list && list.length > 0 ? <div className="field-error">{list.join(" ")}</div> : null;
    }

    return (
        <div className="page">
            <h1>Submit FOIA Request</h1>
            <form onSubmit={onSubmit} className="foia-form">
                <label>
                    Subject *
                    <input value={form.subject} onChange={(e) => update("subject", e.target.value)} />
                    {fieldError("subject")}
                </label>
                <label>
                    Description
                    <textarea value={form.description ?? ""} onChange={(e) => update("description", e.target.value)} />
                </label>
                <div className="row">
                    <label>
                        Start date *
                        <input type="date" value={form.requestedStartDate} onChange={(e) => update("requestedStartDate", e.target.value)} />
                        {fieldError("requestedStartDate")}
                    </label>
                    <label>
                        End date *
                        <input type="date" value={form.requestedEndDate} onChange={(e) => update("requestedEndDate", e.target.value)} />
                        {fieldError("requestedEndDate")}
                    </label>
                </div>
                <label>
                    Full name *
                    <input value={form.requestorFullName} onChange={(e) => update("requestorFullName", e.target.value)} />
                    {fieldError("requestorFullName")}
                </label>
                <label>
                    Organization
                    <input value={form.requestorOrganization ?? ""} onChange={(e) => update("requestorOrganization", e.target.value)} />
                </label>
                <label>
                    Email *
                    <input type="email" value={form.requestorEmail} onChange={(e) => update("requestorEmail", e.target.value)} />
                    {fieldError("requestorEmail")}
                </label>
                <label>
                    Phone
                    <input value={form.requestorPhone ?? ""} onChange={(e) => update("requestorPhone", e.target.value)} />
                </label>
                <label>
                    Mailing address
                    <textarea value={form.requestorMailingAddress ?? ""} onChange={(e) => update("requestorMailingAddress", e.target.value)} />
                </label>
                {errors._ && <div className="field-error">{errors._.join(" ")}</div>}
                <button type="submit" disabled={submitting}>{submitting ? "Submitting…" : "Submit"}</button>
            </form>
        </div>
    );
}
