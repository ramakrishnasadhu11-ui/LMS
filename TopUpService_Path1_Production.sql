SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]', N'U') IS NULL
    BEGIN
        THROW 51000, N'Precondition failed: [dbo].[__EFMigrationsHistory] does not exist.', 1;
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM [dbo].[__EFMigrationsHistory]
        WHERE [MigrationId] = N'20261105051342_AddLaundryOrderTaxLines'
    )
    BEGIN
        THROW 51003, N'Precondition failed: required previous migration 20261105051342_AddLaundryOrderTaxLines is not recorded in [dbo].[__EFMigrationsHistory].', 1;
    END;

    DECLARE @TopUpMigrationRecorded bit = 0;
    DECLARE @TopUpTableCount int = 0;

    SELECT
        @TopUpMigrationRecorded = CASE WHEN EXISTS (
            SELECT 1
            FROM [dbo].[__EFMigrationsHistory]
            WHERE [MigrationId] = N'20261117054518_AddTopUpServiceDomain'
        ) THEN 1 ELSE 0 END,
        @TopUpTableCount =
            CASE WHEN OBJECT_ID(N'[dbo].[TopUpServiceMaster]', N'U') IS NOT NULL THEN 1 ELSE 0 END +
            CASE WHEN OBJECT_ID(N'[dbo].[TopUpServiceApplicability]', N'U') IS NOT NULL THEN 1 ELSE 0 END +
            CASE WHEN OBJECT_ID(N'[dbo].[LaundryOrderTopUps]', N'U') IS NOT NULL THEN 1 ELSE 0 END;

    IF @TopUpMigrationRecorded = 1
    BEGIN
        IF @TopUpTableCount <> 3
        BEGIN
            THROW 51001, N'Precondition failed: migration is recorded but Top-up tables are incomplete.', 1;
        END;

        IF EXISTS (
            SELECT 1
            FROM (
                VALUES
                    (N'TopUpServiceMaster', N'Id', N'int', NULL, 10, 0, 0),
                    (N'TopUpServiceMaster', N'TenantName', N'nvarchar', 512, NULL, NULL, 0),
                    (N'TopUpServiceMaster', N'StoreCode', N'nvarchar', 128, NULL, NULL, 0),
                    (N'TopUpServiceMaster', N'TopUpName', N'nvarchar', 256, NULL, NULL, 0),
                    (N'TopUpServiceMaster', N'ChargeType', N'nvarchar', 64, NULL, NULL, 0),
                    (N'TopUpServiceMaster', N'UnitPrice', N'decimal', NULL, 18, 2, 0),
                    (N'TopUpServiceMaster', N'Description', N'nvarchar', 512, NULL, NULL, 1),
                    (N'TopUpServiceMaster', N'IsActive', N'bit', NULL, NULL, NULL, 0),
                    (N'TopUpServiceMaster', N'IsArchived', N'bit', NULL, NULL, NULL, 0),
                    (N'TopUpServiceMaster', N'CreatedDate', N'datetime2', NULL, NULL, NULL, 0),
                    (N'TopUpServiceMaster', N'ModifiedDate', N'datetime2', NULL, NULL, NULL, 0),

                    (N'TopUpServiceApplicability', N'Id', N'int', NULL, 10, 0, 0),
                    (N'TopUpServiceApplicability', N'TopUpServiceMasterId', N'int', NULL, 10, 0, 0),
                    (N'TopUpServiceApplicability', N'TenantName', N'nvarchar', 512, NULL, NULL, 0),
                    (N'TopUpServiceApplicability', N'StoreCode', N'nvarchar', 128, NULL, NULL, 0),
                    (N'TopUpServiceApplicability', N'ServiceType', N'nvarchar', 128, NULL, NULL, 1),
                    (N'TopUpServiceApplicability', N'Category', N'nvarchar', 128, NULL, NULL, 1),
                    (N'TopUpServiceApplicability', N'ItemName', N'nvarchar', 256, NULL, NULL, 1),
                    (N'TopUpServiceApplicability', N'IsActive', N'bit', NULL, NULL, NULL, 0),
                    (N'TopUpServiceApplicability', N'CreatedDate', N'datetime2', NULL, NULL, NULL, 0),
                    (N'TopUpServiceApplicability', N'ModifiedDate', N'datetime2', NULL, NULL, NULL, 0),

                    (N'LaundryOrderTopUps', N'Id', N'int', NULL, 10, 0, 0),
                    (N'LaundryOrderTopUps', N'LaundryOrderId', N'int', NULL, 10, 0, 0),
                    (N'LaundryOrderTopUps', N'TenantName', N'nvarchar', 512, NULL, NULL, 0),
                    (N'LaundryOrderTopUps', N'StoreCode', N'nvarchar', 128, NULL, NULL, 0),
                    (N'LaundryOrderTopUps', N'OrderNo', N'nvarchar', 128, NULL, NULL, 0),
                    (N'LaundryOrderTopUps', N'LineNo', N'int', NULL, 10, 0, 0),
                    (N'LaundryOrderTopUps', N'TopUpName', N'nvarchar', 256, NULL, NULL, 0),
                    (N'LaundryOrderTopUps', N'ChargeType', N'nvarchar', 64, NULL, NULL, 0),
                    (N'LaundryOrderTopUps', N'ServiceType', N'nvarchar', 128, NULL, NULL, 1),
                    (N'LaundryOrderTopUps', N'Category', N'nvarchar', 128, NULL, NULL, 1),
                    (N'LaundryOrderTopUps', N'ItemName', N'nvarchar', 256, NULL, NULL, 1),
                    (N'LaundryOrderTopUps', N'PieceNo', N'int', NULL, 10, 0, 0),
                    (N'LaundryOrderTopUps', N'AppliedQuantity', N'decimal', NULL, 18, 3, 0),
                    (N'LaundryOrderTopUps', N'UnitPriceSnapshot', N'decimal', NULL, 18, 2, 0),
                    (N'LaundryOrderTopUps', N'LineAmountSnapshot', N'decimal', NULL, 18, 2, 0),
                    (N'LaundryOrderTopUps', N'CreatedDate', N'datetime2', NULL, NULL, NULL, 0)
            ) expected (TableName, ColumnName, SqlType, MaxLength, [Precision], [Scale], IsNullable)
            LEFT JOIN sys.tables t
                ON t.name = expected.TableName
               AND SCHEMA_NAME(t.schema_id) = N'dbo'
            LEFT JOIN sys.columns c
                ON c.object_id = t.object_id
               AND c.name = expected.ColumnName
            LEFT JOIN sys.types ty
                ON ty.user_type_id = c.user_type_id
            WHERE c.column_id IS NULL
               OR ty.name <> expected.SqlType
               OR (expected.MaxLength IS NOT NULL AND c.max_length <> expected.MaxLength)
               OR (expected.[Precision] IS NOT NULL AND c.[precision] <> expected.[Precision])
               OR (expected.[Scale] IS NOT NULL AND c.[scale] <> expected.[Scale])
               OR c.is_nullable <> expected.IsNullable
        )
        BEGIN
            THROW 51010, N'Validation failed: already-applied Top-up schema has missing or incompatible columns.', 1;
        END;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.key_constraints kc
            JOIN sys.tables t ON t.object_id = kc.parent_object_id
            JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE kc.[type] = N'PK'
              AND kc.[name] = N'PK_TopUpServiceMaster'
              AND SCHEMA_NAME(t.schema_id) = N'dbo'
              AND t.[name] = N'TopUpServiceMaster'
            GROUP BY kc.[name], t.[name]
            HAVING STRING_AGG(c.[name], N',') WITHIN GROUP (ORDER BY ic.key_ordinal) = N'Id'
               AND COUNT(*) = 1
        ) OR NOT EXISTS (
            SELECT 1
            FROM sys.key_constraints kc
            JOIN sys.tables t ON t.object_id = kc.parent_object_id
            JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE kc.[type] = N'PK'
              AND kc.[name] = N'PK_TopUpServiceApplicability'
              AND SCHEMA_NAME(t.schema_id) = N'dbo'
              AND t.[name] = N'TopUpServiceApplicability'
            GROUP BY kc.[name], t.[name]
            HAVING STRING_AGG(c.[name], N',') WITHIN GROUP (ORDER BY ic.key_ordinal) = N'Id'
               AND COUNT(*) = 1
        ) OR NOT EXISTS (
            SELECT 1
            FROM sys.key_constraints kc
            JOIN sys.tables t ON t.object_id = kc.parent_object_id
            JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE kc.[type] = N'PK'
              AND kc.[name] = N'PK_LaundryOrderTopUps'
              AND SCHEMA_NAME(t.schema_id) = N'dbo'
              AND t.[name] = N'LaundryOrderTopUps'
            GROUP BY kc.[name], t.[name]
            HAVING STRING_AGG(c.[name], N',') WITHIN GROUP (ORDER BY ic.key_ordinal) = N'Id'
               AND COUNT(*) = 1
        )
        BEGIN
            THROW 51011, N'Validation failed: already-applied Top-up schema has missing or incompatible primary keys.', 1;
        END;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.tables pt ON pt.object_id = fk.parent_object_id
            JOIN sys.columns pc ON pc.object_id = pt.object_id AND pc.column_id = fkc.parent_column_id
            JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
            JOIN sys.columns rc ON rc.object_id = rt.object_id AND rc.column_id = fkc.referenced_column_id
            WHERE fk.[name] = N'FK_LaundryOrderTopUps_LaundryOrders_LaundryOrderId'
              AND fk.is_disabled = 0
              AND fk.is_not_trusted = 0
              AND SCHEMA_NAME(pt.schema_id) = N'dbo' AND pt.[name] = N'LaundryOrderTopUps' AND pc.[name] = N'LaundryOrderId'
              AND SCHEMA_NAME(rt.schema_id) = N'dbo' AND rt.[name] = N'LaundryOrders' AND rc.[name] = N'Id'
            GROUP BY fk.object_id
            HAVING COUNT(*) = 1
        ) OR NOT EXISTS (
            SELECT 1
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.tables pt ON pt.object_id = fk.parent_object_id
            JOIN sys.columns pc ON pc.object_id = pt.object_id AND pc.column_id = fkc.parent_column_id
            JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
            JOIN sys.columns rc ON rc.object_id = rt.object_id AND rc.column_id = fkc.referenced_column_id
            WHERE fk.[name] = N'FK_TopUpServiceApplicability_TopUpServiceMaster_TopUpServiceMasterId'
              AND fk.is_disabled = 0
              AND fk.is_not_trusted = 0
              AND SCHEMA_NAME(pt.schema_id) = N'dbo' AND pt.[name] = N'TopUpServiceApplicability' AND pc.[name] = N'TopUpServiceMasterId'
              AND SCHEMA_NAME(rt.schema_id) = N'dbo' AND rt.[name] = N'TopUpServiceMaster' AND rc.[name] = N'Id'
            GROUP BY fk.object_id
            HAVING COUNT(*) = 1
        )
        BEGIN
            THROW 51012, N'Validation failed: already-applied Top-up schema has missing or incompatible foreign keys.', 1;
        END;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE SCHEMA_NAME(t.schema_id) = N'dbo'
              AND t.[name] = N'LaundryOrderTopUps'
              AND i.[name] = N'IX_LaundryOrderTopUps_LaundryOrderId_LineNo'
              AND i.is_unique = 0
              AND i.has_filter = 0
            GROUP BY i.index_id
            HAVING STRING_AGG(c.[name], N',') WITHIN GROUP (ORDER BY ic.key_ordinal) = N'LaundryOrderId,LineNo'
        ) OR NOT EXISTS (
            SELECT 1
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE SCHEMA_NAME(t.schema_id) = N'dbo'
              AND t.[name] = N'LaundryOrderTopUps'
              AND i.[name] = N'IX_LaundryOrderTopUps_TenantName_StoreCode_OrderNo_LineNo'
              AND i.is_unique = 0
              AND i.has_filter = 0
            GROUP BY i.index_id
            HAVING STRING_AGG(c.[name], N',') WITHIN GROUP (ORDER BY ic.key_ordinal) = N'TenantName,StoreCode,OrderNo,LineNo'
        ) OR NOT EXISTS (
            SELECT 1
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE SCHEMA_NAME(t.schema_id) = N'dbo'
              AND t.[name] = N'TopUpServiceApplicability'
              AND i.[name] = N'IX_TopUpServiceApplicability_TenantName_StoreCode_IsActive'
              AND i.is_unique = 0
              AND i.has_filter = 0
            GROUP BY i.index_id
            HAVING STRING_AGG(c.[name], N',') WITHIN GROUP (ORDER BY ic.key_ordinal) = N'TenantName,StoreCode,IsActive'
        ) OR NOT EXISTS (
            SELECT 1
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE SCHEMA_NAME(t.schema_id) = N'dbo'
              AND t.[name] = N'TopUpServiceApplicability'
              AND i.[name] = N'IX_TopUpServiceApplicability_TopUpServiceMasterId'
              AND i.is_unique = 0
              AND i.has_filter = 0
            GROUP BY i.index_id
            HAVING STRING_AGG(c.[name], N',') WITHIN GROUP (ORDER BY ic.key_ordinal) = N'TopUpServiceMasterId'
        ) OR NOT EXISTS (
            SELECT 1
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE SCHEMA_NAME(t.schema_id) = N'dbo'
              AND t.[name] = N'TopUpServiceMaster'
              AND i.[name] = N'IX_TopUpServiceMaster_TenantName_StoreCode_IsActive_IsArchived'
              AND i.is_unique = 0
              AND i.has_filter = 0
            GROUP BY i.index_id
            HAVING STRING_AGG(c.[name], N',') WITHIN GROUP (ORDER BY ic.key_ordinal) = N'TenantName,StoreCode,IsActive,IsArchived'
        ) OR NOT EXISTS (
            SELECT 1
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE SCHEMA_NAME(t.schema_id) = N'dbo'
              AND t.[name] = N'TopUpServiceMaster'
              AND i.[name] = N'IX_TopUpServiceMaster_TenantName_StoreCode_TopUpName'
              AND i.is_unique = 1
              AND i.has_filter = 0
            GROUP BY i.index_id
            HAVING STRING_AGG(c.[name], N',') WITHIN GROUP (ORDER BY ic.key_ordinal) = N'TenantName,StoreCode,TopUpName'
        ) OR NOT EXISTS (
            SELECT 1
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE SCHEMA_NAME(t.schema_id) = N'dbo'
              AND t.[name] = N'TopUpServiceApplicability'
              AND i.[name] = N'IX_TopUpServiceApplicability_TenantName_StoreCode_TopUpServiceMasterId_ServiceType_Category_ItemName'
              AND i.is_unique = 1
              AND i.has_filter = 1
              AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(i.filter_definition,N''), N'[', N''), N']', N''), N'(', N''), N')', N''), N' ', N''), CHAR(9), N''), CHAR(10), N''), CHAR(13), N'')) = N'SERVICETYPEISNOTNULLANDCATEGORYISNOTNULLANDITEMNAMEISNOTNULL'
            GROUP BY i.index_id
            HAVING STRING_AGG(c.[name], N',') WITHIN GROUP (ORDER BY ic.key_ordinal) = N'TenantName,StoreCode,TopUpServiceMasterId,ServiceType,Category,ItemName'
        )
        BEGIN
            THROW 51013, N'Validation failed: already-applied Top-up schema has missing or incompatible indexes.', 1;
        END;
    END
    ELSE
    BEGIN
        IF @TopUpTableCount <> 0
        BEGIN
            THROW 51002, N'Precondition failed: unexpected existing Top-up tables detected. Path 1 requires all three tables to be absent.', 1;
        END;

        IF OBJECT_ID(N'[dbo].[LaundryOrders]', N'U') IS NULL
        BEGIN
            THROW 51004, N'Precondition failed: required parent table [dbo].[LaundryOrders] does not exist.', 1;
        END;

        CREATE TABLE [dbo].[LaundryOrderTopUps] (
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
            CONSTRAINT [FK_LaundryOrderTopUps_LaundryOrders_LaundryOrderId] FOREIGN KEY ([LaundryOrderId]) REFERENCES [dbo].[LaundryOrders] ([Id])
        );

        CREATE TABLE [dbo].[TopUpServiceMaster] (
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

        CREATE TABLE [dbo].[TopUpServiceApplicability] (
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
            CONSTRAINT [FK_TopUpServiceApplicability_TopUpServiceMaster_TopUpServiceMasterId] FOREIGN KEY ([TopUpServiceMasterId]) REFERENCES [dbo].[TopUpServiceMaster] ([Id])
        );

        CREATE INDEX [IX_LaundryOrderTopUps_LaundryOrderId_LineNo] ON [dbo].[LaundryOrderTopUps] ([LaundryOrderId], [LineNo]);
        CREATE INDEX [IX_LaundryOrderTopUps_TenantName_StoreCode_OrderNo_LineNo] ON [dbo].[LaundryOrderTopUps] ([TenantName], [StoreCode], [OrderNo], [LineNo]);
        CREATE INDEX [IX_TopUpServiceApplicability_TenantName_StoreCode_IsActive] ON [dbo].[TopUpServiceApplicability] ([TenantName], [StoreCode], [IsActive]);
        EXEC(N'CREATE UNIQUE INDEX [IX_TopUpServiceApplicability_TenantName_StoreCode_TopUpServiceMasterId_ServiceType_Category_ItemName] ON [dbo].[TopUpServiceApplicability] ([TenantName], [StoreCode], [TopUpServiceMasterId], [ServiceType], [Category], [ItemName]) WHERE [ServiceType] IS NOT NULL AND [Category] IS NOT NULL AND [ItemName] IS NOT NULL');
        CREATE INDEX [IX_TopUpServiceApplicability_TopUpServiceMasterId] ON [dbo].[TopUpServiceApplicability] ([TopUpServiceMasterId]);
        CREATE INDEX [IX_TopUpServiceMaster_TenantName_StoreCode_IsActive_IsArchived] ON [dbo].[TopUpServiceMaster] ([TenantName], [StoreCode], [IsActive], [IsArchived]);
        CREATE UNIQUE INDEX [IX_TopUpServiceMaster_TenantName_StoreCode_TopUpName] ON [dbo].[TopUpServiceMaster] ([TenantName], [StoreCode], [TopUpName]);

        INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
        VALUES (N'20261117054518_AddTopUpServiceDomain', N'8.0.31');
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
    BEGIN
        ROLLBACK TRANSACTION;
    END;

    THROW;
END CATCH;

