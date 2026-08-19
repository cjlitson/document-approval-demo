using System.Text.RegularExpressions;
using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using DocumentApprovalDemo.Services;
using DocumentApprovalDemo.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace DocumentApprovalDemo.Controllers;

[Authorize(Roles = Roles.SystemAdmin)]
[Route("admin/document-types")]
public sealed partial class DocumentTypesController(
    AppDbContext db,
    ICurrentUserService currentUser,
    IDocumentTypeAdministrationService administration) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? search, string status = "active", CancellationToken cancellationToken = default)
    {
        var documentTypes = await db.DocumentTypes.AsNoTracking()
            .Include(x => x.AccessAssignments).ThenInclude(x => x.User)
            .Include(x => x.Routes).ThenInclude(x => x.Versions)
            .ToListAsync(cancellationToken);
        var requestCounts = await db.Requests.AsNoTracking()
            .Where(x => x.Status == RequestStatus.InApproval)
            .GroupBy(x => x.DocumentTypeId)
            .Select(x => new { DocumentTypeId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.DocumentTypeId, x => x.Count, cancellationToken);
        var usedTypeIds = await db.Requests.AsNoTracking().Select(x => x.DocumentTypeId).Distinct().ToListAsync(cancellationToken);

        IEnumerable<DocumentType> filtered = documentTypes;
        filtered = status.ToLowerInvariant() switch
        {
            "inactive" => filtered.Where(x => !x.IsActive),
            "all" => filtered,
            _ => filtered.Where(x => x.IsActive)
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            filtered = filtered.Where(x =>
                x.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.Key.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.Description.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var model = new DocumentTypeAdministrationIndexViewModel
        {
            Search = search,
            Status = status,
            Items = filtered.OrderBy(x => x.Name).Select(x =>
            {
                var published = x.Routes.SelectMany(route => route.Versions)
                    .Where(version => version.Status == RouteVersionStatus.Published)
                    .OrderByDescending(version => version.VersionNumber).FirstOrDefault();
                var draft = x.Routes.SelectMany(route => route.Versions)
                    .Where(version => version.Status == RouteVersionStatus.Draft)
                    .OrderByDescending(version => version.VersionNumber).FirstOrDefault();
                return new DocumentTypeAdministrationRowViewModel
                {
                    Id = x.Id,
                    Name = x.Name,
                    Key = x.Key,
                    Description = x.Description,
                    Administrators = string.Join(", ", x.AccessAssignments
                        .Where(a => a.IsActive && a.AccessRole == DocumentTypeAccessRole.Administrator)
                        .OrderBy(a => a.User.FullName).Select(a => a.User.FullName)) is { Length: > 0 } names ? names : "Not assigned",
                    WorkflowStatus = draft is not null ? "Draft in progress" : published is not null ? "Published" : "Draft only",
                    WorkflowVersion = published?.VersionNumber ?? draft?.VersionNumber,
                    ActiveRequests = requestCounts.GetValueOrDefault(x.Id),
                    IsActive = x.IsActive,
                    CanDelete = !usedTypeIds.Contains(x.Id) && !x.Routes.SelectMany(route => route.Versions)
                        .Any(version => version.Status is RouteVersionStatus.Published or RouteVersionStatus.Retired)
                };
            }).ToList()
        };
        return View(model);
    }

    [HttpGet("new")]
    public async Task<IActionResult> New(CancellationToken cancellationToken)
    {
        var model = new CreateDocumentTypeViewModel
        {
            Fields = [new DocumentFieldEditViewModel { FieldType = DocumentFieldType.ShortText, IsRequired = true }],
            Access = [new DocumentTypeAccessEditViewModel { AccessRole = DocumentTypeAccessRole.Administrator }],
            Notifications =
            [
                new LifecycleNotificationRuleEditViewModel
                {
                    EventType = LifecycleNotificationEvent.RequestCompleted,
                    RecipientType = LifecycleNotificationRecipient.DocumentTypeAdministrators,
                    SendInApp = true
                }
            ]
        };
        await PopulateUsersAsync(model.Users, cancellationToken);
        return View(model);
    }

    [HttpPost("new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New(CreateDocumentTypeViewModel model, CancellationToken cancellationToken)
    {
        var fields = model.Fields.Where(x => !string.IsNullOrWhiteSpace(x.Key) || !string.IsNullOrWhiteSpace(x.Label)).ToList();
        var access = model.Access.Where(x => x.UserId.HasValue).ToList();
        var notifications = model.Notifications.ToList();
        if (!ModelState.IsValid)
        {
            await PopulateUsersAsync(model.Users, cancellationToken);
            return View(model);
        }

        var result = await administration.CreateAsync(
            new NewDocumentType(
                model.Name,
                model.Key,
                model.Description,
                model.NumberPrefix,
                fields.Select(x => new NewDocumentField(x.Key, x.Label, x.FieldType, x.IsRequired, x.HelpText, x.OptionsCsv)).ToList(),
                access.Select(x => new NewDocumentTypeAccess(x.UserId!.Value, x.AccessRole)).ToList(),
                notifications.Select(x => new NewLifecycleNotificationRule(
                    x.EventType, x.RecipientType, x.NamedUserId, x.UserFieldKey, x.SendInApp, x.SendEmail, x.SendTeams)).ToList()),
            currentUser.UserId!.Value,
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            await PopulateUsersAsync(model.Users, cancellationToken);
            return View(model);
        }

        TempData["Success"] = $"{model.Name.Trim()} was created with an editable workflow draft.";
        return RedirectToAction(nameof(Manage), new { id = result.DocumentTypeId, tab = "workflow" });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Manage(Guid id, string tab = "overview", CancellationToken cancellationToken = default)
    {
        var documentType = await db.DocumentTypes.AsNoTracking()
            .Include(x => x.Fields)
            .Include(x => x.AccessAssignments).ThenInclude(x => x.User)
            .Include(x => x.LifecycleNotificationRules)
            .Include(x => x.Routes).ThenInclude(x => x.Versions).ThenInclude(x => x.Stages)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (documentType is null) return NotFound();

        var usedFieldIds = await db.RequestFieldValues.AsNoTracking()
            .Where(x => x.Request.DocumentTypeId == id)
            .Select(x => x.FieldDefinitionId).Distinct().ToListAsync(cancellationToken);
        var requests = await db.Requests.AsNoTracking()
            .Include(x => x.Requester)
            .Include(x => x.DocumentType)
            .Include(x => x.Approvals)
            .Where(x => x.DocumentTypeId == id)
            .ToListAsync(cancellationToken);
        var versions = documentType.Routes.SelectMany(x => x.Versions).ToList();
        var published = versions.Where(x => x.Status == RouteVersionStatus.Published).OrderByDescending(x => x.VersionNumber).FirstOrDefault();
        var draft = versions.Where(x => x.Status == RouteVersionStatus.Draft).OrderByDescending(x => x.VersionNumber).FirstOrDefault();
        var stageSource = draft ?? published;
        var users = new List<SelectListItem>();
        await PopulateUsersAsync(users, cancellationToken);

        var model = new DocumentTypeConfigurationViewModel
        {
            DocumentType = documentType,
            ActiveTab = NormalizeTab(tab),
            Fields = documentType.Fields.OrderBy(x => x.Sequence).Select(x => new DocumentFieldEditViewModel
            {
                Id = x.Id,
                Key = x.Key,
                Label = x.Label,
                FieldType = x.FieldType,
                Sequence = x.Sequence,
                IsRequired = x.IsRequired,
                HelpText = x.HelpText,
                OptionsCsv = x.OptionsCsv,
                IsUsed = usedFieldIds.Contains(x.Id)
            }).ToList(),
            Access = documentType.AccessAssignments.Where(x => x.IsActive).OrderBy(x => x.AccessRole).ThenBy(x => x.User.FullName)
                .Select(x => new DocumentTypeAccessEditViewModel
                {
                    Id = x.Id,
                    UserId = x.UserId,
                    UserName = x.User.FullName,
                    UserEmail = x.User.Email,
                    AccessRole = x.AccessRole,
                    IsActive = x.IsActive
                }).ToList(),
            Notifications = documentType.LifecycleNotificationRules.OrderBy(x => x.EventType).ThenBy(x => x.RecipientType)
                .Select(x => new LifecycleNotificationRuleEditViewModel
                {
                    Id = x.Id,
                    EventType = x.EventType,
                    StageKey = x.StageKey,
                    RecipientType = x.RecipientType,
                    NamedUserId = x.NamedUserId,
                    UserFieldKey = x.UserFieldKey,
                    SendInApp = x.SendInApp,
                    SendEmail = x.SendEmail,
                    SendTeams = x.SendTeams,
                    IsEnabled = x.IsEnabled,
                    DelayHours = x.DelayHours
                }).ToList(),
            Users = users,
            WorkflowStages = stageSource?.Stages.OrderBy(x => x.Sequence)
                .Select(x => new WorkflowStageOptionViewModel(x.StageKey, x.Name)).ToList() ?? [],
            Requests = requests.OrderByDescending(x => x.CreatedAtUtc).Select(MapManagedRequest).ToList(),
            PublishedVersion = published,
            DraftVersion = draft,
            CanDelete = await administration.CanDeleteAsync(id, cancellationToken)
        };
        return View(model);
    }

    [HttpPost("{id:guid}/overview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateOverview(
        Guid id,
        string? name,
        string? key,
        string? description,
        string? numberPrefix,
        CancellationToken cancellationToken)
    {
        var documentType = await db.DocumentTypes.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (documentType is null) return NotFound();
        name = name?.Trim() ?? "";
        key = key?.Trim().ToLowerInvariant() ?? "";
        numberPrefix = numberPrefix?.Trim().ToUpperInvariant() ?? "";
        if (string.IsNullOrWhiteSpace(name) || !KeyPattern().IsMatch(key) || !PrefixPattern().IsMatch(numberPrefix) ||
            await db.DocumentTypes.AnyAsync(x => x.Id != id && x.Key == key, cancellationToken))
        {
            TempData["Error"] = "Enter a unique lowercase key, a name, and a 2–8 character number prefix.";
            return RedirectToAction(nameof(Manage), new { id, tab = "overview" });
        }
        documentType.Name = name;
        documentType.Key = key;
        documentType.Description = description?.Trim() ?? "";
        documentType.NumberPrefix = numberPrefix;
        AddAudit("DocumentTypeUpdated", $"Updated overview for '{documentType.Name}'.");
        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Document type overview saved.";
        return RedirectToAction(nameof(Manage), new { id, tab = "overview" });
    }

    [HttpPost("{id:guid}/fields/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveField(Guid id, DocumentFieldEditViewModel model, CancellationToken cancellationToken)
    {
        var documentType = await db.DocumentTypes.Include(x => x.Fields).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (documentType is null) return NotFound();
        var key = model.Key.Trim().ToLowerInvariant();
        if (!FieldKeyPattern().IsMatch(key) || string.IsNullOrWhiteSpace(model.Label) ||
            documentType.Fields.Any(x => x.Id != model.Id && x.Key == key) ||
            model.FieldType == DocumentFieldType.Choice && string.IsNullOrWhiteSpace(model.OptionsCsv))
        {
            TempData["Error"] = "Enter a unique field key and label. Choice fields also require options.";
            return RedirectToAction(nameof(Manage), new { id, tab = "fields" });
        }

        DocumentFieldDefinition field;
        if (model.Id.HasValue)
        {
            field = documentType.Fields.SingleOrDefault(x => x.Id == model.Id.Value)!;
            if (field is null) return NotFound();
            var isUsed = await db.RequestFieldValues.AnyAsync(x => x.FieldDefinitionId == field.Id, cancellationToken);
            if (isUsed && (field.Key != key || field.FieldType != model.FieldType))
            {
                TempData["Error"] = "A used field's key and type cannot be changed because historical requests depend on them.";
                return RedirectToAction(nameof(Manage), new { id, tab = "fields" });
            }
        }
        else
        {
            field = new DocumentFieldDefinition
            {
                DocumentType = documentType,
                DocumentTypeId = id,
                Sequence = documentType.Fields.Count == 0 ? 1 : documentType.Fields.Max(x => x.Sequence) + 1
            };
            documentType.Fields.Add(field);
        }

        field.Key = key;
        field.Label = model.Label.Trim();
        field.FieldType = model.FieldType;
        field.IsRequired = model.IsRequired;
        field.HelpText = Clean(model.HelpText);
        field.OptionsCsv = model.FieldType == DocumentFieldType.Choice ? Clean(model.OptionsCsv) : null;
        AddAudit(model.Id.HasValue ? "DocumentFieldUpdated" : "DocumentFieldAdded", $"Saved field '{field.Label}' for '{documentType.Name}'.");
        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Form field saved.";
        return RedirectToAction(nameof(Manage), new { id, tab = "fields" });
    }

    [HttpPost("{id:guid}/fields/{fieldId:guid}/move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveField(Guid id, Guid fieldId, int direction, CancellationToken cancellationToken)
    {
        var fields = await db.DocumentFields.Where(x => x.DocumentTypeId == id).OrderBy(x => x.Sequence).ToListAsync(cancellationToken);
        var index = fields.FindIndex(x => x.Id == fieldId);
        var target = index + Math.Sign(direction);
        if (index < 0) return NotFound();
        if (target >= 0 && target < fields.Count)
        {
            (fields[index].Sequence, fields[target].Sequence) = (fields[target].Sequence, fields[index].Sequence);
            AddAudit("DocumentFieldsReordered", $"Reordered fields for document type {id}.");
            await db.SaveChangesAsync(cancellationToken);
        }
        return RedirectToAction(nameof(Manage), new { id, tab = "fields" });
    }

    [HttpPost("{id:guid}/fields/{fieldId:guid}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveField(Guid id, Guid fieldId, CancellationToken cancellationToken)
    {
        var field = await db.DocumentFields.SingleOrDefaultAsync(x => x.Id == fieldId && x.DocumentTypeId == id, cancellationToken);
        if (field is null) return NotFound();
        var usedByRequest = await db.RequestFieldValues.AnyAsync(x => x.FieldDefinitionId == fieldId, cancellationToken);
        var usedByRoute = await db.RouteConditionRules.AnyAsync(x => x.Group.Stage.RouteVersion.Route.DocumentTypeId == id && x.FieldKey == field.Key, cancellationToken) ||
                          await db.RouteStages.AnyAsync(x => x.RouteVersion.Route.DocumentTypeId == id && x.AssigneeFieldKey == field.Key, cancellationToken);
        if (usedByRequest || usedByRoute)
        {
            TempData["Error"] = "This field is used by request or workflow history and cannot be removed.";
        }
        else
        {
            db.DocumentFields.Remove(field);
            AddAudit("DocumentFieldRemoved", $"Removed unused field '{field.Label}'.");
            await db.SaveChangesAsync(cancellationToken);
            TempData["Success"] = "Unused field removed.";
        }
        return RedirectToAction(nameof(Manage), new { id, tab = "fields" });
    }

    [HttpPost("{id:guid}/access/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAccess(Guid id, DocumentTypeAccessEditViewModel model, CancellationToken cancellationToken)
    {
        if (!await db.DocumentTypes.AnyAsync(x => x.Id == id, cancellationToken)) return NotFound();
        if (!model.UserId.HasValue || !await db.Users.AnyAsync(x => x.Id == model.UserId && x.IsActive, cancellationToken))
        {
            TempData["Error"] = "Select an active user.";
            return RedirectToAction(nameof(Manage), new { id, tab = "access" });
        }
        var assignment = await db.DocumentTypeAccess.SingleOrDefaultAsync(
            x => x.DocumentTypeId == id && x.UserId == model.UserId.Value, cancellationToken);
        if (assignment is null)
        {
            assignment = new DocumentTypeAccess
            {
                DocumentTypeId = id,
                UserId = model.UserId.Value,
                CreatedByUserId = currentUser.UserId
            };
            db.DocumentTypeAccess.Add(assignment);
        }
        assignment.AccessRole = model.AccessRole;
        assignment.IsActive = true;
        AddAudit("DocumentTypeAccessChanged", $"Assigned user {model.UserId} as {model.AccessRole} for document type {id}.");
        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Document type access saved.";
        return RedirectToAction(nameof(Manage), new { id, tab = "access" });
    }

    [HttpPost("{id:guid}/access/{accessId:guid}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveAccess(Guid id, Guid accessId, CancellationToken cancellationToken)
    {
        var assignment = await db.DocumentTypeAccess.SingleOrDefaultAsync(x => x.Id == accessId && x.DocumentTypeId == id, cancellationToken);
        if (assignment is null) return NotFound();
        assignment.IsActive = false;
        AddAudit("DocumentTypeAccessRemoved", $"Removed scoped access assignment {accessId} from document type {id}.");
        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Access assignment removed.";
        return RedirectToAction(nameof(Manage), new { id, tab = "access" });
    }

    [HttpPost("{id:guid}/notifications/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveNotification(Guid id, LifecycleNotificationRuleEditViewModel model, CancellationToken cancellationToken)
    {
        if (!await db.DocumentTypes.AnyAsync(x => x.Id == id, cancellationToken)) return NotFound();
        if (!ModelState.IsValid || model.DelayHours is < 0 or > 720)
        {
            TempData["Error"] = "Enter a notification delay from 0 to 720 hours.";
            return RedirectToAction(nameof(Manage), new { id, tab = "notifications" });
        }
        if (!model.SendInApp && !model.SendEmail && !model.SendTeams)
        {
            TempData["Error"] = "Select at least one notification channel.";
            return RedirectToAction(nameof(Manage), new { id, tab = "notifications" });
        }
        if (model.RecipientType == LifecycleNotificationRecipient.NamedUser &&
            (!model.NamedUserId.HasValue || !await db.Users.AnyAsync(x => x.Id == model.NamedUserId && x.IsActive, cancellationToken)))
        {
            TempData["Error"] = "Select an active named recipient.";
            return RedirectToAction(nameof(Manage), new { id, tab = "notifications" });
        }
        if (model.RecipientType == LifecycleNotificationRecipient.UserFromRequestField &&
            (string.IsNullOrWhiteSpace(model.UserFieldKey) ||
             !await db.DocumentFields.AnyAsync(x => x.DocumentTypeId == id && x.Key == model.UserFieldKey && x.FieldType == DocumentFieldType.User, cancellationToken)))
        {
            TempData["Error"] = "Select a configured user field for this recipient type.";
            return RedirectToAction(nameof(Manage), new { id, tab = "notifications" });
        }
        if (!string.IsNullOrWhiteSpace(model.StageKey) &&
            !await db.RouteStages.AnyAsync(x => x.RouteVersion.Route.DocumentTypeId == id && x.StageKey == model.StageKey, cancellationToken))
        {
            TempData["Error"] = "Select a workflow stage from this document type.";
            return RedirectToAction(nameof(Manage), new { id, tab = "notifications" });
        }

        LifecycleNotificationRule rule;
        if (model.Id.HasValue)
        {
            rule = await db.LifecycleNotificationRules.SingleOrDefaultAsync(x => x.Id == model.Id && x.DocumentTypeId == id, cancellationToken) ?? null!;
            if (rule is null) return NotFound();
        }
        else
        {
            rule = new LifecycleNotificationRule { DocumentTypeId = id, CreatedByUserId = currentUser.UserId };
            db.LifecycleNotificationRules.Add(rule);
        }
        rule.EventType = model.EventType;
        rule.StageKey = model.EventType is LifecycleNotificationEvent.StageStarted or LifecycleNotificationEvent.StageCompleted ? Clean(model.StageKey) : null;
        rule.RecipientType = model.RecipientType;
        rule.NamedUserId = model.RecipientType == LifecycleNotificationRecipient.NamedUser ? model.NamedUserId : null;
        rule.UserFieldKey = model.RecipientType == LifecycleNotificationRecipient.UserFromRequestField ? Clean(model.UserFieldKey) : null;
        rule.SendInApp = model.SendInApp;
        rule.SendEmail = model.SendEmail;
        rule.SendTeams = model.SendTeams;
        rule.DelayHours = model.DelayHours;
        rule.IsEnabled = model.IsEnabled;
        rule.UpdatedAtUtc = DateTimeOffset.UtcNow;
        rule.UpdatedByUserId = currentUser.UserId;
        AddAudit("LifecycleNotificationRuleSaved", $"Saved {rule.EventType} → {rule.RecipientType} notification rule for document type {id}.");
        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Lifecycle notification rule saved.";
        return RedirectToAction(nameof(Manage), new { id, tab = "notifications" });
    }

    [HttpPost("{id:guid}/notifications/{ruleId:guid}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveNotification(Guid id, Guid ruleId, CancellationToken cancellationToken)
    {
        var rule = await db.LifecycleNotificationRules.SingleOrDefaultAsync(x => x.Id == ruleId && x.DocumentTypeId == id, cancellationToken);
        if (rule is null) return NotFound();
        rule.IsEnabled = false;
        rule.UpdatedAtUtc = DateTimeOffset.UtcNow;
        rule.UpdatedByUserId = currentUser.UserId;
        AddAudit("LifecycleNotificationRuleDisabled", $"Disabled lifecycle notification rule {ruleId}.");
        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Lifecycle notification rule disabled; delivery history is retained.";
        return RedirectToAction(nameof(Manage), new { id, tab = "notifications" });
    }

    [HttpPost("{id:guid}/deactivate")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) => SetActive(id, false, cancellationToken);

    [HttpPost("{id:guid}/reactivate")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken) => SetActive(id, true, cancellationToken);

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, string confirmation, CancellationToken cancellationToken)
    {
        var result = await administration.DeleteUnusedAsync(id, confirmation ?? "", currentUser.UserId!.Value, cancellationToken);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Manage), new { id, tab = "overview" });
        }
        TempData["Success"] = "Unused document type permanently deleted.";
        return RedirectToAction(nameof(Index), new { status = "all" });
    }

    private async Task<IActionResult> SetActive(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var result = await administration.SetActiveAsync(id, isActive, currentUser.UserId!.Value, cancellationToken);
        if (!result.Succeeded) return NotFound();
        TempData["Success"] = isActive ? "Document type reactivated." : "Document type deactivated. New requests are now blocked.";
        return RedirectToAction(nameof(Manage), new { id, tab = "overview" });
    }

    private void AddAudit(string eventType, string details) => db.AuditEvents.Add(new AuditEvent
    {
        ActorUserId = currentUser.UserId!.Value,
        EventType = eventType,
        Details = details
    });

    private async Task PopulateUsersAsync(List<SelectListItem> target, CancellationToken cancellationToken)
    {
        target.Clear();
        target.AddRange(await db.Users.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.FullName)
            .Select(x => new SelectListItem(x.FullName + " — " + x.Email, x.Id.ToString())).ToListAsync(cancellationToken));
    }

    private static ManagedRequestRowViewModel MapManagedRequest(ApprovalRequest request) => new()
    {
        Id = request.Id,
        RequestNumber = request.RequestNumber,
        Title = request.Title,
        DocumentTypeName = request.DocumentType.Name,
        RequesterName = request.Requester.FullName,
        Status = request.Status,
        CurrentStep = request.Status switch
        {
            RequestStatus.Approved => "Completed",
            RequestStatus.Rejected => "Revision required",
            RequestStatus.Draft => "Draft",
            _ => request.Approvals.Where(x => x.RevisionNumber == request.CurrentRevisionNumber && x.Status == ApprovalStatus.Pending)
                     .Select(x => x.StageName).SingleOrDefault() ?? "In approval"
        },
        CreatedAtUtc = request.CreatedAtUtc,
        CompletedAtUtc = request.CompletedAtUtc,
        NeedsAttention = request.Status == RequestStatus.Rejected
    };

    private static string NormalizeTab(string tab) => tab.ToLowerInvariant() switch
    {
        "overview" or "fields" or "access" or "notifications" or "workflow" or "requests" => tab.ToLowerInvariant(),
        _ => "overview"
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^[A-Z0-9]{2,8}$")]
    private static partial Regex PrefixPattern();

    [GeneratedRegex("^[a-z0-9]+(?:[-_][a-z0-9]+)*$")]
    private static partial Regex FieldKeyPattern();
}
