# Document Approval Platform Prototype

A .NET 10 demonstration of a reusable document-routing and approval platform. ASP.NET Core MVC runs request, approval, document, and administration flows; interactive server-side Blazor powers the versioned route designer. EF Core uses SQLite for the prototype and retains a SQL Server/Azure SQL provider path.

## What this version proves

- System administrators can create, search, configure, deactivate/reactivate, and—only when safe—permanently delete Document Types.
- Each Document Type owns ordered dynamic fields, scoped access assignments, independent lifecycle-notification rules, and a versioned approval route.
- Scoped Administrators, Coordinators, and Viewers can oversee only their assigned Document Types without becoming global `SystemAdmin` users or approvers.
- Lifecycle notifications respond to submission, stage, rejection, and completion events without creating an approval, requiring a signature, or blocking route progression.
- Managed Requests provides assigned users with filtered operational visibility, current workflow position, request age, and needs-attention state.
- Request Details separates Overview, Workflow, Documents, and Activity. Its workflow stepper distinguishes completed, current, future, skipped/not-required, and rejected states.
- Attachments support drag/drop, multiple files, a pre-submit file table, server-side size/type validation, and revision-aware retention.
- Authorized users can securely preview PDF, PNG, JPEG, and text files in the application and download originals individually. Office documents remain download-only in this pass.
- Final approval exposes a professionally generated Approval Record PDF and a ZIP package containing that record, every attachment from every revision, and a JSON manifest with SHA-256 hashes.
- Existing route versioning, conditional routing, adopted signatures, revision restarts, stage alerts, and outbox delivery behavior remain intact.

Two seeded workflows demonstrate reuse:

- **Purchase Request:** Manager Review → conditional Executive Review when `amount > 1000` → Financial Control Review.
- **Policy Approval:** Owner Manager Review → conditional Compliance Review when `risk_level = High` → Records Approval assigned from a person field.

## Authorization model

| Identity | Scope | Capabilities in this prototype |
|---|---|---|
| SystemAdmin | All Document Types | Create/configure Document Types; manage access and lifecycle rules; deactivate/reactivate; safely delete unused draft-only configuration; design/publish workflows; view all requests and documents |
| Document Type Administrator | Assigned Document Types only | Managed Requests; request/workflow/history visibility; preview/download documents; completed package access; configured lifecycle notifications |
| Coordinator | Assigned Document Types only | The same operational read access as the scoped Administrator, with no configuration or workflow authority |
| Viewer | Assigned Document Types only | Read-only request and document access, with no configuration authority |
| Requester / assigned approver | Individual request | Existing requester or approval access and actions |

Scoped roles never imply approval authority. Taylor Purchasing can oversee Purchase Requests, for example, but cannot approve one unless a published route independently assigns Taylor as an approver. Only central SystemAdmins can configure Document Types or publish workflows.

All request details, attachment previews, attachment downloads, Approval Record endpoints, and ZIP downloads use the same centralized request-level authorization check. Attachment IDs must also belong to the request in the URL. Stored physical paths are never exposed, stored names reject path segments, download names are sanitized by the framework, and response content types are derived from verified content rather than trusting the browser upload header.

## Document Type administration

Open **Document types** as Alex Admin. The workspace provides:

- an active/inactive, searchable administration table;
- a six-step New Document Type flow that creates an isolated version 1 workflow draft;
- Overview, Form Fields, Access, Notifications, Workflow, and Requests tabs;
- safe field add/edit/reorder/removal, including protection for keys/types used by historical requests and fields referenced by routes;
- user-directory-backed scoped access assignments;
- lifecycle rules with event, optional stable stage key, recipient strategy, channel, enabled state, and optional delay;
- links into the existing Blazor Route Designer rather than duplicating workflow governance.

Deactivation prevents new requests while preserving requests, revisions, files, workflow versions, approvals, and audit evidence. Permanent deletion is enforced server-side and is available only when no request exists and no workflow version has ever been published or retired. The confirmation must match the Document Type name or `DELETE`.

## Lifecycle notifications

`LifecycleNotificationRule` is separate from `ApprovalInstance`, `ApprovalDecision`, and versioned approval-stage `AlertPolicy`. Supported events are:

- Request Submitted
- Stage Started
- Stage Completed
- Request Rejected
- Request Completed

Recipients can be the requester, scoped administrators, scoped coordinators, a named user, a person selected in a request field, the current approver, or the requester’s manager. Rules can queue in-app, Email, and Teams rows through the existing idempotent outbox. Email and Teams delivery remain simulated. A lifecycle recipient receives information and a link only—never an approval or signature obligation.

The seed configures Purchase Request completion to notify its Document Type Administrator in app. Normal requester outcome notifications are preserved.

## Documents, Approval Record, and package

The Documents tab uses a two-pane reader on desktop and stacks on smaller screens.

