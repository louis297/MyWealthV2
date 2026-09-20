EXEC sys.sp_rename N'Currencies.IsEnabled', N'IsActive', N'COLUMN';
EXEC sys.sp_rename N'DF_Currencies_IsEnabled', N'DF_Currencies_IsActive', N'OBJECT';

EXEC sys.sp_rename N'Tenants.IsEnabled', N'IsActive', N'COLUMN';
EXEC sys.sp_rename N'DF_Tenants_IsEnabled', N'DF_Tenants_IsActive', N'OBJECT';
EXEC sys.sp_rename N'Tenants.IX_Tenants_IsEnabled', N'IX_Tenants_IsActive', N'INDEX';

EXEC sys.sp_rename N'Instruments.IsEnabled', N'IsActive', N'COLUMN';
EXEC sys.sp_rename N'DF_Instruments_IsEnabled', N'DF_Instruments_IsActive', N'OBJECT';
EXEC sys.sp_rename N'Instruments.IX_Instruments_TenantId_IsEnabled', N'IX_Instruments_TenantId_IsActive', N'INDEX';

ALTER TABLE [Users] ADD [IsActive] AS ISNULL(CAST(CASE WHEN [Status] = 1 THEN 1 ELSE 0 END AS bit), 0) PERSISTED;
CREATE INDEX [IX_Users_TenantId_IsActive] ON [Users] ([TenantId], [IsActive]);
