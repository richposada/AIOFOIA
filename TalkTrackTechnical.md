# AIOFOIA — Technical Talk Track

> Speaker script for a **technical** audience (developers, AI engineers, solution architects).
> Goes deep on architecture, the agent framework, tool design, orchestration, and the deployment surface.
> The non-technical version lives in [TalkTrack.md](TalkTrack.md).

---

## 1. What we're looking at

**AIOFOIA** is a multi-agent FOIA (Freedom of Information Act) request processing proof of concept.
A submitted request flows through five orchestrated agents — **Intake → Search → Redaction → Human Review (paused) → Packaging** — and ends with a SAS-protected ZIP of redacted PDFs in Azure Blob Storage.

**Stack at a glance**

| Layer | Tech |
|---|---|
| Agents / orchestration | **Microsoft Agent Framework** (`Microsoft.Agents.AI` 1.3.0), **Microsoft.Extensions.AI** 10.5.0 |
| LLM | Azure OpenAI (chat completions, structured outputs) |
| Search | Azure AI Search (hybrid: BM25 + vector + semantic ranker) |
| API | ASP.NET Core 8 (Controllers, EF Core 8, Serilog, Swagger) |
| UI | React 18 + TypeScript 5 + Vite 5 + Tailwind 3 |
| Persistence | Azure SQL Database serverless (`GP_S_Gen5_1`, AAD-only auth via Managed Identity) |
| Storage | Azure Blob Storage (user-delegation SAS in Azure, account-key SAS for local dev) |
| Hosting | Azure Container Apps + ACR, User-Assigned Managed Identity |
| IaC / CI | Bicep (resourceGroup scope) + `azd` + GitHub Actions with OIDC federated credentials |

**Solution layout** (key projects)

- `FoiaProcessor.Api` — ASP.NET Core API + Swagger; enqueues workflow runs.
- `FoiaProcessor.Agents` — the five agents + workflow runner + chat client factory.
- `FoiaProcessor.McpTools` — **MCP-shaped servers** (CaseServer, SearchServer, RedactionServer, ReviewServer, BlobStorageServer) that wrap *all* side effects. Agents reach the outside world only through these.
- `FoiaProcessor.Data` — EF Core entities, `FoiaDbContext`, audit writer.
- `foia-processor-ui` — React SPA for submission, status, document review, and release download.

---

## 2. Microsoft Agent Framework — what it is and why we chose it

**Microsoft Agent Framework** (`Microsoft.Agents.AI`) is the unified successor to Semantic Kernel agents and AutoGen — Microsoft's library for building LLM-driven agents in .NET. The pieces we actually use:

- **`ChatClientAgent`** — a thin agent abstraction over `Microsoft.Extensions.AI.IChatClient`. You give it a system prompt, a name, and a list of `AITool`s; calling `RunAsync(userMessage)` runs the prompt → tool-call → tool-result loop until the model stops calling tools.
- **`Microsoft.Extensions.AI`** — the model abstraction. `IChatClient` lets us swap providers (OpenAI, Azure OpenAI, Ollama) without touching agent code. We use Azure OpenAI via `AzureOpenAIClient(...).GetChatClient(deployment).AsIChatClient()`.
- **`AIFunctionFactory.Create(Delegate)`** — turns a regular C# local function (with `[Description(...)]` attributes on the method and parameters) into an `AIFunction` the model can call. **No JSON schema hand-rolling.** Method signatures *are* the tool contract.

### Why this framework over the alternatives

1. **First-party for .NET.** We're a .NET shop. Agent Framework lets us stay in C#, use DI, use `ILogger<T>`, use EF Core in tools — no Python sidecar, no gRPC bridge.
2. **Tool definition = method signature.** Compare to hand-writing OpenAI function schemas: `[Description("...")] async Task<string> SetValidated([Description("...")] string note)` is the entire definition. The framework reflects parameters, builds the schema, validates calls, and dispatches. This is what makes the agents in this repo so small (~80 LOC each).
3. **Provider-agnostic.** `IChatClient` means the redaction agent doesn't care that we're on Azure OpenAI today; we could point it at `gpt-oss` on Ollama for local dev tomorrow.
4. **Composable with the rest of MS.AI.** Streaming, structured outputs, embeddings, telemetry — same abstractions across the platform.
5. **MCP-friendly.** The mental model — "agents talk to *tool servers* over a structured contract" — maps cleanly onto MCP. We don't run an MCP transport in-process, but we shape our tool servers (CaseServer, SearchServer, etc.) so they could be exposed as MCP servers with minimal change.

