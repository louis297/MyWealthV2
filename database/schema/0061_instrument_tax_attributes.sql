-- Tax attributes of an Instrument (docs/features/instrument-tax-attributes.md §6).
-- The issuer country on the Instrument, and one FIF treatment row per Instrument and tax year. The agreement of the
-- rows with the country (R12) spans two tables and is held by Instrument.SetTaxAttributes. The upper bound of a tax
-- year here is a fixed 2199; the moving bound of R9 is held by the Domain.
-- No back-fill (R15): every existing Instrument has no country and no row. Forward only.

ALTER TABLE [Instruments] ADD [IssuerCountry] char(2) NULL;

-- The new column is referenced inside EXEC, so this batch compiles before it exists.
-- R5: NULL or two upper-case letters; the binary collation makes the range case-sensitive.
EXEC(N'ALTER TABLE [Instruments] ADD CONSTRAINT [CK_Instruments_IssuerCountry] CHECK (
    [IssuerCountry] IS NULL OR [IssuerCountry] COLLATE Latin1_General_100_BIN2 LIKE ''[A-Z][A-Z]'')');

CREATE TABLE [InstrumentFifTreatments] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [TenantId] int NOT NULL,
    [InstrumentId] int NOT NULL,
    [TaxYear] smallint NOT NULL,
    [Treatment] tinyint NOT NULL,
    [Note] nvarchar(500) NULL,
    [Created] datetimeoffset NOT NULL,
    [CreatedBy] nvarchar(450) NOT NULL,
    [LastModified] datetimeoffset NULL,
    [LastModifiedBy] nvarchar(450) NULL,
    CONSTRAINT [PK_InstrumentFifTreatments] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_InstrumentFifTreatments_InstrumentId_TaxYear] UNIQUE ([InstrumentId], [TaxYear]),
    CONSTRAINT [FK_InstrumentFifTreatments_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]),
    CONSTRAINT [FK_InstrumentFifTreatments_Instruments_TenantId_InstrumentId]
        FOREIGN KEY ([TenantId], [InstrumentId]) REFERENCES [Instruments] ([TenantId], [Id]),
    -- R8, R9: the calendar year the tax year ends in.
    CONSTRAINT [CK_InstrumentFifTreatments_TaxYear] CHECK ([TaxYear] BETWEEN 2008 AND 2199),
    -- R7: NotForeign to AttributingNoFdr; NotClassified (0) is never stored.
    CONSTRAINT [CK_InstrumentFifTreatments_Treatment] CHECK ([Treatment] BETWEEN 1 AND 5),
    -- R9: ExemptOther names its exemption.
    CONSTRAINT [CK_InstrumentFifTreatments_Note] CHECK ([Treatment] <> 3 OR [Note] IS NOT NULL)
);

CREATE INDEX [IX_InstrumentFifTreatments_TenantId] ON [InstrumentFifTreatments] ([TenantId]);
