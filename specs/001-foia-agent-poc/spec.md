# Feature Specification: FOIA Agent Proof-of-Concept

**Feature Branch**: `001-foia-agent-poc`
**Created**: 2026-04-28
**Status**: Draft
**Input**: User description: "Build a proof-of-concept FOIA request processing system that demonstrates how a multi-agent orchestration framework can take a FOIA request from intake through search, PII redaction, human review, packaging, and shareable-URL release."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Submit a FOIA Request and Watch the Workflow Run (Priority: P1)

A demo user opens the web app and submits a FOIA request — subject, description, requested document
date range, and contact information. The system validates the request, accepts it, returns a request
ID, and immediately begins an automated multi-agent workflow that walks the request through intake
validation, document search, and PII redaction, then parks it in a "Pending Human Review" state. The
user is taken to a status screen where they can watch the workflow status update.

**Why this priority**: This is the demo's "wow moment" — a single submission kicks off a visible
multi-agent pipeline. Without this, no other story has anything to act on. It is also the smallest
end-to-end slice that proves the orchestration is wired up correctly.

**Independent Test**: Submit a valid FOIA request through the UI; observe a request ID returned and
the status screen progressing automatically through Submitted → Validated → Searching → Documents
Found → Redacting → Pending Human Review without manual intervention.

**Acceptance Scenarios**:

1. **Given** the submission form is open, **When** the user fills in all required fields with valid
   values and submits, **Then** the system returns a request ID and the status screen shows the
   workflow advancing through automated stages.
2. **Given** the submission form is open, **When** the user submits with an empty subject, an invalid
   email, or a start date later than the end date, **Then** the submission is rejected with a clear,
   field-level error and no workflow is started.
3. **Given** a request has been submitted, **When** the search stage finds matching documents,
   **Then** the status screen reports the count of documents found and the workflow advances to
   redaction.
4. **Given** a request has been submitted, **When** the search stage returns zero documents, **Then**
   the status reflects "No Documents Found" and the workflow halts gracefully without entering review.

---

### User Story 2 - Human Reviewer Approves Redactions Side-by-Side (Priority: P1)

A reviewer opens the Human Review screen for a request that has reached "Pending Human Review", picks
a document, and sees a side-by-side view of original content and redacted content along with a list
of every detected redaction (type, original value, replacement, confidence when available). The
reviewer approves or rejects each document's redactions, optionally leaving comments. Once every
document has been approved, the reviewer approves the full release package, advancing the workflow.

**Why this priority**: Human-in-the-loop approval is the core value proposition — agents propose,
humans dispose. Without this story, the system has no gate between automated redaction and release,
which contradicts the workflow's purpose.

**Independent Test**: Given a request already in "Pending Human Review", a reviewer can open each
document, see before/after content and detected redactions, click Approve on each, and then click
"Approve Release" to advance the workflow to packaging.

**Acceptance Scenarios**:

1. **Given** a request is in "Pending Human Review", **When** the reviewer opens a document, **Then**
   original and redacted content are shown side-by-side and every detected redaction is listed with
   its type, original value, replacement, and (when available) confidence score.
2. **Given** the reviewer is viewing a document, **When** they click Approve, **Then** the document's
   review status becomes Approved and the workflow records the approval.
3. **Given** the reviewer is viewing a document, **When** they click Reject and enter a comment,
   **Then** the document's review status becomes "Manual Handling" (i.e., flagged for rework and
   excluded from the release package), the comment is stored, and the document is removed from the
   approvable set.
4. **Given** every document for the request is Approved, **When** the reviewer clicks "Approve
   Release", **Then** the workflow advances to packaging and the request status updates accordingly.
5. **Given** at least one document is Rejected, **When** the reviewer attempts "Approve Release",
   **Then** the action is blocked with a clear message that all documents must be approved first.

---

### User Story 3 - Generate and Share the Released ZIP via Shareable URL (Priority: P1)

After release approval, the system bundles the approved redacted documents into a ZIP, uploads the
ZIP to cloud blob storage, generates a time-limited shareable URL, and surfaces it in the Release
Package screen. The reviewer can copy the URL and verify that opening it returns the ZIP.

