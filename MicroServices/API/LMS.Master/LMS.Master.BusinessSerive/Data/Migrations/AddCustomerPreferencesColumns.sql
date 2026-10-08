-- Add missing CustomerPreferences columns with safe defaults
-- Backup the database before running this script.

ALTER TABLE dbo.CustomerPreferences
ADD
	EnableSmsNotifications BIT NOT NULL CONSTRAINT DF_CustomerPreferences_EnableSmsNotifications DEFAULT(0),
	EnableEmailNotifications BIT NOT NULL CONSTRAINT DF_CustomerPreferences_EnableEmailNotifications DEFAULT(0),
	AutoGenerateCustomerCode BIT NOT NULL CONSTRAINT DF_CustomerPreferences_AutoGenerateCustomerCode DEFAULT(0),
	RequirePhoneNumber BIT NOT NULL CONSTRAINT DF_CustomerPreferences_RequirePhoneNumber DEFAULT(0),
	RequireEmail BIT NOT NULL CONSTRAINT DF_CustomerPreferences_RequireEmail DEFAULT(0),
	AllowDuplicatePhoneNumber BIT NOT NULL CONSTRAINT DF_CustomerPreferences_AllowDuplicatePhoneNumber DEFAULT(0),
	PickupReminderHours INT NOT NULL CONSTRAINT DF_CustomerPreferences_PickupReminderHours DEFAULT(0),
	LoyaltyPointsPerOrder INT NOT NULL CONSTRAINT DF_CustomerPreferences_LoyaltyPointsPerOrder DEFAULT(0);

-- Optionally verify columns
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'CustomerPreferences'
ORDER BY ORDINAL_POSITION;
