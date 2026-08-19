# Production roadmap

## Phase 1 — Validate the reusable prototype

- Demonstrate SystemAdmin Document Type creation/configuration and safe deactivate/delete behavior.
- Demonstrate scoped access with Taylor Purchasing and Jordan Smith; verify that Purchase Request access does not leak to Policy Approval.
- Complete the acceptance flow with PDF, image, and Office attachments, conditional approval, completion notification, Approval Record, original downloads, and revision-aware ZIP.
- Validate manager, named-person, and person-field assignment with business owners.
- Confirm lifecycle events, recipients, delay semantics, reminder timing, escalation recipients, and operational ownership.
- Obtain legal/compliance/records acceptance of authenticated typed-name evidence and package contents for each included document class.
- Confirm file types, size limits, retention, accessibility, and support ownership.

## Phase 2 — Production foundation on Azure pay-as-you-go

- Replace demo authentication with Microsoft Entra ID and manager lookup through Microsoft Graph.
- Define Entra group/application-role provisioning for SystemAdmins while retaining relational per-Document-Type assignments.
- Deploy the .NET 10 app to Azure App Service and Azure SQL using managed identities and Key Vault references.
- Replace `EnsureCreated` with reviewed EF Core migrations, migration gates, optimistic concurrency, transactional request numbering, deployment slots, and tested rollback/backup procedures.
- Replace local files with Blob Storage or SharePoint and add malware scanning, retention, encryption, legal hold, and immutable-version controls.
- Threat-model every preview/download/package endpoint; add rate/size controls, security logging, content-disposition tests, and penetration testing.
- Capacity-test PDF and ZIP generation, deploy an approved font package, and decide whether completed packages should be materialized once as immutable records.
- Run the SQL outbox dispatcher as a durable worker/WebJob; add real Graph Email and Teams adapters with retry, dead-letter, monitoring, and consent review.
- Add Application Insights, health checks, audit export/SIEM integration, recovery exercises, accessibility testing, and operational runbooks.

## Phase 3 — Expand governance and routing

- Add explicit access-assignment expiry/review, bulk administration, separation-of-duties checks, and delegated configuration approval if required.
- Add richer condition groups with explicit AND/OR nesting, parallel stages, multiple approvers, observers, delegation, out-of-office handling, and business calendars.
- Add lifecycle-rule templates, delivery diagnostics, escalation ownership, and approved HTML/text notification templates.
- Integrate an approved Microsoft 365/SharePoint/Graph preview provider for Office files behind `IFilePreviewService`; continue to prohibit public viewers.
- Add document classification, retention, package templates, legal hold, integrity verification, and records disposition by Document Type.
- Add reporting for cycle time, rejections, bottlenecks, overdue stages, lifecycle follow-up, alert effectiveness, and process-specific measures.
- Introduce Service Bus/Azure Functions only when volume, resiliency, or independent scaling warrants the operating cost.

## Phase 4 — Copilot Studio

- Expose narrow, Entra-protected APIs through an approved custom connector.
- Ground policy answers in approved SharePoint content.
- Let authenticated users choose a Document Type, start a draft, check status, and identify missing information conversationally.
- Provide evidence-linked summaries for authorized approvers and scoped operators while retaining the human decision and signature in the app.
- Evaluate authorization leakage, prompt injection, attachment handling, answer accuracy, audit completeness, and human override before broader rollout.

## Go-live gates

1. Business owners approve every published route, field, lifecycle rule, scoped assignment process, and alert policy.
2. Legal/compliance approves adopted-signature evidence and Approval Record wording for each document class.
3. Security validates identity, scoped authorization, file isolation, content inspection, storage, secrets, logging, connector permissions, and threat model.
4. Records owners validate retention, manifest/hash integrity, package retrieval, immutable evidence, and legal hold.
5. Operations validates notification retry/dead-letter behavior, monitoring, backup/restore, disaster recovery, font/runtime dependencies, support, and rollback rehearsals.