**Why this priority**: This is the demo's payoff — a tangible, shareable artifact produced by the
agent workflow. Without it, the demo has no closing beat.

**Independent Test**: Starting from a request that has just received "Approve Release", verify that
the status transitions to "Release Package Ready", a ZIP file name and shareable URL appear in the
UI, the URL is copyable, and downloading from the URL returns a ZIP containing the approved redacted
documents.

**Acceptance Scenarios**:

1. **Given** the reviewer has approved release, **When** packaging completes, **Then** the request
   status becomes "Release Package Ready" and the Release Package screen shows the ZIP file name,
   the shareable URL, and the URL's expiration date/time.
2. **Given** the Release Package screen shows a URL, **When** the reviewer clicks "Copy URL",
   **Then** the URL is placed on the clipboard and visual confirmation is shown.
3. **Given** the shareable URL has been generated, **When** anyone opens it before its expiration,
   **Then** the ZIP downloads successfully and contains the redacted versions of every approved
   document.
4. **Given** packaging or upload fails, **When** the failure occurs, **Then** the request status
   becomes "Error", the failure reason is shown in the UI, and no partial/invalid URL is presented.

---

### User Story 4 - Inspect Workflow Audit Trail (Priority: P2)

While reviewing a request, the user can see a timestamped list of major workflow events for that
request — submission, validation, search start/complete, redaction start/complete, document
approvals/rejections, release approval, packaging, URL generation, and any errors.

**Why this priority**: Audit visibility is what makes the demo feel "real" rather than a black box,
and it differentiates this PoC from a simple form-to-output script. It is P2 because the core demo
flow (P1 stories) can be shown without it.

**Independent Test**: For any submitted request, the status/detail screen displays a chronologically
ordered list of audit events with timestamps and event types covering the full workflow journey.

**Acceptance Scenarios**:

1. **Given** a request has progressed through several workflow stages, **When** the user opens the
   request status screen, **Then** an ordered, timestamped list of audit events for that request is
   visible.
2. **Given** an error occurred at any stage, **When** the user opens the request status screen,
   **Then** the error event appears in the audit list with a human-readable message.

---

### Edge Cases

- Submission with all required fields blank → all field errors shown together; nothing persisted; no
  workflow started.
- Subject submitted with only whitespace → treated as empty; rejected with the same error as a
  missing subject.
- Email field contains a syntactically invalid address → rejected with a "valid email required" error.
- Start date after end date → rejected with a "start date must be on or before end date" error.
- Search returns zero documents → workflow halts in a terminal "No Documents Found" state; reviewer
  does not see a review screen for that request; status is clearly explained.
- Search service is unavailable → request transitions to "Error" with a clear message; no review or
  packaging is attempted.
- A document fails redaction → that document is flagged for manual handling; the rest of the workflow
  continues so the reviewer can still progress other documents.
- Reviewer rejects one document and leaves the request → "Approve Release" remains blocked until the
  rejected document is resolved.
- Browser refresh mid-workflow → reopening the request status page shows the current real state from
  the backend (no client-only state is lost).
- Shareable URL is opened after its expiration → download is denied by the storage layer; the UI
  still shows the original expiration so the user understands why.
- Two browser tabs open on the same review document and both approve → second approval is a no-op
  (the document is already approved); no error is shown to the user.

## Requirements *(mandatory)*

### Functional Requirements

**Request intake & validation**

- **FR-001**: System MUST provide a submission form capturing subject, description, requested document
  start date, requested document end date, requestor full name, requestor organization (optional),
  requestor email, requestor phone (optional), and requestor mailing address (optional).
- **FR-002**: System MUST reject any submission missing subject, start date, end date, requestor full
  name, or requestor email, returning per-field error messages.
- **FR-003**: System MUST reject any submission whose email is not a valid email address format.
- **FR-004**: System MUST reject any submission whose start date is after its end date.
- **FR-005**: System MUST assign every accepted request a unique identifier and persist the request
  with an initial status of "Submitted".
