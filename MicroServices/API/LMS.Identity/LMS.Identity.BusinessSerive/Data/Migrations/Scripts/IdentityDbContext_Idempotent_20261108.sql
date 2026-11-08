IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923104959_InitialSqlServer'
)
BEGIN
    CREATE TABLE [Tenants] (
        [Id] int NOT NULL IDENTITY,
        [NoOfStores] int NOT NULL,
        [TenantId] nvarchar(64) NOT NULL,
        [TenantName] nvarchar(256) NOT NULL,
        [Email] nvarchar(256) NOT NULL,
        [PhoneNumber] int NOT NULL,
        [MiddleName] nvarchar(max) NULL,
        [FamilyName] nvarchar(max) NULL,
        [Address] nvarchar(512) NULL,
        [City] nvarchar(128) NULL,
        [Region] nvarchar(128) NULL,
        [Zip] nvarchar(32) NULL,
        [Country] nvarchar(128) NULL,
        [CreatedDate] datetime2 NOT NULL,
        [ModifiedDate] datetime2 NOT NULL,
        CONSTRAINT [PK_Tenants] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923104959_InitialSqlServer'
)
BEGIN
    CREATE TABLE [TenantStoreInfos] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] nvarchar(64) NOT NULL,
        [Name] nvarchar(256) NOT NULL,
        [Password] nvarchar(256) NOT NULL,
        [Status] nvarchar(64) NOT NULL,
        [IsPasswordChanged] bit NOT NULL,
        [Storecodes] nvarchar(max) NULL,
        CONSTRAINT [PK_TenantStoreInfos] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923104959_InitialSqlServer'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tenants_Email] ON [Tenants] ([Email]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923104959_InitialSqlServer'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tenants_TenantId] ON [Tenants] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923104959_InitialSqlServer'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TenantStoreInfos_TenantId] ON [TenantStoreInfos] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923104959_InitialSqlServer'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260923104959_InitialSqlServer', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052159_AddStoreUsersTable'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Tenants]') AND [c].[name] = N'PhoneNumber');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [Tenants] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [Tenants] ALTER COLUMN [PhoneNumber] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052159_AddStoreUsersTable'
)
BEGIN
    CREATE TABLE [StoreUsers] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] nvarchar(64) NOT NULL,
        [StoreCode] nvarchar(32) NOT NULL,
        [Email] nvarchar(256) NOT NULL,
        [Password] nvarchar(256) NOT NULL,
        [Role] nvarchar(32) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedDate] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [CreatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_StoreUsers] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052159_AddStoreUsersTable'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StoreUsers_TenantId_StoreCode_Email] ON [StoreUsers] ([TenantId], [StoreCode], [Email]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052159_AddStoreUsersTable'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260929052159_AddStoreUsersTable', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085805_AddStoreConfigurationsTable'
)
BEGIN
    CREATE TABLE [StoreConfigurations] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] nvarchar(64) NOT NULL,
        [StoreCode] nvarchar(32) NOT NULL,
        [ConfigurationJson] nvarchar(max) NOT NULL,
        [CreatedDate] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [CreatedBy] nvarchar(256) NULL,
        [ModifiedDate] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [ModifiedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_StoreConfigurations] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085805_AddStoreConfigurationsTable'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StoreConfigurations_TenantId_StoreCode] ON [StoreConfigurations] ([TenantId], [StoreCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001085805_AddStoreConfigurationsTable'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261001085805_AddStoreConfigurationsTable', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002120601_AddSuperAdminsTable'
)
BEGIN
    CREATE TABLE [SuperAdmins] (
        [Id] int NOT NULL IDENTITY,
        [Email] nvarchar(256) NOT NULL,
        [Password] nvarchar(512) NOT NULL,
        [DisplayName] nvarchar(128) NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [IsPasswordChanged] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedDate] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [LastLoginDate] datetime2 NULL,
        CONSTRAINT [PK_SuperAdmins] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002120601_AddSuperAdminsTable'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SuperAdmins_Email] ON [SuperAdmins] ([Email]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002120601_AddSuperAdminsTable'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261002120601_AddSuperAdminsTable', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002124422_AddTenantApprovalStatus'
)
BEGIN
    ALTER TABLE [Tenants] ADD [ApprovalDate] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002124422_AddTenantApprovalStatus'
)
BEGIN
    ALTER TABLE [Tenants] ADD [ApprovalReason] nvarchar(512) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002124422_AddTenantApprovalStatus'
)
BEGIN
    ALTER TABLE [Tenants] ADD [ApprovalStatus] nvarchar(32) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002124422_AddTenantApprovalStatus'
)
BEGIN
    ALTER TABLE [Tenants] ADD [ApprovedBy] nvarchar(256) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002124422_AddTenantApprovalStatus'
)
BEGIN
    CREATE INDEX [IX_Tenants_ApprovalStatus] ON [Tenants] ([ApprovalStatus]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002124422_AddTenantApprovalStatus'
)
BEGIN
    UPDATE [Tenants] SET [ApprovalStatus] = 'Approved' WHERE [ApprovalStatus] IS NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002124422_AddTenantApprovalStatus'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261002124422_AddTenantApprovalStatus', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015090000_AddTenantStoreStatusesTable'
)
BEGIN
    CREATE TABLE [TenantStoreStatuses] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] nvarchar(64) NOT NULL,
        [StoreCode] nvarchar(32) NOT NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedDate] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [ActivatedBy] nvarchar(256) NULL,
        [ActivatedDate] datetime2 NULL,
        CONSTRAINT [PK_TenantStoreStatuses] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015090000_AddTenantStoreStatusesTable'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TenantStoreStatuses_TenantId_StoreCode] ON [TenantStoreStatuses] ([TenantId], [StoreCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015090000_AddTenantStoreStatusesTable'
)
BEGIN

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
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015090000_AddTenantStoreStatusesTable'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261015090000_AddTenantStoreStatusesTable', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015100000_RepairSuperAdminDefaults'
)
BEGIN

    DECLARE @constraintName sysname;
    DECLARE @definition nvarchar(max);
    DECLARE @dropSql nvarchar(max);

    SELECT @constraintName = dc.name, @definition = dc.definition
    FROM sys.default_constraints AS dc
    JOIN sys.columns AS c
        ON c.object_id = dc.parent_object_id
        AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'[dbo].[SuperAdmins]')
      AND c.name = N'IsActive';

    IF @constraintName IS NULL OR LOWER(REPLACE(REPLACE(REPLACE(@definition, '(', ''), ')', ''), ' ', '')) <> N'1'
    BEGIN
        IF @constraintName IS NOT NULL
        BEGIN
            SET @dropSql = N'ALTER TABLE [dbo].[SuperAdmins] DROP CONSTRAINT ' + QUOTENAME(@constraintName);
            EXEC sys.sp_executesql @dropSql;
        END;

        ALTER TABLE [dbo].[SuperAdmins]
            ADD CONSTRAINT [DF_SuperAdmins_IsActive] DEFAULT (1) FOR [IsActive];
    END;

    SET @constraintName = NULL;
    SET @definition = NULL;

    SELECT @constraintName = dc.name, @definition = dc.definition
    FROM sys.default_constraints AS dc
    JOIN sys.columns AS c
        ON c.object_id = dc.parent_object_id
        AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'[dbo].[SuperAdmins]')
      AND c.name = N'IsPasswordChanged';

    IF @constraintName IS NULL OR LOWER(REPLACE(REPLACE(REPLACE(@definition, '(', ''), ')', ''), ' ', '')) <> N'0'
    BEGIN
        IF @constraintName IS NOT NULL
        BEGIN
            SET @dropSql = N'ALTER TABLE [dbo].[SuperAdmins] DROP CONSTRAINT ' + QUOTENAME(@constraintName);
            EXEC sys.sp_executesql @dropSql;
        END;

        ALTER TABLE [dbo].[SuperAdmins]
            ADD CONSTRAINT [DF_SuperAdmins_IsPasswordChanged] DEFAULT (0) FOR [IsPasswordChanged];
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015100000_RepairSuperAdminDefaults'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261015100000_RepairSuperAdminDefaults', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015114059_AddCurrencyColsToStoreConfigs'
)
BEGIN

    IF COL_LENGTH('dbo.StoreConfigurations','CurrencyCode') IS NULL
        ALTER TABLE dbo.StoreConfigurations ADD CurrencyCode NVARCHAR(16) NULL;
    IF COL_LENGTH('dbo.StoreConfigurations','CurrencySymbol') IS NULL
        ALTER TABLE dbo.StoreConfigurations ADD CurrencySymbol NVARCHAR(16) NULL;
    IF COL_LENGTH('dbo.StoreConfigurations','CurrencyLocale') IS NULL
        ALTER TABLE dbo.StoreConfigurations ADD CurrencyLocale NVARCHAR(32) NULL;
    IF COL_LENGTH('dbo.StoreConfigurations','CurrencyDecimalDigits') IS NULL
        ALTER TABLE dbo.StoreConfigurations ADD CurrencyDecimalDigits INT NULL;

    IF COL_LENGTH('dbo.TenantStoreInfos','CurrencyCode') IS NULL
        ALTER TABLE dbo.TenantStoreInfos ADD CurrencyCode NVARCHAR(16) NULL;
    IF COL_LENGTH('dbo.TenantStoreInfos','CurrencySymbol') IS NULL
        ALTER TABLE dbo.TenantStoreInfos ADD CurrencySymbol NVARCHAR(16) NULL;
    IF COL_LENGTH('dbo.TenantStoreInfos','CurrencyLocale') IS NULL
        ALTER TABLE dbo.TenantStoreInfos ADD CurrencyLocale NVARCHAR(32) NULL;
    IF COL_LENGTH('dbo.TenantStoreInfos','CurrencyDecimalDigits') IS NULL
        ALTER TABLE dbo.TenantStoreInfos ADD CurrencyDecimalDigits INT NULL;

