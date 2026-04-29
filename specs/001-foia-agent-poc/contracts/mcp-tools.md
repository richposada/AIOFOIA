# MCP Tool Contracts: FOIA Agent PoC

**Feature**: 001-foia-agent-poc

Five logical MCP tool surfaces ("servers") — Case, Search, Redaction, Review, BlobStorage — are
hosted in-process by the API project and exposed to Microsoft Agent Framework agents. Each tool is
a typed C# method registered with the MCP host. Agents MUST NOT touch infrastructure SDKs directly;
all side effects flow through the tools below.

JSON shapes below describe the logical request/response. The C# implementations use equivalent
record types.

---

## Server 1: Case

| Tool | Purpose |
|---|---|
| `case_create` | Persist a validated FOIA request as the initial case row. |
| `case_get` | Read a case by id (used by agents to load context after resume). |
| `case_update_status` | Transition a case to a new `RequestStatus` value. |
| `case_add_note` | Append a free-text note as an `AuditEvent`. |
| `case_save_document_metadata` | Persist Document rows returned by Search. |
| `case_save_redaction_results` | Persist Redaction rows + RedactedContent for a Document. |
| `case_save_review_decision` | Record per-document approve/reject decision. |
| `case_save_release_package` | Persist a ReleasePackage row. |

### `case_create`

**Input**: full FOIA request payload (same shape as the REST DTO, minus `id`/`submittedAt`).
**Output**: `{ "caseId": "<guid>", "status": "Submitted" }`.

### `case_update_status`

**Input**: `{ "caseId": "<guid>", "newStatus": "Searching", "message": "Search starting." }`.
**Output**: `{ "caseId": "<guid>", "status": "Searching", "auditEventId": "<guid>" }`.
**Side effect**: writes a corresponding `AuditEvent`.

### `case_save_document_metadata`

**Input**:

```json
{
  "caseId": "<guid>",
  "documents": [
    { "sourceDocumentId": "search-id-1", "fileName": "memo.txt", "fileType": "txt",
      "sourceUri": "https://...", "originalContent": "..." }
  ]
}
```

**Output**: `{ "savedDocumentIds": ["<guid>", ...] }`.

### `case_save_redaction_results`

**Input**:

```json
{
  "documentId": "<guid>",
  "redactedContent": "...",
  "redactions": [
    { "piiType": "Email", "originalText": "jane@x.com", "replacementText": "[REDACTED EMAIL]",
      "startOffset": 6, "endOffset": 16, "pageNumber": null,
      "confidence": null, "detectionSource": "regex" }
  ]
}
```

**Output**: `{ "documentId": "<guid>", "redactionStatus": "Completed", "redactionCount": 1 }`.

### `case_save_review_decision`

**Input**: `{ "documentId": "<guid>", "decision": "Approved" | "Rejected" | "ManualHandling", "comments": "..." }`.
**Output**: `{ "documentId": "<guid>", "reviewStatus": "Approved" }`.

---

## Server 2: Search

| Tool | Purpose |
|---|---|
| `search_documents` | Free-text search with optional date filter; returns top N. |
| `get_document_by_id` | Look up a single document by index id. |
| `get_document_content` | Fetch full text content for a document id. |
| `search_documents_by_date_range` | Date-only search (no free-text query). |

### `search_documents`

**Input**:

```json
{
  "query": "Project Alpha communications",
  "startDate": "2024-01-01",
  "endDate": "2024-12-31",
  "top": 10
}
```

**Output**:

```json
{
  "results": [
    { "id": "idx-1", "title": "Memo: Alpha", "fileName": "memo.txt", "fileType": "txt",
      "sourceUri": "https://...", "documentDate": "2024-03-15",
      "snippet": "...Project Alpha...", "metadata": { "department": "Ops" } }
  ]
}
```

`top` defaults to the configured `MaxSearchResults` (10).

### `get_document_content`

**Input**: `{ "id": "idx-1" }`.
**Output**: `{ "id": "idx-1", "content": "<full text>" }`.

---

## Server 3: Redaction

