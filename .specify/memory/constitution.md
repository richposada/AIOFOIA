<!--
SYNC IMPACT REPORT
==================
Version change: (none — initial ratification) → 1.0.0
Bump type: MINOR (new constitution established from template)

Modified principles: N/A (initial creation)
Added sections: Core Principles (I–IV), Technology Constraints, Development Workflow, Governance
Removed sections: N/A

Templates reviewed:
  ✅ .specify/templates/plan-template.md  — Constitution Check section is generic; no updates required
  ✅ .specify/templates/spec-template.md  — Tests already marked optional; aligned with Principle III
  ✅ .specify/templates/tasks-template.md — Tests already marked OPTIONAL; aligned with Principle III

Deferred TODOs: none
Suggested next step: Run /speckit.specify to produce the first feature specification.
-->

# FOIA Agent Constitution

## Core Principles

### I. Simplicity-First
Every design choice MUST optimize for simplicity and speed of delivery. Flat structure is preferred over
layered architecture. Configuration is preferred over code. No enterprise patterns, abstractions, or
features are permitted unless demonstrably required by the demo. YAGNI (You Ain't Gonna Need It) is
strictly enforced. When two approaches are equivalent in outcome, always choose the simpler one.

### II. Azure-Ready Deployment
The application MUST be deployable to Azure using `azd` (Azure Developer CLI) with a single command
(`azd up`). Local development MUST function against deployed Azure services using environment variable
configuration (`.env` or `local.settings.json`). No mocking of Azure services — use real deployed
services from day one. Infrastructure MUST be defined as code using Bicep (managed by `azd`).

### III. Demo-Focused Scope
Every feature MUST serve a demonstrable, end-to-end user journey in the demo. Production concerns are
explicitly out of scope: authentication hardening, SLAs, data retention policies, compliance controls,
and scalability optimizations MUST NOT be implemented unless directly required by the demo narrative.
Unit tests are NOT required. Manual demo walk-through is the acceptance gate. The app exists to
showcase AI agent capability, not to serve production workloads.

### IV. Agent-Orchestration Core
The application is built around an AI agent that orchestrates FOIA (Freedom of Information Act) request
processing. The agent pattern — receive request → reason → invoke tools → respond — is the central
architectural element. All supporting services, tools, and APIs exist to serve the agent. The agent
MUST be the primary interaction point visible during the demo.

## Technology Constraints

- **AI/LLM**: Azure OpenAI or Azure AI Foundry — whichever requires fewer deployment steps
- **Infrastructure**: Azure Developer CLI (`azd`) with Bicep; single `azd up` deployment target
- **Hosting**: Azure Container Apps or Azure Functions — choose whichever is simpler for the scenario
- **Storage**: Azure Blob Storage and/or Azure Cosmos DB, added only when the demo requires persistence
- **Local Dev**: `.env` file populated via `azd env get-values`; no local infrastructure required
- **Testing**: Unit tests are NOT required; demo walk-through is the only acceptance gate

## Development Workflow

1. Constitution (this file) defines non-negotiable guardrails for all implementation decisions.
2. Feature specifications (`/speckit.specify`) define demo user journeys and acceptance criteria.
3. Plans (`/speckit.plan`) define the concrete implementation approach and file structure.
4. Tasks (`/speckit.tasks`) break work into parallelizable implementation units.
5. Implementation (`/speckit.implement`) delivers the code against the plan.

No formal PR review is required for PoC work. Commit directly to `main` or short-lived feature branches.

## Governance

This constitution governs all implementation decisions for the FOIA Agent proof-of-concept. It supersedes
any default framework conventions or tooling defaults that conflict with these principles.

When a proposed approach conflicts with multiple principles, **Simplicity-First (Principle I)** is the
tiebreaker — always choose the simpler path.

Amendments require: (a) written justification added to the Sync Impact Report comment block above,
(b) a semantic version bump per the rules below, and (c) `Last Amended` date updated to today.

- **MAJOR**: A principle is removed, renamed, or redefined in a backward-incompatible way.
- **MINOR**: A new principle or section is added, or existing guidance is materially expanded.
- **PATCH**: Clarifications, wording improvements, or typo fixes with no semantic change.

**Version**: 1.0.0 | **Ratified**: 2026-04-28 | **Last Amended**: 2026-04-28
