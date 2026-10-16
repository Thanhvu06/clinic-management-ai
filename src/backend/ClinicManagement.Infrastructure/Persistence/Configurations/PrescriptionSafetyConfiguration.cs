using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class DrugClassMemberConfiguration : IEntityTypeConfiguration<DrugClassMember>
{
    public void Configure(EntityTypeBuilder<DrugClassMember> builder)
    {
        builder.ToTable("DrugClassMembers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ClassKey).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ClassName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.IngredientKey).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => new { x.ClassKey, x.IngredientKey }).IsUnique();
        builder.HasData(
            new DrugClassMember { Id = 1, ClassKey = "penicillin", ClassName = "Kháng sinh penicillin", IngredientKey = "amoxicillin" },
            new DrugClassMember { Id = 2, ClassKey = "cephalosporin", ClassName = "Kháng sinh cephalosporin", IngredientKey = "cefixime" },
            new DrugClassMember { Id = 3, ClassKey = "nsaid", ClassName = "Thuốc chống viêm không steroid", IngredientKey = "ibuprofen" },
            new DrugClassMember { Id = 4, ClassKey = "fluoroquinolone", ClassName = "Kháng sinh fluoroquinolon", IngredientKey = "ciprofloxacin" },
            new DrugClassMember { Id = 5, ClassKey = "macrolide", ClassName = "Kháng sinh macrolid", IngredientKey = "azithromycin" },
            new DrugClassMember { Id = 6, ClassKey = "corticosteroid", ClassName = "Corticosteroid", IngredientKey = "prednisolone" });
    }
}

public class DrugAllergyCrossReactionConfiguration : IEntityTypeConfiguration<DrugAllergyCrossReaction>
{
    public void Configure(EntityTypeBuilder<DrugAllergyCrossReaction> builder)
    {
        builder.ToTable("DrugAllergyCrossReactions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AllergenClassKey).HasMaxLength(50).IsRequired();
        builder.Property(x => x.RelatedClassKey).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(300).IsRequired();
        builder.HasData(new DrugAllergyCrossReaction { Id = 1, AllergenClassKey = "penicillin", RelatedClassKey = "cephalosporin", Note = "Có thể dị ứng chéo nhóm beta-lactam." });
    }
}

public class DrugInteractionRuleConfiguration : IEntityTypeConfiguration<DrugInteractionRule>
{
    public void Configure(EntityTypeBuilder<DrugInteractionRule> builder)
    {
        builder.ToTable("DrugInteractionRules", table => table.HasCheckConstraint("CK_DrugInteractionRules_OrderedPair", "[IngredientKeyA] < [IngredientKeyB]"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.IngredientKeyA).HasMaxLength(100).IsRequired();
        builder.Property(x => x.IngredientKeyB).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Recommendation).HasMaxLength(500).IsRequired();
        builder.HasIndex(x => new { x.IngredientKeyA, x.IngredientKeyB }).IsUnique();
        builder.HasData(
            Rule(1, "ciprofloxacin", "domperidone", DrugInteractionSeverity.Major, "Có nguy cơ kéo dài khoảng QT.", "Tránh phối hợp; cần bác sĩ/dược sĩ đánh giá."),
            Rule(2, "azithromycin", "domperidone", DrugInteractionSeverity.Major, "Có nguy cơ kéo dài khoảng QT.", "Tránh phối hợp; cần bác sĩ/dược sĩ đánh giá."),
            Rule(3, "calci", "ciprofloxacin", DrugInteractionSeverity.Moderate, "Calci có thể làm giảm hấp thu ciprofloxacin.", "Uống cách nhau ít nhất 2 giờ."),
            Rule(4, "ciprofloxacin", "nhom phosphat", DrugInteractionSeverity.Moderate, "Nhôm phosphat có thể làm giảm hấp thu ciprofloxacin.", "Uống cách nhau ít nhất 2 giờ."),
            Rule(5, "ibuprofen", "prednisolone", DrugInteractionSeverity.Moderate, "Tăng nguy cơ xuất huyết tiêu hóa.", "Đánh giá nguy cơ tiêu hóa trước khi phối hợp."),
            Rule(6, "ibuprofen", "losartan", DrugInteractionSeverity.Moderate, "Có thể giảm tác dụng hạ áp và ảnh hưởng chức năng thận.", "Theo dõi huyết áp và chức năng thận."),
            Rule(7, "ciprofloxacin", "gliclazide", DrugInteractionSeverity.Moderate, "Có thể gây rối loạn đường huyết.", "Theo dõi đường huyết khi phối hợp."),
            Rule(8, "metformin", "prednisolone", DrugInteractionSeverity.Moderate, "Prednisolone có thể làm tăng đường huyết.", "Theo dõi đường huyết và đánh giá điều trị."),
            Rule(9, "atorvastatin", "azithromycin", DrugInteractionSeverity.Minor, "Có thể tăng nguy cơ tác dụng phụ trên cơ.", "Theo dõi đau cơ, báo bác sĩ khi có bất thường."));
    }

    private static DrugInteractionRule Rule(long id, string a, string b, DrugInteractionSeverity severity, string description, string recommendation) =>
        new() { Id = id, IngredientKeyA = a, IngredientKeyB = b, Severity = severity, Description = description, Recommendation = recommendation, IsActive = true };
}

public class PrescriptionSafetyConfiguration : IEntityTypeConfiguration<Prescription>
{
    public void Configure(EntityTypeBuilder<Prescription> builder) => builder.Property(x => x.SafetyOverrideReason).HasMaxLength(500);
}
