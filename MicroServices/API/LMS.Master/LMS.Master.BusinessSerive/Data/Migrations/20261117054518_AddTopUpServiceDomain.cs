using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Master.BusinessSerive.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTopUpServiceDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LaundryOrderTopUps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LaundryOrderId = table.Column<int>(type: "int", nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    StoreCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OrderNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    TopUpName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ChargeType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ServiceType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ItemName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PieceNo = table.Column<int>(type: "int", nullable: false),
                    AppliedQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    UnitPriceSnapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LineAmountSnapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LaundryOrderTopUps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LaundryOrderTopUps_LaundryOrders_LaundryOrderId",
                        column: x => x.LaundryOrderId,
                        principalTable: "LaundryOrders",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TopUpServiceMaster",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    StoreCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TopUpName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ChargeType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TopUpServiceMaster", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TopUpServiceApplicability",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TopUpServiceMasterId = table.Column<int>(type: "int", nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    StoreCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ServiceType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ItemName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TopUpServiceApplicability", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TopUpServiceApplicability_TopUpServiceMaster_TopUpServiceMasterId",
                        column: x => x.TopUpServiceMasterId,
                        principalTable: "TopUpServiceMaster",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LaundryOrderTopUps_LaundryOrderId_LineNo",
                table: "LaundryOrderTopUps",
                columns: new[] { "LaundryOrderId", "LineNo" });

            migrationBuilder.CreateIndex(
                name: "IX_LaundryOrderTopUps_TenantName_StoreCode_OrderNo_LineNo",
                table: "LaundryOrderTopUps",
                columns: new[] { "TenantName", "StoreCode", "OrderNo", "LineNo" });

            migrationBuilder.CreateIndex(
                name: "IX_TopUpServiceApplicability_TenantName_StoreCode_IsActive",
                table: "TopUpServiceApplicability",
                columns: new[] { "TenantName", "StoreCode", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_TopUpServiceApplicability_TenantName_StoreCode_TopUpServiceMasterId_ServiceType_Category_ItemName",
                table: "TopUpServiceApplicability",
                columns: new[] { "TenantName", "StoreCode", "TopUpServiceMasterId", "ServiceType", "Category", "ItemName" },
                unique: true,
                filter: "[ServiceType] IS NOT NULL AND [Category] IS NOT NULL AND [ItemName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TopUpServiceApplicability_TopUpServiceMasterId",
                table: "TopUpServiceApplicability",
                column: "TopUpServiceMasterId");

            migrationBuilder.CreateIndex(
                name: "IX_TopUpServiceMaster_TenantName_StoreCode_IsActive_IsArchived",
                table: "TopUpServiceMaster",
                columns: new[] { "TenantName", "StoreCode", "IsActive", "IsArchived" });

            migrationBuilder.CreateIndex(
                name: "IX_TopUpServiceMaster_TenantName_StoreCode_TopUpName",
                table: "TopUpServiceMaster",
                columns: new[] { "TenantName", "StoreCode", "TopUpName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LaundryOrderTopUps");

            migrationBuilder.DropTable(
                name: "TopUpServiceApplicability");

            migrationBuilder.DropTable(
                name: "TopUpServiceMaster");
        }
    }
}