### How an agent is wired (canonical example: `IntakeValidationAgent`)

```csharp
[Description("Mark the FOIA request as validated. Call only when all required fields are present and dates are coherent.")]
async Task<string> SetValidated([Description("Short note explaining why validation passed.")] string note)
{
    await _case.CaseUpdateStatusAsync(
        new CaseUpdateStatusInput(requestId, nameof(RequestStatus.Validated), note), ct);
    return "validated";
}

[Description("Mark the FOIA request as rejected because validation failed.")]
async Task<string> SetRejected([Description("Reason explaining what is missing or invalid.")] string reason) { ... }

var agent = new ChatClientAgent(
    _chatFactory.ChatClient,
    instructions: """
        You are the Intake Validation agent ...
        Required fields: Subject, RequestorFullName, RequestorEmail, RequestedStartDate, RequestedEndDate.
        ...
        Call exactly one tool, then stop.
        """,
    name: "IntakeValidator",
    tools: new List<AITool>
    {
        AIFunctionFactory.Create((Delegate)SetValidated),
        AIFunctionFactory.Create((Delegate)SetRejected),
    });

await agent.RunAsync($"Validate this FOIA request:\n{payload}", cancellationToken: ct);
```

That's the *whole* agent. Closures over `requestId`, `_case`, and `ct` give the tools access to per-invocation context without polluting their public signature — and without any global state or thread-local hacks.

---

## 3. Orchestration — `FoiaWorkflowRunner`

The workflow is **state-machine driven and resumable**, persisted in `FoiaRequest.Status`:

```
Submitted → Validated → Searching → DocumentsFound → Redacting →
PendingHumanReview  ⏸  ApprovedForRelease → Packaging → ReleasePackageReady
```

`FoiaWorkflowRunner.RunAsync(requestId)` reads the current status and **`switch / goto case`** falls through the remaining stages. Two important design choices:

- **Pauses are just early returns.** When `HumanReviewCoordinatorAgent` finishes, the runner returns. The API re-enqueues the request after the analyst clicks "Approve Release", and the runner picks up at `ApprovedForRelease`.
- **Failures persist `RequestStatus.Error` and emit an audit event.** A `db.ChangeTracker.Clear()` happens first so partially-applied agent state doesn't trigger spurious EF concurrency conflicts when we save the error row.

The runner runs in a background `Channel<Guid>` consumer hosted in the API process — one in-flight request at a time per replica, intentionally simple for the PoC.

---

## 4. The five agents — technical detail

### Agent 1 — IntakeValidationAgent (LLM-driven)

- **Pattern:** single LLM turn, one of two tools (`SetValidated` / `SetRejected`).
- **Why an LLM?** Validation rules are mostly objective (`email looks like an email`, `start ≤ end`), but we want graceful handling of fuzzy cases ("the requester wrote their name as 'J.'"). The LLM gives us a coherent rejection note for free.
- **Tool surface:** `CaseServer.CaseUpdateStatusAsync(...)`. Both tools terminate the request.

### Agent 2 — SearchAgent (LLM-driven, multi-tool, **server-side cache**)

- **Pattern:** LLM crafts a query string, calls `SearchDocuments`, then `GetDocumentContent` per hit, then `SaveDocuments` once.
- **Server-side cache trick:** to keep the LLM's context window small and avoid re-marshalling document text through tool args, the agent keeps two C# dictionaries (`foundResults`, `fetchedContent`) in closure. `SaveDocuments` takes **no arguments** — it just flushes whatever's in those dictionaries to `CaseServer.CaseSaveDocumentMetadataAsync`. The model only ever passes IDs around.
- **Tools:**
  - `SearchDocuments(query)` → wraps `SearchServer.SearchDocumentsAsync` (Azure AI Search hybrid query, date-filtered by request range, capped at `WorkflowOptions.MaxSearchResults`).
  - `GetDocumentContent(documentId)`
  - `SaveDocuments()` (no args, idempotent)
  - `MarkNoDocumentsFound()` — escape hatch when search returns empty.

