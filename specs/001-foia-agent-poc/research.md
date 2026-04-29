# Phase 0 Research: FOIA Agent Proof-of-Concept

**Feature**: 001-foia-agent-poc
**Date**: 2026-04-28
**Purpose**: Resolve open technology / pattern questions before Phase 1 design.

The Technical Context in [plan.md](plan.md) contains no `NEEDS CLARIFICATION` markers — every choice
was made in the plan from the user's stated requirements and the constitution. The research below
captures the decisions, why they were chosen, and what was rejected, so future readers (and the
`/speckit.tasks` step) understand the reasoning.

---

## Decision: Microsoft Agent Framework for orchestration

- **Decision**: Use Microsoft Agent Framework (NuGet `Microsoft.Agents.*`) to define five agents and
  compose them sequentially with one human-in-the-loop pause between Redaction and Packaging.
- **Rationale**: Constitution Principle IV makes the agent pattern the central architectural element.
  The framework provides agent + tool abstractions and a workflow runner that the API can invoke and
  resume. Sequential composition matches the spec's linear workflow; no need for graph/branching
  features.
- **Alternatives considered**:
  - *Hand-rolled state machine in C#* — simpler but defeats the demo's purpose (showing agent
    orchestration). Rejected.
  - *Semantic Kernel planners* — overlapping capability but the user explicitly asked for Microsoft
    Agent Framework. Rejected.

## Decision: In-process MCP tool host, one logical server per capability

- **Decision**: Implement five logical MCP "servers" — Case, Search, Redaction, Review, BlobStorage —
  as separate tool collections registered in a single in-process MCP host hosted by the API project.
  Each tool is a typed C# method exposed to agents through the Agent Framework's MCP integration.
- **Rationale**: Constitution Principle I (simplicity) plus the user's explicit allowance:
  > "For the proof of concept, these MCP servers can be implemented as local .NET services or tool
  > adapters if full standalone MCP server implementation is too heavy."
  Keeping the boundaries logical (separate folders, separate registration calls) preserves Principle
  IV's "agents reach side effects only via tools" rule without operational overhead.
- **Alternatives considered**:
  - *Five out-of-process MCP servers (stdio or HTTP)* — clearer separation but adds 5 deployable
    units, 5 process lifecycles, 5 sets of config. Rejected for PoC.
  - *Direct service injection into agents* — violates Principle IV's tool-boundary rule. Rejected.

## Decision: SQLite via EF Core 8 for local persistence

- **Decision**: Single SQLite file (`foia.db`) accessed through `FoiaDbContext`. Code-first model;
  `EnsureCreated()` (or one initial migration) on startup. File path overridable via configuration.
- **Rationale**: Spec explicitly recommends SQLite for demo simplicity. EF Core 8 SQLite provider
  works identically locally and in the Container App (file lives on the writable filesystem;
  acceptable per Principle III since durability across redeploys is not a demo requirement).
- **Alternatives considered**:
  - *Azure SQL / Cosmos DB* — adds provisioning time, cost, and per-machine connection setup with
    no demo benefit. Rejected.
  - *In-memory store* — loses state on restart, breaking the audit trail story. Rejected.

## Decision: Azure AI Search with `Azure.Search.Documents` SDK

- **Decision**: Read-only consumer of an existing index. Tool surface (`Search`) exposes
  `search_documents(query, startDate, endDate, top)`, `get_document_by_id(id)`,
  `get_document_content(id)`, and `search_documents_by_date_range(...)`. Configurable index field
  names via `AzureSearch:Fields:*` in `appsettings.json` (defaults: `id`, `title`, `content`,
  `fileName`, `fileType`, `sourceUri`, `documentDate`, `metadata`).
- **Rationale**: Spec explicitly assumes an existing populated index; user listed the configuration
  values; field-name configurability requested.
- **Alternatives considered**:
  - *Provision a new index in Bicep* — would require seeding documents during deploy. Out of scope.
  - *Hard-code field names* — rejected because user prefers configurable.

## Decision: Hybrid PII detection — regex baseline + Azure OpenAI fallback for names/addresses

- **Decision**: Regex-based detectors for email, phone, SSN, and date-of-birth (high confidence,
  fast, deterministic). Azure OpenAI (`gpt-4o-mini` or equivalent) prompt-based detection for
  person names, mailing addresses, and financial account numbers (lower confidence, best-effort).
  All detections produce the same `RedactionFinding` shape with a `Source` discriminator
  (`"regex" | "ai"`) and an optional `Confidence` (0–1, only populated for AI results).
- **Rationale**: Spec accepts pattern-based for the four "structured" PII types and best-effort
  AI/heuristic for names/addresses. Splitting by detector type keeps the regex path zero-cost and
  uses the LLM only where it adds value. Aligns with constitution Principle I.
- **Alternatives considered**:
  - *Azure AI Language PII detection cognitive skill* — covers everything but adds another Azure
    resource and SDK. Rejected to keep `azd up` lean.
  - *LLM for all detections* — slower, more expensive, less deterministic for regex-friendly types.
    Rejected.

## Decision: Azure Blob Storage with user-delegation SAS

- **Decision**: Single configured container (auto-created on first upload). ZIPs named
  `foia-request-{requestId}-release-package.zip`. Generate a **read-only user-delegation SAS** with
  configurable expiration (default 7 days) using the API's managed identity in Azure, and a
  connection-string-derived account-key SAS when running locally with the storage account's
  connection string in `.env`. Both code paths emit the same SAS URL shape to the UI.
