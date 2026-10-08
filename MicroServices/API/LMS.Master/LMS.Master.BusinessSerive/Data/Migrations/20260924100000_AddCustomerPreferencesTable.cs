using System;
using LMS.Master.BusinessSerive.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Master.BusinessSerive.Data.Migrations
{
    [DbContext(typeof(MasterDbContext))]
    [Migration("20260924100000_AddCustomerPreferencesTable")]
    public partial class AddCustomerPreferencesTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    StoreCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DefaultServiceType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DefaultPaymentMode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DefaultStarchLevel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerPreferences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPreferences_TenantName_StoreCode",
                table: "CustomerPreferences",
                columns: new[] { "TenantName", "StoreCode" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerPreferences");
        }
    }
}
