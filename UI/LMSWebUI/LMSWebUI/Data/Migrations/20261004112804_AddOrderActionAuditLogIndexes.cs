using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMSWebUI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderActionAuditLogIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_OrderActionAuditLogs_TenantName_StoreCode_OrderNo_LoggedAtUtc",
                table: "OrderActionAuditLogs",
                columns: new[] { "TenantName", "StoreCode", "OrderNo", "LoggedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderActionAuditLogs_TenantName_StoreCode_OrderNo_LoggedAtUtc",
                table: "OrderActionAuditLogs");
        }
    }
}
