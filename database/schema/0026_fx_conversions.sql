ALTER TABLE [Transactions] DROP CONSTRAINT [CK_Transactions_Type];

ALTER TABLE [Transactions] ADD CONSTRAINT [CK_Transactions_Type]
    CHECK ([Type] BETWEEN 0 AND 16);

CREATE TABLE [TransactionFxDetails] (
    [TransactionId] int NOT NULL,
    [BaseCurrency] char(3) NOT NULL,
    [QuoteCurrency] char(3) NOT NULL,
    [Rate] decimal(18, 8) NOT NULL,
    [RateAsOfDate] date NOT NULL,
    [RateSource] nvarchar(50) NOT NULL,
    CONSTRAINT [PK_TransactionFxDetails] PRIMARY KEY ([TransactionId]),
    CONSTRAINT [FK_TransactionFxDetails_Transactions_TransactionId] FOREIGN KEY ([TransactionId]) REFERENCES [Transactions] ([Id]),
    CONSTRAINT [FK_TransactionFxDetails_Currencies_BaseCurrency] FOREIGN KEY ([BaseCurrency]) REFERENCES [Currencies] ([Code]),
    CONSTRAINT [FK_TransactionFxDetails_Currencies_QuoteCurrency] FOREIGN KEY ([QuoteCurrency]) REFERENCES [Currencies] ([Code]),
    CONSTRAINT [CK_TransactionFxDetails_Currencies] CHECK ([BaseCurrency] <> [QuoteCurrency]),
    CONSTRAINT [CK_TransactionFxDetails_Rate] CHECK ([Rate] > 0)
);
