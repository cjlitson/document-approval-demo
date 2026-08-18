# Document Approval Platform Prototype

A .NET 10 demonstration of a reusable document-approval platform. ASP.NET Core MVC runs the requester and approver experience; an interactive Blazor workspace lets central administrators design versioned routes and alerts. EF Core uses SQLite for the demo and can switch to SQL Server/Azure SQL.

## What this version proves

- Document types define their own ordered intake fields instead of relying on purchase columns in code.
- Two seeded workflows demonstrate reuse:
  - **Purchase Request:** Manager Review → conditional Executive Review when `amount > 1000` → Financial Control Review.
  - **Policy Approval:** Owner Manager Review → conditional Compliance Review when `risk_level = High` → Records Approval assigned from a person field on the form.
- The Blazor route designer can create a draft, add/remove/reorder/rename stages, choose manager/named-person/person-field assignment, build multiple AND conditions, configure alerts, simulate route results, save, and publish.
- Published route versions are immutable; in-flight requests retain the version selected at submission.
- Every seeded stage requires an adopted signature: the authenticated approver types their full name.
- Rejection creates a new revision, preserves prior field values and decision evidence, and restarts at stage one.
- Supporting documents are required, and final approval produces a signed-package PDF while retaining the originals.
- Alert policies are versioned with each stage. Assignment, reminder, escalation, and outcome events create idempotent SQL outbox records for in-app, Email, and Teams channels.
- A background dispatcher simulates channel delivery, records attempts, and cancels obsolete reminders/escalations when a stage completes.

## Run in Visual Studio 2026

1. Open `DocumentApprovalDemo.sln`.
2. Confirm the startup project is `DocumentApprovalDemo`.
3. Build the solution, then press **F5**.
4. If Visual Studio asks to trust the ASP.NET Core development certificate, accept it.
5. The first run creates `src/DocumentApprovalDemo/App_Data/document-approval-v2-demo.db` and seeds the two document types, routes, alert policies, and demo identities.

Command-line equivalent:

```bash
dotnet restore DocumentApprovalDemo.sln
dotnet run --project src/DocumentApprovalDemo/DocumentApprovalDemo.csproj
```

Prerequisite: the .NET 10 SDK. The solution deliberately moved from .NET 8 because this prototype is intended to grow beyond the November 2026 end of .NET 8 support.

If the data model changes during prototype development, stop the app and delete `src/DocumentApprovalDemo/App_Data/document-approval-v2-demo.db`; the app recreates demo data on the next run. This `EnsureCreated` behavior is for the prototype only.

## Suggested demonstration

### Prove route design is not hardcoded

1. Sign in as **Alex Admin** and open **Route designer**.
2. Switch between Purchase Request and Policy Approval. Compare their form fields, stages, assignee strategies, conditions, and route-simulator inputs.
3. Create an editable draft for Purchase Request.
4. Rename a stage, change the exact `$1,000` operator, reorder or add a stage, and change its named approver.
5. Expand **Alert policy** on a stage. Adjust the reminder/escalation delays or channels, save, and publish.

### Run a purchase approval

1. Sign in as **Avery Employee**, choose Purchase Request, enter an amount above `$1,000`, confirm Morgan as manager, attach a file, and submit.
2. Sign in as **Morgan Manager**, review the document, type `Morgan Manager`, and approve.
3. Sign in as **Pat President** for the conditional stage, then **Finley Finance** for final approval.
4. Return as Avery to download the signed package and originals.

Exactly `$1,000` follows the operator stored in the published route. The seed uses `GreaterThan`, so it initially skips Executive Review; an administrator can publish `GreaterThanOrEqual` or `Equal` without a code change.

### Run a different document route

1. As Avery, choose Policy Approval, set risk to `High`, select a Records approver, attach supporting material, and submit.
2. Observe that the high-risk condition adds Compliance Review and that the final approver came from the submitted person field.

### Explore alerts

1. Open **Alerts** after submitting or advancing a request.
2. Assignment alerts are immediately delivered by the simulated worker; reminders at 48 hours and escalations at 120 hours remain pending.
3. As Alex Admin, choose **Simulate all due now** to advance pending records without waiting.
4. Inspect channel, event, status, cancellation, and delivery-attempt history. No external email or Teams message is sent.

## Database configuration

SQLite is the zero-infrastructure default:

```json
"ConnectionStrings": {
  "ApprovalDatabase": "Data Source=App_Data/document-approval-v2-demo.db"
},
"Database": {
  "Provider": "Sqlite"
}
```

For SQL Server, set `Database:Provider` to `SqlServer` and replace the connection string. Before production, replace `EnsureCreated` with reviewed EF Core migrations, use Azure SQL with managed identity, and add tested rollback/backup procedures.

## Production boundaries

The Development-only identity selector represents Microsoft Entra authentication. Production must add Entra OpenID Connect, immutable object IDs, Conditional Access/MFA as appropriate, and Microsoft Graph manager lookup. The typed name is an adopted signature; it is not a DocuSign certificate and should only be used for document classes accepted by legal, compliance, and records owners.

Also replace local uploads with Blob Storage or SharePoint plus scanning/retention controls, and replace the simulated dispatcher with approved Microsoft Graph Email/Teams delivery. Alerts should continue linking to the authenticated application; they should not bypass the required typed signature.

See [Architecture](docs/architecture.md) and [Production roadmap](docs/production-roadmap.md).

## Tests

```bash
dotnet test DocumentApprovalDemo.sln
```

The suite covers document-type-specific rules, configurable `$1,000` behavior, person-field assignment, typed signatures, SQLite timestamp compatibility, dynamic signed-package evidence, and multi-channel outbox delivery. GitHub Actions builds and tests with .NET 10 on every pull request.
