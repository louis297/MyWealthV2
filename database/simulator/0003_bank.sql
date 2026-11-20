-- The simulator bank, receipts side (docs/features/deposit-matching.md §6.3, R38–R48).
-- The simulator's own trust accounts, the lines on them, and the places of those lines in each account's statement
-- feed. A place never moves, so a cursor never skips a line that arrived late (R43). World.NextLineNumber counts the
-- lines of the world for their outside ids (R42); a reset empties the three tables and sets it back to 1.

ALTER TABLE [World] ADD [NextLineNumber] bigint NOT NULL CONSTRAINT [DF_World_NextLineNumber] DEFAULT (1);

CREATE TABLE [BankAccounts] (
    [Id] int NOT NULL IDENTITY,
    [AccountNumber] nvarchar(34) NOT NULL,
    [Currency] char(3) NOT NULL,
    [Name] nvarchar(140) NOT NULL,
    [OpenedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_BankAccounts] PRIMARY KEY ([Id])
);

-- The database collation compares the number without regard to case (R38).
CREATE UNIQUE INDEX [UX_BankAccounts_AccountNumber] ON [BankAccounts] ([AccountNumber]);

CREATE TABLE [BankLines] (
    [Id] bigint NOT NULL IDENTITY,
    [OutsideId] nvarchar(100) NOT NULL,
    [BankAccountId] int NOT NULL,
    [Direction] int NOT NULL,
    [Amount] decimal(18, 4) NOT NULL,
    [ValueDate] date NOT NULL,
    [PayerName] nvarchar(140) NULL,
    [PayerAccount] nvarchar(34) NULL,
    [Reference] nvarchar(140) NULL,
    [PostedAt] datetimeoffset NOT NULL,
    [AvailableAt] datetimeoffset NOT NULL,
    [DeliverTwice] bit NOT NULL,
    CONSTRAINT [PK_BankLines] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_BankLines_BankAccounts_BankAccountId] FOREIGN KEY ([BankAccountId]) REFERENCES [BankAccounts] ([Id]),
    CONSTRAINT [CK_BankLines_Direction] CHECK ([Direction] BETWEEN 0 AND 1),
    CONSTRAINT [CK_BankLines_Amount] CHECK ([Amount] > 0)
);

CREATE UNIQUE INDEX [UX_BankLines_OutsideId] ON [BankLines] ([OutsideId]);
CREATE INDEX [IX_BankLines_BankAccountId_AvailableAt] ON [BankLines] ([BankAccountId], [AvailableAt]);

CREATE TABLE [BankFeedPlaces] (
    [Place] bigint NOT NULL IDENTITY,
    [BankAccountId] int NOT NULL,
    [BankLineId] bigint NOT NULL,
    CONSTRAINT [PK_BankFeedPlaces] PRIMARY KEY ([Place]),
    CONSTRAINT [FK_BankFeedPlaces_BankAccounts_BankAccountId] FOREIGN KEY ([BankAccountId]) REFERENCES [BankAccounts] ([Id]),
    CONSTRAINT [FK_BankFeedPlaces_BankLines_BankLineId] FOREIGN KEY ([BankLineId]) REFERENCES [BankLines] ([Id])
);

CREATE INDEX [IX_BankFeedPlaces_BankAccountId_Place] ON [BankFeedPlaces] ([BankAccountId], [Place]);
CREATE INDEX [IX_BankFeedPlaces_BankLineId] ON [BankFeedPlaces] ([BankLineId]);
