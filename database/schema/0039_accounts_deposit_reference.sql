-- Deposit reference (docs/features/client-money-accounts.md §6.2).
-- Every Custodial account gets a deposit reference: nine characters from 0123456789ABCDEFGHJKMNPQRSTVWXYZ, the
-- first a letter, characters 2 to 8 random, the ninth a Luhn mod 32 check symbol over the first eight (R15).
-- External accounts have none. The back-fill draws from CRYPT_GEN_RANDOM and draws again while a value is taken.
-- The applicator runs this script without a surrounding transaction, so every step only does what is still
-- missing: a run that stops half-way leaves a database the same script can finish.

IF COL_LENGTH(N'Accounts', N'DepositReference') IS NULL
BEGIN
    ALTER TABLE [Accounts] ADD [DepositReference] char(9) NULL;
END;

-- The new column is referenced inside EXEC, so this batch compiles before it exists.
EXEC(N'
DECLARE @alphabet varchar(32) = ''0123456789ABCDEFGHJKMNPQRSTVWXYZ'' COLLATE Latin1_General_100_BIN2;
DECLARE @id int;
DECLARE @payload varchar(8);
DECLARE @reference char(9);
DECLARE @bytes varbinary(11);
DECLARE @position int;
DECLARE @factor int;
DECLARE @sum int;
DECLARE @product int;

WHILE 1 = 1
BEGIN
    SET @id = NULL;
    SELECT TOP (1) @id = [Id]
    FROM [Accounts]
    WHERE [CustodyMode] = 1 AND [DepositReference] IS NULL
    ORDER BY [Id];

    IF @id IS NULL
        BREAK;

    WHILE 1 = 1
    BEGIN
        -- Four bytes for the first symbol (one of the 22 letters, alphabet positions 11 to 32), then one byte for
        -- each of the seven others (256 is a multiple of 32, so byte % 32 is uniform).
        SET @bytes = CRYPT_GEN_RANDOM(11);
        SET @payload = SUBSTRING(@alphabet, 11 + CAST(CAST(SUBSTRING(@bytes, 1, 4) AS bigint) % 22 AS int), 1);
        SET @position = 5;
        WHILE @position <= 11
        BEGIN
            SET @payload = @payload + SUBSTRING(@alphabet, 1 + CAST(SUBSTRING(@bytes, @position, 1) AS int) % 32, 1);
            SET @position = @position + 1;
        END;

        -- Check symbol: right to left over the eight, factor 2 first, then 1, 2, ...
        SET @position = 8;
        SET @factor = 2;
        SET @sum = 0;
        WHILE @position >= 1
        BEGIN
            SET @product = @factor * (CHARINDEX(SUBSTRING(@payload, @position, 1) COLLATE Latin1_General_100_BIN2, @alphabet) - 1);
            SET @sum = @sum + @product / 32 + @product % 32;
            SET @factor = 3 - @factor;
            SET @position = @position - 1;
        END;

        SET @reference = @payload + SUBSTRING(@alphabet, 1 + (32 - @sum % 32) % 32, 1);

        IF NOT EXISTS (SELECT 1 FROM [Accounts] WHERE [DepositReference] = @reference)
            BREAK;
    END;

    UPDATE [Accounts] SET [DepositReference] = @reference WHERE [Id] = @id;
END;

IF OBJECT_ID(N''CK_Accounts_DepositReference'', N''C'') IS NULL
    ALTER TABLE [Accounts] ADD CONSTRAINT [CK_Accounts_DepositReference]
        CHECK (([CustodyMode] = 1 AND [DepositReference] IS NOT NULL) OR ([CustodyMode] = 0 AND [DepositReference] IS NULL));

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N''UX_Accounts_DepositReference'' AND [object_id] = OBJECT_ID(N''Accounts''))
    CREATE UNIQUE INDEX [UX_Accounts_DepositReference]
        ON [Accounts] ([DepositReference])
        WHERE [DepositReference] IS NOT NULL;
');
