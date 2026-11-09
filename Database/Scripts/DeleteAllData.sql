
/*
    Excel Laundry Services - Tenant/Application Data Cleanup
    Target database: db_acff11_lmsdb

    PRESERVED:
      __EFMigrationsHistory
      AspNetRoles
      AspNetUserRoles
      AspNetUsers
      SuperAdmins
      SubscriptionPlans

    SAFETY:
      - Explicit database name validation
      - Transaction with rollback on error
      - Foreign-key dependency order
      - Identity reseeding based on remaining MAX(Id)
      - Missing optional tables are skipped
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'db_acff11_lmsdb'
BEGIN
    THROW 50001,
        'Wrong database selected. Select db_acff11_lmsdb before running this script.',
        1;
END;

PRINT 'Target database: ' + DB_NAME();
PRINT 'Starting cleanup...';

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @CleanupTables TABLE
    (
        SequenceNo INT PRIMARY KEY,
        TableName SYSNAME
    );

    INSERT INTO @CleanupTables (SequenceNo, TableName)
    VALUES
    -- Subscription child records first
    (1,  N'TenantSubscriptionWebhookEvents'),
    (2,  N'TenantSubscriptionStateHistories'),
    (3,  N'TenantSubscriptionPaymentTransactions'),
    (4,  N'TenantSubscriptionPaymentOrders'),

    -- Laundry order dependencies
    (5,  N'LaundryOrderTaxLines'),
    (6,  N'LaundryOrderItems'),
    (7,  N'LaundryOrders'),

    -- Tax invoice dependencies
    (8,  N'TaxInvoiceComponents'),
    (9,  N'TaxInvoiceSettings'),

    -- Operational and tenant data
    (10, N'OrderActionAuditLogs'),
    (11, N'CustomerAdvances'),
    (12, N'CustomerPreferences'),
    (13, N'Customers'),
    (14, N'LaundryItemPrices'),
    (15, N'PricingRules'),
    (16, N'PaymentTransactions'),
    (17, N'BarcodeTagSettings'),
    (18, N'PaymentSettings'),
    (19, N'TenantIntegrationSettings'),
    (20, N'StoreItemMaster'),
    (21, N'StoreServiceMaster'),
    (22, N'WorkflowStatuses'),
    (23, N'TenantStoreStatuses'),
    (24, N'StoreConfigurations'),
    (25, N'StoreUsers'),
    (26, N'TenantStoreInfos'),

    -- Tenant must be deleted last
    (27, N'Tenants');

    DECLARE @TableName SYSNAME;
    DECLARE @SQL NVARCHAR(MAX);
    DECLARE @DeletedCount INT;

    DECLARE DeleteCursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT TableName
        FROM @CleanupTables
        ORDER BY SequenceNo;

    OPEN DeleteCursor;

    FETCH NEXT FROM DeleteCursor INTO @TableName;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF OBJECT_ID(N'dbo.' + @TableName, N'U') IS NOT NULL
        BEGIN
            SET @SQL =
                N'DELETE FROM dbo.' + QUOTENAME(@TableName) +
                N'; SET @Rows = @@ROWCOUNT;';

            SET @DeletedCount = 0;

            EXEC sys.sp_executesql
                @SQL,
                N'@Rows INT OUTPUT',
                @Rows = @DeletedCount OUTPUT;

            PRINT 'Deleted from ' + @TableName
                + ': ' + CONVERT(VARCHAR(20), @DeletedCount)
                + ' row(s).';
        END
        ELSE
        BEGIN
            PRINT 'Skipped missing table: ' + @TableName;
        END;

        FETCH NEXT FROM DeleteCursor INTO @TableName;
    END;

    CLOSE DeleteCursor;
    DEALLOCATE DeleteCursor;

    /*
       Reseed only the cleanup tables.
       Empty identity tables -> 0 (next generated value = 1).
       Non-empty tables -> their actual MAX(identity value).
       Protected tables are never reseeded.
    */

    DECLARE @IdentityColumn SYSNAME;
    DECLARE @MaxIdentity DECIMAL(38,0);

    DECLARE ReseedCursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT
            t.TableName,
            ic.name
        FROM @CleanupTables t
        INNER JOIN sys.tables st
            ON st.name = t.TableName
           AND st.schema_id = SCHEMA_ID(N'dbo')
        INNER JOIN sys.identity_columns ic
            ON ic.object_id = st.object_id
        ORDER BY t.SequenceNo;

    OPEN ReseedCursor;

    FETCH NEXT FROM ReseedCursor
        INTO @TableName, @IdentityColumn;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @MaxIdentity = 0;

        SET @SQL =
            N'SELECT @MaxValue = ISNULL(MAX(CONVERT(DECIMAL(38,0), '
            + QUOTENAME(@IdentityColumn) + N')), 0) '
            + N'FROM dbo.' + QUOTENAME(@TableName) + N';';

        EXEC sys.sp_executesql
            @SQL,
            N'@MaxValue DECIMAL(38,0) OUTPUT',
            @MaxValue = @MaxIdentity OUTPUT;

        SET @SQL =
            N'DBCC CHECKIDENT ('''
            + N'[dbo].[' + REPLACE(@TableName, N']', N']]') + N']'
            + N''', RESEED, '
            + CONVERT(NVARCHAR(40), @MaxIdentity) + N') WITH NO_INFOMSGS;';

        EXEC sys.sp_executesql @SQL;

        PRINT 'Reseeded ' + @TableName
            + ' to ' + CONVERT(VARCHAR(40), @MaxIdentity);

        FETCH NEXT FROM ReseedCursor
            INTO @TableName, @IdentityColumn;
    END;

    CLOSE ReseedCursor;
    DEALLOCATE ReseedCursor;

    COMMIT TRANSACTION;

    PRINT '========================================';
    PRINT 'CLEANUP COMPLETED SUCCESSFULLY';
    PRINT 'Preserved admin accounts, roles, plans, and migration history.';
    PRINT '========================================';

END TRY
BEGIN CATCH

    IF CURSOR_STATUS('local', 'DeleteCursor') >= 0
        CLOSE DeleteCursor;

    IF CURSOR_STATUS('local', 'DeleteCursor') >= -1
        DEALLOCATE DeleteCursor;

    IF CURSOR_STATUS('local', 'ReseedCursor') >= 0
        CLOSE ReseedCursor;

    IF CURSOR_STATUS('local', 'ReseedCursor') >= -1
        DEALLOCATE ReseedCursor;

    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    PRINT 'CLEANUP FAILED. TRANSACTION ROLLED BACK.';
    PRINT ERROR_MESSAGE();

    THROW;
END CATCH;
