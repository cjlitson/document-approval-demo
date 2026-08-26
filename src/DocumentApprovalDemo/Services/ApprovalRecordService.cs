using System.Globalization;
using DocumentApprovalDemo.Domain;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace DocumentApprovalDemo.Services;

public sealed record ApprovalRecordValue(string Label, string Value);
public sealed record ApprovalRecordApproval(
    int Sequence,
    string Stage,
    string Status,
    string? Approver,
    string? ApproverEmail,
    string? Signature,
    string? Decision,
    DateTimeOffset? DecidedAtUtc,
    string? Comments,
    string? ConditionExplanation);
public sealed record ApprovalRecordAttachment(string FileName, int Revision, string ContentType, long SizeBytes);
public sealed record ApprovalRecordHistory(string EventType, string Details, DateTimeOffset OccurredAtUtc);

public sealed class ApprovalRecordModel
{
    public string RequestNumber { get; init; } = "";
    public string DocumentType { get; init; } = "";
    public string Title { get; init; } = "";
    public string Status { get; init; } = "";
    public string Requester { get; init; } = "";
    public string RequesterEmail { get; init; } = "";
    public string Department { get; init; } = "";
    public DateTimeOffset? SubmittedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public int Revision { get; init; }
    public string RouteVersion { get; init; } = "";
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public IReadOnlyList<ApprovalRecordValue> RequestValues { get; init; } = [];
    public IReadOnlyList<ApprovalRecordApproval> Approvals { get; init; } = [];
    public IReadOnlyList<ApprovalRecordHistory> History { get; init; } = [];
    public IReadOnlyList<ApprovalRecordAttachment> Attachments { get; init; } = [];
}

public interface IApprovalRecordService
{
    ApprovalRecordModel CreateModel(ApprovalRequest request, IReadOnlyList<AuditEvent>? history = null);
    byte[] Build(ApprovalRequest request, IReadOnlyList<AuditEvent>? history = null);
}

public sealed class ApprovalRecordService : IApprovalRecordService
{
    public ApprovalRecordService() => ApprovalRecordFontResolver.Configure();

    public ApprovalRecordModel CreateModel(ApprovalRequest request, IReadOnlyList<AuditEvent>? history = null)
    {
        var currentApprovals = request.Approvals
            .Where(x => x.RevisionNumber == request.CurrentRevisionNumber)
            .ToDictionary(x => x.RouteStageId);
        var routeStages = request.RouteVersion?.Stages.OrderBy(x => x.Sequence).ToList() ?? [];
        var evidence = new List<ApprovalRecordApproval>();

        if (routeStages.Count > 0)
        {
            foreach (var stage in routeStages)
            {
                currentApprovals.TryGetValue(stage.Id, out var approval);
                evidence.Add(MapApproval(stage.Sequence, stage.Name, approval,
                    ConditionExplanation(stage, request.DocumentType.Fields)));
            }
        }
        else
        {
            evidence.AddRange(request.Approvals
                .Where(x => x.RevisionNumber == request.CurrentRevisionNumber)
                .OrderBy(x => x.Sequence)
                .Select(x => MapApproval(x.Sequence, x.StageName, x, null)));
        }

        return new ApprovalRecordModel
        {
            RequestNumber = request.RequestNumber,
            DocumentType = request.DocumentType.Name,
            Title = request.Title,
            Status = request.Status.ToString(),
            Requester = request.Requester.FullName,
            RequesterEmail = request.Requester.Email,
            Department = request.Department,
            SubmittedAtUtc = request.SubmittedAtUtc,
            CompletedAtUtc = request.CompletedAtUtc,
            Revision = request.CurrentRevisionNumber,
            RouteVersion = request.RouteVersion is null ? "Not assigned" : $"{request.RouteVersion.Name} · v{request.RouteVersion.VersionNumber}",
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            RequestValues = request.FieldValues
                .Where(x => x.RevisionNumber == request.CurrentRevisionNumber)
                .OrderBy(x => x.Sequence)
                .Select(x => new ApprovalRecordValue(x.Label, FormatValue(x)))
                .ToList(),
            Approvals = evidence,
            History = (history ?? [])
                .OrderBy(x => x.OccurredAtUtc)
                .Select(x => new ApprovalRecordHistory(x.EventType, x.Details, x.OccurredAtUtc))
                .ToList(),
            Attachments = request.Attachments.OrderBy(x => x.RevisionNumber).ThenBy(x => x.OriginalFileName)
                .Select(x => new ApprovalRecordAttachment(x.OriginalFileName, x.RevisionNumber, FriendlyType(x), x.SizeBytes))
                .ToList()
        };
    }