### Agent 3 — RedactionAgent (deterministic orchestration, **LLM lives in the tool**)

- **Pattern:** plain C# loop. Iterates documents in `RedactionStatus.NotStarted`, calls `RedactionServer.DetectPiiAsync` then `RedactTextAsync` then `CaseServer.CaseSaveRedactionResultsAsync`.
- **Why no agent loop?** The detect → redact → save sequence has zero decision points. Wrapping it in a `ChatClientAgent` would give the model an opportunity to skip a step or hallucinate — and provides no value. Orchestration stays deterministic; **the LLM call is pushed *down* into `RedactionServer.DetectPiiAsync`**.
- **What the LLM actually does (`useAi: true`):** `DetectPiiAsync` runs in two layers:
  1. **Deterministic regex pass** for the unambiguous, regular-grammar PII: `Email`, `Phone`, `SSN`, `DateOfBirth`. Cheap, fast, 100% recall on well-formed values, zero LLM cost.
  2. **LLM pass for the *fuzzy* categories that regex can't catch** — `Name`, `Address`, `FinancialId`. The server builds a `gpt-4o-mini` chat completion with:
     - A tightly-scoped system prompt: *"You extract PII from text. Return ONLY a JSON object `{"findings":[{piiType, originalText, startOffset, endOffset, confidence}]}`. Do not include emails, phones, SSNs, or DOBs (handled separately). Use exact substring offsets."*
     - **Structured output enforcement** via `ChatResponseFormat.CreateJsonObjectFormat()` — the API guarantees parseable JSON, no regex-cleanup of model prose.
     - `Temperature = 0` for determinism across reruns.
     - The full document body as the user message.
  3. The JSON is parsed, each finding is tagged with `DetectionSource = "ai"` and a model-supplied `confidence` score (regex findings are tagged `"regex"`, no confidence). The two lists are merged, sorted by offset, and returned as one stream of `PiiFinding`s.
- **Why split it that way?** Regex is the wrong tool for "is this a person's name" but the right tool for "does this match `\d{3}-\d{2}-\d{4}`". The LLM is the wrong tool for high-volume regular patterns (token cost, latency, occasional miss) but the right tool for context-dependent entities. Splitting the workload plays each to its strength and keeps the LLM prompt focused on a small, well-defined task.
- **Failure isolation.** The AI branch is wrapped in `try/catch`: if Azure OpenAI is down, throttled, or misconfigured, the warning is logged and **redaction continues with regex findings only**. The pipeline never fails closed because of a model outage.
- **Downstream:** `RedactTextAsync` applies findings **right-to-left** so offsets stay valid as substitutions shrink/grow the text. Each PII type maps to a stable label (`[REDACTED NAME]`, `[REDACTED EMAIL]`, etc.) — what the reviewer sees in the green pane on the Document Review page.
- **Concurrency hygiene:** `_db.ChangeTracker.Clear()` between documents prevents per-doc tracked entities from bleeding into the next iteration's `SaveChanges` (which previously caused EF concurrency conflicts when the agent was resumed mid-batch).


### Agent 4 — HumanReviewCoordinatorAgent (LLM-driven, single decision)

- **Pattern:** three tools (`GetReviewStatus`, `CreateReviewTask`, `Acknowledge`); the model picks one.
- **Why an LLM?** Honestly — overkill for one binary decision. Kept as an LLM-driven agent to demonstrate the pattern and to leave room for richer policies (e.g., "re-open review if any redaction confidence < 0.7").
- **The pause:** after this agent runs, the workflow runner returns. The UI takes over: the analyst opens the **Document Review** page (synced-scroll panes — original in red, redacted in green) and approves each doc. The API endpoint that handles "Approve Release" sets `RequestStatus.ApprovedForRelease` and re-enqueues.

### Agent 5 — PackagingReleaseAgent (deterministic, four steps)

