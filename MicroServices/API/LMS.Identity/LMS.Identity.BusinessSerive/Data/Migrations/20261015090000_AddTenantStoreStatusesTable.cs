using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Identity.BusinessSerive.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantStoreStatusesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "IX_TenantStoreStatuses_TenantId_StoreCode",
                table: "TenantStoreStatuses",
                columns: new[] { "TenantId", "StoreCode" },
                unique: true);

            // Existing stores were usable before activation was introduced, so backfill them as active.
            migrationBuilder.Sql(@"
INSERT INTO TenantStoreStatuses (TenantId, StoreCode, IsActive, CreatedDate, ActivatedBy, ActivatedDate)
SELECT s.TenantId, s.StoreCode, 1, GETUTCDATE(), 'Migration', GETUTCDATE()
FROM (
    SELECT DISTINCT t.TenantId, LTRIM(RTRIM(value)) AS StoreCode
    FROM TenantStoreInfos t
    CROSS APPLY STRING_SPLIT(ISNULL(t.Storecodes, ''), ',')
    WHERE LTRIM(RTRIM(value)) <> ''
) s
WHERE NOT EXISTS (
    SELECT 1 FROM TenantStoreStatuses x
    WHERE x.TenantId = s.TenantId AND x.StoreCode = s.StoreCode
);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantStoreStatuses");
        }
    }
}