    public byte[] Build(ApprovalRequest request, IReadOnlyList<AuditEvent>? history = null)
    {
        var model = CreateModel(request, history);
        var document = BuildDocument(model);
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var output = new MemoryStream();
        renderer.PdfDocument.Save(output, false);
        return output.ToArray();
    }

    private static Document BuildDocument(ApprovalRecordModel model)
    {
        var document = new Document();
        document.Info.Title = $"{model.RequestNumber} Approval Record";
        document.Info.Subject = "Document workflow approval evidence";
        document.Info.Author = "Document Routing";

        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = ApprovalRecordFontResolver.FamilyName;
        normal.Font.Size = 9;
        normal.Font.Color = Color.FromRgb(31, 41, 55);
        var heading1 = document.Styles[StyleNames.Heading1]!;
        heading1.Font.Name = ApprovalRecordFontResolver.FamilyName;
        heading1.Font.Bold = true;
        heading1.Font.Size = 15;
        heading1.Font.Color = Color.FromRgb(15, 23, 42);
        heading1.ParagraphFormat.SpaceBefore = Unit.FromPoint(16);
        heading1.ParagraphFormat.SpaceAfter = Unit.FromPoint(7);
        var heading2 = document.Styles[StyleNames.Heading2]!;
        heading2.Font.Name = ApprovalRecordFontResolver.FamilyName;
        heading2.Font.Bold = true;
        heading2.Font.Size = 10;
        heading2.Font.Color = Color.FromRgb(30, 64, 175);
        heading2.ParagraphFormat.SpaceBefore = Unit.FromPoint(13);
        heading2.ParagraphFormat.SpaceAfter = Unit.FromPoint(5);

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.Letter;
        section.PageSetup.TopMargin = Unit.FromInch(0.68);
        section.PageSetup.BottomMargin = Unit.FromInch(0.68);
        section.PageSetup.LeftMargin = Unit.FromInch(0.7);
        section.PageSetup.RightMargin = Unit.FromInch(0.7);

        var header = section.Headers.Primary.AddParagraph();
        header.AddFormattedText("DOCUMENT APPROVAL RECORD", TextFormat.Bold);
        header.AddTab();
        header.AddText(model.RequestNumber);
        header.Format.TabStops.AddTabStop(Unit.FromInch(6.8), TabAlignment.Right);
        header.Format.Font.Size = 7.5;
        header.Format.Font.Color = Color.FromRgb(71, 85, 105);
        header.Format.Borders.Bottom.Width = Unit.FromPoint(0.5);
        header.Format.Borders.Bottom.Color = Color.FromRgb(203, 213, 225);
        header.Format.SpaceAfter = Unit.FromPoint(6);

        var footer = section.Footers.Primary.AddParagraph();
        footer.AddText($"{model.RequestNumber} · Generated {model.GeneratedAtUtc:u} · Page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();
        footer.Format.Alignment = ParagraphAlignment.Center;
        footer.Format.Font.Size = 7;
        footer.Format.Font.Color = Color.FromRgb(100, 116, 139);

        var title = section.AddParagraph();
        title.AddFormattedText("DOCUMENT APPROVAL RECORD", TextFormat.Bold);
        title.Format.Font.Size = 20;
        title.Format.Font.Color = Color.FromRgb(15, 23, 42);
        title.Format.SpaceAfter = Unit.FromPoint(3);
        var subtitle = section.AddParagraph($"{model.DocumentType} · {model.RequestNumber}");
        subtitle.Format.Font.Size = 10;
        subtitle.Format.Font.Color = Color.FromRgb(71, 85, 105);
        subtitle.Format.SpaceAfter = Unit.FromPoint(13);

        AddSummaryTable(section, model);
        AddApprovalSummary(section, model);

        section.AddParagraph("REQUEST INFORMATION", StyleNames.Heading1);
        var values = new List<ApprovalRecordValue>
        {
            new("Title", model.Title),
            new("Requester", $"{model.Requester} ({model.RequesterEmail})"),
            new("Department", model.Department)
        };
        values.AddRange(model.RequestValues);
        AddKeyValueTable(section, values);

        section.AddParagraph("APPROVAL EVIDENCE", StyleNames.Heading1);
        foreach (var approval in model.Approvals)
        {
            var heading = section.AddParagraph();
            heading.AddFormattedText($"{approval.Sequence}. {approval.Stage}", TextFormat.Bold);
            heading.AddText($"  ·  {approval.Status}");
            heading.Format.SpaceBefore = Unit.FromPoint(7);
            heading.Format.SpaceAfter = Unit.FromPoint(3);
            var rows = new List<ApprovalRecordValue>
            {
                new("Authenticated approver", string.IsNullOrWhiteSpace(approval.Approver) ? "Not applicable" : $"{approval.Approver} ({approval.ApproverEmail})"),
                new("Decision", approval.Decision ?? approval.Status),
                new("Decision timestamp", FormatDate(approval.DecidedAtUtc)),
                new("Adopted signature", approval.Signature ?? "Not applicable")
            };
            if (!string.IsNullOrWhiteSpace(approval.Comments)) rows.Add(new("Comments", approval.Comments));
            if (!string.IsNullOrWhiteSpace(approval.ConditionExplanation)) rows.Add(new("Condition", approval.ConditionExplanation));
            AddKeyValueTable(section, rows);
        }

        section.AddParagraph("WORKFLOW HISTORY", StyleNames.Heading1);
        if (model.History.Count == 0)
            section.AddParagraph("No additional audit events were supplied for this generated record.");
        else
            AddHistoryTable(section, model.History);

        section.AddParagraph("DOCUMENT INDEX", StyleNames.Heading1);
        if (model.Attachments.Count == 0)
            section.AddParagraph("No supporting documents are attached.");
        else
            AddAttachmentTable(section, model.Attachments);

        var note = section.AddParagraph();
        note.Format.SpaceBefore = Unit.FromPoint(14);
        note.Format.Borders.Top.Width = Unit.FromPoint(0.5);
        note.Format.Borders.Top.Color = Color.FromRgb(203, 213, 225);
        note.Format.Font.Size = 7.5;
        note.Format.Font.Color = Color.FromRgb(71, 85, 105);
        note.AddText("This record summarizes authenticated workflow evidence and adopted signatures retained by the application. Original documents remain separate package items.");
        return document;
    }

    private static void AddSummaryTable(Section section, ApprovalRecordModel model)
    {
        var table = BaseTable();
        table.AddColumn(Unit.FromInch(1.2));
        table.AddColumn(Unit.FromInch(2.25));
        table.AddColumn(Unit.FromInch(1.25));
        table.AddColumn(Unit.FromInch(2.25));
        AddPairRow(table, "Document Type", model.DocumentType, "Status", model.Status);
        AddPairRow(table, "Title", model.Title, "Revision", model.Revision.ToString(CultureInfo.InvariantCulture));
        AddPairRow(table, "Requested By", model.Requester, "Department", model.Department);
        AddPairRow(table, "Submitted", FormatDate(model.SubmittedAtUtc), "Completed", FormatDate(model.CompletedAtUtc));
        AddPairRow(table, "Workflow", model.RouteVersion, "Generated", FormatDate(model.GeneratedAtUtc));
        section.Add(table);
    }

    private static void AddApprovalSummary(Section section, ApprovalRecordModel model)
    {
        section.AddParagraph("APPROVAL SUMMARY", StyleNames.Heading1);
        var table = BaseTable();
        table.AddColumn(Unit.FromInch(0.42));
        table.AddColumn(Unit.FromInch(2.25));
        table.AddColumn(Unit.FromInch(1.2));
        table.AddColumn(Unit.FromInch(1.75));
        table.AddColumn(Unit.FromInch(1.55));
        AddHeader(table, "#", "Stage", "Status", "Approver", "Decision date");
        foreach (var item in model.Approvals)
            AddRow(table, item.Sequence.ToString(CultureInfo.InvariantCulture), item.Stage, item.Status, item.Approver ?? "Not required", FormatDate(item.DecidedAtUtc));
        section.Add(table);
    }

    private static void AddKeyValueTable(Section section, IReadOnlyList<ApprovalRecordValue> values)
    {
        var table = BaseTable();
        table.AddColumn(Unit.FromInch(1.65));
        table.AddColumn(Unit.FromInch(5.52));
        foreach (var value in values)
        {
            var row = table.AddRow();
            StyleLabelCell(row.Cells[0], value.Label);
            row.Cells[1].AddParagraph(string.IsNullOrWhiteSpace(value.Value) ? "—" : value.Value);
            Pad(row);
        }
        section.Add(table);
    }

    private static void AddHistoryTable(Section section, IReadOnlyList<ApprovalRecordHistory> history)
    {
        var table = BaseTable();
        table.AddColumn(Unit.FromInch(1.38));
        table.AddColumn(Unit.FromInch(1.48));
        table.AddColumn(Unit.FromInch(4.31));
        AddHeader(table, "Timestamp", "Event", "Details");
        foreach (var item in history) AddRow(table, FormatDate(item.OccurredAtUtc), item.EventType, item.Details);
        section.Add(table);
    }

    private static void AddAttachmentTable(Section section, IReadOnlyList<ApprovalRecordAttachment> attachments)
    {
        var table = BaseTable();
        table.AddColumn(Unit.FromInch(3.45));
        table.AddColumn(Unit.FromInch(0.72));
        table.AddColumn(Unit.FromInch(1.72));
        table.AddColumn(Unit.FromInch(1.28));
        AddHeader(table, "Original filename", "Revision", "Type", "Size");
        foreach (var item in attachments)
            AddRow(table, item.FileName, item.Revision.ToString(CultureInfo.InvariantCulture), item.ContentType, FormatSize(item.SizeBytes));
        section.Add(table);
    }

    private static Table BaseTable()
    {
        var table = new Table();
        table.Borders.Width = Unit.FromPoint(0.45);
        table.Borders.Color = Color.FromRgb(203, 213, 225);
        table.Rows.LeftIndent = 0;
        return table;
    }

    private static void AddPairRow(Table table, string label1, string value1, string label2, string value2)
    {
        var row = table.AddRow();
        StyleLabelCell(row.Cells[0], label1);
        row.Cells[1].AddParagraph(value1);
        StyleLabelCell(row.Cells[2], label2);
        row.Cells[3].AddParagraph(value2);
        Pad(row);
    }

    private static void AddHeader(Table table, params string[] values)
    {
        var row = table.AddRow();
        row.HeadingFormat = true;
        row.Shading.Color = Color.FromRgb(241, 245, 249);
        for (var index = 0; index < values.Length; index++)
        {
            row.Cells[index].AddParagraph(values[index]).Format.Font.Bold = true;
        }
        Pad(row);
    }

    private static void AddRow(Table table, params string[] values)
    {
        var row = table.AddRow();
        for (var index = 0; index < values.Length; index++) row.Cells[index].AddParagraph(values[index]);
        Pad(row);
    }

    private static void StyleLabelCell(Cell cell, string label)
    {
        cell.Shading.Color = Color.FromRgb(248, 250, 252);
        cell.AddParagraph(label).Format.Font.Bold = true;
    }

    private static void Pad(Row row)
    {
        row.TopPadding = Unit.FromPoint(3.2);
        row.BottomPadding = Unit.FromPoint(3.2);
        row.VerticalAlignment = VerticalAlignment.Center;
    }

    private static ApprovalRecordApproval MapApproval(
        int sequence,
        string stageName,
        ApprovalInstance? approval,
        string? conditionExplanation)
    {
        if (approval is null)
            return new(sequence, stageName, "Not Required / Skipped", null, null, null, null, null, null, conditionExplanation);
        return new(
            sequence,
            stageName,
            approval.Status.ToString(),
            approval.Approver?.FullName,
            approval.Approver?.Email,
            approval.Decision?.TypedSignature,
            approval.Decision?.Decision.ToString(),
            approval.Decision?.DecidedAtUtc ?? approval.CompletedAtUtc,
            approval.Decision?.Comments,
            conditionExplanation);
    }

    private static string? ConditionExplanation(
        ApprovalRouteStage stage,
        IEnumerable<DocumentFieldDefinition> fields)
    {
        if (!stage.IsConditional) return null;
        if (stage.ConditionGroups.Count == 0) return "Conditional stage had no configured condition tree.";
        return "Runs when " + ConditionFormatter.StageSummary(stage, ConditionField.Build(fields));
    }

    private static string FormatValue(RequestFieldValue field)
    {
        if (!string.IsNullOrWhiteSpace(field.DisplayValue)) return field.DisplayValue;
        if (field.FieldType == DocumentFieldType.Currency &&
            decimal.TryParse(field.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            return amount.ToString("C", CultureInfo.GetCultureInfo("en-US"));
        if (field.FieldType == DocumentFieldType.Date &&
            DateOnly.TryParse(field.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date.ToString("MMMM d, yyyy", CultureInfo.GetCultureInfo("en-US"));
        if (field.FieldType == DocumentFieldType.Boolean && bool.TryParse(field.Value, out var boolean))
            return boolean ? "Yes" : "No";
        return field.Value;
    }

    private static string FriendlyType(RequestAttachment attachment) =>
        Path.GetExtension(attachment.OriginalFileName).ToLowerInvariant() switch
        {
            ".pdf" => "PDF",
            ".doc" => "Microsoft Word",
            ".docx" => "Microsoft Word Open XML",
            ".xls" => "Microsoft Excel",
            ".xlsx" => "Microsoft Excel Open XML",
            ".png" => "PNG image",
            ".jpg" or ".jpeg" => "JPEG image",
            ".txt" => "Text",
            _ => string.IsNullOrWhiteSpace(attachment.ContentType) ? "File" : attachment.ContentType
        };

    private static string FormatDate(DateTimeOffset? value) => value?.ToLocalTime().ToString("MMM d, yyyy h:mm tt zzz") ?? "—";
    private static string FormatSize(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / 1024d / 1024d:N1} MB"
        : $"{Math.Max(0.1, bytes / 1024d):N1} KB";
}

public sealed class ApprovalRecordFontResolver : IFontResolver
{
    public const string FamilyName = "DocumentRoutingSans";
    private const string RegularFace = "document-routing-regular";
    private const string BoldFace = "document-routing-bold";
    private static readonly object Sync = new();
    private static bool configured;
    private readonly string regularPath;
    private readonly string boldPath;

    public ApprovalRecordFontResolver()
    {
        regularPath = FindFont(
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/liberation2/LiberationSans-Regular.ttf",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf"),
            "/System/Library/Fonts/Supplemental/Arial.ttf");
        boldPath = FindFont(
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
            "/usr/share/fonts/truetype/liberation2/LiberationSans-Bold.ttf",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arialbd.ttf"),
            "/System/Library/Fonts/Supplemental/Arial Bold.ttf");
    }

    public static void Configure()
    {
        lock (Sync)
        {
            if (configured) return;
            if (GlobalFontSettings.FontResolver is null)
                GlobalFontSettings.FontResolver = new ApprovalRecordFontResolver();
            configured = true;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        new(bold ? BoldFace : RegularFace, false, italic);

    public byte[]? GetFont(string faceName) => faceName switch
    {
        RegularFace => File.ReadAllBytes(regularPath),
        BoldFace => File.ReadAllBytes(boldPath),
        _ => null
    };

    private static string FindFont(params string[] candidates) =>
        candidates.FirstOrDefault(File.Exists)
        ?? throw new InvalidOperationException("Approval Record PDF generation requires DejaVu Sans, Liberation Sans, or Arial fonts.");
}
