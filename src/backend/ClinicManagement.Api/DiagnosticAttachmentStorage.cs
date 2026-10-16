using System.Text.RegularExpressions;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Diagnostics.Interfaces;

namespace ClinicManagement.Api;

public class DiagnosticAttachmentStorage : IDiagnosticAttachmentStorage
{
    public const int MaxBytes = 10 * 1024 * 1024;
    public const int MaxFilesPerItem = 10;
    public string DirectoryPath { get; }
    private readonly ILogger<DiagnosticAttachmentStorage> _logger;
    private static bool IsStoredName(string name) => Regex.IsMatch(name, @"\A[a-f0-9]{32}\.(jpg|png|webp|pdf)\z");

    public DiagnosticAttachmentStorage(IConfiguration configuration, IWebHostEnvironment environment, ILogger<DiagnosticAttachmentStorage> logger)
    {
        DirectoryPath = Path.GetFullPath(configuration["Storage:DiagnosticAttachmentsPath"]
            ?? Path.Combine(environment.ContentRootPath, "uploads", "diagnostics"));
        Directory.CreateDirectory(DirectoryPath);
        _logger = logger;
    }

    public async Task<StoredDiagnosticAttachment> SaveAsync(Stream input, long length, string originalFileName, CancellationToken cancellationToken)
    {
        if (length > MaxBytes) throw TooLarge();
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (bytes.Length + read > MaxBytes) throw TooLarge();
            await bytes.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        var data = bytes.ToArray();
        var extension = DetectExtension(data) ?? throw new BusinessException("INVALID_ATTACHMENT_TYPE", "Chỉ chấp nhận JPG, PNG, WebP hoặc PDF.");
        var contentType = extension switch { "jpg" => "image/jpeg", "png" => "image/png", "webp" => "image/webp", _ => "application/pdf" };
        // Normalize both separator styles, including client paths from another OS.
        var original = new string(Path.GetFileName(originalFileName.Replace('\\', '/')).Where(c => !char.IsControl(c)).ToArray());
        if (original.Length > 200) original = original[..200];
        if (string.IsNullOrWhiteSpace(original)) original = $"tep-dinh-kem.{extension}";
        var name = $"{Guid.NewGuid():N}.{extension}";
        try { await File.WriteAllBytesAsync(Path.Combine(DirectoryPath, name), data, cancellationToken); }
        catch { DeleteFile(name); throw; }
        return new(name, original, contentType, data.LongLength);
    }

    public Stream? OpenRead(string storedFileName)
    {
        if (!IsStoredName(storedFileName)) return null;
        try { return new FileStream(Path.Combine(DirectoryPath, storedFileName), FileMode.Open, FileAccess.Read, FileShare.Read); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    public void DeleteFile(string storedFileName)
    {
        if (!IsStoredName(storedFileName)) return;
        try { File.Delete(Path.Combine(DirectoryPath, storedFileName)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { _logger.LogWarning(exception, "Could not remove diagnostic attachment {StoredFileName}", storedFileName); }
    }

    private static BusinessException TooLarge() => new("ATTACHMENT_TOO_LARGE", "Mỗi tệp không được vượt quá 10 MB.");
    private static string? DetectExtension(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff) return "jpg";
        if (bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a })) return "png";
        if (bytes.Length >= 16 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)
            && (bytes.Slice(12, 4).SequenceEqual("VP8 "u8) || bytes.Slice(12, 4).SequenceEqual("VP8L"u8) || bytes.Slice(12, 4).SequenceEqual("VP8X"u8))) return "webp";
        if (bytes.StartsWith("%PDF-"u8)) return "pdf";
        return null;
    }
}
