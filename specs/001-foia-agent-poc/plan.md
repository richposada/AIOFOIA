# Implementation Plan: FOIA Agent Proof-of-Concept

**Branch**: `001-foia-agent-poc` | **Date**: 2026-04-28 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/001-foia-agent-poc/spec.md`

## Summary

Demonstrate a multi-agent FOIA workflow: a React TypeScript UI submits a request to a .NET 8 Web API
that orchestrates five agents — Intake Validation, Search, PII Redaction, Human Review Coordinator,
and Packaging & Release — using Microsoft Agent Framework. Agents reach side-effect systems only
through bounded MCP tool surfaces (case store, Azure AI Search, redaction, review, blob storage).
Persistence is local SQLite for demo simplicity. Released ZIPs land in Azure Blob Storage and are
shared via a SAS URL surfaced in the UI. Optimized for `azd up` deployment and local-against-cloud
demoing per the constitution.

## Technical Context

**Language/Version**: C# / .NET 8 (backend, agents, MCP tools); TypeScript 5.x with React 18 (frontend)
**Primary Dependencies**:

- Backend: ASP.NET Core 8 Web API, Microsoft Agent Framework, Azure.Search.Documents,
  Azure.Storage.Blobs, Azure.AI.OpenAI (for AI-assisted name/address PII), EF Core 8 + SQLite,
  Serilog (console sink)
- MCP: ModelContextProtocol C# SDK (in-process tool host for the PoC; one logical MCP server per
  bounded capability — Case, Search, Redaction, Review, BlobStorage)
- Frontend: React 18 + Vite + TypeScript, React Router, fetch-based API client, minimal CSS (no UI kit)
- IaC/Deploy: Azure Developer CLI (`azd`) + Bicep
**Storage**:
- Local relational store: SQLite file (`foia.db`) via EF Core
- Document index (read-only consumer): Azure AI Search (existing, populated)
- Release artifacts: Azure Blob Storage (single configured container)
**Testing**: None. Per constitution Principle III, unit tests are NOT required; the demo walk-through
is the acceptance gate.
**Target Platform**: Cross-platform .NET 8 (Windows demo machine + Azure Container Apps in the cloud).
React app served as static assets locally via Vite, hosted behind the API in Azure (single Container
App with API + static SPA).
**Project Type**: Web application (React frontend + .NET API + agent/MCP libraries + EF data
project), with Bicep + `azd` deployment.
**Performance Goals**: Demo-grade. Reach "Pending Human Review" within 60 s for ≤10 documents
(SC-002). Audit events visible within 5 s of occurrence (SC-007). No throughput targets.
**Constraints**:
- Single `azd up` deployment, single concurrent demo user, single implicit reviewer identity.
- Agents MUST NOT call infrastructure SDKs directly — only via MCP tool boundaries.
- Local dev MUST work against deployed Azure services using `.env` produced by `azd env get-values`
  (consumed by both the API via `appsettings.json` overrides + env vars, and the SPA via Vite env).
- No mocking of Azure services (constitution Principle II).
**Scale/Scope**: 1 reviewer · ≤10 documents/request · ≤25 requests/demo session · 5 agents · 5
logical MCP tool surfaces · 6 entities · ~8 REST endpoints · 5 UI screens.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Plan compliance |
|---|---|
| **I. Simplicity-First** | Flat solution, no DDD/CQRS, no Mediator, no repositories beyond EF `DbContext`. SQLite (not Azure SQL) for demo persistence. MCP tool implementations are in-process (one ASP.NET host wires them up); separate-process MCP servers are explicitly deferred unless the framework requires them. No auth, no caching layer, no message bus. |
| **II. Azure-Ready Deployment** | Single `azd up` → Bicep provisions Azure AI Search index reference, Storage account/container, Azure Container Apps env, Container App for API+SPA, Azure OpenAI deployment for AI-assisted PII detection, and Log Analytics. Local dev pulls config via `azd env get-values > .env`. No mocks. |
| **III. Demo-Focused Scope** | No unit tests authored. Acceptance gate is the demo walk-through. Production hardening (auth, RBAC, retention, multi-tenant, full PDF redaction) explicitly out of scope per spec Assumptions and FR scope. |
| **IV. Agent-Orchestration Core** | Microsoft Agent Framework is the central orchestration mechanism. Each workflow stage is an agent; cross-cutting side effects only through MCP tools. The agent run is the primary thing visible in the UI status timeline. |

**Result**: PASS. No deviations require entries in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/001-foia-agent-poc/
├── plan.md              # This file
├── spec.md              # Feature specification (already complete)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (REST + MCP tool contracts)
│   ├── rest-api.md
│   └── mcp-tools.md
├── checklists/
│   └── requirements.md  # already complete
└── tasks.md             # produced by /speckit.tasks (NOT here)
```