- **Rationale**: User-delegation SAS is the modern, identity-based pattern preferred for Azure
  hosting; account-key SAS is fine for local demoing. The UI sees one URL; complexity is hidden in
  the BlobStorage MCP tool. Default 7-day expiration matches the spec.
- **Alternatives considered**:
  - *Public container* — violates least-privilege; rejected.
  - *Stored access policy* — adds management overhead with no demo upside. Rejected.

## Decision: React + Vite + TypeScript SPA, no UI framework

- **Decision**: Vite-scaffolded React 18 + TS app under `src/foia-processor-ui/`. React Router for
  the five screens. A small typed `api/` module wraps `fetch` calls. Minimal CSS (one `app.css` and
  per-component CSS modules where needed). No Material UI / Chakra / Tailwind for the PoC.
- **Rationale**: Vite gives the fastest dev loop. Skipping a UI framework keeps dependencies small
  and the demo's source easy to read. The five screens are simple forms + lists; no kit needed.
- **Alternatives considered**:
  - *Create React App* — deprecated. Rejected.
  - *Next.js* — overkill for a client-side-only SPA; would force a Node host. Rejected.
  - *Material UI* — nice visuals but adds bundle weight and learning surface. Rejected for v1.

## Decision: Single Azure Container App hosting API + bundled SPA

- **Decision**: Build the SPA at deploy time, copy `dist/` into the API's `wwwroot/`, serve as
  static files alongside `/api/*` from one container. `azure.yaml` defines one service `foia-api`
  pointed at `src/FoiaProcessor.Api`; the SPA build runs as part of the API project's publish step.
- **Rationale**: One container = one deployable, one URL, one cost line. Aligned with Principle I
  and Principle II's "single `azd up`" goal. CORS becomes a non-issue.
- **Alternatives considered**:
  - *Azure Static Web Apps + separate Container App for API* — two services, two URLs, CORS to
    configure. Rejected for PoC.
  - *Azure App Service* — works fine but Container Apps is the simpler `azd` template choice when
    we already need an Azure OpenAI dep (and gives zero-scale by default). Picked Container Apps.
  - *Azure Functions* — workflow has long-running orchestrations and human-in-the-loop pauses;
    Container App with always-on minReplicas=1 is simpler than chaining Durable Functions for a
    PoC. Rejected.

## Decision: Configuration via `appsettings.json` + environment variables; `.env` from `azd`

- **Decision**: Strongly-typed options classes bind to `appsettings.json`. Env vars override using
  the standard ASP.NET Core configuration provider (`__` separator). Locally, `azd env get-values`
  populates an `.env` file at the repo root; the API project's launch profile loads it (e.g., via
  `DotNetEnv` or VS Code launch config). The SPA reads `VITE_API_BASE_URL` from its own `.env`
  defaulting to `http://localhost:5000` for local dev.
- **Rationale**: Matches user requirement and constitution Principle II. Avoids per-developer
  secret files in source control.

## Decision: Audit logging via Serilog → console + EF-persisted `AuditEvent`

- **Decision**: Two sinks. Serilog console for operator visibility (and Container App log capture).
  An `AuditWriter` MCP-adjacent service writes structured `AuditEvent` rows used by the UI's
  audit-timeline endpoint. Both are written from a single `RecordEvent(...)` helper called from
  the workflow runner and from each MCP tool implementation.
- **Rationale**: Two audiences (operator and demo viewer) need different views of the same events.
  EF persistence is required by FR-029/FR-030 for UI display; console keeps debugging frictionless.

## Decision: Long-running workflow execution model

- **Decision**: When `POST /api/foiarequests` accepts a request, the API enqueues the workflow on a
  background `IHostedService` worker (single in-process queue). The API returns 202-style behavior
  (200 with the new request's status) immediately. Status polling endpoints read DB state. The
  Human Review Coordinator agent persists "awaiting review" state to the DB and exits its current
  run; the Approval endpoints (`/approve`, `/reject`, `/approve-release`) write decisions to the DB
  and re-enqueue the workflow to resume from the next stage.
- **Rationale**: Microsoft Agent Framework workflows can be long-running, but for a single-instance
  PoC a database-backed "checkpoint at human pauses + resume on event" pattern is the simplest
  reliable approach. No external durable orchestrator dependency.
- **Alternatives considered**:
  - *Block the HTTP request until workflow completes* — breaks the human-review pause and the UI's
    polling story. Rejected.
  - *Azure Durable Functions / Service Bus* — extra infra + extra deployment. Rejected for PoC.

## Decision: Validation in the API layer with explicit per-field error responses

- **Decision**: A dedicated `FoiaRequestValidator` (manual implementation, no FluentValidation
  dependency unless trivially small) checks the rules from FR-001..FR-006 before any persistence
  or workflow start. Returns HTTP 400 with `{ "errors": { "fieldName": ["message"] } }` shape so
  the React form can render inline errors per FR-006.
- **Rationale**: Spec mandates per-field error messages; standard ASP.NET Core `ValidationProblem`
  shape is well-understood by SPAs.

---

## Open Items

None. All decisions are concrete enough to proceed to Phase 1 design.
