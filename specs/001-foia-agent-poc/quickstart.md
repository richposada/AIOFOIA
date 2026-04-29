# Quickstart: FOIA Agent Proof-of-Concept

**Feature**: 001-foia-agent-poc
**Audience**: Demo runner / new developer setting up the PoC for the first time.

---

## Prerequisites

- **.NET 8 SDK** (`dotnet --version` → 8.x)
- **Node.js 20+** + **npm 10+** (for the React SPA)
- **Azure Developer CLI (azd)** 1.10+ (`azd version`)
- **Azure CLI** (`az login` succeeds)
- **Azure subscription** with permission to create:
  - Azure Container Apps environment + Container App
  - Azure Container Registry
  - Azure OpenAI resource (with a `gpt-4o-mini` deployment)
  - Azure Blob Storage account
  - Log Analytics workspace
- **Pre-existing Azure AI Search index** populated with documents (read-only consumer). You will
  need: endpoint URL, admin or query key, index name, and the field-name mapping.

---

## 1. Clone and bootstrap

```powershell
git clone <repo-url> foia-orchestrator
cd foia-orchestrator/foia-agent
git checkout 001-foia-agent-poc
```

---

## 2. Deploy to Azure (one command)

```powershell
azd auth login
azd up
```

`azd up` will:

1. Prompt for environment name, subscription, and region.
2. Prompt for the existing Azure AI Search connection values (endpoint, key, index name, field
   mapping). These are stored as `azd` env values, not in source.
3. Provision: Container Apps env, Container App, Container Registry, Storage Account (with the
   `foia-releases` container), Azure OpenAI + `gpt-4o-mini` deployment, Log Analytics.
4. Build the API container (which embeds the SPA's `dist/`) and push to ACR.
5. Deploy the Container App and print the public URL.

Open the printed URL — you should see the SPA's "Submit FOIA Request" page.

---

## 3. Run locally with deployed Azure services

After `azd up`, copy environment values into a local `.env`:

```powershell
azd env get-values > .env
```

Start the API (terminal 1):

```powershell
cd src/FoiaProcessor.Api
dotnet run
```

Start the SPA (terminal 2):

```powershell
cd src/foia-processor-ui
npm install
npm run dev
```

The SPA runs at `http://localhost:5173` and proxies API calls to `http://localhost:5000`.

---

## 4. Demo flow

The demo exercises user stories US1–US3 from [spec.md](spec.md).

### Sample submission payload

```json
{
  "subject": "Communications about Project Alpha",
  "description": "All emails and memos referencing Project Alpha.",
  "requestedStartDate": "2024-01-01",
  "requestedEndDate": "2024-12-31",
  "requestorFullName": "Jane Doe",
  "requestorOrganization": "Acme News",
  "requestorEmail": "jane@example.com",
  "requestorPhone": "+1-555-0100",
  "requestorMailingAddress": "123 Main St, Springfield, IL 62701"
}
```

### Step-by-step

1. **Submit** — On the home screen, fill the form (or paste the payload above) and click
   **Submit**. The status screen opens and begins polling.
   - Watch status transition: `Submitted → Validated → Searching → DocumentsFound → Redacting →
     PendingHumanReview`.
   - The audit timeline grows in real time. *(Demonstrates US1 — Spec SC-001, SC-002.)*

2. **Review** — Click **Review Documents**. For each document the screen shows side-by-side
   original vs. redacted text with redactions highlighted.
   - Click **Approve** on each document. Optionally click **Reject** with a comment to demonstrate
     the rework path. *(Demonstrates US2 — SC-003, SC-005.)*

3. **Approve release** — Once every document is Approved, the **Approve Release** button enables.
   Click it. Status moves to `ApprovedForRelease → Packaging → ReleasePackageReady`.

4. **Download** — A **Download Release Package** link appears with the SAS URL. Click it; the
   `foia-request-<id>-release-package.zip` downloads. Open it locally to see only redacted
   versions of approved documents. *(Demonstrates US3 — SC-006.)*

5. **Audit** — The audit timeline on the status screen shows every event end-to-end.
   *(Demonstrates SC-007.)*

---

## 5. Configuration reference

`appsettings.json` keys (all overridable via env / `azd` env values):

| Key | Purpose | Example |
|---|---|---|
| `ConnectionStrings:Sqlite` | Local DB path | `Data Source=foia.db` |
| `AzureOpenAI:Endpoint` | OpenAI endpoint | `https://...openai.azure.com/` |
| `AzureOpenAI:Deployment` | Chat deployment name | `gpt-4o-mini` |
| `AzureOpenAI:ApiKey` | Key (or use managed identity) | secret |
| `AzureSearch:Endpoint` | Search service URL | `https://...search.windows.net` |
| `AzureSearch:ApiKey` | Query/admin key | secret |
| `AzureSearch:IndexName` | Index name | `foia-documents` |
| `AzureSearch:Fields:Id` | Index field for id | `id` |
| `AzureSearch:Fields:Content` | Index field for content | `content` |
| `AzureSearch:Fields:DocumentDate` | Index field for date | `documentDate` |
| `AzureBlobStorage:ConnectionString` | Local-dev account-key | secret |
| `AzureBlobStorage:AccountName` | For managed-identity SAS | `demoacct` |
| `AzureBlobStorage:ContainerName` | Releases container | `foia-releases` |
| `AzureBlobStorage:SasExpirationDays` | SAS lifetime | `7` |
| `Workflow:MaxSearchResults` | Top N per search | `10` |

---

## 6. Reset

To wipe local state:

```powershell
Remove-Item src/FoiaProcessor.Api/foia.db -ErrorAction SilentlyContinue
```

To tear down Azure resources:

```powershell
azd down --purge
```

---

## 7. Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `400` on submit with `requestorEmail` error | Invalid email format | Use `name@host.tld` |
| Status stuck at `Searching` | AI Search creds wrong / network blocked | Re-check `AzureSearch:*` and `azd env get-values` |
| Status `NoDocumentsFound` | Query returned 0 hits | Try a broader date range or different `subject` text |
| Status `Error` | See latest `AuditEvent` of type `Error` on the status screen | Inspect API logs (`azd monitor` or local console) |
| SAS URL returns 403 | SAS expired (>7 days) | Re-trigger packaging by re-approving release (PoC: re-deploy or extend `SasExpirationDays`) |