### Source Code (repository root)

```text
src/
├── FoiaProcessor.Api/                 # ASP.NET Core 8 Web API; hosts agent orchestrator + MCP tool host
│   ├── Controllers/                   # FoiaRequestsController, DocumentsController
│   ├── Dtos/                          # request/response DTOs
│   ├── Validation/                    # FluentValidation-style validators (or manual validators)
│   ├── Orchestration/                 # WorkflowRunner — Agent Framework wiring
│   ├── Configuration/                 # strongly-typed options
│   ├── Program.cs
│   ├── appsettings.json
│   └── appsettings.Development.json
├── FoiaProcessor.Agents/              # Microsoft Agent Framework agents
│   ├── IntakeValidationAgent.cs
│   ├── SearchAgent.cs
│   ├── RedactionAgent.cs
│   ├── HumanReviewCoordinatorAgent.cs
│   ├── PackagingReleaseAgent.cs
│   └── WorkflowDefinition.cs          # composes agents into the pipeline
├── FoiaProcessor.McpTools/            # MCP tool implementations (in-process, registered with the API host)
│   ├── Case/                          # case_create, case_get, case_update_status, ...
│   ├── Search/                        # search_documents, get_document_by_id, ...
│   ├── Redaction/                     # detect_pii, redact_text, create_redaction_report, ...
│   ├── Review/                        # create_review_task, record_document_approval, ...
│   ├── BlobStorage/                   # create_zip_package, upload_to_blob_storage, generate_sas_url
│   └── ToolHostExtensions.cs          # AddFoiaMcpTools(...) DI registration
├── FoiaProcessor.Data/                # EF Core 8 + SQLite
│   ├── FoiaDbContext.cs
│   ├── Entities/                      # FoiaRequest, Document, Redaction, ReviewTask, ReleasePackage, AuditEvent
│   └── Migrations/
└── foia-processor-ui/                 # React + Vite + TypeScript SPA
    ├── src/
    │   ├── pages/                     # SubmitRequest, RequestStatus, HumanReview, DocumentReview, ReleasePackage
    │   ├── components/                # SideBySideDiff, RedactionList, AuditTimeline, ...
    │   ├── api/                       # typed fetch client (mirrors REST contracts)
    │   ├── types/                     # TS types mirroring DTOs
    │   └── main.tsx
    ├── index.html
    ├── package.json
    └── vite.config.ts

infra/                                  # Bicep modules used by azd
├── main.bicep
├── main.parameters.json
└── modules/
    ├── containerApp.bicep
    ├── storage.bicep
    ├── search.bicep                    # references existing AI Search; or provisions empty index
    └── openai.bicep
azure.yaml                              # azd service mapping (api → Container App, web → bundled)
.env.sample                             # produced manually; .env is generated by azd env get-values
README.md                               # demo-runner README
```

**Structure Decision**: Web application layout. The .NET solution has four projects under `src/`
(`Api`, `Agents`, `McpTools`, `Data`) plus the React app at `src/foia-processor-ui/`. The API
project is the composition root: it hosts controllers, the Microsoft Agent Framework workflow
runner, and the in-process MCP tool registrations. This keeps the PoC to a single deployable
container while preserving clear boundaries between orchestration (`Agents`) and side effects
(`McpTools`/`Data`), satisfying constitution Principle IV without the operational overhead of
separate MCP server processes.

## Complexity Tracking

> No constitution violations to justify. Section intentionally empty.
