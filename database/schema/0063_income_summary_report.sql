-- docs/features/income-summary.md R15, §6: the tax year income summary in the catalog. Scope Customer, manual only. Granted to nobody.
IF NOT EXISTS (SELECT 1 FROM [Reports] WHERE [Code] = N'customer-income-summary')
    INSERT INTO [Reports] ([PublicId], [Code], [Name], [Function], [Scope], [AllowedTriggers], [IsActive]) VALUES
        ('4b6e2d91-3f7a-4c18-9d52-8a1e7c0b6f55', N'customer-income-summary', N'Tax Year Income Summary',
         N'Dividends and interest paid to one customer in one New Zealand tax year, in New Zealand dollars, with the tax taken from them and the imputation credits.',
         N'Customer', N'["manual"]', 1);
