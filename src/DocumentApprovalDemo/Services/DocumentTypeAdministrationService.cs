using System.Text.RegularExpressions;
using DocumentApprovalDemo.Data;
using DocumentApprovalDemo.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocumentApprovalDemo.Services;

public sealed record NewDocumentField(
    string Key,
    string Label,
    DocumentFieldType FieldType,
    bool IsRequired,
    string? HelpText,
    string? OptionsCsv);

public sealed record NewDocumentTypeAccess(Guid UserId, DocumentTypeAccessRole AccessRole);

public sealed record NewLifecycleNotificationRule(
    LifecycleNotificationEvent EventType,
    LifecycleNotificationRecipient RecipientType,
    Guid? NamedUserId,
    string? UserFieldKey,
    bool SendInApp,
    bool SendEmail,
    bool SendTeams);

public sealed record NewDocumentType(
    string Name,
    string Key,
    string Description,
    string NumberPrefix,
    IReadOnlyList<NewDocumentField> Fields,
    IReadOnlyList<NewDocumentTypeAccess> Access,
    IReadOnlyList<NewLifecycleNotificationRule> Notifications);

public sealed record DocumentTypeMutationResult(bool Succeeded, string? Error = null, Guid? DocumentTypeId = null);

public interface IDocumentTypeAdministrationService
{
    Task<DocumentTypeMutationResult> CreateAsync(NewDocumentType definition, Guid actorId, CancellationToken cancellationToken = default);
    Task<DocumentTypeMutationResult> SetActiveAsync(Guid documentTypeId, bool isActive, Guid actorId, CancellationToken cancellationToken = default);
    Task<DocumentTypeMutationResult> DeleteUnusedAsync(Guid documentTypeId, string confirmation, Guid actorId, CancellationToken cancellationToken = default);
    Task<bool> CanDeleteAsync(Guid documentTypeId, CancellationToken cancellationToken = default);
}

public sealed partial class DocumentTypeAdministrationService(AppDbContext db) : IDocumentTypeAdministrationService
{
    public async Task<DocumentTypeMutationResult> CreateAsync(
        NewDocumentType definition,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        var name = definition.Name.Trim();
        var key = definition.Key.Trim().ToLowerInvariant();
        var prefix = definition.NumberPrefix.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(name)) return new(false, "Enter a document type name.");
        if (!KeyPattern().IsMatch(key)) return new(false, "Key must use lowercase letters, numbers, and single hyphens.");
        if (!PrefixPattern().IsMatch(prefix)) return new(false, "Number prefix must contain 2–8 letters or numbers.");
        if (await db.DocumentTypes.AnyAsync(x => x.Key == key, cancellationToken))
            return new(false, "That document type key is already in use.");

        var fieldKeys = definition.Fields.Select(x => x.Key.Trim().ToLowerInvariant()).ToList();
        if (fieldKeys.Any(x => !FieldKeyPattern().IsMatch(x)) || fieldKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != fieldKeys.Count)
            return new(false, "Every field needs a unique lowercase key using letters, numbers, hyphens, or underscores.");
        if (definition.Fields.Any(x => string.IsNullOrWhiteSpace(x.Label)))
            return new(false, "Every field needs a label.");
        if (definition.Fields.Any(x => x.FieldType == DocumentFieldType.Choice && string.IsNullOrWhiteSpace(x.OptionsCsv)))
            return new(false, "Choice fields need at least one option.");

