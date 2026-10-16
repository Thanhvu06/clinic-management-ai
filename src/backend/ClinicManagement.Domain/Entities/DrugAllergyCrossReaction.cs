namespace ClinicManagement.Domain.Entities;

public class DrugAllergyCrossReaction
{
    public long Id { get; set; }
    public string AllergenClassKey { get; set; } = string.Empty;
    public string RelatedClassKey { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}
