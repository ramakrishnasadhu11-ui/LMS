SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
	IF DB_NAME() <> N'db_acff11_lmsdb'
	BEGIN
		THROW 52000, 'Database guard failed. Expected db_acff11_lmsdb.', 1;
	END;

	IF OBJECT_ID(N'[dbo].[LaundryOrders]', N'U') IS NULL
	BEGIN
		THROW 52001, 'Required table [dbo].[LaundryOrders] was not found.', 1;
	END;

	IF COL_LENGTH(N'[dbo].[LaundryOrders]', N'Id') IS NULL
	BEGIN
		THROW 52002, 'Required column [dbo].[LaundryOrders].[Id] was not found.', 1;
	END;

	IF EXISTS (
		SELECT 1
		FROM sys.columns c
		INNER JOIN sys.tables t ON t.object_id = c.object_id
		INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
		INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
		WHERE s.name = N'dbo'
		  AND t.name = N'LaundryOrders'
		  AND c.name = N'Id'
		  AND (
				ty.name <> N'int'
				OR c.is_nullable <> 0
				OR c.is_identity <> 1
			  )
	)
	BEGIN
		THROW 52003, 'Column [dbo].[LaundryOrders].[Id] is not int NOT NULL IDENTITY as expected.', 1;
	END;

	IF EXISTS (
		SELECT 1
		FROM [dbo].[LaundryOrders]
		WHERE [Id] IS NULL
	)
	BEGIN
		THROW 52004, 'Column [dbo].[LaundryOrders].[Id] contains NULL values.', 1;
	END;

	IF EXISTS (
		SELECT [Id]
		FROM [dbo].[LaundryOrders]
		GROUP BY [Id]
		HAVING COUNT_BIG(1) > 1
	)
	BEGIN
		THROW 52005, 'Column [dbo].[LaundryOrders].[Id] contains duplicate values.', 1;
	END;

	IF EXISTS (
		SELECT 1
		FROM sys.key_constraints kc
		INNER JOIN sys.tables t ON t.object_id = kc.parent_object_id
		INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
		WHERE s.name = N'dbo'
		  AND t.name = N'LaundryOrders'
		  AND kc.[type] = N'PK'
		  AND kc.name = N'PK_LaundryOrders'
	)
	BEGIN
		IF NOT EXISTS (
			SELECT 1
			FROM sys.key_constraints kc
			INNER JOIN sys.tables t ON t.object_id = kc.parent_object_id
			INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
			INNER JOIN sys.indexes i ON i.object_id = kc.parent_object_id AND i.index_id = kc.unique_index_id
			INNER JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
			INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
			WHERE s.name = N'dbo'
			  AND t.name = N'LaundryOrders'
			  AND kc.[type] = N'PK'
			  AND kc.name = N'PK_LaundryOrders'
			  AND i.[type] = 1
			  AND ic.key_ordinal = 1
			  AND ic.is_descending_key = 0
			  AND c.name = N'Id'
			  AND NOT EXISTS (
					SELECT 1
					FROM sys.index_columns ic2
					WHERE ic2.object_id = i.object_id
					  AND ic2.index_id = i.index_id
					  AND ic2.key_ordinal > 1
			  )
		)
		BEGIN
			THROW 52006, 'Primary key PK_LaundryOrders exists but does not match expected definition PRIMARY KEY CLUSTERED (Id ASC).', 1;
		END;

		SELECT N'No change: PK_LaundryOrders already exists with expected definition.' AS [Result];
		RETURN;
	END;

	IF EXISTS (
		SELECT 1
		FROM sys.key_constraints kc
		INNER JOIN sys.tables t ON t.object_id = kc.parent_object_id
		INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
		WHERE s.name = N'dbo'
		  AND t.name = N'LaundryOrders'
		  AND kc.[type] = N'PK'
		  AND kc.name <> N'PK_LaundryOrders'
	)
	BEGIN
		THROW 52007, 'Unexpected primary key exists on [dbo].[LaundryOrders]. Manual review required.', 1;
	END;

	IF EXISTS (
		SELECT 1
		FROM sys.indexes i
		INNER JOIN sys.tables t ON t.object_id = i.object_id
		INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
		WHERE s.name = N'dbo'
		  AND t.name = N'LaundryOrders'
		  AND i.[type] = 1
		  AND i.is_primary_key = 0
		  AND i.name <> N'PK_LaundryOrders'
	)
	BEGIN
		THROW 52008, 'Conflicting clustered index exists on [dbo].[LaundryOrders]. Manual review required.', 1;
	END;

	BEGIN TRANSACTION;

	ALTER TABLE [dbo].[LaundryOrders]
		ADD CONSTRAINT [PK_LaundryOrders]
			PRIMARY KEY CLUSTERED ([Id] ASC);

	COMMIT TRANSACTION;
END TRY
BEGIN CATCH
	IF XACT_STATE() <> 0
	BEGIN
		ROLLBACK TRANSACTION;
	END;

	THROW;
END CATCH;