END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015114059_AddCurrencyColsToStoreConfigs'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261015114059_AddCurrencyColsToStoreConfigs', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102043837_AddSubscriptionPlanCatalog'
)
BEGIN
    CREATE TABLE [SubscriptionPlans] (
        [Id] int NOT NULL IDENTITY,
        [PlanCode] nvarchar(32) NOT NULL,
        [BillingCycle] nvarchar(16) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Currency] nvarchar(8) NOT NULL DEFAULT N'INR',
        [IsActive] bit NOT NULL,
        [CreatedDateUtc] datetime2 NOT NULL,
        [ModifiedDateUtc] datetime2 NULL,
        [UpdatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_SubscriptionPlans] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102043837_AddSubscriptionPlanCatalog'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SubscriptionPlans_PlanCode_BillingCycle] ON [SubscriptionPlans] ([PlanCode], [BillingCycle]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102043837_AddSubscriptionPlanCatalog'
)
BEGIN

    IF NOT EXISTS (SELECT 1 FROM dbo.SubscriptionPlans WHERE PlanCode = 'Standard' AND BillingCycle = 'Monthly')
    BEGIN
        INSERT INTO dbo.SubscriptionPlans (PlanCode, BillingCycle, Amount, Currency, IsActive, CreatedDateUtc, ModifiedDateUtc, UpdatedBy)
        VALUES ('Standard', 'Monthly', 499.00, 'INR', 1, GETUTCDATE(), NULL, NULL);
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.SubscriptionPlans WHERE PlanCode = 'Standard' AND BillingCycle = 'Yearly')
    BEGIN
        INSERT INTO dbo.SubscriptionPlans (PlanCode, BillingCycle, Amount, Currency, IsActive, CreatedDateUtc, ModifiedDateUtc, UpdatedBy)
        VALUES ('Standard', 'Yearly', 4999.00, 'INR', 1, GETUTCDATE(), NULL, NULL);
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.SubscriptionPlans WHERE PlanCode = 'Premium' AND BillingCycle = 'Monthly')
    BEGIN
        INSERT INTO dbo.SubscriptionPlans (PlanCode, BillingCycle, Amount, Currency, IsActive, CreatedDateUtc, ModifiedDateUtc, UpdatedBy)
        VALUES ('Premium', 'Monthly', 899.00, 'INR', 1, GETUTCDATE(), NULL, NULL);
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.SubscriptionPlans WHERE PlanCode = 'Premium' AND BillingCycle = 'Yearly')
    BEGIN
        INSERT INTO dbo.SubscriptionPlans (PlanCode, BillingCycle, Amount, Currency, IsActive, CreatedDateUtc, ModifiedDateUtc, UpdatedBy)
        VALUES ('Premium', 'Yearly', 8999.00, 'INR', 1, GETUTCDATE(), NULL, NULL);
    END

