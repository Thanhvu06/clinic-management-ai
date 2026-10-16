namespace ClinicManagement.Domain.Entities;

public class DiagnosticResultAttachment
{
    public long Id { get; set; }
    public long DiagnosticOrderItemId { get; set; }
    public string StoredFileName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public Guid UploadedByUserId { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public DiagnosticOrderItem DiagnosticOrderItem { get; set; } = null!;
}
