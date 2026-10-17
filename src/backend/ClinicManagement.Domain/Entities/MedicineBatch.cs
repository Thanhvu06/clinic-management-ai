namespace ClinicManagement.Domain.Entities;

public class MedicineBatch
{
    public long Id { get; set; }
    public long MedicineId { get; set; }
    public Medicine Medicine { get; set; } = null!;
    public string BatchNumber { get; set; } = string.Empty;
    public DateOnly ExpiryDate { get; set; }
    public int QuantityReceived { get; set; }
    public int QuantityRemaining { get; set; }
    public decimal? UnitCost { get; set; }
    public DateTime ReceivedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public byte[]? RowVersion { get; set; }
    public DateTime? DisposedAt { get; set; }
    public string? DisposeReason { get; set; }
}
