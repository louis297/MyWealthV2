ALTER TABLE [Tenants] ADD [ReportingCurrency] char(3) NOT NULL CONSTRAINT [DF_Tenants_ReportingCurrency] DEFAULT ('NZD');

ALTER TABLE [Tenants] ADD CONSTRAINT [FK_Tenants_Currencies_ReportingCurrency]
    FOREIGN KEY ([ReportingCurrency]) REFERENCES [Currencies] ([Code]);

ALTER TABLE [Tenants] DROP CONSTRAINT [DF_Tenants_ReportingCurrency];
