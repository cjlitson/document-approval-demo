# Document Approval Demo

An ASP.NET Core MVC demonstration of a reusable, configuration-driven document approval platform. The pilot implements a purchase-request process while keeping the workflow model extensible for additional document types.

## What the demo proves

- One purchase request with `One-Time Purchase`, `Subscription`, and `Operational Expense` subcategories.
- Required supporting documents, vendor details, amount, department, and business justification.
- Manager suggestion/confirmation, with a requester-selectable fallback and self-selection prevention.
- Versioned route: **Manager → conditional President → VP Finance**.
- Configurable President amount operator and threshold, including explicit behavior at exactly `$1,000`.
- Named President and VP Finance approvers stored on each immutable published route version.
- An adopted signature at every stage: the approver types their full name while signed in.
- Approve/reject decisions with identity, email, signature, comments, route, revision, and UTC timestamp evidence.
- Rejected requests create a new revision and restart at stage one; prior evidence remains preserved.
- Final signed-package PDF plus downloadable original attachments.
- Email and Teams notification queue records (delivery is simulated in this demo).
- Central system administrator-only route drafting and publishing.
- SQLite for a zero-infrastructure demo, with a configuration switch for SQL Server.

## Run locally

Prerequisites: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or Visual Studio 2022 17.8+.

> Visual Studio 2019 does not support .NET 8. Use Visual Studio 2022 or the `dotnet` command line for this repository.

```bash
dotnet restore DocumentApprovalDemo.sln
dotnet run --project src/DocumentApprovalDemo/DocumentApprovalDemo.csproj
```

Open the HTTPS URL printed by ASP.NET Core. The first run creates `App_Data/document-approval-demo.db` and seeds the route and demo identities.

To reset the demo, stop the app and delete `src/DocumentApprovalDemo/App_Data`. The folder is ignored by Git.

## Suggested demonstration

1. Sign in as **Avery Employee** and submit a request with a supporting file.
2. Sign in as **Morgan Manager**, open Approvals, type `Morgan Manager`, and approve.
3. For an amount above the seeded `$1,000` threshold, sign in as **Pat President** and approve.
4. Sign in as **Finley Finance** and provide the final approval.
5. Return as Avery to download the signed-package PDF and original attachments.
6. Sign in as **Alex Admin** to create a route draft, change the amount operator or named approvers, and publish a new immutable version.

The seed rule is `Amount > 1000`. Therefore exactly `$1,000` skips President until an administrator publishes a version using `GreaterThanOrEqual` or `Equal`.

## Database configuration

SQLite is the default:

```json
"ConnectionStrings": {
  "ApprovalDatabase": "Data Source=App_Data/document-approval-demo.db"
},
"Database": {
  "Provider": "Sqlite"
}
```

For SQL Server, set `Database:Provider` to `SqlServer` and replace the connection string. The SQL Server EF Core provider is already referenced. The demo uses `EnsureCreated`; before production, introduce reviewed EF Core migrations and remove automatic schema creation.

## Authentication and signature evidence

The Development-only identity selector makes the workflow easy to demonstrate. Production should replace it with Microsoft Entra ID (OpenID Connect), use immutable Entra object IDs, enforce Conditional Access/MFA as required, and obtain the requester manager from Microsoft Graph. The typed name is an adopted signature; the authenticated identity remains authoritative.

This is not a DocuSign certificate and should only replace DocuSign for document classes whose legal, compliance, and records owners accept this evidence model.

## Production boundaries

Before real use, add:

- Entra ID and Graph manager integration; disable demo identity switching.
- Azure SQL, migrations, managed identity, Key Vault, and private networking as appropriate.
- Blob or SharePoint file storage with malware scanning, retention, legal hold, and access controls.
- Real Microsoft Graph email/Teams delivery with retry, idempotency, and dead-letter handling.
- Concurrency controls for request numbering and approval decisions.
- Central telemetry, audit export, backup/restore testing, accessibility testing, and threat modeling.
- Business validation of signature sufficiency, segregation of duties, retention, and document classifications.

See [Architecture](docs/architecture.md) and [Production roadmap](docs/production-roadmap.md) for more detail.

## Tests

```bash
dotnet test DocumentApprovalDemo.sln
```

The repository includes tests for configurable `$1,000` routing behavior, mandatory final finance routing, and signed-package evidence output. GitHub Actions builds and tests every pull request.

