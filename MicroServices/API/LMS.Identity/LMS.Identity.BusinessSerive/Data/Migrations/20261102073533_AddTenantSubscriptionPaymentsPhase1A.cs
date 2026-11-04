using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Identity.BusinessSerive.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantSubscriptionPaymentsPhase1A : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "GraceEndUtc",
                table: "Tenants",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubscriptionEndUtc",
                table: "Tenants",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubscriptionStartUtc",
                table: "Tenants",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TenantSubscriptionPaymentOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantEntityId = table.Column<int>(type: "int", nullable: true),
                    PlanCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    BillingCycle = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false, defaultValue: "INR"),
                    OrderStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    GatewayOrderId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReceiptId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OrderExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PendingVerificationStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    PendingVerificationUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSubscriptionPaymentOrders", x => x.Id);
                    table.CheckConstraint("CK_TenantSubscriptionPaymentOrders_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_TenantSubscriptionPaymentOrders_Expiry", "[OrderExpiresUtc] IS NULL OR [OrderExpiresUtc] >= [CreatedDateUtc]");
                });

            migrationBuilder.CreateTable(
                name: "TenantSubscriptionPaymentTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantSubscriptionPaymentOrderId = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantEntityId = table.Column<int>(type: "int", nullable: true),
                    GatewayPaymentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    GatewayOrderId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PaymentProvider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsLiveMode = table.Column<bool>(type: "bit", nullable: false),
                    TransactionStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false, defaultValue: "INR"),
                    CreatedDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VerifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsSignatureVerified = table.Column<bool>(type: "bit", nullable: false),
                    FailureCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSubscriptionPaymentTransactions", x => x.Id);
                    table.CheckConstraint("CK_TenantSubscriptionPaymentTransactions_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_TenantSubscriptionPaymentTransactions_TenantSubscriptionPaymentOrders_TenantSubscriptionPaymentOrderId",
                        column: x => x.TenantSubscriptionPaymentOrderId,
                        principalTable: "TenantSubscriptionPaymentOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TenantSubscriptionStateHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantEntityId = table.Column<int>(type: "int", nullable: true),
                    PreviousSubscriptionStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    NewSubscriptionStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PreviousPlanCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    NewPlanCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ChangedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ChangeReason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantSubscriptionPaymentOrderId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSubscriptionStateHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantSubscriptionStateHistories_TenantSubscriptionPaymentOrders_TenantSubscriptionPaymentOrderId",
                        column: x => x.TenantSubscriptionPaymentOrderId,
                        principalTable: "TenantSubscriptionPaymentOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TenantSubscriptionWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantEntityId = table.Column<int>(type: "int", nullable: true),
                    TenantSubscriptionPaymentOrderId = table.Column<int>(type: "int", nullable: true),
                    EventId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PaymentProvider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsLiveMode = table.Column<bool>(type: "bit", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProcessingStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ProcessingError = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsSignatureVerified = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSubscriptionWebhookEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantSubscriptionWebhookEvents_TenantSubscriptionPaymentOrders_TenantSubscriptionPaymentOrderId",
                        column: x => x.TenantSubscriptionPaymentOrderId,
                        principalTable: "TenantSubscriptionPaymentOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubscriptionPaymentOrders_GatewayOrderId",
                table: "TenantSubscriptionPaymentOrders",
                column: "GatewayOrderId",
                unique: true,
                filter: "[GatewayOrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubscriptionPaymentOrders_ReceiptId",
                table: "TenantSubscriptionPaymentOrders",
                column: "ReceiptId",
                unique: true,
                filter: "[ReceiptId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubscriptionPaymentOrders_TenantEntityId",
                table: "TenantSubscriptionPaymentOrders",
                column: "TenantEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubscriptionPaymentOrders_TenantId_CreatedDateUtc",
                table: "TenantSubscriptionPaymentOrders",
                columns: new[] { "TenantId", "CreatedDateUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_TenantSubscriptionPaymentOrders_TenantId_Open",
                table: "TenantSubscriptionPaymentOrders",
                column: "TenantId",
                unique: true,
                filter: "[OrderStatus] IN ('Pending','Created','PendingVerification')");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubscriptionPaymentTransactions_TenantEntityId",
                table: "TenantSubscriptionPaymentTransactions",
                column: "TenantEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubscriptionPaymentTransactions_TenantId",
                table: "TenantSubscriptionPaymentTransactions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubscriptionPaymentTransactions_TenantSubscriptionPaymentOrderId",
                table: "TenantSubscriptionPaymentTransactions",
                column: "TenantSubscriptionPaymentOrderId");

            migrationBuilder.CreateIndex(
                name: "UX_TenantSubscriptionPaymentTransactions_Provider_Mode_PaymentId",
                table: "TenantSubscriptionPaymentTransactions",
                columns: new[] { "PaymentProvider", "IsLiveMode", "GatewayPaymentId" },
                unique: true,
                filter: "[GatewayPaymentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubscriptionStateHistories_TenantEntityId",
                table: "TenantSubscriptionStateHistories",
                column: "TenantEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubscriptionStateHistories_TenantId_ChangedAtUtc",
                table: "TenantSubscriptionStateHistories",
                columns: new[] { "TenantId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubscriptionStateHistories_TenantSubscriptionPaymentOrderId",
                table: "TenantSubscriptionStateHistories",
                column: "TenantSubscriptionPaymentOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubscriptionWebhookEvents_TenantEntityId",
                table: "TenantSubscriptionWebhookEvents",
                column: "TenantEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubscriptionWebhookEvents_TenantId",
                table: "TenantSubscriptionWebhookEvents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubscriptionWebhookEvents_TenantSubscriptionPaymentOrderId",
                table: "TenantSubscriptionWebhookEvents",
                column: "TenantSubscriptionPaymentOrderId");

            migrationBuilder.CreateIndex(
                name: "UX_TenantSubscriptionWebhookEvents_Provider_Mode_EventId",
                table: "TenantSubscriptionWebhookEvents",
                columns: new[] { "PaymentProvider", "IsLiveMode", "EventId" },
                unique: true,
                filter: "[EventId] IS NOT NULL");

            migrationBuilder.Sql(@"
UPDATE o
SET o.TenantEntityId = t.Id
FROM dbo.TenantSubscriptionPaymentOrders o
INNER JOIN dbo.Tenants t ON t.TenantId = o.TenantId
WHERE o.TenantEntityId IS NULL;

UPDATE tx
SET tx.TenantEntityId = t.Id
FROM dbo.TenantSubscriptionPaymentTransactions tx
INNER JOIN dbo.Tenants t ON t.TenantId = tx.TenantId
WHERE tx.TenantEntityId IS NULL;

UPDATE h
SET h.TenantEntityId = t.Id
FROM dbo.TenantSubscriptionStateHistories h
INNER JOIN dbo.Tenants t ON t.TenantId = h.TenantId
WHERE h.TenantEntityId IS NULL;

UPDATE w
SET w.TenantEntityId = t.Id
FROM dbo.TenantSubscriptionWebhookEvents w
INNER JOIN dbo.Tenants t ON t.TenantId = w.TenantId
WHERE w.TenantEntityId IS NULL;

IF EXISTS (SELECT 1 FROM dbo.TenantSubscriptionPaymentOrders WHERE TenantEntityId IS NULL)
    THROW 51000, 'Unable to backfill TenantEntityId for TenantSubscriptionPaymentOrders from Tenants.TenantId.', 1;

IF EXISTS (SELECT 1 FROM dbo.TenantSubscriptionPaymentTransactions WHERE TenantEntityId IS NULL)
    THROW 51000, 'Unable to backfill TenantEntityId for TenantSubscriptionPaymentTransactions from Tenants.TenantId.', 1;

IF EXISTS (SELECT 1 FROM dbo.TenantSubscriptionStateHistories WHERE TenantEntityId IS NULL)
    THROW 51000, 'Unable to backfill TenantEntityId for TenantSubscriptionStateHistories from Tenants.TenantId.', 1;

IF EXISTS (SELECT 1 FROM dbo.TenantSubscriptionWebhookEvents WHERE TenantEntityId IS NULL)
    THROW 51000, 'Unable to backfill TenantEntityId for TenantSubscriptionWebhookEvents from Tenants.TenantId.', 1;
");

            migrationBuilder.AlterColumn<int>(
                name: "TenantEntityId",
                table: "TenantSubscriptionWebhookEvents",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "TenantEntityId",
                table: "TenantSubscriptionStateHistories",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "TenantEntityId",
                table: "TenantSubscriptionPaymentTransactions",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "TenantEntityId",
                table: "TenantSubscriptionPaymentOrders",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TenantSubscriptionPaymentOrders_Tenants_TenantEntityId",
                table: "TenantSubscriptionPaymentOrders",
                column: "TenantEntityId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TenantSubscriptionPaymentTransactions_Tenants_TenantEntityId",
                table: "TenantSubscriptionPaymentTransactions",
                column: "TenantEntityId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TenantSubscriptionStateHistories_Tenants_TenantEntityId",
                table: "TenantSubscriptionStateHistories",
                column: "TenantEntityId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TenantSubscriptionWebhookEvents_Tenants_TenantEntityId",
                table: "TenantSubscriptionWebhookEvents",
                column: "TenantEntityId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantSubscriptionPaymentTransactions");

            migrationBuilder.DropTable(
                name: "TenantSubscriptionStateHistories");

            migrationBuilder.DropTable(
                name: "TenantSubscriptionWebhookEvents");

            migrationBuilder.DropTable(
                name: "TenantSubscriptionPaymentOrders");

            migrationBuilder.DropColumn(
                name: "GraceEndUtc",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SubscriptionEndUtc",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SubscriptionStartUtc",
                table: "Tenants");
        }
    }
}
