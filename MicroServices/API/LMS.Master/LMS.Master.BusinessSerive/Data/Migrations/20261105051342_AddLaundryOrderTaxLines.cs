using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Master.BusinessSerive.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLaundryOrderTaxLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LaundryOrderTaxLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LaundryOrderId = table.Column<int>(type: "int", nullable: false),
                    TenantName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    StoreCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OrderNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    TaxMode = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    ComponentCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ComponentDisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Jurisdiction = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    IsCompound = table.Column<bool>(type: "bit", nullable: false),
                    RatePercent = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    TaxableBaseAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LaundryOrderTaxLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LaundryOrderTaxLines_LaundryOrders_LaundryOrderId",
                        column: x => x.LaundryOrderId,
                        principalTable: "LaundryOrders",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LaundryOrderTaxLines_LaundryOrderId_LineNo",
                table: "LaundryOrderTaxLines",
                columns: new[] { "LaundryOrderId", "LineNo" });

            migrationBuilder.CreateIndex(
                name: "IX_LaundryOrderTaxLines_TenantName_StoreCode_OrderNo_LineNo",
                table: "LaundryOrderTaxLines",
                columns: new[] { "TenantName", "StoreCode", "OrderNo", "LineNo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LaundryOrderTaxLines");
        }
    }
}
