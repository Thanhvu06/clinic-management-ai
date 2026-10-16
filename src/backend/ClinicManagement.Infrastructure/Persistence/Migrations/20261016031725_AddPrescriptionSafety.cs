using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ClinicManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrescriptionSafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SafetyAcknowledgedAtUtc",
                table: "Prescriptions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SafetyAcknowledgedByUserId",
                table: "Prescriptions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SafetyOverrideReason",
                table: "Prescriptions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SafetyWarningsSnapshot",
                table: "Prescriptions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DrugAllergyCrossReactions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AllergenClassKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RelatedClassKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DrugAllergyCrossReactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DrugClassMembers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClassKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ClassName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IngredientKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DrugClassMembers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DrugInteractionRules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IngredientKeyA = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IngredientKeyB = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Recommendation = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DrugInteractionRules", x => x.Id);
                    table.CheckConstraint("CK_DrugInteractionRules_OrderedPair", "[IngredientKeyA] < [IngredientKeyB]");
                });

            migrationBuilder.InsertData(
                table: "DrugAllergyCrossReactions",
                columns: new[] { "Id", "AllergenClassKey", "Note", "RelatedClassKey" },
                values: new object[] { 1L, "penicillin", "Có thể dị ứng chéo nhóm beta-lactam.", "cephalosporin" });

            migrationBuilder.InsertData(
                table: "DrugClassMembers",
                columns: new[] { "Id", "ClassKey", "ClassName", "IngredientKey" },
                values: new object[,]
                {
                    { 1L, "penicillin", "Kháng sinh penicillin", "amoxicillin" },
                    { 2L, "cephalosporin", "Kháng sinh cephalosporin", "cefixime" },
                    { 3L, "nsaid", "Thuốc chống viêm không steroid", "ibuprofen" },
                    { 4L, "fluoroquinolone", "Kháng sinh fluoroquinolon", "ciprofloxacin" },
                    { 5L, "macrolide", "Kháng sinh macrolid", "azithromycin" },
                    { 6L, "corticosteroid", "Corticosteroid", "prednisolone" }
                });

            migrationBuilder.InsertData(
                table: "DrugInteractionRules",
                columns: new[] { "Id", "Description", "IngredientKeyA", "IngredientKeyB", "IsActive", "Recommendation", "Severity" },
                values: new object[,]
                {
                    { 1L, "Có nguy cơ kéo dài khoảng QT.", "ciprofloxacin", "domperidone", true, "Tránh phối hợp; cần bác sĩ/dược sĩ đánh giá.", 3 },
                    { 2L, "Có nguy cơ kéo dài khoảng QT.", "azithromycin", "domperidone", true, "Tránh phối hợp; cần bác sĩ/dược sĩ đánh giá.", 3 },
                    { 3L, "Calci có thể làm giảm hấp thu ciprofloxacin.", "calci", "ciprofloxacin", true, "Uống cách nhau ít nhất 2 giờ.", 2 },
                    { 4L, "Nhôm phosphat có thể làm giảm hấp thu ciprofloxacin.", "ciprofloxacin", "nhom phosphat", true, "Uống cách nhau ít nhất 2 giờ.", 2 },
                    { 5L, "Tăng nguy cơ xuất huyết tiêu hóa.", "ibuprofen", "prednisolone", true, "Đánh giá nguy cơ tiêu hóa trước khi phối hợp.", 2 },
                    { 6L, "Có thể giảm tác dụng hạ áp và ảnh hưởng chức năng thận.", "ibuprofen", "losartan", true, "Theo dõi huyết áp và chức năng thận.", 2 },
                    { 7L, "Có thể gây rối loạn đường huyết.", "ciprofloxacin", "gliclazide", true, "Theo dõi đường huyết khi phối hợp.", 2 },
                    { 8L, "Prednisolone có thể làm tăng đường huyết.", "metformin", "prednisolone", true, "Theo dõi đường huyết và đánh giá điều trị.", 2 },
                    { 9L, "Có thể tăng nguy cơ tác dụng phụ trên cơ.", "atorvastatin", "azithromycin", true, "Theo dõi đau cơ, báo bác sĩ khi có bất thường.", 1 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_DrugClassMembers_ClassKey_IngredientKey",
                table: "DrugClassMembers",
                columns: new[] { "ClassKey", "IngredientKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DrugInteractionRules_IngredientKeyA_IngredientKeyB",
                table: "DrugInteractionRules",
                columns: new[] { "IngredientKeyA", "IngredientKeyB" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DrugAllergyCrossReactions");

            migrationBuilder.DropTable(
                name: "DrugClassMembers");

            migrationBuilder.DropTable(
                name: "DrugInteractionRules");

            migrationBuilder.DropColumn(
                name: "SafetyAcknowledgedAtUtc",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "SafetyAcknowledgedByUserId",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "SafetyOverrideReason",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "SafetyWarningsSnapshot",
                table: "Prescriptions");
        }
    }
}
