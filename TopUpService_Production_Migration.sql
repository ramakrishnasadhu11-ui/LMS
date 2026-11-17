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
    WHERE [MigrationId] = N'20260923104941_InitialSqlServer'
)
BEGIN
    CREATE TABLE [Customers] (
        [Id] int NOT NULL IDENTITY,
        [CustomerName] nvarchar(256) NOT NULL,
        [Address] nvarchar(512) NULL,
        [StoreCode] nvarchar(64) NULL,
        [TenantName] nvarchar(256) NULL,
        [CustCode] nvarchar(64) NULL,
        [CreatedDate] datetime2 NOT NULL,
        [ModifiedDate] datetime2 NOT NULL,
        CONSTRAINT [PK_Customers] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923104941_InitialSqlServer'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Customers_CustCode] ON [Customers] ([CustCode]) WHERE [CustCode] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923104941_InitialSqlServer'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Customers_CustomerName] ON [Customers] ([CustomerName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923104941_InitialSqlServer'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260923104941_InitialSqlServer', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923163000_MakeCustomerAddressRequired'
)
BEGIN
    UPDATE Customers SET Address = '' WHERE Address IS NULL
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923163000_MakeCustomerAddressRequired'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Customers]') AND [c].[name] = N'Address');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [Customers] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [Customers] ALTER COLUMN [Address] nvarchar(512) NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923163000_MakeCustomerAddressRequired'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260923163000_MakeCustomerAddressRequired', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923170000_AddCustomerOptionalFields'
)
BEGIN
    ALTER TABLE [Customers] ADD [BarCode] nvarchar(64) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923170000_AddCustomerOptionalFields'
)
BEGIN
    ALTER TABLE [Customers] ADD [Email] nvarchar(256) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923170000_AddCustomerOptionalFields'
)
BEGIN
    ALTER TABLE [Customers] ADD [MembershipId] nvarchar(64) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923170000_AddCustomerOptionalFields'
)
BEGIN
    ALTER TABLE [Customers] ADD [PhoneNumber] nvarchar(32) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260923170000_AddCustomerOptionalFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260923170000_AddCustomerOptionalFields', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924100000_AddCustomerPreferencesTable'
)
BEGIN
    CREATE TABLE [CustomerPreferences] (
        [Id] int NOT NULL IDENTITY,
        [TenantName] nvarchar(256) NOT NULL,
        [StoreCode] nvarchar(64) NOT NULL,
        [DefaultServiceType] nvarchar(64) NULL,
        [DefaultPaymentMode] nvarchar(64) NULL,
        [DefaultStarchLevel] nvarchar(32) NULL,
        [CreatedDate] datetime2 NOT NULL,
        [ModifiedDate] datetime2 NOT NULL,
        CONSTRAINT [PK_CustomerPreferences] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924100000_AddCustomerPreferencesTable'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_CustomerPreferences_TenantName_StoreCode] ON [CustomerPreferences] ([TenantName], [StoreCode]) WHERE [TenantName] IS NOT NULL AND [StoreCode] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924100000_AddCustomerPreferencesTable'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260924100000_AddCustomerPreferencesTable', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104120255_PhaseC1TaxConfiguration'
)
BEGIN
    ALTER TABLE [TaxInvoiceSettings] ADD [CountryCode] nvarchar(8) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104120255_PhaseC1TaxConfiguration'
)
BEGIN
    ALTER TABLE [TaxInvoiceSettings] ADD [CurrencyCode] nvarchar(8) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104120255_PhaseC1TaxConfiguration'
)
BEGIN
    ALTER TABLE [TaxInvoiceSettings] ADD [IsPricesInclusive] bit NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104120255_PhaseC1TaxConfiguration'
)
BEGIN
    ALTER TABLE [TaxInvoiceSettings] ADD [RowVersion] rowversion NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104120255_PhaseC1TaxConfiguration'
)
BEGIN
    ALTER TABLE [TaxInvoiceSettings] ADD [TaxMode] nvarchar(24) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104120255_PhaseC1TaxConfiguration'
)
BEGIN
    CREATE TABLE [TaxInvoiceComponents] (
        [Id] int NOT NULL IDENTITY,
        [TaxInvoiceSettingsId] int NOT NULL,
        [TenantName] nvarchar(256) NOT NULL,
        [StoreCode] nvarchar(64) NOT NULL,
        [Code] nvarchar(32) NOT NULL,
        [DisplayName] nvarchar(128) NOT NULL,
        [RatePercent] decimal(5,2) NOT NULL,
        [Jurisdiction] nvarchar(64) NULL,
        [Priority] int NOT NULL,
        [IsCompound] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [EffectiveFromUtc] datetime2 NULL,
        [EffectiveToUtc] datetime2 NULL,
        [CreatedDate] datetime2 NOT NULL,
        [ModifiedDate] datetime2 NOT NULL,
        [RowVersion] rowversion NULL,
        [JurisdictionNormalizedKey] AS COALESCE(NULLIF(LTRIM(RTRIM([Jurisdiction])),N''),N'__NULL__') PERSISTED,
        CONSTRAINT [PK_TaxInvoiceComponents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TaxInvoiceComponents_TaxInvoiceSettings_TaxInvoiceSettingsId] FOREIGN KEY ([TaxInvoiceSettingsId]) REFERENCES [TaxInvoiceSettings] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104120255_PhaseC1TaxConfiguration'
)
BEGIN
    CREATE INDEX [IX_TaxInvoiceComponents_TaxInvoiceSettingsId_Code_IsActive] ON [TaxInvoiceComponents] ([TaxInvoiceSettingsId], [Code], [IsActive]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104120255_PhaseC1TaxConfiguration'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_TaxInvoiceComponents_TenantName_StoreCode_Code_Priority_JurisdictionNormalizedKey] ON [TaxInvoiceComponents] ([TenantName], [StoreCode], [Code], [Priority], [JurisdictionNormalizedKey]) WHERE [IsActive] = 1');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104120255_PhaseC1TaxConfiguration'
)
BEGIN
    CREATE INDEX [IX_TaxInvoiceComponents_TenantName_StoreCode_IsActive] ON [TaxInvoiceComponents] ([TenantName], [StoreCode], [IsActive]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104120255_PhaseC1TaxConfiguration'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261104120255_PhaseC1TaxConfiguration', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105051342_AddLaundryOrderTaxLines'
)
BEGIN
    CREATE TABLE [LaundryOrderTaxLines] (
        [Id] int NOT NULL IDENTITY,
        [LaundryOrderId] int NOT NULL,
        [TenantName] nvarchar(256) NOT NULL,
        [StoreCode] nvarchar(64) NOT NULL,
        [OrderNo] nvarchar(64) NOT NULL,
        [LineNo] int NOT NULL,
        [TaxMode] nvarchar(24) NOT NULL,
        [CurrencyCode] nvarchar(8) NOT NULL,
        [ComponentCode] nvarchar(32) NOT NULL,
        [ComponentDisplayName] nvarchar(128) NOT NULL,
        [Jurisdiction] nvarchar(64) NULL,
        [Priority] int NOT NULL,
        [IsCompound] bit NOT NULL,
        [RatePercent] decimal(9,4) NOT NULL,
        [TaxableBaseAmount] decimal(18,4) NOT NULL,
        [TaxAmount] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_LaundryOrderTaxLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LaundryOrderTaxLines_LaundryOrders_LaundryOrderId] FOREIGN KEY ([LaundryOrderId]) REFERENCES [LaundryOrders] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105051342_AddLaundryOrderTaxLines'
)
BEGIN
    CREATE INDEX [IX_LaundryOrderTaxLines_LaundryOrderId_LineNo] ON [LaundryOrderTaxLines] ([LaundryOrderId], [LineNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105051342_AddLaundryOrderTaxLines'
)
BEGIN
    CREATE INDEX [IX_LaundryOrderTaxLines_TenantName_StoreCode_OrderNo_LineNo] ON [LaundryOrderTaxLines] ([TenantName], [StoreCode], [OrderNo], [LineNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105051342_AddLaundryOrderTaxLines'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261105051342_AddLaundryOrderTaxLines', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117054518_AddTopUpServiceDomain'
)
BEGIN
    CREATE TABLE [LaundryOrderTopUps] (
        [Id] int NOT NULL IDENTITY,
        [LaundryOrderId] int NOT NULL,
        [TenantName] nvarchar(256) NOT NULL,
        [StoreCode] nvarchar(64) NOT NULL,
        [OrderNo] nvarchar(64) NOT NULL,
        [LineNo] int NOT NULL,
        [TopUpName] nvarchar(128) NOT NULL,
        [ChargeType] nvarchar(32) NOT NULL,
        [ServiceType] nvarchar(64) NULL,
        [Category] nvarchar(64) NULL,
        [ItemName] nvarchar(128) NULL,
        [PieceNo] int NOT NULL,
        [AppliedQuantity] decimal(18,3) NOT NULL,
        [UnitPriceSnapshot] decimal(18,2) NOT NULL,
        [LineAmountSnapshot] decimal(18,2) NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        CONSTRAINT [PK_LaundryOrderTopUps] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LaundryOrderTopUps_LaundryOrders_LaundryOrderId] FOREIGN KEY ([LaundryOrderId]) REFERENCES [LaundryOrders] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117054518_AddTopUpServiceDomain'
)
BEGIN
    CREATE TABLE [TopUpServiceMaster] (
        [Id] int NOT NULL IDENTITY,
        [TenantName] nvarchar(256) NOT NULL,
        [StoreCode] nvarchar(64) NOT NULL,
        [TopUpName] nvarchar(128) NOT NULL,
        [ChargeType] nvarchar(32) NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        [Description] nvarchar(256) NULL,
        [IsActive] bit NOT NULL,
        [IsArchived] bit NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        [ModifiedDate] datetime2 NOT NULL,
        CONSTRAINT [PK_TopUpServiceMaster] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117054518_AddTopUpServiceDomain'
)
BEGIN
    CREATE TABLE [TopUpServiceApplicability] (
        [Id] int NOT NULL IDENTITY,
        [TopUpServiceMasterId] int NOT NULL,
        [TenantName] nvarchar(256) NOT NULL,
        [StoreCode] nvarchar(64) NOT NULL,
        [ServiceType] nvarchar(64) NULL,
        [Category] nvarchar(64) NULL,
        [ItemName] nvarchar(128) NULL,
        [IsActive] bit NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        [ModifiedDate] datetime2 NOT NULL,
        CONSTRAINT [PK_TopUpServiceApplicability] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TopUpServiceApplicability_TopUpServiceMaster_TopUpServiceMasterId] FOREIGN KEY ([TopUpServiceMasterId]) REFERENCES [TopUpServiceMaster] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117054518_AddTopUpServiceDomain'
)
BEGIN
    CREATE INDEX [IX_LaundryOrderTopUps_LaundryOrderId_LineNo] ON [LaundryOrderTopUps] ([LaundryOrderId], [LineNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117054518_AddTopUpServiceDomain'
)
BEGIN
    CREATE INDEX [IX_LaundryOrderTopUps_TenantName_StoreCode_OrderNo_LineNo] ON [LaundryOrderTopUps] ([TenantName], [StoreCode], [OrderNo], [LineNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117054518_AddTopUpServiceDomain'
)
BEGIN
    CREATE INDEX [IX_TopUpServiceApplicability_TenantName_StoreCode_IsActive] ON [TopUpServiceApplicability] ([TenantName], [StoreCode], [IsActive]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117054518_AddTopUpServiceDomain'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_TopUpServiceApplicability_TenantName_StoreCode_TopUpServiceMasterId_ServiceType_Category_ItemName] ON [TopUpServiceApplicability] ([TenantName], [StoreCode], [TopUpServiceMasterId], [ServiceType], [Category], [ItemName]) WHERE [ServiceType] IS NOT NULL AND [Category] IS NOT NULL AND [ItemName] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117054518_AddTopUpServiceDomain'
)
BEGIN
    CREATE INDEX [IX_TopUpServiceApplicability_TopUpServiceMasterId] ON [TopUpServiceApplicability] ([TopUpServiceMasterId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117054518_AddTopUpServiceDomain'
)
BEGIN
    CREATE INDEX [IX_TopUpServiceMaster_TenantName_StoreCode_IsActive_IsArchived] ON [TopUpServiceMaster] ([TenantName], [StoreCode], [IsActive], [IsArchived]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117054518_AddTopUpServiceDomain'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TopUpServiceMaster_TenantName_StoreCode_TopUpName] ON [TopUpServiceMaster] ([TenantName], [StoreCode], [TopUpName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117054518_AddTopUpServiceDomain'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261117054518_AddTopUpServiceDomain', N'8.0.31');
END;
GO

COMMIT;
GO