- **FR-006**: System MUST surface validation errors in the UI inline with the offending fields
  before any workflow is started.

**Workflow orchestration**

- **FR-007**: Upon successful validation, the system MUST automatically start a multi-step workflow
  consisting of: intake validation → document search → PII redaction → human review → release
  approval → packaging & release.
- **FR-008**: The system MUST update and persist the request's status as it transitions through
  these stages: Submitted, Validated, Searching, Documents Found, No Documents Found, Redacting,
  Pending Human Review, Approved for Release, Packaging, Release Package Ready, Rejected, Error.
- **FR-009**: The system MUST NOT advance to packaging or generate any shareable URL until a human
  reviewer has explicitly approved release for the request.
- **FR-010**: If any stage fails, the system MUST set the request status to "Error", record the
  failure reason, and stop further automated progression for that request.

**Document search**

- **FR-011**: The search stage MUST query the configured document index using the request's subject,
  description, and requested date range.
- **FR-012**: The search stage MUST return, for each matching document, at minimum: document
  identifier, file name, file type, source location/reference, a content snippet, and any available
  document date.
- **FR-013**: The number of documents returned per request MUST be capped by a configurable maximum
  (default 10).
- **FR-014**: If zero documents are returned, the request MUST be moved to a terminal "No Documents
  Found" state without entering redaction or review.

**PII redaction**

- **FR-015**: The redaction stage MUST scan returned documents for PII covering at least: email
  addresses, phone numbers, government identification numbers (e.g., SSN), dates of birth, person
  names, mailing addresses, and financial account numbers when present.
- **FR-016**: For each detected PII instance, the system MUST record the original value, the
  replacement label, the PII type, location within the document where applicable, and a confidence
  score when available.
- **FR-017**: The system MUST produce a redacted version of each document in which detected PII is
  replaced with a category-specific label (e.g., "[REDACTED EMAIL]", "[REDACTED NAME]") and MUST
  preserve the original version alongside it.
- **FR-018**: The system MUST mark documents as "Pending Human Review" once redaction completes.

**Human review**

- **FR-019**: The system MUST present, for each document, a side-by-side view of original content
  and redacted content along with a list of every detected redaction (type, original value,
  replacement, confidence when available).
- **FR-020**: A reviewer MUST be able to approve a document's redactions, recording who approved and
  when.
- **FR-021**: A reviewer MUST be able to reject a document's redactions and record a free-text
  comment; rejected documents MUST be flagged for rework / manual handling and excluded from the
  release package.
- **FR-022**: A reviewer MUST be able to approve the full release for a request, but ONLY when every
  document for that request is in an Approved state.
- **FR-023**: The system MUST persist all review decisions (reviewer identifier, decision,
  timestamp, comments) and associate them with the request and document.

**Packaging & release**

- **FR-024**: After release approval, the system MUST assemble the approved redacted document
  versions into a single ZIP archive named `foia-request-{requestId}-release-package.zip`.
- **FR-025**: The system MUST upload the ZIP to a configured cloud storage container, creating the
  container if it does not exist.
- **FR-026**: The system MUST generate a time-limited, read-only shareable URL for the uploaded ZIP
  with a configurable expiration window (default 7 days) and persist the URL and its expiration with
  the request.
- **FR-027**: The system MUST display the ZIP file name, the shareable URL, and the URL expiration
  date/time on the Release Package screen, with a one-click "Copy URL" action.

**Status & visibility**

- **FR-028**: The system MUST expose, for each request, current status, count of documents found,
  count pending review, count approved, count rejected, and release package status with shareable
  URL when present.
- **FR-029**: The system MUST record audit events for: request submitted, request validated, search
  started, search completed, documents found, redaction started, redaction completed, human review
  started, document approved, document rejected, release approved, package created, URL generated,
  and any error occurrence. Each event MUST include a timestamp, the request ID, the event type, a
  message, and an optional related document ID.
- **FR-030**: The system MUST surface the audit event list for a request in the UI, ordered
  chronologically.

**Error visibility**

- **FR-031**: User-facing error messages MUST be shown for: invalid request submission, document
  search unavailable, no documents found, redaction failure, packaging/upload failure, and
  shareable-URL generation failure.

