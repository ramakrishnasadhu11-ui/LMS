using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Identity.BusinessSerive.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionPlanCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlanCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    BillingCycle = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false, defaultValue: "INR"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlans", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlans_PlanCode_BillingCycle",
                table: "SubscriptionPlans",
                columns: new[] { "PlanCode", "BillingCycle" },
                unique: true);

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM dbo.SubscriptionPlans WHERE PlanCode = 'Standard' AND BillingCycle = 'Monthly')
BEGIN
    INSERT INTO dbo.SubscriptionPlans (PlanCode, BillingCycle, Amount, Currency, IsActive, CreatedDateUtc, ModifiedDateUtc, UpdatedBy)
    VALUES ('Standard', 'Monthly', 499.00, 'INR', 1, GETUTCDATE(), NULL, NULL);
END

IF NOT EXISTS (SELECT 1 FROM dbo.SubscriptionPlans WHERE PlanCode = 'Standard' AND BillingCycle = 'Yearly')
BEGIN
    INSERT INTO dbo.SubscriptionPlans (PlanCode, BillingCycle, Amount, Currency, IsActive, CreatedDateUtc, ModifiedDateUtc, UpdatedBy)
    VALUES ('Standard', 'Yearly', 4999.00, 'INR', 1, GETUTCDATE(), NULL, NULL);
END

IF NOT EXISTS (SELECT 1 FROM dbo.SubscriptionPlans WHERE PlanCode = 'Premium' AND BillingCycle = 'Monthly')
BEGIN
    INSERT INTO dbo.SubscriptionPlans (PlanCode, BillingCycle, Amount, Currency, IsActive, CreatedDateUtc, ModifiedDateUtc, UpdatedBy)
    VALUES ('Premium', 'Monthly', 899.00, 'INR', 1, GETUTCDATE(), NULL, NULL);
END

IF NOT EXISTS (SELECT 1 FROM dbo.SubscriptionPlans WHERE PlanCode = 'Premium' AND BillingCycle = 'Yearly')
BEGIN
    INSERT INTO dbo.SubscriptionPlans (PlanCode, BillingCycle, Amount, Currency, IsActive, CreatedDateUtc, ModifiedDateUtc, UpdatedBy)
    VALUES ('Premium', 'Yearly', 8999.00, 'INR', 1, GETUTCDATE(), NULL, NULL);
END
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionPlans");
        }
    }
}
