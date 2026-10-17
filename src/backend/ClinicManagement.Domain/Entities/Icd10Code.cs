namespace ClinicManagement.Domain.Entities;

public class Icd10Code
{
    public string Code { get; set; } = string.Empty;
    public string NameVi { get; set; } = string.Empty;
    public string SearchKey { get; set; } = string.Empty;
    public string ChapterCode { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
