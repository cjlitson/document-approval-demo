using System.Globalization;
using System.Text;
using DocumentApprovalDemo.Domain;

namespace DocumentApprovalDemo.Services;

public interface ISignedPackageService
{
    byte[] Build(ApprovalRequest request);
}

public sealed class SignedPackageService : ISignedPackageService
{
    public byte[] Build(ApprovalRequest request)
    {
        var lines = new List<string>
        {
            "DOCUMENT APPROVAL - SIGNED PACKAGE",
            $"Request: {request.RequestNumber}",
            $"Title: {request.Title}",
            $"Requester: {request.Requester.FullName} ({request.Requester.Email})",
            $"Department: {request.Department}",
            $"Document type: {request.DocumentType.Name}",
            $"Revision: {request.CurrentRevisionNumber}",
            $"Route version: {request.RouteVersion?.VersionNumber}",
            $"Status: {request.Status}",
            "",
            "DOCUMENT DATA"
        };
        foreach (var field in request.FieldValues
                     .Where(x => x.RevisionNumber == request.CurrentRevisionNumber)
                     .OrderBy(x => x.Sequence))
        {
            var value = FormatValue(field);
            var wrapped = Wrap($"{field.Label}: {value}", 88).ToList();
            lines.AddRange(wrapped.Count > 0 ? wrapped : [field.Label + ":"]);
        }
        lines.Add("");
        lines.Add("APPROVAL EVIDENCE");

        foreach (var approval in request.Approvals.Where(x => x.RevisionNumber == request.CurrentRevisionNumber).OrderBy(x => x.Sequence))
        {
            lines.Add($"{approval.Sequence}. {approval.StageName}: {approval.Status}");
            if (approval.Decision is { } decision)
            {
                lines.Add($"   Authenticated user: {decision.AuthenticatedFullName} ({decision.AuthenticatedEmail})");
                lines.Add($"   Adopted signature: {decision.TypedSignature}");
                lines.Add($"   Decision/time: {decision.Decision} at {decision.DecidedAtUtc:u}");
                if (!string.IsNullOrWhiteSpace(decision.Comments)) lines.Add($"   Comments: {decision.Comments}");
            }
        }

        lines.Add("");
        lines.Add("ORIGINAL ATTACHMENTS RETAINED WITH REQUEST");
        foreach (var attachment in request.Attachments.OrderBy(x => x.RevisionNumber).ThenBy(x => x.OriginalFileName))
            lines.Add($"Revision {attachment.RevisionNumber}: {attachment.OriginalFileName} ({attachment.SizeBytes:N0} bytes)");
        lines.Add("");
        lines.Add($"Package generated: {DateTimeOffset.UtcNow:u}");
        lines.Add("This demonstration records authenticated approval evidence; it is not a DocuSign electronic-signature certificate.");

        return MinimalPdfWriter.Write(lines);
    }

    private static string FormatValue(RequestFieldValue field)
    {
        if (!string.IsNullOrWhiteSpace(field.DisplayValue)) return field.DisplayValue;
        if (field.FieldType == DocumentFieldType.Currency &&
            decimal.TryParse(field.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            return amount.ToString("C", CultureInfo.GetCultureInfo("en-US"));
        return field.Value;
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        var words = text.Replace("\r", " ").Replace("\n", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = new StringBuilder();
        foreach (var word in words)
        {
            if (line.Length > 0 && line.Length + word.Length + 1 > width)
            {
                yield return line.ToString();
                line.Clear();
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }
        if (line.Length > 0) yield return line.ToString();
    }
}

internal static class MinimalPdfWriter
{
    public static byte[] Write(IReadOnlyList<string> lines)
    {
        const int linesPerPage = 48;
        var pages = lines.Chunk(linesPerPage).ToList();
        var objects = new List<byte[]> { Array.Empty<byte>() };
        var pageObjectNumbers = new List<int>();
        var contentObjectNumbers = new List<int>();

        objects.Add(Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"));
        objects.Add(Array.Empty<byte>());
        objects.Add(Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));

        foreach (var page in pages)
        {
            var pageNumber = objects.Count;
            pageObjectNumbers.Add(pageNumber);
            objects.Add(Array.Empty<byte>());
            var contentNumber = objects.Count;
            contentObjectNumbers.Add(contentNumber);
            var content = BuildContent(page);
            objects.Add(Encoding.ASCII.GetBytes($"<< /Length {content.Length} >>\nstream\n{content}\nendstream"));
        }

        var kids = string.Join(' ', pageObjectNumbers.Select(x => $"{x} 0 R"));
        objects[2] = Encoding.ASCII.GetBytes($"<< /Type /Pages /Kids [{kids}] /Count {pageObjectNumbers.Count} >>");
        for (var i = 0; i < pageObjectNumbers.Count; i++)
            objects[pageObjectNumbers[i]] = Encoding.ASCII.GetBytes($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents {contentObjectNumbers[i]} 0 R >>");

        using var output = new MemoryStream();
        WriteAscii(output, "%PDF-1.4\n%âãÏÓ\n");
        var offsets = new long[objects.Count];
        for (var i = 1; i < objects.Count; i++)
        {
            offsets[i] = output.Position;
            WriteAscii(output, $"{i} 0 obj\n");
            output.Write(objects[i]);
            WriteAscii(output, "\nendobj\n");
        }
        var xref = output.Position;
        WriteAscii(output, $"xref\n0 {objects.Count}\n0000000000 65535 f \n");
        for (var i = 1; i < objects.Count; i++) WriteAscii(output, $"{offsets[i]:D10} 00000 n \n");
        WriteAscii(output, $"trailer\n<< /Size {objects.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
        return output.ToArray();
    }

    private static string BuildContent(IEnumerable<string> lines)
    {
        var builder = new StringBuilder("BT\n/F1 10 Tf\n50 752 Td\n13 TL\n");
        foreach (var line in lines)
        {
            builder.Append('(').Append(EscapeAscii(line)).Append(") Tj\nT*\n");
        }
        builder.Append("ET");
        return builder.ToString();
    }

    private static string EscapeAscii(string value)
    {
        var ascii = new string(value.Select(c => c is >= ' ' and <= '~' ? c : '?').ToArray());
        return ascii.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }

    private static void WriteAscii(Stream stream, string text) => stream.Write(Encoding.Latin1.GetBytes(text));
}