        var accessUsers = definition.Access.Select(x => x.UserId).ToList();
        if (accessUsers.Distinct().Count() != accessUsers.Count)
            return new(false, "Assign each person only once per document type.");
        if (accessUsers.Count > 0 && await db.Users.CountAsync(x => accessUsers.Contains(x.Id) && x.IsActive, cancellationToken) != accessUsers.Count)
            return new(false, "Every access assignment must reference an active user.");
        if (definition.Notifications.Any(x => !x.SendInApp && !x.SendEmail && !x.SendTeams))
            return new(false, "Every notification rule needs at least one channel.");
        if (definition.Notifications.Any(x => x.RecipientType == LifecycleNotificationRecipient.NamedUser && !x.NamedUserId.HasValue))
            return new(false, "Named-user notification rules need a recipient.");
        if (definition.Notifications.Any(x => x.RecipientType == LifecycleNotificationRecipient.UserFromRequestField && string.IsNullOrWhiteSpace(x.UserFieldKey)))
            return new(false, "Request-field notification rules need a user field.");
        var namedRecipients = definition.Notifications
            .Where(x => x.RecipientType == LifecycleNotificationRecipient.NamedUser && x.NamedUserId.HasValue)
            .Select(x => x.NamedUserId!.Value)
            .Distinct()
            .ToList();
        if (namedRecipients.Count > 0 &&
            await db.Users.CountAsync(x => namedRecipients.Contains(x.Id) && x.IsActive, cancellationToken) != namedRecipients.Count)
            return new(false, "Every named notification recipient must reference an active user.");
        var userFieldKeys = definition.Fields
            .Where(x => x.FieldType == DocumentFieldType.User)
            .Select(x => x.Key.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (definition.Notifications.Any(x =>
                x.RecipientType == LifecycleNotificationRecipient.UserFromRequestField &&
                !userFieldKeys.Contains(x.UserFieldKey?.Trim() ?? "")))
            return new(false, "Request-field notification rules must reference a configured user field.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var documentType = new DocumentType
        {
            Name = name,
            Key = key,
            Description = definition.Description.Trim(),
            NumberPrefix = prefix,
            IsActive = true
        };

        for (var index = 0; index < definition.Fields.Count; index++)
        {
            var input = definition.Fields[index];
            documentType.Fields.Add(new DocumentFieldDefinition
            {
                DocumentType = documentType,
                Key = input.Key.Trim().ToLowerInvariant(),
                Label = input.Label.Trim(),
                FieldType = input.FieldType,
                Sequence = index + 1,
                IsRequired = input.IsRequired,
                HelpText = Clean(input.HelpText),
                OptionsCsv = Clean(input.OptionsCsv)
            });
        }

        foreach (var assignment in definition.Access)
        {
            documentType.AccessAssignments.Add(new DocumentTypeAccess
            {
                DocumentType = documentType,
                UserId = assignment.UserId,
                AccessRole = assignment.AccessRole,
                CreatedByUserId = actorId
            });
        }

        foreach (var input in definition.Notifications)
        {
            documentType.LifecycleNotificationRules.Add(new LifecycleNotificationRule
            {
                DocumentType = documentType,
                EventType = input.EventType,
                RecipientType = input.RecipientType,
                NamedUserId = input.RecipientType == LifecycleNotificationRecipient.NamedUser ? input.NamedUserId : null,
                UserFieldKey = input.RecipientType == LifecycleNotificationRecipient.UserFromRequestField ? Clean(input.UserFieldKey) : null,
                SendInApp = input.SendInApp,
                SendEmail = input.SendEmail,
                SendTeams = input.SendTeams,
                CreatedByUserId = actorId,
                UpdatedByUserId = actorId
            });
        }

        var route = new ApprovalRoute { DocumentType = documentType, Name = $"{name} Approval" };
        documentType.Routes.Add(route);
        route.Versions.Add(new ApprovalRouteVersion
        {
            Route = route,
            VersionNumber = 1,
            Name = $"{name} initial draft",
            Status = RouteVersionStatus.Draft
        });

        db.DocumentTypes.Add(documentType);
        db.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = actorId,
            EventType = "DocumentTypeCreated",
            Details = $"Created document type '{name}' ({key}) with an initial workflow draft."
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(true, DocumentTypeId: documentType.Id);
    }

    public async Task<DocumentTypeMutationResult> SetActiveAsync(
        Guid documentTypeId,
        bool isActive,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        var documentType = await db.DocumentTypes.SingleOrDefaultAsync(x => x.Id == documentTypeId, cancellationToken);
        if (documentType is null) return new(false, "Document type was not found.");
        if (documentType.IsActive == isActive) return new(true, DocumentTypeId: documentType.Id);

        documentType.IsActive = isActive;
        db.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = actorId,
            EventType = isActive ? "DocumentTypeReactivated" : "DocumentTypeDeactivated",
            Details = $"{(isActive ? "Reactivated" : "Deactivated")} document type '{documentType.Name}'."
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(true, DocumentTypeId: documentType.Id);
    }

    public async Task<DocumentTypeMutationResult> DeleteUnusedAsync(
        Guid documentTypeId,
        string confirmation,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        var documentType = await db.DocumentTypes
            .Include(x => x.Routes).ThenInclude(x => x.Versions)
            .SingleOrDefaultAsync(x => x.Id == documentTypeId, cancellationToken);
        if (documentType is null) return new(false, "Document type was not found.");
        if (!string.Equals(confirmation.Trim(), documentType.Name, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(confirmation.Trim(), "DELETE", StringComparison.Ordinal))
            return new(false, $"Type DELETE or {documentType.Name} to confirm permanent deletion.");
        if (!await CanDeleteAsync(documentTypeId, cancellationToken))
            return new(false, "This document type has operational history. Deactivate it instead of deleting it.");

        var name = documentType.Name;
        db.Routes.RemoveRange(documentType.Routes);
        db.DocumentTypes.Remove(documentType);
        db.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = actorId,
            EventType = "UnusedDocumentTypeDeleted",
            Details = $"Permanently deleted unused document type '{name}'."
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(true);
    }

    public async Task<bool> CanDeleteAsync(Guid documentTypeId, CancellationToken cancellationToken = default)
    {
        if (await db.Requests.AsNoTracking().AnyAsync(x => x.DocumentTypeId == documentTypeId, cancellationToken)) return false;
        return !await db.RouteVersions.AsNoTracking().AnyAsync(
            x => x.Route.DocumentTypeId == documentTypeId &&
                 (x.Status == RouteVersionStatus.Published || x.Status == RouteVersionStatus.Retired),
            cancellationToken);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^[A-Z0-9]{2,8}$")]
    private static partial Regex PrefixPattern();

    [GeneratedRegex("^[a-z0-9]+(?:[-_][a-z0-9]+)*$")]
    private static partial Regex FieldKeyPattern();
}
