CREATE TABLE [Currencies] (
    [Code] char(3) NOT NULL,
    [Name] nvarchar(100) NOT NULL,
    [DecimalPlaces] tinyint NOT NULL,
    [IsEnabled] bit NOT NULL CONSTRAINT [DF_Currencies_IsEnabled] DEFAULT (1),
    CONSTRAINT [PK_Currencies] PRIMARY KEY ([Code])
);

INSERT INTO [Currencies] ([Code], [Name], [DecimalPlaces], [IsEnabled]) VALUES
    ('AUD', N'Australian Dollar', 2, 1),
    ('EUR', N'Euro', 2, 1),
    ('GBP', N'Pound Sterling', 2, 1),
    ('JPY', N'Japanese Yen', 0, 1),
    ('NZD', N'New Zealand Dollar', 2, 1),
    ('USD', N'United States Dollar', 2, 1);