**Configuration & local-demo fitness**

- **FR-032**: All external service endpoints, credentials, container/index names, the maximum search
  result count, and the shareable-URL expiration window MUST be configurable without code changes.
- **FR-033**: The system MUST be runnable locally for the demo using configuration values that point
  to the deployed cloud services (per the project constitution's Azure-Ready Deployment principle).

### Key Entities *(include if feature involves data)*

- **FOIA Request**: A submitted information request. Attributes: id, subject, description, requested
  document start date, requested document end date, requestor full name, requestor organization
  (optional), requestor email, requestor phone (optional), requestor mailing address (optional),
  submitted date/time, current status.
- **Document**: A single document surfaced by the search stage and processed through redaction and
  review. Attributes: id, source document id from the search index, file name, file type, source
  URI or source metadata, original content (or reference), redacted content (or reference),
  redaction status, review status. Belongs to one FOIA Request.
- **Redaction**: A single PII finding within a document. Attributes: id, document id, PII type,
  original text, replacement text/placeholder, location (start/end offsets and/or page number when
  applicable), confidence score, reviewer-approved flag, reviewer comments. Belongs to one Document.
- **Review Task**: Reviewer's working item for a request. Attributes: id, FOIA request id, assigned
  reviewer, status, created date/time, completed date/time, reviewer decision, reviewer comments.
  Belongs to one FOIA Request.
- **Release Package**: The packaged output for a request. Attributes: id, FOIA request id, ZIP blob
  name, blob container name, shareable URL, expiration date/time, created date/time. Belongs to one
  FOIA Request.
- **Audit Event**: A timestamped record of a notable workflow occurrence. Attributes: id, timestamp,
  FOIA request id, event type, message, optional related document id.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A presenter can demo the full happy-path flow — submission, automated multi-stage
  processing, side-by-side review, release approval, and shareable-URL handoff — in under 10 minutes
  without leaving the UI.
- **SC-002**: For a valid submission against an indexed document set, the workflow reaches the
  "Pending Human Review" state within 60 seconds under typical demo conditions.
- **SC-003**: A reviewer can approve all documents and the release for a typical 5-document request
  in under 3 minutes from opening the Human Review screen.
- **SC-004**: 100% of submissions that violate any documented validation rule are rejected with a
  field-level error and never start the workflow.
- **SC-005**: 100% of generated shareable URLs successfully download a ZIP whose contents match the
  approved redacted document set when opened before expiration.
- **SC-006**: 100% of automated redactions for the demo PII categories (email, phone, SSN, date of
  birth) are visibly labeled in the redacted view and listed in the document's redaction report.
- **SC-007**: Every major workflow event listed in FR-029 is observable on the request's audit
  timeline within 5 seconds of occurrence.
- **SC-008**: A new operator can configure and run the system locally against the deployed cloud
  services in under 15 minutes by following the project README.

## Assumptions

- A document index already exists and is populated with demo FOIA-style documents containing some
  recognizable PII; provisioning that index is out of scope for this feature.
- A cloud blob storage account is available for storing release ZIPs and issuing shareable URLs.
- Documents from the index are primarily textual; binary/PDF redaction beyond simple text
  replacement is not required for the demo.
- The demo runs in a trusted environment with a single, implicit reviewer identity. Authentication,
  authorization, and multi-reviewer assignment are out of scope (see constitution Principle III).
- "Confidence score" for a redaction is best-effort and may be absent for heuristic/pattern
  detections; the UI handles missing confidence gracefully.
- Pattern-based detection is sufficient for email, phone, SSN, and date-of-birth PII; name and
  address detection may rely on AI-assisted or heuristic methods and is best-effort.
- The shareable-URL expiration default of 7 days is acceptable for demo audiences.
- Local persistence for request, document, redaction, review, release, and audit data uses a
  lightweight, file-based store appropriate for a single-machine demo.
- The presence of an external orchestration framework, document search service, and blob storage
  service is assumed to be available and configured before the demo runs (per FR-032 / FR-033).
