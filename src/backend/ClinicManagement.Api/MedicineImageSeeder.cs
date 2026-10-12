using System.Text.RegularExpressions;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Api;

public static class MedicineImageSeeder
{
    private static readonly Regex SourceFilePattern = new(
        @"^MED\d{2}\.(png|jpg|jpeg|webp)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static async Task SeedAsync(AppDbContext db, MedicineImageStorage storage,
        IWebHostEnvironment env, ILogger logger, CancellationToken ct)
    {
        var sourceDirectory = Path.Combine(env.ContentRootPath, "SeedData", "medicine-images");
        if (!Directory.Exists(sourceDirectory))
        {
            logger.LogInformation("Medicine seed image directory {SourceDirectory} does not exist; skipping.", sourceDirectory);
            return;
        }

        var assigned = 0;
        foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            if (!SourceFilePattern.IsMatch(Path.GetFileName(sourcePath))) continue;
            var code = Path.GetFileNameWithoutExtension(sourcePath).ToUpperInvariant();
            var medicine = await db.Medicines.SingleOrDefaultAsync(m => m.Code == code, ct);
            if (medicine is null)
            {
                logger.LogWarning("Skipping seed image {SourcePath}: medicine {MedicineCode} does not exist.", sourcePath, code);
                continue;
            }
            if (medicine.ImagePath is not null && File.Exists(Path.Combine(storage.DirectoryPath, medicine.ImagePath))) continue;

            byte[] bytes;
            try
            {
                if (new FileInfo(sourcePath).Length > MedicineImageStorage.MaxBytes)
                {
                    logger.LogWarning("Skipping seed image {SourcePath}: exceeds the 2 MB limit.", sourcePath);
                    continue;
                }
                bytes = await File.ReadAllBytesAsync(sourcePath, ct);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Could not read seed image {SourcePath}.", sourcePath);
                continue;
            }

            var extension = DetectExtension(bytes);
            var sourceExtension = Path.GetExtension(sourcePath)[1..].ToLowerInvariant();
            if (sourceExtension == "jpeg") sourceExtension = "jpg";
            if (bytes.Length > MedicineImageStorage.MaxBytes || extension is null || extension != sourceExtension)
            {
                logger.LogWarning("Skipping seed image {SourcePath}: size or image signature does not match its extension.", sourcePath);
                continue;
            }

            var name = $"{medicine.Id}-{Guid.NewGuid():N}.{extension}";
            var targetPath = Path.Combine(storage.DirectoryPath, name);
            try
            {
                await File.WriteAllBytesAsync(targetPath, bytes, ct);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                try { File.Delete(targetPath); }
                catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
                { logger.LogWarning(cleanupException, "Could not remove incomplete seed image {ImageName}.", name); }
                if (exception is OperationCanceledException) throw;
                logger.LogWarning(exception, "Could not write seed image for medicine {MedicineCode}.", code);
                continue;
            }
            medicine.ImagePath = name;
            medicine.UpdatedAt = DateTime.UtcNow;
            assigned++;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {AssignedCount} medicine images.", assigned);
    }

    private static string? DetectExtension(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff) return "jpg";
        if (bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a })) return "png";
        if (bytes.Length >= 16 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)
            && (bytes.Slice(12, 4).SequenceEqual("VP8 "u8) || bytes.Slice(12, 4).SequenceEqual("VP8L"u8) || bytes.Slice(12, 4).SequenceEqual("VP8X"u8))) return "webp";
        return null;
    }
}
