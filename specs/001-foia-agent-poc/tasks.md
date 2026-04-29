# Tasks: FOIA Agent Proof-of-Concept

**Input**: Design documents from `/specs/001-foia-agent-poc/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/rest-api.md, contracts/mcp-tools.md, quickstart.md

**Tests**: NOT included. Constitution Principle III explicitly excludes unit testing from this PoC.

**Organization**: Tasks are grouped by user story (US1–US4) so each story can be implemented and demoed independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: User story label (US1, US2, US3, US4) for story-phase tasks
- File paths are workspace-relative (root: `foia-agent/`)

## Path Conventions

Paths follow [plan.md](plan.md) → "Project Structure":
- Backend: `src/FoiaProcessor.Api/`, `src/FoiaProcessor.Agents/`, `src/FoiaProcessor.McpTools/`, `src/FoiaProcessor.Data/`
- Frontend: `src/foia-processor-ui/`
- Infra: `infra/`, `azure.yaml` at repo root

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create solution scaffolding, project files, and dependency graph.

- [X] T001 Create .NET solution at [foia-agent.sln](foia-agent.sln) and four C# projects: `src/FoiaProcessor.Api` (web, .NET 8), `src/FoiaProcessor.Agents` (classlib), `src/FoiaProcessor.McpTools` (classlib), `src/FoiaProcessor.Data` (classlib); wire `Api → Agents, McpTools, Data`, `Agents → McpTools, Data`, `McpTools → Data`.
- [X] T002 [P] Add NuGet packages to `src/FoiaProcessor.Api/FoiaProcessor.Api.csproj`: `Microsoft.AspNetCore.OpenApi`, `Swashbuckle.AspNetCore`, `Serilog.AspNetCore`, `DotNetEnv`, `Microsoft.Extensions.Hosting`.
- [X] T003 [P] Add NuGet packages to `src/FoiaProcessor.Agents/FoiaProcessor.Agents.csproj`: `Microsoft.Agents.AI`, `Microsoft.Agents.AI.OpenAI`, `Azure.AI.OpenAI`, `Microsoft.Extensions.AI`.
- [X] T004 [P] Add NuGet packages to `src/FoiaProcessor.McpTools/FoiaProcessor.McpTools.csproj`: `ModelContextProtocol`, `Azure.Search.Documents`, `Azure.Storage.Blobs`, `Azure.Identity`, `System.IO.Compression`.
- [X] T005 [P] Add NuGet packages to `src/FoiaProcessor.Data/FoiaProcessor.Data.csproj`: `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Sqlite`.
- [X] T006 [P] Scaffold the React + Vite + TypeScript SPA at `src/foia-processor-ui/` (`npm create vite@latest . -- --template react-ts`); add `react-router-dom`; remove default boilerplate styling.
- [X] T007 [P] Create [.editorconfig](.editorconfig), [.gitignore](.gitignore) (covers `bin/`, `obj/`, `node_modules/`, `dist/`, `*.db`, `.env`, `.azure/`), and update root [README.md](README.md) with one-paragraph PoC overview pointing to [specs/001-foia-agent-poc/quickstart.md](specs/001-foia-agent-poc/quickstart.md).
- [X] T008 [P] Create [azure.yaml](azure.yaml) declaring one service `foia-api` (host: `containerapp`, project: `src/FoiaProcessor.Api`, language: `dotnet`, docker: `./Dockerfile`).
- [X] T009 [P] Create [src/FoiaProcessor.Api/Dockerfile](src/FoiaProcessor.Api/Dockerfile) — multi-stage: stage 1 `node:20-alpine` builds the SPA (`npm ci && npm run build`); stage 2 `mcr.microsoft.com/dotnet/sdk:8.0` publishes the API; stage 3 `mcr.microsoft.com/dotnet/aspnet:8.0` runs the API and copies SPA `dist/` into `/app/wwwroot`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain model, persistence, configuration, MCP host, agent runner skeleton, and Bicep — everything every user story will reuse.

**⚠️ CRITICAL**: All user-story phases depend on this phase.

### Data layer

- [X] T010 Create entity classes per [data-model.md](specs/001-foia-agent-poc/data-model.md) in `src/FoiaProcessor.Data/Entities/`: `FoiaRequest.cs`, `Document.cs`, `Redaction.cs`, `ReviewTask.cs`, `ReleasePackage.cs`, `AuditEvent.cs`.
- [X] T011 Create enum types in `src/FoiaProcessor.Data/Entities/Enums.cs`: `RequestStatus`, `RedactionStatus`, `ReviewStatus`, `ReviewTaskStatus`, `PiiType`, `DetectionSource`, `AuditEventType`.
- [X] T012 Create `src/FoiaProcessor.Data/FoiaDbContext.cs` with `DbSet<>`s for all six entities, string-converter mappings for every enum, and the indexes from [data-model.md](specs/001-foia-agent-poc/data-model.md) → "Indexes".
- [X] T013 Add `AddFoiaData(IServiceCollection, IConfiguration)` extension in `src/FoiaProcessor.Data/ServiceCollectionExtensions.cs` that registers `FoiaDbContext` with the SQLite provider using `ConnectionStrings:Sqlite`.

### Configuration

- [X] T014 [P] Create options classes in `src/FoiaProcessor.Api/Options/`: `AzureOpenAIOptions.cs`, `AzureSearchOptions.cs` (including nested `Fields` map), `AzureBlobStorageOptions.cs`, `WorkflowOptions.cs`. Bind in `Program.cs` later.
- [X] T015 [P] Create [src/FoiaProcessor.Api/appsettings.json](src/FoiaProcessor.Api/appsettings.json) and `appsettings.Development.json` with the keys listed in [quickstart.md](specs/001-foia-agent-poc/quickstart.md) → "Configuration reference"; leave secret values empty.

### Audit + logging infrastructure

- [X] T016 Create `src/FoiaProcessor.Data/Audit/IAuditWriter.cs` and `AuditWriter.cs` exposing `Task RecordAsync(Guid foiaRequestId, AuditEventType type, string message, Guid? relatedDocumentId = null, CancellationToken ct = default)`; persists an `AuditEvent` row and emits a structured Serilog log line tagged with `RequestId`.
- [X] T017 Configure Serilog in `src/FoiaProcessor.Api/Program.cs`: console sink, `RequestId` enricher, minimum level Information.

### MCP tool host

- [X] T018 Create `src/FoiaProcessor.McpTools/Hosting/McpToolHostExtensions.cs` exposing `AddFoiaMcpTools(IServiceCollection)` that registers an in-process MCP host containing all five logical servers (Case, Search, Redaction, Review, BlobStorage). Servers are registered as keyed/named tool collections so agents can request the right surface.
- [X] T019 Create skeleton tool classes (method signatures from [contracts/mcp-tools.md](specs/001-foia-agent-poc/contracts/mcp-tools.md), bodies throwing `NotImplementedException`):
  - `src/FoiaProcessor.McpTools/Servers/CaseServer.cs`
  - `src/FoiaProcessor.McpTools/Servers/SearchServer.cs`
  - `src/FoiaProcessor.McpTools/Servers/RedactionServer.cs`
  - `src/FoiaProcessor.McpTools/Servers/ReviewServer.cs`
  - `src/FoiaProcessor.McpTools/Servers/BlobStorageServer.cs`
  Each exposes `[McpTool]`-decorated methods matching the contract.
- [X] T020 Create DTO records used by tool inputs/outputs in `src/FoiaProcessor.McpTools/Contracts/` (one file per server, e.g., `CaseContracts.cs`, `SearchContracts.cs`, …).

### Agent framework skeleton

- [X] T021 Create `src/FoiaProcessor.Agents/Agents/` skeleton classes (one per agent, no logic yet): `IntakeValidationAgent.cs`, `SearchAgent.cs`, `RedactionAgent.cs`, `HumanReviewCoordinatorAgent.cs`, `PackagingReleaseAgent.cs`. Each has a constructor taking the MCP tool surfaces it needs and a single `RunAsync(Guid requestId, CancellationToken ct)` entry point.
- [X] T022 Create `src/FoiaProcessor.Agents/Workflow/FoiaWorkflowRunner.cs` that orchestrates the five agents in order, persists status transitions via `case_update_status`, and supports a "pause at human review / resume from DB checkpoint" pattern as documented in [research.md](specs/001-foia-agent-poc/research.md) → "Long-running workflow execution model".
- [X] T023 Create `src/FoiaProcessor.Agents/Workflow/WorkflowQueue.cs` (in-memory `Channel<Guid>` queue) and `WorkflowHostedService.cs` (`IHostedService` consuming the channel, calling `FoiaWorkflowRunner.RunAsync`).

### API host wiring

- [X] T024 Wire `src/FoiaProcessor.Api/Program.cs`: bind options classes (T014), `AddFoiaData` (T013), `AddFoiaMcpTools` (T018), register agents + runner + queue + hosted service (T021–T023), `AddControllers`, `AddEndpointsApiExplorer`, `AddSwaggerGen`. Configure middleware: Serilog request logging, exception handler, `MapControllers`, static-file serving from `wwwroot/` with SPA fallback to `index.html`.
- [X] T025 Add `EnsureCreated()` startup hook in `Program.cs` against `FoiaDbContext` so SQLite file is created on first run.

### Infrastructure as code

- [X] T026 Create [infra/main.bicep](infra/main.bicep) provisioning: Log Analytics workspace, Container Apps environment, Azure Container Registry, Container App `foia-api` (managed identity, env vars from outputs), Storage account + `foia-releases` container, Azure OpenAI account + `gpt-4o-mini` deployment.
- [X] T027 [P] Create [infra/main.parameters.json](infra/main.parameters.json) sourcing values from `azd` env (`environmentName`, `location`, `azureSearchEndpoint`, `azureSearchIndexName`, `azureSearchApiKey`).
- [X] T028 [P] Create [infra/abbreviations.json](infra/abbreviations.json) (azd-standard) and a small `infra/modules/containerApp.bicep` if T026 grows large.
- [X] T029 In [infra/main.bicep](infra/main.bicep) grant the Container App's managed identity the Storage Blob Data Contributor + Storage Blob Delegator roles on the storage account (required for user-delegation SAS).

**Checkpoint**: Solution builds (`dotnet build`), SPA builds (`npm run build`), `azd provision` would succeed. No user-story logic yet.

---

## Phase 3: User Story 1 — Submit a FOIA Request and Watch the Workflow Run (Priority: P1) 🎯 MVP

**Goal**: A FOIA officer submits a request via the SPA, validation runs, AI Search returns matching documents, and the status screen reflects the live state.

**Independent Test**: Submit a valid request with the sample payload from [quickstart.md](specs/001-foia-agent-poc/quickstart.md). The status screen progresses through `Submitted → Validated → Searching → DocumentsFound` (or `NoDocumentsFound`), and `GET /api/foiarequests/{id}` returns the matching `counts.documentsFound`.

### Backend — DTOs, validator, controller (intake)

- [X] T030 [US1] Create request DTOs in `src/FoiaProcessor.Api/Contracts/FoiaRequestContracts.cs`: `SubmitFoiaRequestDto`, `SubmitFoiaRequestResponseDto`, `FoiaRequestStatusDto` (with nested `CountsDto`, `ReleaseDto`, `AuditEventDto`) per [contracts/rest-api.md](specs/001-foia-agent-poc/contracts/rest-api.md) #1 and #2.
- [X] T031 [US1] Create `src/FoiaProcessor.Api/Validation/FoiaRequestValidator.cs` enforcing rules from [data-model.md](specs/001-foia-agent-poc/data-model.md) → "Validation Rules"; returns `IDictionary<string, string[]>` for the `errors` dictionary.
- [X] T032 [US1] Create `src/FoiaProcessor.Api/Controllers/FoiaRequestsController.cs` with `POST /api/foiarequests` and `GET /api/foiarequests/{id}` endpoints. Submit endpoint validates → calls `case_create` tool → enqueues workflow via `WorkflowQueue.Enqueue(id)` → returns 200 per contract.

### MCP — Case server (write side) + Search server

- [X] T033 [P] [US1] Implement `CaseServer.case_create`, `case_get`, `case_update_status`, `case_add_note`, `case_save_document_metadata` against `FoiaDbContext` and `IAuditWriter`. (Other Case methods deferred to US2/US3.)
- [X] T034 [P] [US1] Implement `SearchServer` (all four tools) using `Azure.Search.Documents.SearchClient` with field-name resolution from `AzureSearchOptions.Fields`. Returns DTOs from [contracts/mcp-tools.md](specs/001-foia-agent-poc/contracts/mcp-tools.md) → Server 2.

### Agents

- [X] T035 [US1] Implement `IntakeValidationAgent`: loads case via `case_get`, re-asserts validation rules (defense in depth), records `RequestValidated`, transitions to `Validated`. On failure → `Rejected` with reason in audit event.
- [X] T036 [US1] Implement `SearchAgent`: builds query from request `Subject` + `Description` + date range, calls `search_documents`, fetches content per result via `get_document_content`, calls `case_save_document_metadata`, transitions status to `DocumentsFound` or `NoDocumentsFound`. Configurable `top` from `WorkflowOptions:MaxSearchResults`.
- [X] T037 [US1] Wire US1 agents into `FoiaWorkflowRunner` so a freshly enqueued request runs Intake → Search and stops (US2/US3 stages remain TODO at this checkpoint).

### Frontend — submit + status screens

- [X] T038 [P] [US1] Create `src/foia-processor-ui/src/api/client.ts` with typed `submitFoiaRequest` and `getFoiaRequestStatus` functions reading `VITE_API_BASE_URL`.
- [X] T039 [P] [US1] Create `src/foia-processor-ui/src/types.ts` with TypeScript types matching the REST DTOs (mirror [contracts/rest-api.md](specs/001-foia-agent-poc/contracts/rest-api.md) shapes).
- [X] T040 [US1] Create `src/foia-processor-ui/src/pages/SubmitRequestPage.tsx` with a form for all fields from [contracts/rest-api.md](specs/001-foia-agent-poc/contracts/rest-api.md) #1, inline per-field error rendering from `errors` response, and navigation to the status screen on success.
- [X] T041 [US1] Create `src/foia-processor-ui/src/pages/StatusPage.tsx` showing current `status`, `counts`, audit-event timeline, with 2-second polling while status is non-terminal.
- [X] T042 [US1] Create `src/foia-processor-ui/src/App.tsx` with `BrowserRouter` and routes `/` → SubmitRequestPage, `/requests/:id` → StatusPage; minimal `app.css` styling.
- [X] T043 [US1] Configure Vite dev proxy in `src/foia-processor-ui/vite.config.ts` so `/api/*` proxies to `http://localhost:5000` during `npm run dev`.

**Checkpoint**: Submit a request via the SPA. Status reaches `DocumentsFound` (or `NoDocumentsFound`) and the audit timeline displays the workflow steps. **MVP-1 complete and demoable.**

---

## Phase 4: User Story 2 — Human Reviewer Approves Redactions Side-by-Side (Priority: P1)

**Goal**: After search, redactions are produced and a reviewer approves/rejects each document side-by-side.

**Independent Test**: For a request that reached `DocumentsFound`, the workflow continues into `Redacting → PendingHumanReview`. The review UI shows original vs. redacted content with redactions highlighted. Approving every document leaves status at `PendingHumanReview` with all `documentsApproved` matching `documentsFound`. Rejecting a document moves it to `ManualHandling` and excludes it from later release counts.

### MCP — Redaction + Review servers + remaining Case writes

- [ ] T044 [US2] Implement `RedactionServer.detect_pii` with two paths: regex detectors for `Email`, `Phone`, `SSN`, `DateOfBirth`; Azure-OpenAI prompt-based detection (using `WorkflowOptions` flag) for `Name`, `Address`, `FinancialId`. Each finding carries `detectionSource` and `confidence` per [contracts/mcp-tools.md](specs/001-foia-agent-poc/contracts/mcp-tools.md).
- [X] T045 [US2] Implement `RedactionServer.redact_text`, `create_redaction_report`, `save_redacted_document` (the last delegates to `case_save_redaction_results`).
- [X] T046 [US2] Implement `CaseServer.case_save_redaction_results` and `case_save_review_decision` (extending the class started in T033).
- [X] T047 [US2] Implement `ReviewServer.create_review_task`, `get_review_status`, `record_document_approval`, `record_document_rejection`. Uses `FoiaDbContext` + `IAuditWriter`.

### Agents

- [X] T048 [US2] Implement `RedactionAgent`: for each Document, calls `detect_pii` → `redact_text` → `save_redacted_document`; transitions request to `Redacting` then to `PendingHumanReview` after the last document is redacted; calls `create_review_task`.
- [X] T049 [US2] Implement `HumanReviewCoordinatorAgent.RunAsync`: checks per-document review statuses via `get_review_status`. If any document is still `Pending`, persist "awaiting review" and exit (workflow runner returns; resume happens when reviewer acts). When all documents are `Approved`, do nothing and let the explicit release-approval endpoint resume packaging.
- [X] T050 [US2] Extend `FoiaWorkflowRunner` (T022) to chain US1 stages → `RedactionAgent` → `HumanReviewCoordinatorAgent`, honouring the pause/resume semantics.

### REST endpoints

- [X] T051 [US2] Add to `FoiaRequestsController`: `GET /api/foiarequests/{id}/documents` (returns the per-document summary list per [contracts/rest-api.md](specs/001-foia-agent-poc/contracts/rest-api.md) #3).
- [X] T052 [US2] Create `src/FoiaProcessor.Api/Controllers/DocumentsController.cs` with `GET /api/documents/{id}/review`, `POST /api/documents/{id}/approve`, `POST /api/documents/{id}/reject`. Approve/reject call the corresponding `ReviewServer` tools and re-enqueue the workflow when state changes warrant it (e.g., after the last approval).

### Frontend

- [X] T053 [P] [US2] Extend `src/foia-processor-ui/src/api/client.ts` with `getDocuments`, `getDocumentReview`, `approveDocument`, `rejectDocument` calls.
- [X] T054 [US2] Create `src/foia-processor-ui/src/pages/ReviewListPage.tsx` (`/requests/:id/review`) listing each document with its `redactionStatus`, `reviewStatus`, and a "Review" button.
- [X] T055 [US2] Create `src/foia-processor-ui/src/pages/DocumentReviewPage.tsx` (`/documents/:id/review`) showing original (left) vs. redacted (right) text with redactions highlighted (use `<mark>` spans driven by `startOffset`/`endOffset`). Approve / Reject buttons; Reject requires a comment.
- [ ] T056 [US2] Add routes in `App.tsx` for the two new pages and a "Review Documents" CTA on the StatusPage that appears when status is `PendingHumanReview`.

**Checkpoint**: Reviewer can approve/reject every document. Per-document and per-request states reflect decisions. Workflow remains paused at `PendingHumanReview` until release approval.

---

## Phase 5: User Story 3 — Generate and Share the Released ZIP via Shareable URL (Priority: P1)

**Goal**: After release approval, packaging produces a ZIP in Blob Storage and the UI displays a SAS URL.

**Independent Test**: With every document Approved, click "Approve Release". Workflow proceeds `ApprovedForRelease → Packaging → ReleasePackageReady`. `GET /api/foiarequests/{id}/release` returns a `Ready` payload with a working SAS URL whose download is a ZIP containing only Approved documents' redacted content.

### MCP — BlobStorage server + remaining Case write

- [X] T057 [US3] Implement `BlobStorageServer.create_zip_package` building an in-memory `ZipArchive` from the supplied (filename, content) tuples.
- [X] T058 [US3] Implement `BlobStorageServer.upload_to_blob_storage` (auto-creates container) and `get_release_package_status`. Use `BlobServiceClient` with `DefaultAzureCredential` in Azure, falling back to `AzureBlobStorageOptions.ConnectionString` when present (local dev).
- [X] T059 [US3] Implement `BlobStorageServer.generate_sas_url` with the dual code path described in [research.md](specs/001-foia-agent-poc/research.md) → "Azure Blob Storage with user-delegation SAS": user-delegation SAS via managed identity in Azure, account-key SAS locally. Honour `AzureBlobStorageOptions.SasExpirationDays` (default 7).
- [X] T060 [US3] Implement `CaseServer.case_save_release_package` (extending the class from T033/T046) and `ReviewServer.record_release_approval` (extending T047).

### Agent

- [X] T061 [US3] Implement `PackagingReleaseAgent`: loads all Approved documents, calls `create_zip_package`, `upload_to_blob_storage`, `generate_sas_url`, `case_save_release_package`, transitions status `Packaging → ReleasePackageReady`.
- [X] T062 [US3] Extend `FoiaWorkflowRunner` (T050) so resumes triggered by the release-approval endpoint run `PackagingReleaseAgent` after `ApprovedForRelease`.

### REST endpoints

- [X] T063 [US3] Add to `FoiaRequestsController`: `POST /api/foiarequests/{id}/approve-release` and `GET /api/foiarequests/{id}/release` per [contracts/rest-api.md](specs/001-foia-agent-poc/contracts/rest-api.md) #7 and #8. Approve-release returns 409 if any document is not Approved (call `get_review_status` to check), otherwise transitions status, calls `record_release_approval`, and re-enqueues the workflow.

### Frontend

- [X] T064 [P] [US3] Extend `src/foia-processor-ui/src/api/client.ts` with `approveRelease` and `getRelease` calls.
- [X] T065 [US3] Update `StatusPage.tsx`: when every document is Approved and status is `PendingHumanReview`, enable an "Approve Release" button. When status reaches `ReleasePackageReady`, render: (a) a "Download Release Package" link with the SAS URL, (b) the `sasExpiresAt` timestamp, (c) a one-click "Copy URL" button (per FR-027) that writes the SAS URL to the clipboard via `navigator.clipboard.writeText` and shows a transient "Copied!" confirmation, and (d) when `now > sasExpiresAt`, replace the download link with a message explaining the URL has expired while still showing the original expiration.

**Checkpoint**: Full happy-path demo runs end-to-end from submit through downloadable ZIP. **MVP-3 complete.**

---

## Phase 6: User Story 4 — Inspect Workflow Audit Trail (Priority: P2)

**Goal**: Demo viewers see a chronological, queryable audit log of every workflow event.

**Independent Test**: The status screen shows every audit event from `RequestSubmitted` through `SasUrlGenerated` in chronological order with timestamps, types, and human-readable messages. Per-document events display the related document's filename.

- [X] T066 [US4] Audit the codebase: every state-mutating MCP tool method (Case, Redaction, Review, BlobStorage) and every agent stage transition emits exactly one `IAuditWriter.RecordAsync` call with the appropriate `AuditEventType`. Add the missing calls. Reference list of expected event types: [data-model.md](specs/001-foia-agent-poc/data-model.md) → `AuditEventType`.
- [X] T067 [US4] Extend `FoiaRequestStatusDto.AuditEventDto` (T030) and the `GET /api/foiarequests/{id}` response so audit events include the related document's `fileName` (join in the controller query). Order events ascending by `Timestamp`.
- [X] T068 [US4] Polish the audit-timeline rendering in `StatusPage.tsx`: vertical timeline, event-type badge, message text, related-document filename when present, formatted local-time timestamp.

**Checkpoint**: Audit trail satisfies SC-007 and FR-029/FR-030 for the demo.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T069 [P] Verify error handling: every controller maps unhandled exceptions to a 500 with `traceId`; workflow exceptions transition the request to `Status = Error` and emit an `AuditEvent` of type `Error`.
- [ ] T070 [P] Verify managed-identity flow end-to-end in Azure: `azd up` succeeds, the Container App starts, a submitted request reaches `ReleasePackageReady`, and the SAS URL downloads.
- [ ] T071 [P] Update [specs/001-foia-agent-poc/quickstart.md](specs/001-foia-agent-poc/quickstart.md) "Troubleshooting" table with any new symptoms discovered during integration.
- [ ] T072 Walk through every step in [specs/001-foia-agent-poc/quickstart.md](specs/001-foia-agent-poc/quickstart.md) §4 "Demo flow" against the deployed app; fix any gaps found.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: no dependencies — start immediately.
- **Phase 2 (Foundational)**: depends on Phase 1. **Blocks all user-story phases.**
- **Phase 3 (US1)**: depends on Phase 2.
- **Phase 4 (US2)**: depends on Phase 3 (uses the workflow runner, Case server, controller, and SPA shell created in US1).
- **Phase 5 (US3)**: depends on Phase 4 (release approval requires per-document approvals).
- **Phase 6 (US4)**: depends on Phase 3 minimum; full effect requires Phases 4 + 5 events to exist.
- **Phase 7 (Polish)**: depends on Phases 3–6.

### Within a User Story

- DTOs / contracts → MCP tool implementations → agent implementations → REST controller actions → SPA pages.
- Agents must not be wired into the runner until their tools exist.

### Parallel Opportunities

- Phase 1: T002–T009 all `[P]` — different files.
- Phase 2: T014–T015 `[P]`; T027–T028 `[P]`.
- Phase 3: T033 + T034 `[P]`; T038 + T039 `[P]`.
- Phase 4: T053 `[P]` while backend tasks (T044–T052) run.
- Phase 5: T064 `[P]` while T057–T063 run.
- Phase 7: T069–T071 all `[P]`.

---

## Parallel Example: User Story 1 backend kickoff

```text
# After T030–T032 are done, launch in parallel:
Task: "T033 [US1] Implement CaseServer write methods (case_create, case_get, ...) in src/FoiaProcessor.McpTools/Servers/CaseServer.cs"
Task: "T034 [US1] Implement SearchServer in src/FoiaProcessor.McpTools/Servers/SearchServer.cs"

# Frontend work in parallel with the backend:
Task: "T038 [US1] Create src/foia-processor-ui/src/api/client.ts"
Task: "T039 [US1] Create src/foia-processor-ui/src/types.ts"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete Phase 1 (Setup) and Phase 2 (Foundational).
2. Complete Phase 3 (US1).
3. **Stop and validate**: submit → search succeeds; the demo can show the agent framework + MCP tools handling intake and discovery.
4. Decide whether to continue to US2.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 → submit + search demoable.
3. US2 → human review demoable.
4. US3 → full happy path with downloadable ZIP (the headline demo).
5. US4 → audit trail polished.
6. Polish → demo dry-run.

### Notes

- No unit tests per Constitution Principle III. Manual demo-flow validation (T072) is the only acceptance gate.
- Constitution Principle IV: agents reach side effects ONLY through MCP tools — review every agent task to ensure no direct SDK use.
- Commit after each task or each agent completion for easy rollback during the demo.
- Keep the SPA dependency-light; resist adding a UI framework mid-stream.
