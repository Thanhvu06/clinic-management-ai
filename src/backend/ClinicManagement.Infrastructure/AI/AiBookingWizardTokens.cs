using System.Security.Cryptography;
using System.Text;
using ClinicManagement.Application.Common.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace ClinicManagement.Infrastructure.AI;

public enum BookingWizardStage : byte { Specialty, Doctor, Day, Slot, Reason, Review }
public enum BookingWizardOperation : byte { Pick, Back, Reason, Preset }
public sealed record BookingWizardSelection(
    Guid DraftId, BookingWizardStage Stage = BookingWizardStage.Specialty,
    long SpecialtyId = 0, long DoctorId = 0, int Day = 0, long SlotId = 0, byte Preset = 0, bool AnyDoctor = false);

/// <summary>Compact encrypted, authenticated capabilities; no server-side wizard state.</summary>
public sealed class AiBookingWizardTokens
{
    private readonly ITimeLimitedDataProtector _protector;
    private readonly IDateTimeProvider _clock;
    public AiBookingWizardTokens(IDataProtectionProvider protection, IDateTimeProvider clock)
    {
        _protector = protection.CreateProtector("ClinicManagement.AI.BookingWizard.v1").ToTimeLimitedDataProtector();
        _clock = clock;
    }

    public string Issue(Guid user, string session, BookingWizardSelection selection, BookingWizardOperation operation)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)1);
            writer.Write(Binding(user, session));
            writer.Write(_clock.UtcNow.AddMinutes(15).Ticks);
            writer.Write(selection.DraftId.ToByteArray());
            writer.Write((byte)operation);
            writer.Write((byte)selection.Stage);
            writer.Write(selection.SpecialtyId);
            writer.Write(selection.DoctorId);
            writer.Write(selection.Day);
            writer.Write(selection.SlotId);
            writer.Write(selection.Preset);
            writer.Write(selection.AnyDoctor);
        }
        return WebEncoders.Base64UrlEncode(_protector.Protect(stream.ToArray(), TimeSpan.FromMinutes(15)));
    }

    public BookingWizardSelection? Read(string? token, Guid user, string session, string requestStep)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 512) return null;
        try
        {
            var bytes = _protector.Unprotect(WebEncoders.Base64UrlDecode(token), out _);
            using var stream = new MemoryStream(bytes);
            using var reader = new BinaryReader(stream);
            if (reader.ReadByte() != 1 || !CryptographicOperations.FixedTimeEquals(reader.ReadBytes(32), Binding(user, session)) ||
                reader.ReadInt64() <= _clock.UtcNow.Ticks) return null;
            var draft = new Guid(reader.ReadBytes(16));
            var operation = (BookingWizardOperation)reader.ReadByte();
            var stage = (BookingWizardStage)reader.ReadByte();
            var selection = new BookingWizardSelection(draft, stage, reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt32(), reader.ReadInt64(), reader.ReadByte(), reader.ReadBoolean());
            if (stream.Position != stream.Length || !Enum.IsDefined(stage)) return null;
            return (requestStep, operation) switch
            {
                ("pick", BookingWizardOperation.Pick) when stage is > BookingWizardStage.Specialty and <= BookingWizardStage.Reason => selection,
                ("pick", BookingWizardOperation.Preset) when stage == BookingWizardStage.Reason && selection.Preset is >= 1 and <= 3 => selection,
                ("back", BookingWizardOperation.Back) => selection,
                ("reason", BookingWizardOperation.Reason) when stage == BookingWizardStage.Reason => selection,
                _ => null
            };
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or IOException or ArgumentException)
        {
            return null;
        }
    }

    private static byte[] Binding(Guid user, string session) => SHA256.HashData(Encoding.UTF8.GetBytes($"{user:N}|{session}"));
}
