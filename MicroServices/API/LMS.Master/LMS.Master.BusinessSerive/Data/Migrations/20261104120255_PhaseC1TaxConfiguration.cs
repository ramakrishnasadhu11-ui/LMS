using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Master.BusinessSerive.Data.Migrations
{
    /// <inheritdoc />
    public partial class PhaseC1TaxConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "TaxInvoiceSettings",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "TaxInvoiceSettings",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPricesInclusive",
                table: "TaxInvoiceSettings",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "TaxInvoiceSettings",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxMode",
                table: "TaxInvoiceSettings",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TaxInvoiceComponents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaxInvoiceSettingsId = table.Column<int>(type: "int", nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    StoreCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RatePercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    Jurisdiction = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    IsCompound = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EffectiveToUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    JurisdictionNormalizedKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, computedColumnSql: "COALESCE(NULLIF(LTRIM(RTRIM([Jurisdiction])),N''),N'__NULL__')", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxInvoiceComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxInvoiceComponents_TaxInvoiceSettings_TaxInvoiceSettingsId",
                        column: x => x.TaxInvoiceSettingsId,
                        principalTable: "TaxInvoiceSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaxInvoiceComponents_TaxInvoiceSettingsId_Code_IsActive",
                table: "TaxInvoiceComponents",
                columns: new[] { "TaxInvoiceSettingsId", "Code", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxInvoiceComponents_TenantName_StoreCode_Code_Priority_JurisdictionNormalizedKey",
                table: "TaxInvoiceComponents",
                columns: new[] { "TenantName", "StoreCode", "Code", "Priority", "JurisdictionNormalizedKey" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_TaxInvoiceComponents_TenantName_StoreCode_IsActive",
                table: "TaxInvoiceComponents",
                columns: new[] { "TenantName", "StoreCode", "IsActive" });

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaxInvoiceComponents");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "TaxInvoiceSettings");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "TaxInvoiceSettings");

            migrationBuilder.DropColumn(
                name: "IsPricesInclusive",
                table: "TaxInvoiceSettings");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "TaxInvoiceSettings");

            migrationBuilder.DropColumn(
                name: "TaxMode",
                table: "TaxInvoiceSettings");
        }
    }
}
