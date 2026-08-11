# Production roadmap

## Phase 1 — Validate the pilot

- Walk the demonstration with requester, manager, President, VP Finance, and administrator roles.
- Confirm the exact `$1,000` rule and any department/subcategory exceptions.
- Obtain legal/compliance/records acceptance of authenticated typed-name evidence.
- Confirm required file types, maximum sizes, retention, and final-package contents.
- Define operational ownership, support hours, and approval delegation/escalation behavior.

## Phase 2 — Production foundation

- Replace demo authentication with Microsoft Entra ID and Microsoft Graph manager lookup.
- Deploy App Service and Azure SQL using managed identities and Key Vault references.
- Move attachments to Blob Storage or SharePoint and add malware scanning.
- Add EF Core migrations, optimistic concurrency, idempotency keys, and durable background jobs.
- Deliver real email and Teams adaptive-card notifications without allowing card actions to bypass the signature page.
- Add Application Insights, audit export, backup/restore validation, and alerting.

## Phase 3 — Reusable approval engine

- Add request-type and field-definition configuration.
- Expand the rule builder to amount, department, subcategory, requester attributes, and compound AND/OR groups.
- Add delegation, out-of-office, escalation, parallel stages, observers, and service-level targets.
- Add reporting for cycle time, rejections, bottlenecks, spend, subscriptions, and renewals.
- Apply retention and legal-hold policies by document type.

## Phase 4 — Copilot Studio

- Ground policy answers in approved SharePoint content.
- Let authenticated users start drafts, check status, and identify missing information conversationally.
- Provide approvers with evidence-linked summaries while keeping the human decision and typed signature in the app.
- Evaluate answer accuracy, authorization leakage, prompt injection, and audit completeness before broader rollout.

## Go-live gates

1. Business owner signs off on every route and exception.
2. Legal/compliance approves the signature evidence for the included document classes.
3. Security validates authentication, authorization, storage, secrets, logging, and threat model.
4. Records owners validate retention, package integrity, retrieval, and legal hold.
5. Operations completes recovery, monitoring, support, and rollback rehearsals.
