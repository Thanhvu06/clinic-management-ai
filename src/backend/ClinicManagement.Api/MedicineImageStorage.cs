using System.Text.RegularExpressions;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Medicines.DTOs;
using ClinicManagement.Application.Medicines.Interfaces;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Api;

public class MedicineImageStorage
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public string DirectoryPath { get; }
    private readonly ILogger<MedicineImageStorage> _logger;
    public MedicineImageStorage(IConfiguration configuration, IWebHostEnvironment environment, ILogger<MedicineImageStorage> logger)
    {
        DirectoryPath = Path.GetFullPath(configuration["Storage:MedicineImagesPath"]
            ?? Path.Combine(environment.ContentRootPath, "uploads", "medicines"));
        Directory.CreateDirectory(DirectoryPath);
        _logger = logger;
    }

    public async Task<MedicineDto> UploadAsync(long id, IFormFile file, AppDbContext db, IMedicineService service, CancellationToken cancellationToken)
    {
        var medicine = await db.Medicines.FindAsync(new object[] { id }, cancellationToken)
            ?? throw new NotFoundException("Thuốc không tồn tại.");
        if (file.Length > MaxBytes) throw TooLarge();
        // Bound the stream as well as checking the declared multipart length.
        using var input = file.OpenReadStream();
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (bytes.Length + read > MaxBytes) throw TooLarge();
            await bytes.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        var data = bytes.ToArray();
        var extension = DetectExtension(data) ?? throw new BusinessException("INVALID_IMAGE_TYPE", "Chỉ chấp nhận ảnh JPEG, PNG hoặc WebP.");
        var name = $"{id}-{Guid.NewGuid():N}.{extension}";
        var oldName = medicine.ImagePath;
        var path = Path.Combine(DirectoryPath, name);
        try
        {
            await File.WriteAllBytesAsync(path, data, cancellationToken);
            medicine.ImagePath = name;
            medicine.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            DeleteFile(name);
            throw;
        }
        DeleteFile(oldName);
        return await service.GetMedicineByIdAsync(id);
    }

    public async Task<MedicineDto> DeleteAsync(long id, AppDbContext db, IMedicineService service, CancellationToken cancellationToken)
    {
        var medicine = await db.Medicines.FindAsync(new object[] { id }, cancellationToken)
            ?? throw new NotFoundException("Thuốc không tồn tại.");
        var oldName = medicine.ImagePath;
        medicine.ImagePath = null;
        medicine.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        DeleteFile(oldName);
        return await service.GetMedicineByIdAsync(id);
    }

    private void DeleteFile(string? name)
    {
        if (name == null || !Regex.IsMatch(name, @"^\d+-[a-f0-9]{32}\.(jpg|png|webp)$")) return;
        try { File.Delete(Path.Combine(DirectoryPath, name)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { _logger.LogWarning(exception, "Could not remove previous medicine image {ImageName}", name); }
    }

    private static BusinessException TooLarge() => new("IMAGE_TOO_LARGE", "Ảnh thuốc không được vượt quá 2 MB.");
    private static string? DetectExtension(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff) return "jpg";
        if (bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a })) return "png";
        if (bytes.Length >= 16 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)
            && (bytes.Slice(12, 4).SequenceEqual("VP8 "u8) || bytes.Slice(12, 4).SequenceEqual("VP8L"u8) || bytes.Slice(12, 4).SequenceEqual("VP8X"u8))) return "webp";
        return null;
    }
}
