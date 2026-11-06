SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    IF DB_NAME() <> N'db_acff11_lmsdb'
    BEGIN
        THROW 51000, 'Database guard failed. Expected db_acff11_lmsdb.', 1;
    END;

    IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]', N'U') IS NULL
    BEGIN
        THROW 51001, '__EFMigrationsHistory table not found. Unexpected schema state.', 1;
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM [dbo].[__EFMigrationsHistory]
        WHERE [MigrationId] = N'20261104120255_PhaseC1TaxConfiguration')
    BEGIN
        THROW 51002, 'Required baseline migration 20261104120255_PhaseC1TaxConfiguration is not applied.', 1;
    END;

    IF EXISTS (
        SELECT 1
        FROM [dbo].[__EFMigrationsHistory]
        WHERE [MigrationId] = N'20261105051342_AddLaundryOrderTaxLines')
    BEGIN
        THROW 51003, 'Target migration 20261105051342_AddLaundryOrderTaxLines is already applied.', 1;
    END;

    IF OBJECT_ID(N'[dbo].[LaundryOrders]', N'U') IS NULL
    BEGIN
        THROW 51004, 'Required table [dbo].[LaundryOrders] not found. Unexpected schema state.', 1;
    END;

    IF COL_LENGTH(N'[dbo].[LaundryOrders]', N'Id') IS NULL
    BEGIN
        THROW 51005, 'Required column [dbo].[LaundryOrders].[Id] not found. Unexpected schema state.', 1;
    END;

    IF OBJECT_ID(N'[dbo].[LaundryOrderTaxLines]', N'U') IS NOT NULL
    BEGIN
        THROW 51006, 'Table [dbo].[LaundryOrderTaxLines] already exists. Unexpected schema state.', 1;
    END;

    BEGIN TRANSACTION;

    CREATE TABLE [dbo].[LaundryOrderTaxLines] (
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
        CONSTRAINT [FK_LaundryOrderTaxLines_LaundryOrders_LaundryOrderId]
            FOREIGN KEY ([LaundryOrderId]) REFERENCES [dbo].[LaundryOrders] ([Id])
    );

    CREATE INDEX [IX_LaundryOrderTaxLines_LaundryOrderId_LineNo]
        ON [dbo].[LaundryOrderTaxLines] ([LaundryOrderId], [LineNo]);

    CREATE INDEX [IX_LaundryOrderTaxLines_TenantName_StoreCode_OrderNo_LineNo]
        ON [dbo].[LaundryOrderTaxLines] ([TenantName], [StoreCode], [OrderNo], [LineNo]);

    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261105051342_AddLaundryOrderTaxLines', N'8.0.31');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
    BEGIN
        ROLLBACK TRANSACTION;
    END;

    DECLARE @ErrorMessage nvarchar(4000) = ERROR_MESSAGE();
    DECLARE @ErrorNumber int = ERROR_NUMBER();
    DECLARE @ErrorState int = ERROR_STATE();

    THROW;
END CATCH;