END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102043837_AddSubscriptionPlanCatalog'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261102043837_AddSubscriptionPlanCatalog', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    ALTER TABLE [Tenants] ADD [GraceEndUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    ALTER TABLE [Tenants] ADD [SubscriptionEndUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    ALTER TABLE [Tenants] ADD [SubscriptionStartUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    CREATE TABLE [TenantSubscriptionPaymentOrders] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] nvarchar(64) NOT NULL,
        [TenantEntityId] int NULL,
        [PlanCode] nvarchar(32) NOT NULL,
        [BillingCycle] nvarchar(16) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Currency] nvarchar(8) NOT NULL DEFAULT N'INR',
        [OrderStatus] nvarchar(32) NOT NULL,
        [GatewayOrderId] nvarchar(128) NULL,
        [ReceiptId] nvarchar(128) NULL,
        [CreatedDateUtc] datetime2 NOT NULL,
        [ModifiedDateUtc] datetime2 NULL,
        [OrderExpiresUtc] datetime2 NULL,
        [PendingVerificationStatus] nvarchar(32) NULL,
        [PendingVerificationUntilUtc] datetime2 NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_TenantSubscriptionPaymentOrders] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TenantSubscriptionPaymentOrders_Amount] CHECK ([Amount] > 0),
        CONSTRAINT [CK_TenantSubscriptionPaymentOrders_Expiry] CHECK ([OrderExpiresUtc] IS NULL OR [OrderExpiresUtc] >= [CreatedDateUtc])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    CREATE TABLE [TenantSubscriptionPaymentTransactions] (
        [Id] int NOT NULL IDENTITY,
        [TenantSubscriptionPaymentOrderId] int NOT NULL,
        [TenantId] nvarchar(64) NOT NULL,
        [TenantEntityId] int NULL,
        [GatewayPaymentId] nvarchar(128) NULL,
        [GatewayOrderId] nvarchar(128) NULL,
        [PaymentProvider] nvarchar(32) NOT NULL,
        [IsLiveMode] bit NOT NULL,
        [TransactionStatus] nvarchar(32) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Currency] nvarchar(8) NOT NULL DEFAULT N'INR',
        [CreatedDateUtc] datetime2 NOT NULL,
        [PaidAtUtc] datetime2 NULL,
        [VerifiedAtUtc] datetime2 NULL,
        [IsSignatureVerified] bit NOT NULL,
        [FailureCode] nvarchar(64) NULL,
        [FailureReason] nvarchar(512) NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_TenantSubscriptionPaymentTransactions] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_TenantSubscriptionPaymentTransactions_Amount] CHECK ([Amount] > 0),
        CONSTRAINT [FK_TenantSubscriptionPaymentTransactions_TenantSubscriptionPaymentOrders_TenantSubscriptionPaymentOrderId] FOREIGN KEY ([TenantSubscriptionPaymentOrderId]) REFERENCES [TenantSubscriptionPaymentOrders] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    CREATE TABLE [TenantSubscriptionStateHistories] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] nvarchar(64) NOT NULL,
        [TenantEntityId] int NULL,
        [PreviousSubscriptionStatus] nvarchar(32) NULL,
        [NewSubscriptionStatus] nvarchar(32) NOT NULL,
        [PreviousPlanCode] nvarchar(32) NULL,
        [NewPlanCode] nvarchar(32) NULL,
        [ChangedBy] nvarchar(256) NULL,
        [ChangeReason] nvarchar(256) NULL,
        [ChangedAtUtc] datetime2 NOT NULL,
        [TenantSubscriptionPaymentOrderId] int NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_TenantSubscriptionStateHistories] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TenantSubscriptionStateHistories_TenantSubscriptionPaymentOrders_TenantSubscriptionPaymentOrderId] FOREIGN KEY ([TenantSubscriptionPaymentOrderId]) REFERENCES [TenantSubscriptionPaymentOrders] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    CREATE TABLE [TenantSubscriptionWebhookEvents] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] nvarchar(64) NOT NULL,
        [TenantEntityId] int NULL,
        [TenantSubscriptionPaymentOrderId] int NULL,
        [EventId] nvarchar(128) NULL,
        [PaymentProvider] nvarchar(32) NOT NULL,
        [IsLiveMode] bit NOT NULL,
        [EventType] nvarchar(128) NOT NULL,
        [PayloadJson] nvarchar(max) NOT NULL,
        [ProcessingStatus] nvarchar(32) NOT NULL,
        [ProcessingError] nvarchar(1024) NULL,
        [ReceivedAtUtc] datetime2 NOT NULL,
        [ProcessedAtUtc] datetime2 NULL,
        [IsSignatureVerified] bit NOT NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_TenantSubscriptionWebhookEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TenantSubscriptionWebhookEvents_TenantSubscriptionPaymentOrders_TenantSubscriptionPaymentOrderId] FOREIGN KEY ([TenantSubscriptionPaymentOrderId]) REFERENCES [TenantSubscriptionPaymentOrders] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_TenantSubscriptionPaymentOrders_GatewayOrderId] ON [TenantSubscriptionPaymentOrders] ([GatewayOrderId]) WHERE [GatewayOrderId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_TenantSubscriptionPaymentOrders_ReceiptId] ON [TenantSubscriptionPaymentOrders] ([ReceiptId]) WHERE [ReceiptId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    CREATE INDEX [IX_TenantSubscriptionPaymentOrders_TenantEntityId] ON [TenantSubscriptionPaymentOrders] ([TenantEntityId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    CREATE INDEX [IX_TenantSubscriptionPaymentOrders_TenantId_CreatedDateUtc] ON [TenantSubscriptionPaymentOrders] ([TenantId], [CreatedDateUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TenantSubscriptionPaymentOrders_TenantId_Open] ON [TenantSubscriptionPaymentOrders] ([TenantId]) WHERE [OrderStatus] IN (''Pending'',''Created'',''PendingVerification'')');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    CREATE INDEX [IX_TenantSubscriptionPaymentTransactions_TenantEntityId] ON [TenantSubscriptionPaymentTransactions] ([TenantEntityId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    CREATE INDEX [IX_TenantSubscriptionPaymentTransactions_TenantId] ON [TenantSubscriptionPaymentTransactions] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    CREATE INDEX [IX_TenantSubscriptionPaymentTransactions_TenantSubscriptionPaymentOrderId] ON [TenantSubscriptionPaymentTransactions] ([TenantSubscriptionPaymentOrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TenantSubscriptionPaymentTransactions_Provider_Mode_PaymentId] ON [TenantSubscriptionPaymentTransactions] ([PaymentProvider], [IsLiveMode], [GatewayPaymentId]) WHERE [GatewayPaymentId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    CREATE INDEX [IX_TenantSubscriptionStateHistories_TenantEntityId] ON [TenantSubscriptionStateHistories] ([TenantEntityId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    CREATE INDEX [IX_TenantSubscriptionStateHistories_TenantId_ChangedAtUtc] ON [TenantSubscriptionStateHistories] ([TenantId], [ChangedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    CREATE INDEX [IX_TenantSubscriptionStateHistories_TenantSubscriptionPaymentOrderId] ON [TenantSubscriptionStateHistories] ([TenantSubscriptionPaymentOrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    CREATE INDEX [IX_TenantSubscriptionWebhookEvents_TenantEntityId] ON [TenantSubscriptionWebhookEvents] ([TenantEntityId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    CREATE INDEX [IX_TenantSubscriptionWebhookEvents_TenantId] ON [TenantSubscriptionWebhookEvents] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    CREATE INDEX [IX_TenantSubscriptionWebhookEvents_TenantSubscriptionPaymentOrderId] ON [TenantSubscriptionWebhookEvents] ([TenantSubscriptionPaymentOrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TenantSubscriptionWebhookEvents_Provider_Mode_EventId] ON [TenantSubscriptionWebhookEvents] ([PaymentProvider], [IsLiveMode], [EventId]) WHERE [EventId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN

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

END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    DROP INDEX [IX_TenantSubscriptionWebhookEvents_TenantEntityId] ON [TenantSubscriptionWebhookEvents];
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSubscriptionWebhookEvents]') AND [c].[name] = N'TenantEntityId');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [TenantSubscriptionWebhookEvents] DROP CONSTRAINT [' + @var1 + '];');
    ALTER TABLE [TenantSubscriptionWebhookEvents] ALTER COLUMN [TenantEntityId] int NOT NULL;
    CREATE INDEX [IX_TenantSubscriptionWebhookEvents_TenantEntityId] ON [TenantSubscriptionWebhookEvents] ([TenantEntityId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    DROP INDEX [IX_TenantSubscriptionStateHistories_TenantEntityId] ON [TenantSubscriptionStateHistories];
    DECLARE @var2 sysname;
    SELECT @var2 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSubscriptionStateHistories]') AND [c].[name] = N'TenantEntityId');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [TenantSubscriptionStateHistories] DROP CONSTRAINT [' + @var2 + '];');
    ALTER TABLE [TenantSubscriptionStateHistories] ALTER COLUMN [TenantEntityId] int NOT NULL;
    CREATE INDEX [IX_TenantSubscriptionStateHistories_TenantEntityId] ON [TenantSubscriptionStateHistories] ([TenantEntityId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    DROP INDEX [IX_TenantSubscriptionPaymentTransactions_TenantEntityId] ON [TenantSubscriptionPaymentTransactions];
    DECLARE @var3 sysname;
    SELECT @var3 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSubscriptionPaymentTransactions]') AND [c].[name] = N'TenantEntityId');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [TenantSubscriptionPaymentTransactions] DROP CONSTRAINT [' + @var3 + '];');
    ALTER TABLE [TenantSubscriptionPaymentTransactions] ALTER COLUMN [TenantEntityId] int NOT NULL;
    CREATE INDEX [IX_TenantSubscriptionPaymentTransactions_TenantEntityId] ON [TenantSubscriptionPaymentTransactions] ([TenantEntityId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    DROP INDEX [IX_TenantSubscriptionPaymentOrders_TenantEntityId] ON [TenantSubscriptionPaymentOrders];
    DECLARE @var4 sysname;
    SELECT @var4 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[TenantSubscriptionPaymentOrders]') AND [c].[name] = N'TenantEntityId');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [TenantSubscriptionPaymentOrders] DROP CONSTRAINT [' + @var4 + '];');
    ALTER TABLE [TenantSubscriptionPaymentOrders] ALTER COLUMN [TenantEntityId] int NOT NULL;
    CREATE INDEX [IX_TenantSubscriptionPaymentOrders_TenantEntityId] ON [TenantSubscriptionPaymentOrders] ([TenantEntityId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    ALTER TABLE [TenantSubscriptionPaymentOrders] ADD CONSTRAINT [FK_TenantSubscriptionPaymentOrders_Tenants_TenantEntityId] FOREIGN KEY ([TenantEntityId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    ALTER TABLE [TenantSubscriptionPaymentTransactions] ADD CONSTRAINT [FK_TenantSubscriptionPaymentTransactions_Tenants_TenantEntityId] FOREIGN KEY ([TenantEntityId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    ALTER TABLE [TenantSubscriptionStateHistories] ADD CONSTRAINT [FK_TenantSubscriptionStateHistories_Tenants_TenantEntityId] FOREIGN KEY ([TenantEntityId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    ALTER TABLE [TenantSubscriptionWebhookEvents] ADD CONSTRAINT [FK_TenantSubscriptionWebhookEvents_Tenants_TenantEntityId] FOREIGN KEY ([TenantEntityId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102073533_AddTenantSubscriptionPaymentsPhase1A'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261102073533_AddTenantSubscriptionPaymentsPhase1A', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102115027_AddActivationIdempotencyKeyToSubscriptionHistory'
)
BEGIN
    ALTER TABLE [TenantSubscriptionStateHistories] ADD [ActivationIdempotencyKey] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102115027_AddActivationIdempotencyKeyToSubscriptionHistory'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TenantSubscriptionStateHistories_ActivationKey] ON [TenantSubscriptionStateHistories] ([ActivationIdempotencyKey]) WHERE [ActivationIdempotencyKey] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102115027_AddActivationIdempotencyKeyToSubscriptionHistory'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261102115027_AddActivationIdempotencyKeyToSubscriptionHistory', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104075223_AddTenantCountryCode'
)
BEGIN
    ALTER TABLE [Tenants] ADD [CountryCode] nvarchar(2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104075223_AddTenantCountryCode'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261104075223_AddTenantCountryCode', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104082059_AddTenantLocalizationFields'
)
BEGIN
    ALTER TABLE [Tenants] ADD [PhoneDialingCode] nvarchar(8) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104082059_AddTenantLocalizationFields'
)
BEGIN
    ALTER TABLE [Tenants] ADD [TimeZoneIana] nvarchar(64) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104082059_AddTenantLocalizationFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261104082059_AddTenantLocalizationFields', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108111451_AddSubscriptionActivationEmailSentAtUtcToPaymentTransactions'
)
BEGIN
    ALTER TABLE [TenantSubscriptionPaymentTransactions] ADD [SubscriptionActivationEmailSentAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108111451_AddSubscriptionActivationEmailSentAtUtcToPaymentTransactions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261108111451_AddSubscriptionActivationEmailSentAtUtcToPaymentTransactions', N'8.0.31');
END;
GO

COMMIT;
GO

