using System.Globalization;
using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using DocumentApprovalDemo.Services;
using DocumentApprovalDemo.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace DocumentApprovalDemo.Controllers;

[Authorize]
[Route("requests")]
public sealed class RequestsController(
    AppDbContext db,
    ICurrentUserService currentUser,
    IWorkflowService workflow,
    IFileStorageService fileStorage,
    ISignedPackageService signedPackage) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId!.Value;
        var requests = await db.Requests.AsNoTracking()
            .Include(x => x.DocumentType)
            .Where(x => x.RequesterId == userId)
            .ToListAsync(cancellationToken);
        return View(requests.OrderByDescending(x => x.CreatedAtUtc).ToList());
    }

    [HttpGet("new")]
    public async Task<IActionResult> Create(Guid? documentTypeId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken) ?? throw new UnauthorizedAccessException();
        var type = await SelectDocumentTypeAsync(documentTypeId, cancellationToken);
        if (type is null) return Problem("No active document types are configured.");
        var model = new RequestFormViewModel
        {
            DocumentTypeId = type.Id,
            Title = "",
            Department = user.Department,
            ManagerId = user.ManagerId,
            ManagerSource = user.ManagerId.HasValue ? "Entra" : "RequesterSelected",
            Fields = BuildFields(type, Array.Empty<DynamicFieldInputViewModel>(), revisionNumber: 1)
        };
        await PopulateFormAsync(model, user.Id, type, cancellationToken);
        return View(model);
    }

    [HttpPost("new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RequestFormViewModel model, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetAsync(cancellationToken) ?? throw new UnauthorizedAccessException();
        var type = model.DocumentTypeId.HasValue
            ? await db.DocumentTypes.Include(x => x.Fields).SingleOrDefaultAsync(x => x.Id == model.DocumentTypeId && x.IsActive, cancellationToken)
            : null;
        if (type is null)
        {
            ModelState.AddModelError(nameof(model.DocumentTypeId), "Select an active document type.");
            type = await SelectDocumentTypeAsync(null, cancellationToken);
        }
        if (type is null) return Problem("No active document types are configured.");

        model.Fields = BuildFields(type, model.Fields, revisionNumber: 1);
        await ValidateDynamicFieldsAsync(model.Fields, cancellationToken);
        if (model.ManagerId == user.Id) ModelState.AddModelError(nameof(model.ManagerId), "You cannot select yourself as manager.");
        if (model.SupportingDocuments.Count == 0 || model.SupportingDocuments.All(x => x.Length == 0))
            ModelState.AddModelError(nameof(model.SupportingDocuments), "At least one supporting document is required.");
        var manager = model.ManagerId.HasValue
            ? await db.Users.SingleOrDefaultAsync(x => x.Id == model.ManagerId && x.IsActive, cancellationToken)
            : null;
        if (manager is null) ModelState.AddModelError(nameof(model.ManagerId), "Select an active manager.");
        if (!ModelState.IsValid)
        {
            await PopulateFormAsync(model, user.Id, type, cancellationToken);
            return View(model);
        }

        var request = new ApprovalRequest
        {
            RequestNumber = await NextRequestNumberAsync(type.NumberPrefix, cancellationToken),
            DocumentTypeId = type.Id,
            DocumentType = type,
            RequesterId = user.Id,
            ConfirmedManagerId = manager!.Id,
            ManagerSource = user.ManagerId == manager.Id ? "EntraConfirmed" : "RequesterSelected",
            Title = model.Title.Trim(),
            Department = model.Department.Trim(),
            CurrentRevisionNumber = 1
        };
        var displayValues = await BuildDisplayValuesAsync(model.Fields, cancellationToken);
        foreach (var field in model.Fields)
        {
            request.FieldValues.Add(new RequestFieldValue
            {
                Request = request,
                RevisionNumber = 1,
                FieldDefinitionId = field.FieldDefinitionId,
                FieldKey = field.Key,
                Label = field.Label,
                FieldType = field.FieldType,
                Value = field.Value?.Trim() ?? "",
                DisplayValue = displayValues.GetValueOrDefault(field.FieldDefinitionId),
                Sequence = field.Sequence
            });
        }
        request.Revisions.Add(new RequestRevision { Request = request, RevisionNumber = 1, ChangeSummary = "Initial submission" });

        foreach (var file in model.SupportingDocuments.Where(x => x.Length > 0))
        {
            try
            {
                var stored = await fileStorage.SaveAsync(file, cancellationToken);
                request.Attachments.Add(new RequestAttachment
                {
                    Request = request,
                    RevisionNumber = 1,
                    OriginalFileName = stored.OriginalFileName,
                    StoredFileName = stored.StoredFileName,
                    ContentType = stored.ContentType,
                    SizeBytes = stored.SizeBytes
                });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(nameof(model.SupportingDocuments), ex.Message);
                await PopulateFormAsync(model, user.Id, type, cancellationToken);
                return View(model);
            }
        }

        db.Requests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        await workflow.StartAsync(request, user, cancellationToken);
        return RedirectToAction(nameof(Details), new { id = request.Id });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var request = await LoadDetailsAsync(id, cancellationToken);
        if (request is null) return NotFound();
        if (!await CanViewAsync(request, cancellationToken)) return Forbid();
        return View(request);
    }

    [HttpGet("{id:guid}/revise")]
    public async Task<IActionResult> Revise(Guid id, CancellationToken cancellationToken)
    {
        var request = await db.Requests.AsNoTracking()
            .Include(x => x.DocumentType).ThenInclude(x => x.Fields)
            .Include(x => x.FieldValues)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (request is null) return NotFound();
        if (request.RequesterId != currentUser.UserId || request.Status != RequestStatus.Rejected) return Forbid();
        var model = new ReviseRequestViewModel
        {
            RequestId = request.Id,
            Title = request.Title,
            DocumentTypeName = request.DocumentType.Name,
            Fields = BuildFields(request.DocumentType, request.FieldValues.Where(x => x.RevisionNumber == request.CurrentRevisionNumber), request.CurrentRevisionNumber)
        };
        await PopulateUsersAsync(model.Users, cancellationToken);
        return View(model);
    }

    [HttpPost("{id:guid}/revise")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revise(Guid id, ReviseRequestViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.RequestId) return BadRequest();
        var request = await db.Requests
            .Include(x => x.DocumentType).ThenInclude(x => x.Fields)
            .Include(x => x.FieldValues)
            .Include(x => x.Revisions)
            .Include(x => x.Approvals)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (request is null) return NotFound();
        var user = await currentUser.GetAsync(cancellationToken) ?? throw new UnauthorizedAccessException();
        if (request.RequesterId != user.Id || request.Status != RequestStatus.Rejected) return Forbid();

        model.DocumentTypeName = request.DocumentType.Name;
        model.Fields = BuildFields(request.DocumentType, model.Fields, request.CurrentRevisionNumber + 1);
        await ValidateDynamicFieldsAsync(model.Fields, cancellationToken);
        if (model.SupportingDocuments.Count == 0 || model.SupportingDocuments.All(x => x.Length == 0))
            ModelState.AddModelError(nameof(model.SupportingDocuments), "Upload at least one supporting document for the new revision.");
        if (!ModelState.IsValid)
        {
            await PopulateUsersAsync(model.Users, cancellationToken);
            return View(model);
        }

        request.Title = model.Title.Trim();
        var nextRevision = request.CurrentRevisionNumber + 1;
        var displayValues = await BuildDisplayValuesAsync(model.Fields, cancellationToken);
        foreach (var field in model.Fields)
        {
            request.FieldValues.Add(new RequestFieldValue
            {
                Request = request,
                RevisionNumber = nextRevision,
                FieldDefinitionId = field.FieldDefinitionId,
                FieldKey = field.Key,
                Label = field.Label,
                FieldType = field.FieldType,
                Value = field.Value?.Trim() ?? "",
                DisplayValue = displayValues.GetValueOrDefault(field.FieldDefinitionId),
                Sequence = field.Sequence
            });
        }
        foreach (var file in model.SupportingDocuments.Where(x => x.Length > 0))
        {
            try
            {
                var stored = await fileStorage.SaveAsync(file, cancellationToken);
                request.Attachments.Add(new RequestAttachment
                {
                    Request = request,
                    RevisionNumber = nextRevision,
                    OriginalFileName = stored.OriginalFileName,
                    StoredFileName = stored.StoredFileName,
                    ContentType = stored.ContentType,
                    SizeBytes = stored.SizeBytes
                });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(nameof(model.SupportingDocuments), ex.Message);
                await PopulateUsersAsync(model.Users, cancellationToken);
                return View(model);
            }
        }
        await workflow.RestartAsync(request, user, model.ChangeSummary, cancellationToken);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("{requestId:guid}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> Attachment(Guid requestId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var request = await db.Requests.Include(x => x.Approvals).SingleOrDefaultAsync(x => x.Id == requestId, cancellationToken);
        if (request is null) return NotFound();
        if (!await CanViewAsync(request, cancellationToken)) return Forbid();
        var attachment = await db.Attachments.SingleOrDefaultAsync(x => x.Id == attachmentId && x.RequestId == requestId, cancellationToken);
        if (attachment is null) return NotFound();
        var stream = await fileStorage.OpenReadAsync(attachment.StoredFileName, cancellationToken);
        return File(stream, attachment.ContentType, attachment.OriginalFileName);
    }

    [HttpGet("{id:guid}/signed-package")]
    public async Task<IActionResult> SignedPackage(Guid id, CancellationToken cancellationToken)
    {
        var request = await LoadDetailsAsync(id, cancellationToken);
        if (request is null) return NotFound();
        if (!await CanViewAsync(request, cancellationToken)) return Forbid();
        if (request.Status != RequestStatus.Approved) return BadRequest("The signed package is available after final approval.");
        var bytes = signedPackage.Build(request);
        return File(bytes, "application/pdf", $"{request.RequestNumber}-signed-package.pdf");
    }

    private async Task<bool> CanViewAsync(ApprovalRequest request, CancellationToken cancellationToken) =>
        request.RequesterId == currentUser.UserId ||
        request.Approvals.Any(x => x.ApproverId == currentUser.UserId) ||
        User.IsInRole(Roles.SystemAdmin) ||
        await db.NotificationOutbox.AnyAsync(x => x.RequestId == request.Id && x.UserId == currentUser.UserId, cancellationToken);

    private Task<ApprovalRequest?> LoadDetailsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Requests
            .Include(x => x.DocumentType)
            .Include(x => x.FieldValues)
            .Include(x => x.Requester).Include(x => x.ConfirmedManager).Include(x => x.RouteVersion)
            .Include(x => x.Revisions)
            .Include(x => x.Attachments)
            .Include(x => x.Approvals).ThenInclude(x => x.Approver)
            .Include(x => x.Approvals).ThenInclude(x => x.Decision)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    private async Task<DocumentType?> SelectDocumentTypeAsync(Guid? id, CancellationToken cancellationToken)
    {
        var query = db.DocumentTypes.AsNoTracking().Include(x => x.Fields).Where(x => x.IsActive);
        if (id.HasValue)
        {
            var selected = await query.SingleOrDefaultAsync(x => x.Id == id.Value, cancellationToken);
            if (selected is not null) return selected;
        }
        return await query.OrderBy(x => x.Name).FirstOrDefaultAsync(cancellationToken);
    }

    private async Task PopulateFormAsync(RequestFormViewModel model, Guid userId, DocumentType type, CancellationToken cancellationToken)
    {
        model.DocumentTypeId = type.Id;
        model.DocumentTypeName = type.Name;
        model.DocumentTypeDescription = type.Description;
        model.DocumentTypes = await db.DocumentTypes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString(), x.Id == type.Id)).ToListAsync(cancellationToken);
        model.Managers = await db.Users.AsNoTracking().Where(x => x.IsActive && x.Id != userId)
            .OrderBy(x => x.FullName).Select(x => new SelectListItem(x.FullName + " — " + x.Email, x.Id.ToString())).ToListAsync(cancellationToken);
        await PopulateUsersAsync(model.Users, cancellationToken);

        var route = await db.RouteVersions.AsNoTracking()
            .Include(x => x.Stages).ThenInclude(x => x.Rules)
            .Include(x => x.Stages).ThenInclude(x => x.NamedApprover)
            .Include(x => x.Stages).ThenInclude(x => x.AlertPolicies)
            .SingleAsync(x => x.Route.DocumentTypeId == type.Id && x.Status == RouteVersionStatus.Published, cancellationToken);
        model.RoutePreview = route.Stages.OrderBy(x => x.Sequence).Select(stage => new RouteStagePreviewViewModel
        {
            Sequence = stage.Sequence,
            Name = stage.Name,
            Assignment = stage.AssignmentStrategy switch
            {
                AssignmentStrategy.RequesterManager => "Requester manager",
                AssignmentStrategy.NamedUser => stage.NamedApprover?.FullName ?? "Named user not set",
                AssignmentStrategy.UserField => $"Person selected in {type.Fields.FirstOrDefault(x => x.Key == stage.AssigneeFieldKey)?.Label ?? stage.AssigneeFieldKey}",
                _ => "Unknown"
            },
            Condition = stage.IsConditional
                ? string.Join(" and ", stage.Rules.Select(x => $"{type.Fields.FirstOrDefault(f => f.Key == x.FieldKey)?.Label ?? x.FieldKey} {x.Operator} {x.Value}"))
                : null,
            Alerts = BuildAlertSummary(stage.AlertPolicies)
        }).ToList();
    }

    private static string BuildAlertSummary(IEnumerable<AlertPolicy> policies)
    {
        var active = policies.Where(x => x.IsEnabled).OrderBy(x => x.DelayHours).ToList();
        var assignment = active.Any(x => x.EventType == AlertEventType.Assignment) ? "immediate" : "no assignment alert";
        var reminder = active.FirstOrDefault(x => x.EventType == AlertEventType.Reminder);
        var escalation = active.FirstOrDefault(x => x.EventType == AlertEventType.Escalation);
        return $"{assignment}; reminder {(reminder is null ? "off" : $"{reminder.DelayHours}h")}; escalation {(escalation is null ? "off" : $"{escalation.DelayHours}h")}";
    }

    private async Task PopulateUsersAsync(List<SelectListItem> target, CancellationToken cancellationToken)
    {
        target.Clear();
        target.AddRange(await db.Users.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.FullName)
            .Select(x => new SelectListItem(x.FullName + " — " + x.Email, x.Id.ToString())).ToListAsync(cancellationToken));
    }

    private static List<DynamicFieldInputViewModel> BuildFields(DocumentType type, IEnumerable<DynamicFieldInputViewModel> posted, int revisionNumber)
    {
        var postedById = posted.GroupBy(x => x.FieldDefinitionId).ToDictionary(x => x.Key, x => x.First().Value);
        return type.Fields.OrderBy(x => x.Sequence).Select(definition => new DynamicFieldInputViewModel
        {
            FieldDefinitionId = definition.Id,
            Key = definition.Key,
            Label = definition.Label,
            FieldType = definition.FieldType,
            Sequence = definition.Sequence,
            IsRequired = definition.IsRequired,
            HelpText = definition.HelpText,
            OptionsCsv = definition.OptionsCsv,
            Value = postedById.GetValueOrDefault(definition.Id)
        }).ToList();
    }

    private static List<DynamicFieldInputViewModel> BuildFields(DocumentType type, IEnumerable<RequestFieldValue> values, int revisionNumber)
    {
        var valuesById = values.Where(x => x.RevisionNumber == revisionNumber).ToDictionary(x => x.FieldDefinitionId, x => x.Value);
        return type.Fields.OrderBy(x => x.Sequence).Select(definition => new DynamicFieldInputViewModel
        {
            FieldDefinitionId = definition.Id,
            Key = definition.Key,
            Label = definition.Label,
            FieldType = definition.FieldType,
            Sequence = definition.Sequence,
            IsRequired = definition.IsRequired,
            HelpText = definition.HelpText,
            OptionsCsv = definition.OptionsCsv,
            Value = valuesById.GetValueOrDefault(definition.Id)
        }).ToList();
    }

    private async Task ValidateDynamicFieldsAsync(List<DynamicFieldInputViewModel> fields, CancellationToken cancellationToken)
    {
        for (var index = 0; index < fields.Count; index++)
        {
            var field = fields[index];
            var value = field.Value?.Trim() ?? "";
            var key = $"Fields[{index}].Value";
            if (field.IsRequired && string.IsNullOrWhiteSpace(value))
            {
                ModelState.AddModelError(key, $"{field.Label} is required.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(value)) continue;

            switch (field.FieldType)
            {
                case DocumentFieldType.Currency:
                    if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) &&
                        !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
                        ModelState.AddModelError(key, $"Enter a valid amount for {field.Label}.");
                    else if (amount < 0)
                        ModelState.AddModelError(key, $"{field.Label} cannot be negative.");
                    else
                        field.Value = amount.ToString(CultureInfo.InvariantCulture);
                    break;
                case DocumentFieldType.Date:
                    if (!DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        ModelState.AddModelError(key, $"Enter a valid date for {field.Label}.");
                    else
                        field.Value = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    break;
                case DocumentFieldType.Url:
                    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                        ModelState.AddModelError(key, $"Enter a complete http or https URL for {field.Label}.");
                    break;
                case DocumentFieldType.Choice:
                    if (!field.Options.Contains(value, StringComparer.OrdinalIgnoreCase))
                        ModelState.AddModelError(key, $"Select a valid option for {field.Label}.");
                    break;
                case DocumentFieldType.Boolean:
                    if (!bool.TryParse(value, out _)) ModelState.AddModelError(key, $"Select yes or no for {field.Label}.");
                    break;
                case DocumentFieldType.User:
                    if (!Guid.TryParse(value, out var userId) || !await db.Users.AnyAsync(x => x.Id == userId && x.IsActive, cancellationToken))
                        ModelState.AddModelError(key, $"Select an active user for {field.Label}.");
                    break;
                default:
                    if (value.Length > 8000) ModelState.AddModelError(key, $"{field.Label} is too long.");
                    break;
            }
        }
    }

    private async Task<Dictionary<Guid, string>> BuildDisplayValuesAsync(
        IEnumerable<DynamicFieldInputViewModel> fields,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, string>();
        foreach (var field in fields.Where(x => x.FieldType == DocumentFieldType.User && Guid.TryParse(x.Value, out _)))
        {
            var userId = Guid.Parse(field.Value!);
            var name = await db.Users.Where(x => x.Id == userId).Select(x => x.FullName).SingleAsync(cancellationToken);
            result[field.FieldDefinitionId] = name;
        }
        return result;
    }

    private async Task<string> NextRequestNumberAsync(string numberPrefix, CancellationToken cancellationToken)
    {
        var year = DateTimeOffset.UtcNow.Year;
        var prefix = $"{numberPrefix}-{year}-";
        var existingNumbers = await db.Requests.AsNoTracking()
            .Where(x => x.RequestNumber.StartsWith(prefix))
            .Select(x => x.RequestNumber)
            .ToListAsync(cancellationToken);
        var next = existingNumbers
            .Select(x => int.TryParse(x[prefix.Length..], out var sequence) ? sequence : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
        return $"{prefix}{next:0000}";
    }
}
