# AIOFOIA — Talk Track

> Speaker script for demoing **AIOFOIA: AI Orchestrated Freedom of Information Assistant**.
> Audience: slightly non-technical. Keep it conversational, skip the implementation jargon, and lean on the *why*.

---

## 1. The problem: the FOIA backlog

Every year, federal agencies receive **hundreds of thousands** of Freedom of Information Act requests — and every year, the backlog grows. Citizens, journalists, and researchers wait months, sometimes *years*, for a response.

The work itself is painstaking:

- Someone has to **read the request** and figure out if it's complete and actionable.
- Someone has to **search** through agency records to find what's responsive.
- Someone has to **read every page** of every document and **redact** anything sensitive — names, Social Security numbers, phone numbers, anything covered by a FOIA exemption.
- Someone has to **review the redactions** to make sure nothing leaked and nothing was over-redacted.
- And finally, someone has to **package it all up** and send it back to the requester.

It's slow, it's expensive, and it's a perfect use case for AI — not to *replace* the FOIA officer, but to give them an assistant that does the tedious 80% so they can focus on the judgment calls.

That's what **AIOFOIA** is. It's a proof-of-concept showing how a small team of AI agents can take a FOIA request from submission all the way to a downloadable, redacted, ready-to-release package — with a human reviewer firmly in control at the critical moment.

---

## 2. The workflow — five agents, one pipeline

A request flows through **five specialized AI agents**. Think of them as five coworkers who each do one job really well and hand the work off to the next person.

### Step 1 — Intake Validation Agent

A citizen submits a request through the web app: *subject, description, date range, contact info*.

The first agent reads it like an experienced FOIA officer would: **Is the subject clear? Is the email valid? Does the date range make sense?** If something's missing or contradictory, it rejects the request with a clear reason. If it's good, it green-lights the workflow and the next agent picks it up automatically.

> *No human had to triage the inbox. That's hours saved on day one.*

### Step 2 — Search Agent

This agent takes the request's subject and description and searches the agency's document repository. It uses **AI-powered search** — not just keyword matching, but semantic search that understands what the request is *actually about*, even if the requester didn't use the exact same words as the documents.

It pulls back every responsive document, grabs the full text, and saves them to the case file.

> *What used to take a records officer days of database queries now happens in seconds.*

### Step 3 — Redaction Agent

This is where the AI really earns its keep. For every single document found, a dedicated redaction agent reads the text and uses AI to **detect personally identifiable information** — names, addresses, Social Security numbers, phone numbers, email addresses, credit card numbers, and other sensitive content covered by FOIA exemptions.

It produces a **redacted version** of each document, side-by-side with the original, and flags every change it made.

> *A human reading every page used to be the bottleneck. Now the AI does the first pass — every page, every document, in minutes.*

### Step 4 — Human Review Coordinator

Here's the critical part: **the AI does not release anything on its own.**

This agent pauses the workflow and opens a review task for a real FOIA analyst. The analyst opens the web app and sees, for each document, the **original on the left in red** and the **redacted version on the right in green**, with the two panes scrolling in sync. Every redaction the AI made is highlighted. The analyst can:

- **Approve** the document as-is,
- **Reject** it and send it back for manual handling, or
- **Edit** any redaction the AI got wrong.

Nothing moves forward until a human says it's good. *Agents propose, humans dispose.*

### Step 5 — Packaging & Release Agent

Once every document is approved, the final agent kicks in. It **converts each approved, redacted document to a PDF**, bundles them all into a **ZIP file**, uploads the ZIP to secure cloud storage, and generates a **time-limited download link**.

That link goes to the requester. They click it, the ZIP downloads, and the request is closed.

> *From submission to download link — a process that used to take months can now happen in a single sitting, with a human only spending time on the judgment calls.*

---

## 3. How we built it — GitHub Copilot + Spec Kit

Here's the part that surprised even us: **this entire system — the five AI agents, the web app, the cloud infrastructure, the deployment pipeline — was built with GitHub Copilot using Spec Kit, in a fraction of the time it would normally take.**

**Spec Kit** is a methodology for building software by writing the *specification* first — what should this system do, who are the users, what are the acceptance criteria — and then letting AI handle the bulk of the implementation. We wrote a spec describing the FOIA workflow, the agents, the user stories, and the success criteria. **GitHub Copilot** then helped us:

- Generate the agent code, the API, the React UI, and the database schema directly from the spec.
- Wire up the Azure infrastructure as code (Bicep templates for Container Apps, AI Search, Blob Storage, OpenAI, SQL Database).
- Build the GitHub Actions deployment pipeline.
- Debug the inevitable issues — region quotas, identity permissions, connection strings — with conversational back-and-forth instead of hours of Stack Overflow.

The result:

- **Quality went up.** Because the spec was the source of truth, the code stayed consistent. Copilot followed the patterns we established and didn't drift.
- **Speed went up — dramatically.** Features that would normally take a sprint took an afternoon. The entire PoC, end-to-end, was built in a window of time that would barely have covered just the *planning* of a traditional project.
- **The developer stayed in control.** Just like our FOIA reviewer in Step 4, the developer was the human-in-the-loop — reviewing, approving, and steering. Copilot proposed, the developer disposed.

That's the real meta-story here: **we used AI to build an AI system that helps humans do better work, faster. And the way we built it mirrors the way the system itself works.**

---

## 4. Closing

AIOFOIA is a proof of concept, not a production system. But it shows what's possible when you combine:

- **Multi-agent AI workflows** — small, focused agents that hand work off to each other,
- **Human-in-the-loop review** at the moments that matter,
- **Modern AI development tools** like GitHub Copilot and Spec Kit that let a small team ship something this ambitious this fast.

The FOIA backlog isn't going to fix itself. But with the right AI assistance, the people working on it can finally get ahead of it.

Thank you.
