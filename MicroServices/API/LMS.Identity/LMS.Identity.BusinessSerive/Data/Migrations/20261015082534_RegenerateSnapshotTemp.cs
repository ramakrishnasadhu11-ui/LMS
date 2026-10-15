using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Identity.BusinessSerive.Data.Migrations
{
    /// <inheritdoc />
    public partial class RegenerateSnapshotTemp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add tenant columns only if they do not already exist to avoid duplicate column errors
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.Tenants','Address') IS NULL
BEGIN
    ALTER TABLE dbo.Tenants ADD Address NVARCHAR(512) NULL;
END
IF COL_LENGTH('dbo.Tenants','ApprovalDate') IS NULL
BEGIN
    ALTER TABLE dbo.Tenants ADD ApprovalDate DATETIME2 NULL;
END
IF COL_LENGTH('dbo.Tenants','ApprovalReason') IS NULL
BEGIN
    ALTER TABLE dbo.Tenants ADD ApprovalReason NVARCHAR(512) NULL;
END
IF COL_LENGTH('dbo.Tenants','ApprovalStatus') IS NULL
BEGIN
    ALTER TABLE dbo.Tenants ADD ApprovalStatus NVARCHAR(32) NULL;
END
IF COL_LENGTH('dbo.Tenants','ApprovedBy') IS NULL
BEGIN
    ALTER TABLE dbo.Tenants ADD ApprovedBy NVARCHAR(256) NULL;
END
IF COL_LENGTH('dbo.Tenants','City') IS NULL
BEGIN
    ALTER TABLE dbo.Tenants ADD City NVARCHAR(128) NULL;
END
IF COL_LENGTH('dbo.Tenants','Country') IS NULL
BEGIN
    ALTER TABLE dbo.Tenants ADD Country NVARCHAR(128) NULL;
END
IF COL_LENGTH('dbo.Tenants','CreatedDate') IS NULL
BEGIN
    ALTER TABLE dbo.Tenants ADD CreatedDate DATETIME2 NULL;
END
IF COL_LENGTH('dbo.Tenants','Email') IS NULL
BEGIN
    ALTER TABLE dbo.Tenants ADD Email NVARCHAR(256) NULL;
END
IF COL_LENGTH('dbo.Tenants','FamilyName') IS NULL
BEGIN
    ALTER TABLE dbo.Tenants ADD FamilyName NVARCHAR(MAX) NULL;
END
IF COL_LENGTH('dbo.Tenants','MiddleName') IS NULL
BEGIN
    ALTER TABLE dbo.Tenants ADD MiddleName NVARCHAR(MAX) NULL;
END
IF COL_LENGTH('dbo.Tenants','ModifiedDate') IS NULL
BEGIN
    ALTER TABLE dbo.Tenants ADD ModifiedDate DATETIME2 NULL;
END
IF COL_LENGTH('dbo.Tenants','NoOfStores') IS NULL
BEGIN
    ALTER TABLE dbo.Tenants ADD NoOfStores INT NULL;
END
IF COL_LENGTH('dbo.Tenants','PhoneNumber') IS NULL
BEGIN
    ALTER TABLE dbo.Tenants ADD PhoneNumber NVARCHAR(MAX) NULL;
END
IF COL_LENGTH('dbo.Tenants','Region') IS NULL
BEGIN
    ALTER TABLE dbo.Tenants ADD Region NVARCHAR(128) NULL;
END
IF COL_LENGTH('dbo.Tenants','TenantId') IS NULL
BEGIN
    ALTER TABLE dbo.Tenants ADD TenantId NVARCHAR(64) NULL;
END
IF COL_LENGTH('dbo.Tenants','TenantName') IS NULL
BEGIN
    ALTER TABLE dbo.Tenants ADD TenantName NVARCHAR(256) NULL;
END
IF COL_LENGTH('dbo.Tenants','Zip') IS NULL
BEGIN
    ALTER TABLE dbo.Tenants ADD Zip NVARCHAR(32) NULL;
END
");

            migrationBuilder.CreateTable(
                name: "TenantStoreInfos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Password = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsPasswordChanged = table.Column<bool>(type: "bit", nullable: false),
                    Storecodes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantStoreInfos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantStoreStatuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StoreCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    ActivatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ActivatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantStoreStatuses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_ApprovalStatus",
                table: "Tenants",
                column: "ApprovalStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Email",
                table: "Tenants",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_TenantId",
                table: "Tenants",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantStoreInfos_TenantId",
                table: "TenantStoreInfos",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantStoreStatuses_TenantId_StoreCode",
                table: "TenantStoreStatuses",
                columns: new[] { "TenantId", "StoreCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantStoreInfos");

            migrationBuilder.DropTable(
                name: "TenantStoreStatuses");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_ApprovalStatus",
                table: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_Email",
                table: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_TenantId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ApprovalDate",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ApprovalReason",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "CreatedDate",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "FamilyName",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "MiddleName",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ModifiedDate",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "NoOfStores",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Region",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TenantName",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Zip",
                table: "Tenants");
        }
    }
}
