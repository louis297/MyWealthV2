-- The simulator custodian (docs/features/in-kind-transfers.md §6.3, R60–R72).
-- Safekeeping accounts, each in one currency, whose position in a symbol is the signed sum of its movements; the
-- transfer instructions the platform sends; and one row of settings. The custodian checks no position. It does not
-- read the platform; the instruction id, the trade reference and an order's safekeeping account are opaque text.
-- A reset empties the accounts, movements and transfers and sets the settings to their defaults (R71).
-- The applicator runs this script without a surrounding transaction.

CREATE TABLE [CustodianAccounts] (
    [Id] int NOT NULL IDENTITY,
    [AccountNumber] nvarchar(34) NOT NULL,
    [Currency] char(3) NOT NULL,
    [Name] nvarchar(140) NOT NULL,
    [OpenedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_CustodianAccounts] PRIMARY KEY ([Id]),
    CONSTRAINT [UX_CustodianAccounts_AccountNumber] UNIQUE ([AccountNumber])
);

CREATE TABLE [CustodianMovements] (
    [Id] int NOT NULL IDENTITY,
    -- The n of the outside id {WorldId:N}-M{n}, counting the movements of the world from 1.
    [Number] int NOT NULL,
    [AccountNumber] nvarchar(34) NOT NULL,
    -- Its place in the account's feed order, from 1.
    [Place] int NOT NULL,
    [Direction] int NOT NULL,
    [Kind] int NOT NULL,
    [Symbol] nvarchar(32) NOT NULL,
    [Currency] char(3) NOT NULL,
    [Quantity] decimal(18, 8) NOT NULL,
    [SettlementDate] date NOT NULL,
    [InstructionId] nvarchar(100) NULL,
    [TradeReference] nvarchar(100) NULL,
    [CorporateActionId] nvarchar(100) NULL,
    [CounterpartyName] nvarchar(140) NULL,
    [CounterpartyAccount] nvarchar(34) NULL,
    [Reference] nvarchar(140) NULL,
    [OmitFromFeed] bit NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_CustodianMovements] PRIMARY KEY ([Id]),
    CONSTRAINT [UX_CustodianMovements_Number] UNIQUE ([Number]),
    CONSTRAINT [UX_CustodianMovements_AccountNumber_Place] UNIQUE ([AccountNumber], [Place]),
    CONSTRAINT [FK_CustodianMovements_CustodianAccounts_AccountNumber]
        FOREIGN KEY ([AccountNumber]) REFERENCES [CustodianAccounts] ([AccountNumber]),
    -- In, Out.
    CONSTRAINT [CK_CustodianMovements_Direction] CHECK ([Direction] BETWEEN 0 AND 1),
    -- Transfer, Trade, CorporateAction.
    CONSTRAINT [CK_CustodianMovements_Kind] CHECK ([Kind] BETWEEN 0 AND 2),
    CONSTRAINT [CK_CustodianMovements_Quantity] CHECK ([Quantity] > 0)
);

CREATE TABLE [CustodianTransfers] (
    [Id] int NOT NULL IDENTITY,
    -- The n of the transfer id {WorldId:N}-T{n}.
    [Number] int NOT NULL,
    [AccountNumber] nvarchar(34) NOT NULL,
    [InstructionId] nvarchar(100) NOT NULL,
    [Body] nvarchar(max) NOT NULL,
    [RequestHash] varbinary(32) NOT NULL,
    -- Held, Executed, Rejected.
    [Status] int NOT NULL,
    -- Execute, Hold, Reject: the transfer mode when the instruction arrived.
    [ModeAtReceipt] int NOT NULL,
    [RejectReason] nvarchar(40) NULL,
    [MovementNumber] int NULL,
    [ReceivedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_CustodianTransfers] PRIMARY KEY ([Id]),
    CONSTRAINT [UX_CustodianTransfers_Number] UNIQUE ([Number]),
    CONSTRAINT [UX_CustodianTransfers_AccountNumber_InstructionId] UNIQUE ([AccountNumber], [InstructionId]),
    CONSTRAINT [FK_CustodianTransfers_CustodianAccounts_AccountNumber]
        FOREIGN KEY ([AccountNumber]) REFERENCES [CustodianAccounts] ([AccountNumber]),
    CONSTRAINT [CK_CustodianTransfers_Status] CHECK ([Status] BETWEEN 0 AND 2),
    CONSTRAINT [CK_CustodianTransfers_ModeAtReceipt] CHECK ([ModeAtReceipt] BETWEEN 0 AND 2)
);

CREATE TABLE [CustodianSettings] (
    [Id] int NOT NULL,
    -- Execute, Hold, Reject.
    [TransferMode] int NOT NULL,
    [RejectReason] nvarchar(40) NULL,
    -- Fractional, WholeShares.
    [RoundingMode] int NOT NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_CustodianSettings] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_CustodianSettings_SingleRow] CHECK ([Id] = 1),
    CONSTRAINT [CK_CustodianSettings_TransferMode] CHECK ([TransferMode] BETWEEN 0 AND 2),
    CONSTRAINT [CK_CustodianSettings_RejectReason] CHECK (
        ([TransferMode] = 2 AND [RejectReason] IS NOT NULL) OR ([TransferMode] <> 2 AND [RejectReason] IS NULL)),
    CONSTRAINT [CK_CustodianSettings_RoundingMode] CHECK ([RoundingMode] BETWEEN 0 AND 1)
);

INSERT INTO [CustodianSettings] ([Id], [TransferMode], [RejectReason], [RoundingMode]) VALUES (1, 0, NULL, 0);

-- R67: the safekeeping account an order names, stored as text.
ALTER TABLE [BrokerOrders] ADD [SafekeepingAccount] nvarchar(34) NULL;