- **Pattern:** plain C# pipeline. No LLM — same reasoning as Agent 3.
- **Steps:**
  1. Query approved documents, project to `(FileName, RedactedContent ?? OriginalContent)`.
  2. **Render each doc to PDF** with `PdfRenderer.Render(title, body)` (QuestPDF: Letter, 1" margins, monospaced 10pt body, title header, page-number footer).
  3. Build the ZIP via `BlobStorageServer.CreateZipPackageBinaryAsync` — a **byte-array entry overload** added specifically for binary content (the original `CreateZipPackageAsync` UTF-8-encodes string content and would corrupt PDF bytes).
  4. Upload to Blob Storage, mint a SAS URL (account-key SAS locally; **user-delegation SAS via Managed Identity in Azure**), persist the `ReleasePackage`, advance status to `ReleasePackageReady`.
- **Filenames:** `Path.ChangeExtension(d.FileName, ".pdf")` so `report.txt` → `report.pdf`, stems preserved.

---

## 5. The MCP tool layer — `FoiaProcessor.McpTools`

Every side effect — every DB write, every Azure AI Search call, every Azure OpenAI call, every blob I/O — lives in **one of five tool servers**:

| Server | Responsibility |
|---|---|
| `CaseServer` | All `FoiaRequest`, `Document`, `ReleasePackage` CRUD + status transitions + audit |
| `SearchServer` | Azure AI Search hybrid query + content fetch |
| `RedactionServer` | Regex + Azure OpenAI structured-output PII detection + redaction |
| `ReviewServer` | Review tasks, per-document approve/reject |
| `BlobStorageServer` | Zip (string + binary), upload, SAS, delete |

Why this matters:

- **Agents are pure orchestration.** They have no `using Azure.*`, no `using Microsoft.EntityFrameworkCore` in the hot path beyond a couple of read-only queries — they're just prompts, tool wiring, and closures.
- **Testable seam.** Servers are `public virtual` methods on instantiable classes — straightforward to mock with Moq / NSubstitute.
- **MCP-shaped on purpose.** Each method takes a single `Input` record and returns an `Output` record (see `FoiaProcessor.McpTools/Contracts`). Promoting a server to a real MCP server (stdio or HTTP transport) becomes a wrapping exercise, not a rewrite.

---

## 6. Identity, auth, and secrets

- **Single User-Assigned Managed Identity** (`id-foia-<token>`) attached to the Container App, used as the credential for *every* downstream Azure service:
  - Azure OpenAI: `Cognitive Services OpenAI User`.
  - Azure AI Search: `Search Index Data Reader` + `Search Service Contributor`.
  - Azure Blob Storage: `Storage Blob Data Contributor` (intentionally not `Owner` — the health probe was simplified to use `ExistsAsync` instead of `GetPropertiesAsync` to fit this least-privilege role).
  - Azure SQL: AAD-only auth; the MI is the **primary AAD admin**, and `db.Database.Migrate()` runs at startup.
- **`DefaultAzureCredential`** everywhere. The `AZURE_CLIENT_ID` env var (set by Bicep) tells `DefaultAzureCredential` *which* MI to use when multiple are visible.
- **SQL connection string:** `Server=tcp:...,1433;Database=...;Encrypt=True;Authentication=Active Directory Default;` — `Microsoft.Data.SqlClient` handles the token exchange via `DefaultAzureCredential` automatically.
- **No secrets in `appsettings.json` in production.** API keys (e.g., the Azure AI Search admin key for setup) flow through GitHub Actions repository secrets → `azd` env vars → Container App env vars.

---

## 7. Infrastructure & deployment

- **Bicep at `resourceGroup` scope** (`infra/main.bicep`) provisions: Log Analytics, ACR, User-Assigned MI, Storage Account, Azure OpenAI (with `gpt-4o-mini` deployment), Container Apps Environment + Container App, Azure SQL serverless (`GP_S_Gen5_1`, AAD-only, MI as admin, auto-pause 60min, 0.5–1 vCore).
- **`sqlLocation` parameter override** — Azure SQL provisioning can be region-restricted on some subscriptions independently of the rest of the workload. We exposed `AZURE_SQL_LOCATION` as a separate env var so SQL can be deployed in a permitted region while everything else stays in the primary region.
- **`azd up`** for first-time provisioning; **GitHub Actions** for everything after:
  - OIDC federation via `Azure/setup-azd@v2` + `azd auth login --client-id ... --federated-credential-provider github`.
  - `actions/setup-dotnet@v4` with `global-json-file: global.json` (we pin to SDK `8.0.420`).
  - Path-filtered triggers on `src/**`, `infra/**`, `azure.yaml`.

---

## 8. Notable engineering decisions worth defending

1. **LLM where you need flexibility, deterministic code where you don't.** Agents 3 and 5 are pure C#. Agents 1, 2, 4 are LLM-driven. The `ChatClientAgent` shape doesn't impose itself on logic that doesn't need it.
2. **Server-side caches inside agent closures (`SearchAgent`).** Keeps document text out of the LLM's context, keeps tool args small, keeps token costs predictable.
3. **Binary-vs-string zip overload (`PackagingReleaseAgent`).** Reusing the string-based `CreateZipPackageAsync` for PDFs would have UTF-8-encoded the binary bytes and silently corrupted every output. The `CreateZipPackageBinaryAsync` overload is additive — the original tool stays intact for any text-content callers.
4. **`switch / goto case` resume in the workflow runner.** Looks ancient, reads beautifully, eliminates an entire class of "did we re-run this stage?" bugs because the persisted status *is* the program counter.
5. **Tools as local functions with `[Description]`, not classes.** Smaller surface area, closure over per-request state, no DI gymnastics for `requestId` / `ct`.

---

## 9. How this was built — Spec Kit + GitHub Copilot

The methodology mattered as much as the architecture. The whole repo started with a **spec** — see `specs/001-foia-agent-poc/{spec.md,plan.md,tasks.md,data-model.md,contracts/}`.

**Spec Kit** is GitHub's spec-driven development framework: write the *what* (user stories, acceptance criteria, data model, API contracts) before the *how*. Spec Kit gives you a template, slash-command prompts, and a lifecycle (`/specify` → `/plan` → `/tasks` → `/implement`). The spec becomes the single source of truth that both the human and the AI work from.

**GitHub Copilot** (in agent mode) consumes that spec and:

- Scaffolds projects, generates entities, controllers, agents, tool servers, React components, and Bicep modules **from the spec's contracts**.
- Iterates on focused changes ("add Azure SQL serverless to the Bicep, AAD-only, MI as admin, region overridable") with full repo context.
- Debugs deployment in conversation — quota errors, FIC subject mismatches, missing connection strings, blob authz — without leaving the editor.

Two outcomes worth measuring:

- **Quality.** With the spec as the contract, generated code stayed *consistent across modules* — same naming, same error-handling shape, same tool-definition pattern across all five agents. No drift.
- **Speed.** End-to-end PoC — agents, API, UI, IaC, CI/CD, deployed and demo-ready — shipped in a window that would normally cover the *design phase* of a traditional engagement. Most individual features (e.g., adding the PDF conversion + binary zip overload) were completed in a single conversational turn including the build verification.

The meta-narrative is the punch line: **we used an AI development workflow (Spec Kit + Copilot) to build an AI workflow product (Agent Framework + Azure OpenAI). The same human-in-the-loop pattern shows up at both layers** — agents propose, humans approve.

---

## 10. Q&A primers

- **"Could you run this fully agentic — let the LLM decide between agents?"** Yes. The current state machine is a deliberate guardrail for a deterministic demo. Replacing `FoiaWorkflowRunner.RunAsync` with a coordinator agent that calls each downstream agent as a tool is a one-day refactor.
- **"Why not Semantic Kernel?"** Microsoft Agent Framework *is* the convergence target for SK agents and AutoGen. New work should land here.
- **"Why MCP-shaped servers if you don't run MCP?"** Cheap optionality. The shape is good design either way (single-input/single-output records, no hidden state), and exposing them as MCP servers later is a wrapper change.
- **"How do you handle prompt injection in fetched documents?"** Today, partially — the redaction agent's LLM call uses Azure OpenAI structured outputs with a strict schema, which limits arbitrary instruction-following. Production hardening would add input/output content filters and an explicit "treat document text as data, not instructions" frame.
- **"Cost?"** PoC scale: pennies per request. The bulk of LLM tokens go to redaction (one structured-output call per document); intake / coordinator are tiny. SQL serverless auto-pauses after 60 minutes. Container Apps scales to zero.
