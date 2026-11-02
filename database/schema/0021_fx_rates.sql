CREATE TABLE [FxRates] (
    [BaseCurrency] char(3) NOT NULL,
    [QuoteCurrency] char(3) NOT NULL,
    [Rate] decimal(18, 8) NOT NULL,
    [AsOfDate] date NOT NULL,
    [Source] nvarchar(50) NOT NULL,
    [FetchedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_FxRates] PRIMARY KEY ([BaseCurrency], [QuoteCurrency], [AsOfDate]),
    CONSTRAINT [FK_FxRates_Currencies_BaseCurrency] FOREIGN KEY ([BaseCurrency]) REFERENCES [Currencies] ([Code]),
    CONSTRAINT [FK_FxRates_Currencies_QuoteCurrency] FOREIGN KEY ([QuoteCurrency]) REFERENCES [Currencies] ([Code])
);

CREATE INDEX [IX_FxRates_FetchedAt] ON [FxRates] ([FetchedAt]);