- PDF, PNG, JPEG, and TXT are previewed from authenticated same-origin endpoints after magic-byte/content inspection.
- DOC, DOCX, XLS, and XLSX are available for authenticated download but are not sent to a public viewer. `IFilePreviewService` provides a future integration boundary for an approved Microsoft 365/SharePoint/Graph provider.
- Unsupported or mismatched content is not rendered inline.

After final approval, the neutral enterprise Approval Record PDF contains request metadata, current-revision values, approval summary/evidence, skipped conditional stages and their configured conditions, chronological workflow history, attachment index, page numbers, and generation metadata. It uses PDFsharp/MigraDoc rather than the former hand-written PDF writer.

The final ZIP does not flatten originals into one PDF. It contains:

```text
PUR-2026-0048-Approved-Package.zip
├── PUR-2026-0048-Approval-Record.pdf
├── Attachments/
│   ├── Revision-01/...
│   └── Revision-02/...
└── Package-Manifest.json
```

The manifest includes request/package metadata and each attachment’s original name, packaged path, revision, verified content type, actual packaged byte count, and SHA-256 hash. It contains no physical storage paths.

## Run in Visual Studio 2026

1. Open `DocumentApprovalDemo.sln`.
2. Confirm the startup project is `DocumentApprovalDemo`.
3. Build the solution, then press **F5**.
4. Trust the ASP.NET Core development certificate if prompted.
5. The first run creates `src/DocumentApprovalDemo/App_Data/document-approval-v3-demo.db` and seeds demo users, Document Types, routes, access, and notification rules.

Command-line equivalent:

```bash
dotnet restore DocumentApprovalDemo.sln
dotnet run --project src/DocumentApprovalDemo/DocumentApprovalDemo.csproj
```

Prerequisite: the .NET 10 SDK.

### Prototype database note

The application still uses `EnsureCreated` rather than migrations. This domain change therefore moves the default SQLite filename from `document-approval-v2-demo.db` to `document-approval-v3-demo.db`; an existing v2 file is left untouched instead of being silently destroyed or opened with an incompatible schema.

To reset only the current prototype data, stop the app, optionally back up, and delete:

```text
src/DocumentApprovalDemo/App_Data/document-approval-v3-demo.db
```

The next run recreates and reseeds it. If a custom connection string points at an older prototype file, point it at a new file or explicitly back up and reset that database. Production must replace `EnsureCreated` with reviewed EF Core migrations.

Default configuration:

```json
"ConnectionStrings": {
  "ApprovalDatabase": "Data Source=App_Data/document-approval-v3-demo.db"
},
"Database": {
  "Provider": "Sqlite"
}
```

For SQL Server, set `Database:Provider` to `SqlServer` and replace the connection string. The relational model and LINQ queries avoid SQLite-only configuration constructs.

## Suggested demonstration

1. Sign in as **Alex Admin**. Open **Document types → Purchase Request** to review fields, Taylor/Jordan access, the completion rule, route state, and request history.
2. Optionally create a new Document Type. The wizard creates a safe draft; use **Open Route Designer** to add stages and publish it.
3. Sign in as **Avery Employee** and submit a Purchase Request with PDF, image, and Office attachments. An amount above `$1,000` includes Executive Review; exactly `$1,000` skips it because the seeded operator is `GreaterThan`.
4. Sign in as **Taylor Purchasing**. Taylor is a requester, not a SystemAdmin or approver, but **Managed Requests** exposes the assigned Purchase Request and its documents.
5. Complete approvals as **Morgan Manager**, **Pat President** when applicable, and **Finley Finance**, typing each authenticated full name.
6. Return as Taylor. The completion notification states that operational follow-up is ready and requires no signature. Open the completed request to preview supported files, download originals, view/download the Approval Record, and download the final ZIP.
7. Confirm Taylor cannot see a Policy Approval through scoped access.

## Production boundaries

The Development-only identity selector represents Microsoft Entra authentication. Production must add Entra OpenID Connect, immutable object IDs, Conditional Access/MFA as appropriate, and Microsoft Graph manager lookup. Typed names are adopted signatures, not DocuSign certificates, and require legal/compliance/records approval for each document class.

Replace local uploads with Blob Storage or SharePoint plus scanning, retention, legal hold, and encryption controls. Replace simulated Email/Teams delivery with approved channel adapters and durable worker hosting. PDF generation also requires an approved deployed font set (the prototype resolver uses DejaVu Sans, Liberation Sans, or Arial).

See [Architecture](docs/architecture.md) and [Production roadmap](docs/production-roadmap.md).

## Tests

```bash
dotnet test DocumentApprovalDemo.sln
```

The suite covers scoped access isolation, SystemAdmin visibility, Document Type creation/lifecycle/deletion rules, lifecycle notification independence and stable stage matching, secured document endpoints and attachment/request pairing, package lifecycle gating and hash integrity, Approval Record semantics/PDF generation, route conditions, signatures, revisions, SQLite query compatibility, and outbox delivery. GitHub Actions restores, builds, and tests with .NET 10 for every pull request.
