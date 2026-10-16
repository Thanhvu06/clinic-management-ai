namespace ClinicManagement.Application.Diagnostics.Interfaces;

// Internal metadata only; never returned as an API DTO.
public record StoredDiagnosticAttachment(string StoredFileName, string OriginalFileName, string ContentType, long SizeBytes);

public interface IDiagnosticAttachmentStorage
{
    Task<StoredDiagnosticAttachment> SaveAsync(Stream input, long length, string originalFileName, CancellationToken cancellationToken);
    Stream? OpenRead(string storedFileName);
    void DeleteFile(string storedFileName);
}