| Tool | Purpose |
|---|---|
| `detect_pii` | Run hybrid (regex + AI) PII detection over a text payload. |
| `redact_text` | Apply detected PII findings to produce redacted text. |
| `create_redaction_report` | Build a structured report for human review. |
| `save_redacted_document` | Persist the redacted version (delegates to `case_save_redaction_results`). |

### `detect_pii`

**Input**: `{ "text": "...", "useAi": true }`.
**Output**:

```json
{
  "findings": [
    { "piiType": "Email", "originalText": "jane@example.com",
      "startOffset": 23, "endOffset": 39,
      "confidence": null, "detectionSource": "regex" },
    { "piiType": "Name", "originalText": "Jane Doe",
      "startOffset": 6, "endOffset": 14,
      "confidence": 0.91, "detectionSource": "ai" }
  ]
}
```

### `redact_text`

**Input**: `{ "text": "...", "findings": [ ... as above ... ] }`.
**Output**: `{ "redactedText": "...", "appliedFindings": [ { ..., "replacementText": "[REDACTED EMAIL]" } ] }`.

Replacement labels are fixed per `PiiType` (see [data-model.md](../data-model.md)).

### `create_redaction_report`

**Input**: `{ "documentId": "<guid>", "findings": [ ... ] }`.
**Output**: a JSON object suitable for the human-review screen (mirrors REST endpoint #4's
`redactions` array).

---

## Server 4: Review

| Tool | Purpose |
|---|---|
| `create_review_task` | Open a request-level ReviewTask. |
| `get_review_status` | Roll up per-document statuses for a request. |
| `record_document_approval` | Mark a document Approved (called from the REST controller). |
| `record_document_rejection` | Mark a document Rejected/ManualHandling. |
| `record_release_approval` | Close the request-level ReviewTask with `Approved`. |

### `get_review_status`

**Input**: `{ "caseId": "<guid>" }`.
**Output**:

```json
{
  "caseId": "<guid>",
  "totalDocuments": 7,
  "approved": 7,
  "rejected": 0,
  "pending": 0,
  "allApproved": true
}
```

The Human Review Coordinator agent uses `allApproved` to decide whether to proceed to packaging.

---

## Server 5: BlobStorage

| Tool | Purpose |
|---|---|
| `create_zip_package` | Build an in-memory ZIP from a list of (filename, redacted text) entries. |
| `upload_to_blob_storage` | Upload a byte payload to a blob; create container if needed. |
| `generate_sas_url` | Produce a read-only SAS URL with configurable expiration. |
| `get_release_package_status` | Read the persisted ReleasePackage for a request. |

### `create_zip_package`

**Input**:

```json
{
  "caseId": "<guid>",
  "files": [
    { "fileName": "memo-2024-03.redacted.txt", "content": "..." }
  ]
}
```

**Output**: `{ "zipBytesBase64": "<...>", "fileCount": 1 }`.

### `upload_to_blob_storage`

**Input**: `{ "containerName": "foia-releases", "blobName": "foia-request-<id>-release-package.zip", "contentBase64": "<...>", "contentType": "application/zip" }`.
**Output**: `{ "blobUrl": "https://acct.blob.core.windows.net/foia-releases/..." }`.

### `generate_sas_url`

**Input**: `{ "containerName": "foia-releases", "blobName": "...", "expirationDays": 7 }`.
**Output**: `{ "sasUrl": "https://...?sv=...", "expiresAt": "2026-05-05T14:16:00Z" }`.

Implementation: in Azure → user-delegation SAS via managed identity. Locally → account-key SAS
derived from `AzureBlobStorageConnectionString`.

---

## Tool-invocation rules

1. Agents reach tools **only** via the Agent Framework's MCP integration (no direct DI of tool
   classes into agents).
2. Every tool that mutates state writes a corresponding `AuditEvent` (either directly or via
   `case_add_note`).
3. Tools are idempotent for reads. Mutating tools are NOT required to be idempotent for the PoC,
   but `case_save_review_decision` accepts a no-op repeat of the same decision without error.
4. All tools return structured JSON; errors surface as exceptions which the workflow runner
   converts into a `RequestStatus = Error` transition + an `AuditEvent` of type `Error`.
