namespace ClinicManagement.Domain.Entities;

public class DrugClassMember
{
    public long Id { get; set; }
    public string ClassKey { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string IngredientKey { get; set; } = string.Empty;
}
