using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClinicManagement.Domain.Entities;

public class PrescriptionItem
{
    public long PrescriptionId { get; set; }
    public long MedicineId { get; set; }
    
    public int Quantity { get; set; }
    
    [Required]
    [StringLength(100)]
    public string Dosage { get; set; } = string.Empty;
    
    [Required]
    [StringLength(100)]
    public string Frequency { get; set; } = string.Empty;
    
    public int? DurationDays { get; set; }
    public string? Instructions { get; set; }
    
    [ForeignKey("PrescriptionId")]
    public Prescription? Prescription { get; set; }
    
    [ForeignKey("MedicineId")]
    public Medicine? Medicine { get; set; }
}
