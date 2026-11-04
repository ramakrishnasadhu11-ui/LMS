SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
	BEGIN TRANSACTION;

	IF DB_NAME() <> N'db_acff11_lmsdb'
	BEGIN
		THROW 52000, 'Safety check failed: this recovery script is only for database db_acff11_lmsdb.', 1;
	END;

	IF OBJECT_ID(N'[dbo].[TaxInvoiceSettings]', N'U') IS NULL
	BEGIN
		THROW 52001, 'TaxInvoiceSettings table is missing. Recovery script expects existing GST settings table and will not create it.', 1;
	END;

	IF COL_LENGTH(N'[dbo].[TaxInvoiceSettings]', N'Id') IS NULL
	BEGIN
		THROW 52002, 'TaxInvoiceSettings.Id column is missing.', 1;
	END;

	IF EXISTS (
		SELECT 1
		FROM sys.columns c
		INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
		WHERE c.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceSettings]')
		  AND c.name = N'Id'
		  AND (
			t.name <> N'int'
			OR c.is_nullable <> 0
			OR c.is_identity <> 1
		  ))
	BEGIN
		THROW 52024, 'TaxInvoiceSettings.Id must be int IDENTITY NOT NULL.', 1;
	END;

	IF EXISTS (SELECT 1 FROM [dbo].[TaxInvoiceSettings] WHERE [Id] IS NULL)
	BEGIN
		THROW 52003, 'TaxInvoiceSettings contains NULL Id values. Cannot create/validate primary key safely.', 1;
	END;

	IF EXISTS (
		SELECT [Id]
		FROM [dbo].[TaxInvoiceSettings]
		GROUP BY [Id]
		HAVING COUNT(1) > 1)
	BEGIN
		THROW 52004, 'TaxInvoiceSettings contains duplicate Id values. Cannot create/validate primary key safely.', 1;
	END;

	DECLARE @TaxInvoiceSettingsPkName sysname;
	SELECT @TaxInvoiceSettingsPkName = kc.[name]
	FROM sys.key_constraints kc
	WHERE kc.[parent_object_id] = OBJECT_ID(N'[dbo].[TaxInvoiceSettings]')
	  AND kc.[type] = 'PK';

	IF @TaxInvoiceSettingsPkName IS NULL
	BEGIN
		ALTER TABLE [dbo].[TaxInvoiceSettings]
		ADD CONSTRAINT [PK_TaxInvoiceSettings] PRIMARY KEY CLUSTERED ([Id] ASC);
	END
	ELSE
	BEGIN
		IF EXISTS (
			SELECT 1
			FROM sys.key_constraints kc
			INNER JOIN sys.indexes i
				ON i.object_id = kc.parent_object_id
			   AND i.index_id = kc.unique_index_id
			INNER JOIN sys.index_columns ic
				ON ic.[object_id] = kc.[parent_object_id]
			   AND ic.[index_id] = kc.[unique_index_id]
			INNER JOIN sys.columns c
				ON c.[object_id] = ic.[object_id]
			   AND c.[column_id] = ic.[column_id]
			WHERE kc.[parent_object_id] = OBJECT_ID(N'[dbo].[TaxInvoiceSettings]')
			  AND kc.[type] = 'PK'
			  AND (
					i.[type_desc] <> N'CLUSTERED'
					OR c.[name] <> N'Id'
					OR ic.[key_ordinal] <> 1
					OR ic.[is_descending_key] <> 0
					OR EXISTS (
						SELECT 1
						FROM sys.index_columns ic2
						WHERE ic2.[object_id] = kc.[parent_object_id]
						  AND ic2.[index_id] = kc.[unique_index_id]
						  AND ic2.[key_ordinal] > 1)
				  ))
		BEGIN
			THROW 52005, 'Existing TaxInvoiceSettings primary key is not exactly on [Id]. Manual remediation is required.', 1;
		END;
	END;

	IF COL_LENGTH(N'[dbo].[TaxInvoiceSettings]', N'CountryCode') IS NULL
	BEGIN
		ALTER TABLE [dbo].[TaxInvoiceSettings] ADD [CountryCode] [nvarchar](8) NULL;
	END
	ELSE IF EXISTS (
		SELECT 1
		FROM sys.columns c
		INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
		WHERE c.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceSettings]')
		  AND c.name = N'CountryCode'
		  AND (t.name <> N'nvarchar' OR c.max_length <> 16 OR c.is_nullable <> 1))
	BEGIN
		THROW 52006, 'CountryCode exists with incompatible definition. Expected nvarchar(8) NULL.', 1;
	END;

	IF COL_LENGTH(N'[dbo].[TaxInvoiceSettings]', N'CurrencyCode') IS NULL
	BEGIN
		ALTER TABLE [dbo].[TaxInvoiceSettings] ADD [CurrencyCode] [nvarchar](8) NULL;
	END
	ELSE IF EXISTS (
		SELECT 1
		FROM sys.columns c
		INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
		WHERE c.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceSettings]')
		  AND c.name = N'CurrencyCode'
		  AND (t.name <> N'nvarchar' OR c.max_length <> 16 OR c.is_nullable <> 1))
	BEGIN
		THROW 52007, 'CurrencyCode exists with incompatible definition. Expected nvarchar(8) NULL.', 1;
	END;

	IF COL_LENGTH(N'[dbo].[TaxInvoiceSettings]', N'IsPricesInclusive') IS NULL
	BEGIN
		ALTER TABLE [dbo].[TaxInvoiceSettings] ADD [IsPricesInclusive] [bit] NULL;
	END
	ELSE IF EXISTS (
		SELECT 1
		FROM sys.columns c
		INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
		WHERE c.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceSettings]')
		  AND c.name = N'IsPricesInclusive'
		  AND (t.name <> N'bit' OR c.is_nullable <> 1))
	BEGIN
		THROW 52008, 'IsPricesInclusive exists with incompatible definition. Expected bit NULL.', 1;
	END;

	IF COL_LENGTH(N'[dbo].[TaxInvoiceSettings]', N'RowVersion') IS NULL
	BEGIN
		ALTER TABLE [dbo].[TaxInvoiceSettings] ADD [RowVersion] [rowversion] NULL;
	END
	ELSE IF EXISTS (
		SELECT 1
		FROM sys.columns c
		WHERE c.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceSettings]')
		  AND c.name = N'RowVersion'
		  AND (c.system_type_id <> 189 OR c.max_length <> 8 OR c.is_nullable <> 1))
	BEGIN
		THROW 52009, 'RowVersion exists with incompatible definition. Expected rowversion(8) NULL.', 1;
	END;

	IF COL_LENGTH(N'[dbo].[TaxInvoiceSettings]', N'TaxMode') IS NULL
	BEGIN
		ALTER TABLE [dbo].[TaxInvoiceSettings] ADD [TaxMode] [nvarchar](24) NULL;
	END
	ELSE IF EXISTS (
		SELECT 1
		FROM sys.columns c
		INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
		WHERE c.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceSettings]')
		  AND c.name = N'TaxMode'
		  AND (t.name <> N'nvarchar' OR c.max_length <> 48 OR c.is_nullable <> 1))
	BEGIN
		THROW 52010, 'TaxMode exists with incompatible definition. Expected nvarchar(24) NULL.', 1;
	END;

	IF OBJECT_ID(N'[dbo].[TaxInvoiceComponents]', N'U') IS NULL
	BEGIN
		CREATE TABLE [dbo].[TaxInvoiceComponents](
			[Id] [int] IDENTITY(1,1) NOT NULL,
			[TaxInvoiceSettingsId] [int] NOT NULL,
			[TenantName] [nvarchar](256) NOT NULL,
			[StoreCode] [nvarchar](64) NOT NULL,
			[Code] [nvarchar](32) NOT NULL,
			[DisplayName] [nvarchar](128) NOT NULL,
			[RatePercent] [decimal](5,2) NOT NULL,
			[Jurisdiction] [nvarchar](64) NULL,
			[Priority] [int] NOT NULL,
			[IsCompound] [bit] NOT NULL,
			[IsActive] [bit] NOT NULL,
			[EffectiveFromUtc] [datetime2](7) NULL,
			[EffectiveToUtc] [datetime2](7) NULL,
			[CreatedDate] [datetime2](7) NOT NULL,
			[ModifiedDate] [datetime2](7) NOT NULL,
			[RowVersion] [rowversion] NULL,
			[JurisdictionNormalizedKey] AS COALESCE(NULLIF(LTRIM(RTRIM([Jurisdiction])),N''),N'__NULL__') PERSISTED,
			CONSTRAINT [PK_TaxInvoiceComponents] PRIMARY KEY CLUSTERED ([Id] ASC),
			CONSTRAINT [FK_TaxInvoiceComponents_TaxInvoiceSettings_TaxInvoiceSettingsId]
				FOREIGN KEY ([TaxInvoiceSettingsId])
				REFERENCES [dbo].[TaxInvoiceSettings]([Id])
				ON DELETE NO ACTION
		);
	END
	ELSE
	BEGIN
		IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'Id') IS NULL
		BEGIN
			THROW 52030, 'TaxInvoiceComponents.Id column is missing.', 1;
		END;

		IF EXISTS (
			SELECT 1
			FROM sys.columns c
			INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
			WHERE c.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
			  AND c.name = N'Id'
			  AND (
				t.name <> N'int'
				OR c.is_nullable <> 0
				OR c.is_identity <> 1
			  ))
		BEGIN
			THROW 52031, 'TaxInvoiceComponents.Id must be int IDENTITY NOT NULL.', 1;
		END;

		DECLARE @TaxInvoiceComponentsPkName sysname;
		SELECT @TaxInvoiceComponentsPkName = kc.[name]
		FROM sys.key_constraints kc
		WHERE kc.[parent_object_id] = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
		  AND kc.[type] = 'PK';

		IF @TaxInvoiceComponentsPkName IS NULL
		BEGIN
			IF EXISTS (SELECT 1 FROM [dbo].[TaxInvoiceComponents] WHERE [Id] IS NULL)
			BEGIN
				THROW 52065, 'TaxInvoiceComponents contains NULL Id values. Cannot create primary key safely.', 1;
			END;

			IF EXISTS (
				SELECT [Id]
				FROM [dbo].[TaxInvoiceComponents]
				GROUP BY [Id]
				HAVING COUNT(1) > 1)
			BEGIN
				THROW 52066, 'TaxInvoiceComponents contains duplicate Id values. Cannot create primary key safely.', 1;
			END;

			ALTER TABLE [dbo].[TaxInvoiceComponents]
			ADD CONSTRAINT [PK_TaxInvoiceComponents] PRIMARY KEY CLUSTERED ([Id] ASC);
		END
		ELSE IF EXISTS (
			SELECT 1
			FROM sys.key_constraints kc
			INNER JOIN sys.indexes i
				ON i.object_id = kc.parent_object_id
			   AND i.index_id = kc.unique_index_id
			INNER JOIN sys.index_columns ic
				ON ic.[object_id] = kc.[parent_object_id]
			   AND ic.[index_id] = kc.[unique_index_id]
			INNER JOIN sys.columns c
				ON c.[object_id] = ic.[object_id]
			   AND c.[column_id] = ic.[column_id]
			WHERE kc.[parent_object_id] = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
			  AND kc.[type] = 'PK'
			  AND (
				i.[type_desc] <> N'CLUSTERED'
				OR c.[name] <> N'Id'
				OR ic.[key_ordinal] <> 1
				OR ic.[is_descending_key] <> 0
				OR EXISTS (
					SELECT 1
					FROM sys.index_columns ic2
					WHERE ic2.[object_id] = kc.[parent_object_id]
					  AND ic2.[index_id] = kc.[unique_index_id]
					  AND ic2.[key_ordinal] > 1)
			  ))
		BEGIN
			THROW 52067, 'Existing TaxInvoiceComponents primary key is not exactly clustered on [Id] ASC.', 1;
		END;

		IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'TaxInvoiceSettingsId') IS NULL
		BEGIN
			THROW 52032, 'TaxInvoiceComponents.TaxInvoiceSettingsId column is missing.', 1;
		END;

		IF EXISTS (SELECT 1 FROM [dbo].[TaxInvoiceComponents] WHERE [TaxInvoiceSettingsId] IS NULL)
		BEGIN
			THROW 52011, 'TaxInvoiceComponents contains NULL TaxInvoiceSettingsId values. Existing schema/data is incompatible with Phase C1.', 1;
		END;

		IF EXISTS (
			SELECT 1
			FROM [dbo].[TaxInvoiceComponents] c
			LEFT JOIN [dbo].[TaxInvoiceSettings] s ON s.[Id] = c.[TaxInvoiceSettingsId]
			WHERE s.[Id] IS NULL)
		BEGIN
			THROW 52012, 'TaxInvoiceComponents contains orphan TaxInvoiceSettingsId values. Existing schema/data is incompatible with Phase C1.', 1;
		END;

		IF EXISTS (
			SELECT 1
			FROM sys.columns c
			INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
			WHERE c.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
			  AND c.name = N'TaxInvoiceSettingsId'
			  AND (t.name <> N'int' OR c.is_nullable <> 0))
		BEGIN
			THROW 52013, 'TaxInvoiceComponents.TaxInvoiceSettingsId has incompatible definition. Expected int NOT NULL.', 1;
		END;

		IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'TenantName') IS NULL
		BEGIN
			THROW 52033, 'TaxInvoiceComponents.TenantName column is missing.', 1;
		END;

		IF EXISTS (
			SELECT 1
			FROM sys.columns c
			INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
			WHERE c.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
			  AND c.name = N'TenantName'
			  AND (t.name <> N'nvarchar' OR c.max_length <> 512 OR c.is_nullable <> 0))
		BEGIN
			THROW 52014, 'TaxInvoiceComponents.TenantName has incompatible definition. Expected nvarchar(256) NOT NULL.', 1;
		END;

		IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'StoreCode') IS NULL
		BEGIN
			THROW 52034, 'TaxInvoiceComponents.StoreCode column is missing.', 1;
		END;

		IF EXISTS (
			SELECT 1
			FROM sys.columns c
			INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
			WHERE c.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
			  AND c.name = N'StoreCode'
			  AND (t.name <> N'nvarchar' OR c.max_length <> 128 OR c.is_nullable <> 0))
		BEGIN
			THROW 52015, 'TaxInvoiceComponents.StoreCode has incompatible definition. Expected nvarchar(64) NOT NULL.', 1;
		END;

		IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'Code') IS NULL
		BEGIN
			THROW 52035, 'TaxInvoiceComponents.Code column is missing.', 1;
		END;

		IF EXISTS (
			SELECT 1
			FROM sys.columns c
			INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
			WHERE c.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
			  AND c.name = N'Code'
			  AND (t.name <> N'nvarchar' OR c.max_length <> 64 OR c.is_nullable <> 0))
		BEGIN
			THROW 52016, 'TaxInvoiceComponents.Code has incompatible definition. Expected nvarchar(32) NOT NULL.', 1;
		END;

		IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'DisplayName') IS NULL
		BEGIN
			THROW 52036, 'TaxInvoiceComponents.DisplayName column is missing.', 1;
		END;

		IF EXISTS (
			SELECT 1
			FROM sys.columns c
			INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
			WHERE c.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
			  AND c.name = N'DisplayName'
			  AND (t.name <> N'nvarchar' OR c.max_length <> 256 OR c.is_nullable <> 0))
		BEGIN
			THROW 52017, 'TaxInvoiceComponents.DisplayName has incompatible definition. Expected nvarchar(128) NOT NULL.', 1;
		END;

		IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'RatePercent') IS NULL
		BEGIN
			THROW 52037, 'TaxInvoiceComponents.RatePercent column is missing.', 1;
		END;

		IF EXISTS (
			SELECT 1
			FROM sys.columns c
			INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
			WHERE c.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
			  AND c.name = N'RatePercent'
			  AND (t.name <> N'decimal' OR c.precision <> 5 OR c.scale <> 2 OR c.is_nullable <> 0))
		BEGIN
			THROW 52038, 'TaxInvoiceComponents.RatePercent has incompatible definition. Expected decimal(5,2) NOT NULL.', 1;
		END;

		IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'Jurisdiction') IS NULL
		BEGIN
			THROW 52039, 'TaxInvoiceComponents.Jurisdiction column is missing.', 1;
		END;

		IF EXISTS (
			SELECT 1
			FROM sys.columns c
			INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
			WHERE c.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
			  AND c.name = N'Jurisdiction'
			  AND (t.name <> N'nvarchar' OR c.max_length <> 128 OR c.is_nullable <> 1))
		BEGIN
			THROW 52040, 'TaxInvoiceComponents.Jurisdiction has incompatible definition. Expected nvarchar(64) NULL.', 1;
		END;

		IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'Priority') IS NULL
		BEGIN
			THROW 52041, 'TaxInvoiceComponents.Priority column is missing.', 1;
		END;

		IF EXISTS (
			SELECT 1
			FROM sys.columns c
			INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
			WHERE c.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
			  AND c.name = N'Priority'
			  AND (t.name <> N'int' OR c.is_nullable <> 0))
		BEGIN
			THROW 52042, 'TaxInvoiceComponents.Priority has incompatible definition. Expected int NOT NULL.', 1;
		END;

		IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'IsCompound') IS NULL
		BEGIN
			THROW 52043, 'TaxInvoiceComponents.IsCompound column is missing.', 1;
		END;

		IF EXISTS (
			SELECT 1
			FROM sys.columns c
			INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
			WHERE c.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
			  AND c.name = N'IsCompound'
			  AND (t.name <> N'bit' OR c.is_nullable <> 0))
		BEGIN
			THROW 52044, 'TaxInvoiceComponents.IsCompound has incompatible definition. Expected bit NOT NULL.', 1;
		END;

		IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'IsActive') IS NULL
		BEGIN
			THROW 52045, 'TaxInvoiceComponents.IsActive column is missing.', 1;
		END;

		IF EXISTS (
			SELECT 1
			FROM sys.columns c
			INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
			WHERE c.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
			  AND c.name = N'IsActive'
			  AND (t.name <> N'bit' OR c.is_nullable <> 0))
		BEGIN
			THROW 52046, 'TaxInvoiceComponents.IsActive has incompatible definition. Expected bit NOT NULL.', 1;
		END;

		IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'EffectiveFromUtc') IS NULL
		BEGIN
			THROW 52047, 'TaxInvoiceComponents.EffectiveFromUtc column is missing.', 1;
		END;

		IF EXISTS (
			SELECT 1
			FROM sys.columns c
			INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
			WHERE c.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
			  AND c.name = N'EffectiveFromUtc'
			  AND (t.name <> N'datetime2' OR c.scale <> 7 OR c.is_nullable <> 1))
		BEGIN
			THROW 52048, 'TaxInvoiceComponents.EffectiveFromUtc has incompatible definition. Expected datetime2(7) NULL.', 1;
		END;

		IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'EffectiveToUtc') IS NULL
		BEGIN
			THROW 52049, 'TaxInvoiceComponents.EffectiveToUtc column is missing.', 1;
		END;

		IF EXISTS (
			SELECT 1
			FROM sys.columns c
			INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
			WHERE c.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
			  AND c.name = N'EffectiveToUtc'
			  AND (t.name <> N'datetime2' OR c.scale <> 7 OR c.is_nullable <> 1))
		BEGIN
			THROW 52050, 'TaxInvoiceComponents.EffectiveToUtc has incompatible definition. Expected datetime2(7) NULL.', 1;
		END;

		IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'CreatedDate') IS NULL
		BEGIN
			THROW 52051, 'TaxInvoiceComponents.CreatedDate column is missing.', 1;
		END;

		IF EXISTS (
			SELECT 1
			FROM sys.columns c
			INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
			WHERE c.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
			  AND c.name = N'CreatedDate'
			  AND (t.name <> N'datetime2' OR c.scale <> 7 OR c.is_nullable <> 0))
		BEGIN
			THROW 52052, 'TaxInvoiceComponents.CreatedDate has incompatible definition. Expected datetime2(7) NOT NULL.', 1;
		END;

		IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'ModifiedDate') IS NULL
		BEGIN
			THROW 52053, 'TaxInvoiceComponents.ModifiedDate column is missing.', 1;
		END;

		IF EXISTS (
			SELECT 1
			FROM sys.columns c
			INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
			WHERE c.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
			  AND c.name = N'ModifiedDate'
			  AND (t.name <> N'datetime2' OR c.scale <> 7 OR c.is_nullable <> 0))
		BEGIN
			THROW 52054, 'TaxInvoiceComponents.ModifiedDate has incompatible definition. Expected datetime2(7) NOT NULL.', 1;
		END;

		IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'RowVersion') IS NULL
		BEGIN
			THROW 52055, 'TaxInvoiceComponents.RowVersion column is missing.', 1;
		END;

		IF EXISTS (
			SELECT 1
			FROM sys.columns c
			WHERE c.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
			  AND c.name = N'RowVersion'
			  AND (c.system_type_id <> 189 OR c.max_length <> 8 OR c.is_nullable <> 1))
		BEGIN
			THROW 52056, 'TaxInvoiceComponents.RowVersion has incompatible definition. Expected rowversion(8) NULL.', 1;
		END;

		IF COL_LENGTH(N'[dbo].[TaxInvoiceComponents]', N'JurisdictionNormalizedKey') IS NULL
		BEGIN
			THROW 52057, 'TaxInvoiceComponents.JurisdictionNormalizedKey computed column is missing.', 1;
		END;

		IF NOT EXISTS (
			SELECT 1
			FROM sys.computed_columns cc
			INNER JOIN sys.columns c ON c.object_id = cc.object_id AND c.column_id = cc.column_id
			WHERE cc.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
			  AND c.name = N'JurisdictionNormalizedKey')
		BEGIN
			THROW 52058, 'TaxInvoiceComponents.JurisdictionNormalizedKey exists but is not a computed column.', 1;
		END;

		IF EXISTS (
			SELECT 1
			FROM sys.computed_columns cc
			INNER JOIN sys.columns c ON c.object_id = cc.object_id AND c.column_id = cc.column_id
			WHERE cc.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
			  AND c.name = N'JurisdictionNormalizedKey'
			  AND (cc.is_persisted <> 1
				   OR REPLACE(REPLACE(REPLACE(cc.definition, ' ', ''), CHAR(10), ''), CHAR(13), '')
					<> REPLACE(REPLACE(REPLACE(N'COALESCE(NULLIF(LTRIM(RTRIM([Jurisdiction])),N''''),N''__NULL__'')', ' ', ''), CHAR(10), ''), CHAR(13), '')))
		BEGIN
			THROW 52059, 'TaxInvoiceComponents.JurisdictionNormalizedKey computed definition is incompatible.', 1;
		END;
	END;

	IF NOT EXISTS (
		SELECT 1
		FROM sys.foreign_keys fk
		WHERE fk.parent_object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
		  AND fk.name = N'FK_TaxInvoiceComponents_TaxInvoiceSettings_TaxInvoiceSettingsId')
	BEGIN
		IF EXISTS (
			SELECT 1
			FROM [dbo].[TaxInvoiceComponents] c
			LEFT JOIN [dbo].[TaxInvoiceSettings] s ON s.[Id] = c.[TaxInvoiceSettingsId]
			WHERE s.[Id] IS NULL)
		BEGIN
			THROW 52020, 'Cannot create FK: orphan TaxInvoiceSettingsId values exist.', 1;
		END;

		ALTER TABLE [dbo].[TaxInvoiceComponents] WITH CHECK
		ADD CONSTRAINT [FK_TaxInvoiceComponents_TaxInvoiceSettings_TaxInvoiceSettingsId]
			FOREIGN KEY ([TaxInvoiceSettingsId])
			REFERENCES [dbo].[TaxInvoiceSettings]([Id])
			ON DELETE NO ACTION;
	END
	ELSE
	BEGIN
		IF EXISTS (
			SELECT 1
			FROM sys.foreign_keys fk
			WHERE fk.parent_object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
			  AND fk.name = N'FK_TaxInvoiceComponents_TaxInvoiceSettings_TaxInvoiceSettingsId'
			  AND (
				fk.referenced_object_id <> OBJECT_ID(N'[dbo].[TaxInvoiceSettings]')
				OR fk.delete_referential_action <> 0
				OR fk.is_disabled <> 0
				OR fk.is_not_trusted <> 0
			  ))
		BEGIN
			THROW 52060, 'Existing FK is incompatible (target/delete action/enabled/trusted).', 1;
		END;

		IF EXISTS (
			SELECT 1
			FROM sys.foreign_keys fk
			LEFT JOIN sys.foreign_key_columns fkc
				ON fkc.constraint_object_id = fk.object_id
			LEFT JOIN sys.columns pc
				ON pc.object_id = fkc.parent_object_id
			   AND pc.column_id = fkc.parent_column_id
			LEFT JOIN sys.columns rc
				ON rc.object_id = fkc.referenced_object_id
			   AND rc.column_id = fkc.referenced_column_id
			WHERE fk.parent_object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
			  AND fk.name = N'FK_TaxInvoiceComponents_TaxInvoiceSettings_TaxInvoiceSettingsId'
			GROUP BY fk.object_id
			HAVING COUNT(1) <> 1
			   OR MAX(pc.name) <> N'TaxInvoiceSettingsId'
			   OR MAX(rc.name) <> N'Id')
		BEGIN
			THROW 52061, 'Existing FK columns are incompatible. Expected TaxInvoiceSettingsId -> TaxInvoiceSettings.Id.', 1;
		END;
	END;

	DECLARE @Idx1Cols nvarchar(4000);
	DECLARE @Idx2Cols nvarchar(4000);
	DECLARE @Idx3Cols nvarchar(4000);
	DECLARE @Idx2Filter nvarchar(4000);
	DECLARE @Idx1Unique bit;
	DECLARE @Idx2Unique bit;
	DECLARE @Idx3Unique bit;
	DECLARE @Idx1Filter nvarchar(4000);
	DECLARE @Idx3Filter nvarchar(4000);
	DECLARE @Idx1HasDescending bit;
	DECLARE @Idx2HasDescending bit;
	DECLARE @Idx3HasDescending bit;
	DECLARE @Idx1HasIncluded bit;
	DECLARE @Idx2HasIncluded bit;
	DECLARE @Idx3HasIncluded bit;
	DECLARE @Idx1TypeDesc nvarchar(60);
	DECLARE @Idx2TypeDesc nvarchar(60);
	DECLARE @Idx3TypeDesc nvarchar(60);
	DECLARE @Idx1Disabled bit;
	DECLARE @Idx2Disabled bit;
	DECLARE @Idx3Disabled bit;
	DECLARE @Idx1Hypothetical bit;
	DECLARE @Idx2Hypothetical bit;
	DECLARE @Idx3Hypothetical bit;

	SELECT @Idx1Cols = STRING_AGG(c.name, N',') WITHIN GROUP (ORDER BY ic.key_ordinal),
		   @Idx1Unique = i.is_unique,
		   @Idx1Filter = i.filter_definition,
		   @Idx1TypeDesc = i.type_desc,
		   @Idx1Disabled = i.is_disabled,
		   @Idx1Hypothetical = i.is_hypothetical,
		   @Idx1HasDescending = MAX(CASE WHEN ic.is_descending_key = 1 THEN 1 ELSE 0 END),
		   @Idx1HasIncluded = CASE WHEN EXISTS (
				SELECT 1
				FROM sys.index_columns icInc
				WHERE icInc.object_id = i.object_id
				  AND icInc.index_id = i.index_id
				  AND icInc.is_included_column = 1) THEN 1 ELSE 0 END
	FROM sys.indexes i
	INNER JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
	INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
	WHERE i.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
	  AND i.name = N'IX_TaxInvoiceComponents_TaxInvoiceSettingsId_Code_IsActive'
	GROUP BY i.object_id, i.index_id, i.is_unique, i.filter_definition, i.type_desc, i.is_disabled, i.is_hypothetical;

	IF @Idx1Cols IS NULL
	BEGIN
		CREATE INDEX [IX_TaxInvoiceComponents_TaxInvoiceSettingsId_Code_IsActive]
			ON [dbo].[TaxInvoiceComponents] ([TaxInvoiceSettingsId], [Code], [IsActive]);
	END
	ELSE IF @Idx1Cols <> N'TaxInvoiceSettingsId,Code,IsActive'
		 OR @Idx1Unique <> 0
		 OR @Idx1TypeDesc <> N'NONCLUSTERED'
		 OR @Idx1Disabled <> 0
		 OR @Idx1Hypothetical <> 0
		 OR ISNULL(@Idx1Filter, N'') <> N''
		 OR ISNULL(@Idx1HasDescending, 0) <> 0
		 OR ISNULL(@Idx1HasIncluded, 0) <> 0
	BEGIN
		THROW 52062, 'Index IX_TaxInvoiceComponents_TaxInvoiceSettingsId_Code_IsActive is incompatible.', 1;
	END;

	SELECT @Idx2Cols = STRING_AGG(c.name, N',') WITHIN GROUP (ORDER BY ic.key_ordinal),
		   @Idx2Unique = i.is_unique,
		   @Idx2Filter = i.filter_definition,
		   @Idx2TypeDesc = i.type_desc,
		   @Idx2Disabled = i.is_disabled,
		   @Idx2Hypothetical = i.is_hypothetical,
		   @Idx2HasDescending = MAX(CASE WHEN ic.is_descending_key = 1 THEN 1 ELSE 0 END),
		   @Idx2HasIncluded = CASE WHEN EXISTS (
				SELECT 1
				FROM sys.index_columns icInc
				WHERE icInc.object_id = i.object_id
				  AND icInc.index_id = i.index_id
				  AND icInc.is_included_column = 1) THEN 1 ELSE 0 END
	FROM sys.indexes i
	INNER JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
	INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
	WHERE i.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
	  AND i.name = N'IX_TaxInvoiceComponents_TenantName_StoreCode_Code_Priority_JurisdictionNormalizedKey'
	GROUP BY i.object_id, i.index_id, i.is_unique, i.filter_definition, i.type_desc, i.is_disabled, i.is_hypothetical;

	IF @Idx2Cols IS NULL
	BEGIN
		IF EXISTS (
			SELECT 1
			FROM [dbo].[TaxInvoiceComponents]
			WHERE [IsActive] = 1
			GROUP BY [TenantName], [StoreCode], [Code], [Priority],
					 COALESCE(NULLIF(LTRIM(RTRIM([Jurisdiction])), N''), N'__NULL__')
			HAVING COUNT(1) > 1)
		BEGIN
			THROW 52022, 'Cannot create/validate filtered unique index: active duplicate component keys exist.', 1;
		END;

		EXEC(N'CREATE UNIQUE INDEX [IX_TaxInvoiceComponents_TenantName_StoreCode_Code_Priority_JurisdictionNormalizedKey] ON [dbo].[TaxInvoiceComponents] ([TenantName], [StoreCode], [Code], [Priority], [JurisdictionNormalizedKey]) WHERE [IsActive] = 1');
	END
	ELSE IF @Idx2Cols <> N'TenantName,StoreCode,Code,Priority,JurisdictionNormalizedKey'
		 OR @Idx2Unique <> 1
		 OR @Idx2TypeDesc <> N'NONCLUSTERED'
		 OR @Idx2Disabled <> 0
		 OR @Idx2Hypothetical <> 0
		 OR ISNULL(@Idx2HasDescending, 0) <> 0
		 OR ISNULL(@Idx2HasIncluded, 0) <> 0
		 OR REPLACE(REPLACE(REPLACE(ISNULL(@Idx2Filter, N''), N' ', N''), N'(', N''), N')', N'') <> N'[IsActive]=1'
	BEGIN
		THROW 52063, 'Index IX_TaxInvoiceComponents_TenantName_StoreCode_Code_Priority_JurisdictionNormalizedKey is incompatible.', 1;
	END;

	SELECT @Idx3Cols = STRING_AGG(c.name, N',') WITHIN GROUP (ORDER BY ic.key_ordinal),
		   @Idx3Unique = i.is_unique,
		   @Idx3Filter = i.filter_definition,
		   @Idx3TypeDesc = i.type_desc,
		   @Idx3Disabled = i.is_disabled,
		   @Idx3Hypothetical = i.is_hypothetical,
		   @Idx3HasDescending = MAX(CASE WHEN ic.is_descending_key = 1 THEN 1 ELSE 0 END),
		   @Idx3HasIncluded = CASE WHEN EXISTS (
				SELECT 1
				FROM sys.index_columns icInc
				WHERE icInc.object_id = i.object_id
				  AND icInc.index_id = i.index_id
				  AND icInc.is_included_column = 1) THEN 1 ELSE 0 END
	FROM sys.indexes i
	INNER JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
	INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
	WHERE i.object_id = OBJECT_ID(N'[dbo].[TaxInvoiceComponents]')
	  AND i.name = N'IX_TaxInvoiceComponents_TenantName_StoreCode_IsActive'
	GROUP BY i.object_id, i.index_id, i.is_unique, i.filter_definition, i.type_desc, i.is_disabled, i.is_hypothetical;

	IF @Idx3Cols IS NULL
	BEGIN
		CREATE INDEX [IX_TaxInvoiceComponents_TenantName_StoreCode_IsActive]
			ON [dbo].[TaxInvoiceComponents] ([TenantName], [StoreCode], [IsActive]);
	END
	ELSE IF @Idx3Cols <> N'TenantName,StoreCode,IsActive'
		 OR @Idx3Unique <> 0
		 OR @Idx3TypeDesc <> N'NONCLUSTERED'
		 OR @Idx3Disabled <> 0
		 OR @Idx3Hypothetical <> 0
		 OR ISNULL(@Idx3Filter, N'') <> N''
		 OR ISNULL(@Idx3HasDescending, 0) <> 0
		 OR ISNULL(@Idx3HasIncluded, 0) <> 0
	BEGIN
		THROW 52064, 'Index IX_TaxInvoiceComponents_TenantName_StoreCode_IsActive is incompatible.', 1;
	END;

	IF NOT EXISTS (
		SELECT 1
		FROM [dbo].[__EFMigrationsHistory]
		WHERE [MigrationId] = N'20261104120255_PhaseC1TaxConfiguration')
	BEGIN
		THROW 52023, 'Phase C1 migration-history row is missing. This script is for schema repair with existing history row only.', 1;
	END;

	COMMIT TRANSACTION;
END TRY
BEGIN CATCH
	IF XACT_STATE() <> 0
		ROLLBACK TRANSACTION;

	DECLARE @ErrorMessage nvarchar(4000) = ERROR_MESSAGE();
	DECLARE @ErrorNumber int = ERROR_NUMBER();
	DECLARE @ErrorState int = ERROR_STATE();
	DECLARE @ErrorLine int = ERROR_LINE();
	DECLARE @RecoveryFailureMessage nvarchar(4000);

	SET @RecoveryFailureMessage = CONCAT('Phase C1 recovery failed at line ', @ErrorLine, '. Error ', @ErrorNumber, ': ', @ErrorMessage);

	THROW 52999, @RecoveryFailureMessage, 1;
END CATCH;
