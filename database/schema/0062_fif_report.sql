-- docs/features/fif-report.md R1, §6: the FIF report in the catalog. Scope Customer, manual only. Granted to nobody.
IF NOT EXISTS (SELECT 1 FROM [Reports] WHERE [Code] = N'customer-fif')
    INSERT INTO [Reports] ([PublicId], [Code], [Name], [Function], [Scope], [AllowedTriggers], [IsActive]) VALUES
        ('7d2f6b48-1a9c-4e35-a7d0-5c8e3b1f9a44', N'customer-fif', N'FIF Report',
         N'Foreign investment fund (FIF) figures of one customer for one New Zealand tax year: the threshold test, and FIF income under the fair dividend rate and the comparative value methods.',
         N'Customer', N'["manual"]', 1);
