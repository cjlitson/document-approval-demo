# Production roadmap

## Phase 1 — Validate the reusable prototype

- Demonstrate both Purchase Request and Policy Approval from intake through signed package.
- Use the designer to prove stage names, assignees, conditions, exact `$1,000` behavior, and alert timing are configuration rather than code.
- Validate manager, named-person, and person-field assignment with business owners.
- Obtain legal/compliance/records acceptance of authenticated typed-name evidence for each included document class.
- Confirm required file types, sizes, retention, package contents, reminder timing, escalation recipients, and support ownership.

## Phase 2 — Production foundation on Azure pay-as-you-go

- Replace demo authentication with Microsoft Entra ID and manager lookup through Microsoft Graph.
- Deploy the .NET 10 app to Azure App Service and use Azure SQL with managed identities and Key Vault references.
- Replace local files with Blob Storage or SharePoint and add malware scanning, retention, and legal-hold controls.
- Introduce reviewed EF Core migrations, optimistic concurrency, transactional request numbering, and deployment slots.
- Run the SQL outbox dispatcher as an App Service WebJob initially; add real Graph Email and Teams delivery adapters with retry and dead-letter handling.
- Add Application Insights, health checks, audit export, backup/restore validation, accessibility testing, and threat modeling.

## Phase 3 — Expand the approval engine

- Add administrator-designed document types and field definitions, not just seeded types.
- Add condition groups with explicit AND/OR nesting, parallel stages, multiple approvers, observers, delegation, out-of-office handling, and due-date calendars.
- Add template-controlled final-package assembly, document classification, retention, and legal hold by document type.
- Add reporting for cycle time, rejections, bottlenecks, overdue stages, alert effectiveness, and process-specific measures.
- Introduce Service Bus/Azure Functions only when volume, resiliency, or independent scaling warrants the added operating cost.

## Phase 4 — Copilot Studio

- Expose narrow, Entra-protected APIs through an approved custom connector.
- Ground policy answers in approved SharePoint content.
- Let authenticated users choose a document type, start a draft, check status, and identify missing information conversationally.
- Provide evidence-linked summaries for assigned approvers while retaining the human decision and typed signature in the app.
- Evaluate authorization leakage, prompt injection, attachment handling, answer accuracy, audit completeness, and human override before broader rollout.

## Go-live gates

1. Business owners sign off on every published route, field, rule, and alert policy.
2. Legal/compliance approves the signature evidence for the included document classes.
3. Security validates authentication, authorization, storage, secrets, logging, connector permissions, and threat model.
4. Records owners validate retention, package integrity, retrieval, and legal hold.
5. Operations completes monitoring, retry/dead-letter procedures, recovery, support, and rollback rehearsals.
