namespace ClinicManagement.Domain.Entities;

public class InvoiceItem
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public string ReferenceType { get; set; } = string.Empty;
    public long ReferenceId { get; set; }

    // Navigation property
    public Invoice Invoice { get; set; } = null!;
}
