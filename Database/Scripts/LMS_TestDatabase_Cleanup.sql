# LMS Test Database Cleanup Script

-- IMPORTANT:
-- Run only against an isolated LMS test database.
-- Back up the database before execution.
-- Preserve schema and EF migration history.

SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    -- 1. Subscription dependent records
    DELETE FROM dbo.TenantSubscriptionWebhookEvents;
    DELETE FROM dbo.TenantSubscriptionStateHistories;
    DELETE FROM dbo.TenantSubscriptionPaymentTransactions;
    DELETE FROM dbo.TenantSubscriptionPaymentOrders;

    -- 2. Payment and customer records
    DELETE FROM dbo.PaymentTransactions;
    DELETE FROM dbo.CustomerAdvances;
    DELETE FROM dbo.CustomerPreferences;

    -- 3. Order dependent records
    DELETE FROM dbo.OrderActionAuditLogs;
    DELETE FROM dbo.LaundryOrderTaxLines;
    DELETE FROM dbo.LaundryOrderItems;
    DELETE FROM dbo.LaundryOrders;

    -- 4. Tax configuration
    DELETE FROM dbo.TaxInvoiceComponents;
    DELETE FROM dbo.TaxInvoiceSettings;

    -- 5. Tenant and store configuration
    DELETE FROM dbo.BarcodeTagSettings;
    DELETE FROM dbo.LaundryItemPrices;
    DELETE FROM dbo.PricingRules;
    DELETE FROM dbo.PaymentSettings;
    DELETE FROM dbo.TenantIntegrationSettings;

    -- 6. Master records
    DELETE FROM dbo.StoreItemMaster;
    DELETE FROM dbo.StoreServiceMaster;
    DELETE FROM dbo.Customers;

    -- 7. Store records
    DELETE FROM dbo.StoreConfigurations;
    DELETE FROM dbo.StoreUsers;
    DELETE FROM dbo.TenantStoreStatuses;
    DELETE FROM dbo.TenantStoreInfos;

    -- 8. Tenants last
    DELETE FROM dbo.Tenants;

    COMMIT TRANSACTION;

    SELECT 'LMS test database cleanup completed successfully' AS Result;

END TRY
BEGIN CATCH

    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;

END CATCH;

-- PRESERVED:
-- AspNetUsers
-- AspNetRoles
-- AspNetUserRoles
-- SuperAdmins
-- SubscriptionPlans
-- WorkflowStatuses
-- __EFMigrationsHistory