using System;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class Bed
{
    public long Id { get; set; }
    public long RoomId { get; set; }
    public string BedNumber { get; set; } = string.Empty;
    public BedType BedType { get; set; } = BedType.Standard;
    public decimal DailyRate { get; set; }
    public BedStatus Status { get; set; } = BedStatus.Available;
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    public byte[]? RowVersion { get; set; } = Guid.NewGuid().ToByteArray();

    public Room Room { get; set; } = null!;
}
