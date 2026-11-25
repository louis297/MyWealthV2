using Microsoft.Data.SqlClient;
using MyWealthV2.Schema;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Tenant composite keys B2: the database rejects a reference to another tenant's row (script 0028).</summary>
public class TenantCompositeKeysSchemaTests
{
    private const string TenantCompositeKeysScript = "0028_tenant_composite_keys.sql";

    /// <summary>
    /// One case per tenant composite FK. <c>{p}</c> is the prefix of the tenant that owns the parent row;
    /// the child row always belongs to tenant <c>a</c>.
    /// </summary>
    private static readonly (string ForeignKey, string Parent, string Statement)[] CompositeKeys =
    [
        ("FK_Users_Users_TenantId_AdviserId", "Users", """
            INSERT INTO [AspNetUsers] ([Id], [EmailConfirmed], [PhoneNumberConfirmed], [TwoFactorEnabled], [LockoutEnabled], [AccessFailedCount])
            VALUES (@identity, 1, 0, 0, 0, 0);
            INSERT INTO [Users] ([PublicId], [TenantId], [Name], [Email], [Role], [Status], [AdviserId], [IdentityUserId], [Created], [LastModified])
            VALUES (NEWID(), @a_tenant, N'New customer', N'new@a.example', 3, 1, @{p}_adviser, @identity, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """),
        ("FK_Accounts_Users_TenantId_CustomerId", "Users", """
            INSERT INTO [Accounts] ([PublicId], [TenantId], [CustomerId], [Name], [Type], [CustodyMode], [Status], [Currency], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @{p}_customer, N'New', 0, 0, 0, 'NZD', SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_Holdings_Accounts_TenantId_AccountId", "Accounts", """
            INSERT INTO [Holdings] ([PublicId], [TenantId], [AccountId], [InstrumentId], [Quantity], [CostAmount], [CostCurrency], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @{p}_brokerage, @a_instrument, 1, 10, 'USD', SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_Holdings_Instruments_TenantId_InstrumentId", "Instruments", """
            INSERT INTO [Holdings] ([PublicId], [TenantId], [AccountId], [InstrumentId], [Quantity], [CostAmount], [CostCurrency], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_brokerage, @{p}_instrument, 1, 10, 'USD', SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_Transactions_Transactions_TenantId_OriginalTransactionId", "Transactions", """
            INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [SourceKind], [BookedAt], [OriginalTransactionId], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, 5, 0, SYSDATETIMEOFFSET(), @{p}_buy, SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_Transactions_Transactions_TenantId_ReversedByTransactionId", "Transactions", """
            UPDATE [Transactions] SET [ReversedByTransactionId] = @{p}_fx WHERE [Id] = @a_buy;
            """),
        ("FK_TransactionCashLegs_Transactions_TenantId_TransactionId", "Transactions", """
            INSERT INTO [TransactionCashLegs] ([TenantId], [TransactionId], [AccountId], [Amount], [Currency], [Created], [CreatedBy])
            VALUES (@a_tenant, @{p}_buy, @a_bank, 1, 'USD', SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_TransactionCashLegs_Accounts_TenantId_AccountId", "Accounts", """
            INSERT INTO [TransactionCashLegs] ([TenantId], [TransactionId], [AccountId], [Amount], [Currency], [Created], [CreatedBy])
            VALUES (@a_tenant, @a_buy, @{p}_bank, 1, 'USD', SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_TransactionSecurityLegs_Transactions_TenantId_TransactionId", "Transactions", """
            INSERT INTO [TransactionSecurityLegs] ([TenantId], [TransactionId], [AccountId], [InstrumentId], [Quantity], [CostAmount], [CostCurrency], [Created], [CreatedBy])
            VALUES (@a_tenant, @{p}_fx, @a_brokerage, @a_instrument, 1, 10, 'USD', SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_TransactionSecurityLegs_Accounts_TenantId_AccountId", "Accounts", """
            INSERT INTO [TransactionSecurityLegs] ([TenantId], [TransactionId], [AccountId], [InstrumentId], [Quantity], [CostAmount], [CostCurrency], [Created], [CreatedBy])
            VALUES (@a_tenant, @a_fx, @{p}_brokerage, @a_instrument, 1, 10, 'USD', SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_TransactionSecurityLegs_Instruments_TenantId_InstrumentId", "Instruments", """
            INSERT INTO [TransactionSecurityLegs] ([TenantId], [TransactionId], [AccountId], [InstrumentId], [Quantity], [CostAmount], [CostCurrency], [Created], [CreatedBy])
            VALUES (@a_tenant, @a_fx, @a_brokerage, @{p}_instrument, 1, 10, 'USD', SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_TransactionFxDetails_Transactions_TenantId_TransactionId", "Transactions", """
            INSERT INTO [TransactionFxDetails] ([TenantId], [TransactionId], [BaseCurrency], [QuoteCurrency], [Rate], [RateAsOfDate], [RateSource])
            VALUES (@a_tenant, @{p}_buy, 'NZD', 'USD', 0.5, '2026-11-12', N'Frankfurter');
            """),
        ("FK_TransactionAdjustmentDetails_Transactions_TenantId_TransactionId", "Transactions", """
            INSERT INTO [TransactionAdjustmentDetails] ([TransactionId], [TenantId], [Reason])
            VALUES (@{p}_buy, @a_tenant, 0);
            """),
        ("FK_BankStatementLines_Accounts_TenantId_MatchedAccountId", "Accounts", """
            INSERT INTO [ClientMoneyAccounts]
                ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy], [BankConnection])
            VALUES (NEWID(), @a_tenant, 'USD', N'Simulator Bank', N'Trust', CONVERT(nvarchar(34), LEFT(REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''), 30)), 1, SYSDATETIMEOFFSET(), N'test', N'simulator');
            INSERT INTO [BankStatementLines]
                ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [OutsideId], [ValueDate], [Direction], [Amount], [Currency],
                 [ReceivedAt], [Status], [MatchedAccountId], [MatchedTransactionId], [MatchMethod], [MatchedAt], [LastActionByUserId], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, SCOPE_IDENTITY(), N'simulator', CONVERT(nvarchar(100), NEWID()), '2026-11-20', 0, 5, 'USD',
                    SYSDATETIMEOFFSET(), 1, @{p}_bank, @a_buy, 0, SYSDATETIMEOFFSET(), NULL, SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_BankStatementLines_Transactions_TenantId_MatchedTransactionId", "Transactions", """
            INSERT INTO [ClientMoneyAccounts]
                ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy], [BankConnection])
            VALUES (NEWID(), @a_tenant, 'USD', N'Simulator Bank', N'Trust', CONVERT(nvarchar(34), LEFT(REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''), 30)), 1, SYSDATETIMEOFFSET(), N'test', N'simulator');
            INSERT INTO [BankStatementLines]
                ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [OutsideId], [ValueDate], [Direction], [Amount], [Currency],
                 [ReceivedAt], [Status], [MatchedAccountId], [MatchedTransactionId], [MatchMethod], [MatchedAt], [LastActionByUserId], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, SCOPE_IDENTITY(), N'simulator', CONVERT(nvarchar(100), NEWID()), '2026-11-20', 0, 5, 'USD',
                    SYSDATETIMEOFFSET(), 1, @a_bank, @{p}_buy, 0, SYSDATETIMEOFFSET(), NULL, SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_BankStatementLines_Users_TenantId_LastActionByUserId", "Users", """
            INSERT INTO [ClientMoneyAccounts]
                ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy], [BankConnection])
            VALUES (NEWID(), @a_tenant, 'USD', N'Simulator Bank', N'Trust', CONVERT(nvarchar(34), LEFT(REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''), 30)), 1, SYSDATETIMEOFFSET(), N'test', N'simulator');
            INSERT INTO [BankStatementLines]
                ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [OutsideId], [ValueDate], [Direction], [Amount], [Currency],
                 [ReceivedAt], [Status], [MatchedAccountId], [MatchedTransactionId], [MatchMethod], [MatchedAt], [LastActionByUserId], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, SCOPE_IDENTITY(), N'simulator', CONVERT(nvarchar(100), NEWID()), '2026-11-20', 0, 5, 'USD',
                    SYSDATETIMEOFFSET(), 0, NULL, NULL, NULL, NULL, @{p}_adviser, SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_TransactionBankReceiptDetails_Transactions_TenantId_TransactionId", "Transactions", """
            INSERT INTO [TransactionBankReceiptDetails] ([TransactionId], [TenantId], [ValueDate])
            VALUES (@{p}_buy, @a_tenant, '2026-11-20');
            """),
        ("FK_TradeFeeBands_TradeFeeSchedules_TenantId_TradeFeeScheduleId", "TradeFeeSchedules", """
            INSERT INTO [TradeFeeBands] ([TenantId], [TradeFeeScheduleId], [FromAmount], [RatePercent])
            VALUES (@a_tenant, @{p}_feeschedule, 1000, 0.3);
            """),
        ("FK_TransactionFeeDetails_TransactionCashLegs_TenantId_CashLegId", "TransactionCashLegs", """
            INSERT INTO [TransactionFeeDetails] ([CashLegId], [TenantId], [TransactionId], [Kind], [TradeFeeScheduleId], [BasisAmount], [FeeAmount], [Currency])
            VALUES (@{p}_feeleg, @a_tenant, @a_buy, 0, @a_feeschedule, 10, 10, 'USD');
            """),
        ("FK_TransactionFeeDetails_Transactions_TenantId_TransactionId", "Transactions", """
            INSERT INTO [TransactionFeeDetails] ([CashLegId], [TenantId], [TransactionId], [Kind], [TradeFeeScheduleId], [BasisAmount], [FeeAmount], [Currency])
            VALUES (@a_feeleg, @a_tenant, @{p}_buy, 0, @a_feeschedule, 10, 10, 'USD');
            """),
        ("FK_TransactionFeeDetails_TradeFeeSchedules_TenantId_TradeFeeScheduleId", "TradeFeeSchedules", """
            INSERT INTO [TransactionFeeDetails] ([CashLegId], [TenantId], [TransactionId], [Kind], [TradeFeeScheduleId], [BasisAmount], [FeeAmount], [Currency])
            VALUES (@a_feeleg, @a_tenant, @a_buy, 0, @{p}_feeschedule, 10, 10, 'USD');
            """),
        ("FK_PortfolioLines_Portfolios_TenantId_PortfolioId", "Portfolios", """
            INSERT INTO [PortfolioLines] ([TenantId], [PortfolioId], [InstrumentId], [TargetWeight], [SortOrder])
            VALUES (@a_tenant, @{p}_portfolio, @a_instrument, 10, 1);
            """),
        ("FK_PortfolioLines_Instruments_TenantId_InstrumentId", "Instruments", """
            INSERT INTO [PortfolioLines] ([TenantId], [PortfolioId], [InstrumentId], [TargetWeight], [SortOrder])
            VALUES (@a_tenant, @a_portfolio, @{p}_instrument, 10, 1);
            """),
        ("FK_AccountPortfolioAssignments_Accounts_TenantId_AccountId", "Accounts", """
            INSERT INTO [AccountPortfolioAssignments] ([PublicId], [TenantId], [AccountId], [PortfolioId], [Status], [AssignedAt], [AssignedBy], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @{p}_brokerage, @a_portfolio, 0, SYSDATETIMEOFFSET(), N'test', SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_AccountPortfolioAssignments_Portfolios_TenantId_PortfolioId", "Portfolios", """
            INSERT INTO [AccountPortfolioAssignments] ([PublicId], [TenantId], [AccountId], [PortfolioId], [Status], [AssignedAt], [AssignedBy], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_brokerage, @{p}_portfolio, 0, SYSDATETIMEOFFSET(), N'test', SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_NominatedBankAccounts_Users_TenantId_CustomerId", "Users", """
            INSERT INTO [NominatedBankAccounts]
                ([PublicId], [TenantId], [CustomerId], [Currency], [BankName], [AccountName], [AccountNumber], [AccountNumberKey], [Status],
                 [AddedByUserId], [AddedAt], [ReviewedByUserId], [ReviewedAt], [ReviewNote], [RemovedByUserId], [RemovedAt], [RemovedNote], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @{p}_customer, 'USD', N'ANZ', N'J Smith', N'0601', N'0601', 0,
                    @a_adviser, SYSDATETIMEOFFSET(), NULL, NULL, NULL, NULL, NULL, NULL, SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_NominatedBankAccounts_Users_TenantId_AddedByUserId", "Users", """
            INSERT INTO [NominatedBankAccounts]
                ([PublicId], [TenantId], [CustomerId], [Currency], [BankName], [AccountName], [AccountNumber], [AccountNumberKey], [Status],
                 [AddedByUserId], [AddedAt], [ReviewedByUserId], [ReviewedAt], [ReviewNote], [RemovedByUserId], [RemovedAt], [RemovedNote], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_customer, 'USD', N'ANZ', N'J Smith', N'0601', N'0601', 0,
                    @{p}_adviser, SYSDATETIMEOFFSET(), NULL, NULL, NULL, NULL, NULL, NULL, SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_NominatedBankAccounts_Users_TenantId_ReviewedByUserId", "Users", """
            INSERT INTO [NominatedBankAccounts]
                ([PublicId], [TenantId], [CustomerId], [Currency], [BankName], [AccountName], [AccountNumber], [AccountNumberKey], [Status],
                 [AddedByUserId], [AddedAt], [ReviewedByUserId], [ReviewedAt], [ReviewNote], [RemovedByUserId], [RemovedAt], [RemovedNote], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_customer, 'USD', N'ANZ', N'J Smith', N'0601', N'0601', 1,
                    @a_adviser, SYSDATETIMEOFFSET(), @{p}_adviser, SYSDATETIMEOFFSET(), N'Checked', NULL, NULL, NULL, SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_NominatedBankAccounts_Users_TenantId_RemovedByUserId", "Users", """
            INSERT INTO [NominatedBankAccounts]
                ([PublicId], [TenantId], [CustomerId], [Currency], [BankName], [AccountName], [AccountNumber], [AccountNumberKey], [Status],
                 [AddedByUserId], [AddedAt], [ReviewedByUserId], [ReviewedAt], [ReviewNote], [RemovedByUserId], [RemovedAt], [RemovedNote], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_customer, 'USD', N'ANZ', N'J Smith', N'0601', N'0601', 3,
                    @a_adviser, SYSDATETIMEOFFSET(), NULL, NULL, NULL, @{p}_adviser, SYSDATETIMEOFFSET(), N'Gone', SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_Withdrawals_Accounts_TenantId_AccountId", "Accounts", """
            INSERT INTO [NominatedBankAccounts]
                ([PublicId], [TenantId], [CustomerId], [Currency], [BankName], [AccountName], [AccountNumber], [AccountNumberKey], [Status],
                 [AddedByUserId], [AddedAt], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_customer, 'USD', N'ANZ', N'J Smith', N'0601', N'0601', 0, @a_adviser, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            DECLARE @nba int = SCOPE_IDENTITY();
            INSERT INTO [Withdrawals]
                ([PublicId], [TenantId], [AccountId], [NominatedBankAccountId], [Amount], [Currency], [Reference], [Status],
                 [RequestedByUserId], [RequestedAt], [DecisionAction], [DecisionByUserId], [DecisionAt], [PayoutTransactionId], [ReturnTransactionId], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @{p}_bank, @nba, 300, 'USD', N'REF', 0, @a_adviser, SYSDATETIMEOFFSET(), NULL, NULL, NULL, NULL, NULL, SYSDATETIMEOFFSET(), N'test');
            DECLARE @w int = SCOPE_IDENTITY();
            """),
        ("FK_Withdrawals_NominatedBankAccounts_TenantId_NominatedBankAccountId", "NominatedBankAccounts", """
            INSERT INTO [NominatedBankAccounts]
                ([PublicId], [TenantId], [CustomerId], [Currency], [BankName], [AccountName], [AccountNumber], [AccountNumberKey], [Status],
                 [AddedByUserId], [AddedAt], [Created], [CreatedBy])
            VALUES (NEWID(), @{p}_tenant, @{p}_customer, 'USD', N'ANZ', N'J Smith', N'0601', N'0601', 0, @{p}_adviser, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            DECLARE @nba int = SCOPE_IDENTITY();
            INSERT INTO [Withdrawals]
                ([PublicId], [TenantId], [AccountId], [NominatedBankAccountId], [Amount], [Currency], [Reference], [Status],
                 [RequestedByUserId], [RequestedAt], [DecisionAction], [DecisionByUserId], [DecisionAt], [PayoutTransactionId], [ReturnTransactionId], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_bank, @nba, 300, 'USD', N'REF', 0, @a_adviser, SYSDATETIMEOFFSET(), NULL, NULL, NULL, NULL, NULL, SYSDATETIMEOFFSET(), N'test');
            DECLARE @w int = SCOPE_IDENTITY();
            """),
        ("FK_Withdrawals_Users_TenantId_RequestedByUserId", "Users", """
            INSERT INTO [NominatedBankAccounts]
                ([PublicId], [TenantId], [CustomerId], [Currency], [BankName], [AccountName], [AccountNumber], [AccountNumberKey], [Status],
                 [AddedByUserId], [AddedAt], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_customer, 'USD', N'ANZ', N'J Smith', N'0601', N'0601', 0, @a_adviser, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            DECLARE @nba int = SCOPE_IDENTITY();
            INSERT INTO [Withdrawals]
                ([PublicId], [TenantId], [AccountId], [NominatedBankAccountId], [Amount], [Currency], [Reference], [Status],
                 [RequestedByUserId], [RequestedAt], [DecisionAction], [DecisionByUserId], [DecisionAt], [PayoutTransactionId], [ReturnTransactionId], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_bank, @nba, 300, 'USD', N'REF', 0, @{p}_adviser, SYSDATETIMEOFFSET(), NULL, NULL, NULL, NULL, NULL, SYSDATETIMEOFFSET(), N'test');
            DECLARE @w int = SCOPE_IDENTITY();
            """),
        ("FK_Withdrawals_Users_TenantId_DecisionByUserId", "Users", """
            INSERT INTO [NominatedBankAccounts]
                ([PublicId], [TenantId], [CustomerId], [Currency], [BankName], [AccountName], [AccountNumber], [AccountNumberKey], [Status],
                 [AddedByUserId], [AddedAt], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_customer, 'USD', N'ANZ', N'J Smith', N'0601', N'0601', 0, @a_adviser, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            DECLARE @nba int = SCOPE_IDENTITY();
            INSERT INTO [Withdrawals]
                ([PublicId], [TenantId], [AccountId], [NominatedBankAccountId], [Amount], [Currency], [Reference], [Status],
                 [RequestedByUserId], [RequestedAt], [DecisionAction], [DecisionByUserId], [DecisionAt], [PayoutTransactionId], [ReturnTransactionId], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_bank, @nba, 300, 'USD', N'REF', 1, @a_adviser, SYSDATETIMEOFFSET(), 0, @{p}_adviser, SYSDATETIMEOFFSET(), NULL, NULL, SYSDATETIMEOFFSET(), N'test');
            DECLARE @w int = SCOPE_IDENTITY();
            """),
        ("FK_Withdrawals_Transactions_TenantId_PayoutTransactionId", "Transactions", """
            INSERT INTO [NominatedBankAccounts]
                ([PublicId], [TenantId], [CustomerId], [Currency], [BankName], [AccountName], [AccountNumber], [AccountNumberKey], [Status],
                 [AddedByUserId], [AddedAt], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_customer, 'USD', N'ANZ', N'J Smith', N'0601', N'0601', 0, @a_adviser, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            DECLARE @nba int = SCOPE_IDENTITY();
            INSERT INTO [Withdrawals]
                ([PublicId], [TenantId], [AccountId], [NominatedBankAccountId], [Amount], [Currency], [Reference], [Status],
                 [RequestedByUserId], [RequestedAt], [DecisionAction], [DecisionByUserId], [DecisionAt], [PayoutTransactionId], [ReturnTransactionId], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_bank, @nba, 300, 'USD', N'REF', 3, @a_adviser, SYSDATETIMEOFFSET(), 0, @a_adviser, SYSDATETIMEOFFSET(), @{p}_buy, NULL, SYSDATETIMEOFFSET(), N'test');
            DECLARE @w int = SCOPE_IDENTITY();
            """),
        ("FK_Withdrawals_Transactions_TenantId_ReturnTransactionId", "Transactions", """
            INSERT INTO [NominatedBankAccounts]
                ([PublicId], [TenantId], [CustomerId], [Currency], [BankName], [AccountName], [AccountNumber], [AccountNumberKey], [Status],
                 [AddedByUserId], [AddedAt], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_customer, 'USD', N'ANZ', N'J Smith', N'0601', N'0601', 0, @a_adviser, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            DECLARE @nba int = SCOPE_IDENTITY();
            INSERT INTO [Withdrawals]
                ([PublicId], [TenantId], [AccountId], [NominatedBankAccountId], [Amount], [Currency], [Reference], [Status],
                 [RequestedByUserId], [RequestedAt], [DecisionAction], [DecisionByUserId], [DecisionAt], [PayoutTransactionId], [ReturnTransactionId], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_bank, @nba, 300, 'USD', N'REF', 7, @a_adviser, SYSDATETIMEOFFSET(), 0, @a_adviser, SYSDATETIMEOFFSET(), @a_buy, @{p}_fx, SYSDATETIMEOFFSET(), N'test');
            DECLARE @w int = SCOPE_IDENTITY();
            """),
        ("FK_OutgoingPayments_Withdrawals_TenantId_WithdrawalId", "Withdrawals", """
            INSERT INTO [ClientMoneyAccounts]
                ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy], [BankConnection])
            VALUES (NEWID(), @a_tenant, 'USD', N'Simulator Bank', N'Trust', CONVERT(nvarchar(34), LEFT(REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''), 30)), 1, SYSDATETIMEOFFSET(), N'test', N'simulator');
            DECLARE @cma int = SCOPE_IDENTITY();
            INSERT INTO [NominatedBankAccounts]
                ([PublicId], [TenantId], [CustomerId], [Currency], [BankName], [AccountName], [AccountNumber], [AccountNumberKey], [Status],
                 [AddedByUserId], [AddedAt], [Created], [CreatedBy])
            VALUES (NEWID(), @{p}_tenant, @{p}_customer, 'USD', N'ANZ', N'J Smith', N'0601', N'0601', 0, @{p}_adviser, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            DECLARE @nba int = SCOPE_IDENTITY();
            INSERT INTO [Withdrawals]
                ([PublicId], [TenantId], [AccountId], [NominatedBankAccountId], [Amount], [Currency], [Reference], [Status],
                 [RequestedByUserId], [RequestedAt], [DecisionAction], [DecisionByUserId], [DecisionAt], [PayoutTransactionId], [ReturnTransactionId], [Created], [CreatedBy])
            VALUES (NEWID(), @{p}_tenant, @{p}_bank, @nba, 300, 'USD', N'REF', 0, @{p}_adviser, SYSDATETIMEOFFSET(), NULL, NULL, NULL, NULL, NULL, SYSDATETIMEOFFSET(), N'test');
            DECLARE @w int = SCOPE_IDENTITY();
            INSERT INTO [OutgoingPayments]
                ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [Kind], [InstructionPublicId], [WithdrawalId], [Amount], [Currency], [Status], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @cma, N'simulator', 0, NEWID(), @w, 300, 'USD', 0, SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_OutgoingPayments_BankStatementLines_TenantId_ReturnOfLineId", "BankStatementLines", """
            INSERT INTO [ClientMoneyAccounts]
                ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy], [BankConnection])
            VALUES (NEWID(), @a_tenant, 'USD', N'Simulator Bank', N'Trust', CONVERT(nvarchar(34), LEFT(REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''), 30)), 1, SYSDATETIMEOFFSET(), N'test', N'simulator');
            DECLARE @cma int = SCOPE_IDENTITY();
            INSERT INTO [ClientMoneyAccounts]
                ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy], [BankConnection])
            VALUES (NEWID(), @{p}_tenant, 'NZD', N'Simulator Bank', N'Trust', CONVERT(nvarchar(34), LEFT(REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''), 30)), 1, SYSDATETIMEOFFSET(), N'test', N'simulator');
            DECLARE @pcma int = SCOPE_IDENTITY();
            INSERT INTO [BankStatementLines]
                ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [OutsideId], [ValueDate], [Direction], [Amount], [Currency],
                 [ReceivedAt], [Status], [Created], [CreatedBy])
            VALUES (NEWID(), @{p}_tenant, @pcma, N'simulator', CONVERT(nvarchar(100), NEWID()), '2026-11-21', 0, 5, 'USD',
                    SYSDATETIMEOFFSET(), 0, SYSDATETIMEOFFSET(), N'test');
            DECLARE @returned int = SCOPE_IDENTITY();
            INSERT INTO [OutgoingPayments]
                ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [Kind], [InstructionPublicId], [ReturnOfLineId], [Amount], [Currency], [Status], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @cma, N'simulator', 1, NEWID(), @returned, 5, 'USD', 0, SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_TransactionPaymentDetails_Transactions_TenantId_TransactionId", "Transactions", """
            INSERT INTO [TransactionPaymentDetails] ([TransactionId], [TenantId], [ValueDate])
            VALUES (@{p}_buy, @a_tenant, '2026-11-20');
            """),
        ("FK_BankStatementLines_BankStatementLines_TenantId_RelatedLineId", "BankStatementLines", """
            INSERT INTO [ClientMoneyAccounts]
                ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy], [BankConnection])
            VALUES (NEWID(), @a_tenant, 'USD', N'Simulator Bank', N'Trust', CONVERT(nvarchar(34), LEFT(REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''), 30)), 1, SYSDATETIMEOFFSET(), N'test', N'simulator');
            DECLARE @cma int = SCOPE_IDENTITY();
            INSERT INTO [ClientMoneyAccounts]
                ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy], [BankConnection])
            VALUES (NEWID(), @{p}_tenant, 'NZD', N'Simulator Bank', N'Trust', CONVERT(nvarchar(34), LEFT(REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''), 30)), 1, SYSDATETIMEOFFSET(), N'test', N'simulator');
            DECLARE @pcma int = SCOPE_IDENTITY();
            INSERT INTO [BankStatementLines]
                ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [OutsideId], [ValueDate], [Direction], [Amount], [Currency],
                 [ReceivedAt], [Status], [RelatedLineId], [WithdrawalId], [Created], [CreatedBy])
            VALUES (NEWID(), @{p}_tenant, @pcma, N'simulator', CONVERT(nvarchar(100), NEWID()), '2026-11-20', 1, 5, 'USD',
                    SYSDATETIMEOFFSET(), 0, NULL, NULL, SYSDATETIMEOFFSET(), N'test');
            DECLARE @returned int = SCOPE_IDENTITY();
            INSERT INTO [BankStatementLines]
                ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [OutsideId], [ValueDate], [Direction], [Amount], [Currency],
                 [ReceivedAt], [Status], [RelatedLineId], [WithdrawalId], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @cma, N'simulator', CONVERT(nvarchar(100), NEWID()), '2026-11-20', 1, 5, 'USD',
                    SYSDATETIMEOFFSET(), 0, @returned, NULL, SYSDATETIMEOFFSET(), N'test');
            DECLARE @line int = SCOPE_IDENTITY();
            """),
        ("FK_BankStatementLines_Withdrawals_TenantId_WithdrawalId", "Withdrawals", """
            INSERT INTO [ClientMoneyAccounts]
                ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy], [BankConnection])
            VALUES (NEWID(), @a_tenant, 'USD', N'Simulator Bank', N'Trust', CONVERT(nvarchar(34), LEFT(REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''), 30)), 1, SYSDATETIMEOFFSET(), N'test', N'simulator');
            DECLARE @cma int = SCOPE_IDENTITY();
            INSERT INTO [NominatedBankAccounts]
                ([PublicId], [TenantId], [CustomerId], [Currency], [BankName], [AccountName], [AccountNumber], [AccountNumberKey], [Status],
                 [AddedByUserId], [AddedAt], [Created], [CreatedBy])
            VALUES (NEWID(), @{p}_tenant, @{p}_customer, 'USD', N'ANZ', N'J Smith', N'0601', N'0601', 0, @{p}_adviser, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            DECLARE @nba int = SCOPE_IDENTITY();
            INSERT INTO [Withdrawals]
                ([PublicId], [TenantId], [AccountId], [NominatedBankAccountId], [Amount], [Currency], [Reference], [Status],
                 [RequestedByUserId], [RequestedAt], [DecisionAction], [DecisionByUserId], [DecisionAt], [PayoutTransactionId], [ReturnTransactionId], [Created], [CreatedBy])
            VALUES (NEWID(), @{p}_tenant, @{p}_bank, @nba, 300, 'USD', N'REF', 0, @{p}_adviser, SYSDATETIMEOFFSET(), NULL, NULL, NULL, NULL, NULL, SYSDATETIMEOFFSET(), N'test');
            DECLARE @w int = SCOPE_IDENTITY();
            INSERT INTO [BankStatementLines]
                ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [OutsideId], [ValueDate], [Direction], [Amount], [Currency],
                 [ReceivedAt], [Status], [RelatedLineId], [WithdrawalId], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @cma, N'simulator', CONVERT(nvarchar(100), NEWID()), '2026-11-20', 1, 5, 'USD',
                    SYSDATETIMEOFFSET(), 0, NULL, @w, SYSDATETIMEOFFSET(), N'test');
            DECLARE @line int = SCOPE_IDENTITY();
            """),
        ("FK_BankReconciliations_Users_TenantId_RanByUserId", "Users", """
            INSERT INTO [ClientMoneyAccounts]
                ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy], [BankConnection])
            VALUES (NEWID(), @a_tenant, 'USD', N'Simulator Bank', N'Trust', CONVERT(nvarchar(34), LEFT(REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''), 30)), 1, SYSDATETIMEOFFSET(), N'test', N'simulator');
            DECLARE @cma int = SCOPE_IDENTITY();
            INSERT INTO [BankReconciliations]
                ([PublicId], [TenantId], [ClientMoneyAccountId], [Currency], [Trigger], [RanAt], [RanByUserId], [Status], [CustodialCash],
                 [BreakCount], [BreakTotal], [TimingCount], [TimingTotal], [KnownCount], [KnownTotal], [ReservedCash],
                 [PaymentsInFlightCount], [PaymentsInFlightAmount], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @cma, 'USD', 1, SYSDATETIMEOFFSET(), @{p}_adviser, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, SYSDATETIMEOFFSET(), N'test');
            DECLARE @rec int = SCOPE_IDENTITY();
            """),
        ("FK_BankReconciliationItems_BankReconciliations_TenantId_ReconciliationId", "BankReconciliations", """
            INSERT INTO [ClientMoneyAccounts]
                ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy], [BankConnection])
            VALUES (NEWID(), @{p}_tenant, 'NZD', N'Simulator Bank', N'Trust', CONVERT(nvarchar(34), LEFT(REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''), 30)), 1, SYSDATETIMEOFFSET(), N'test', N'simulator');
            DECLARE @pcma int = SCOPE_IDENTITY();
            INSERT INTO [BankReconciliations]
                ([PublicId], [TenantId], [ClientMoneyAccountId], [Currency], [Trigger], [RanAt], [RanByUserId], [Status], [CustodialCash],
                 [BreakCount], [BreakTotal], [TimingCount], [TimingTotal], [KnownCount], [KnownTotal], [ReservedCash],
                 [PaymentsInFlightCount], [PaymentsInFlightAmount], [Created], [CreatedBy])
            VALUES (NEWID(), @{p}_tenant, @pcma, 'USD', 0, SYSDATETIMEOFFSET(), NULL, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, SYSDATETIMEOFFSET(), N'test');
            DECLARE @rec int = SCOPE_IDENTITY();
            INSERT INTO [BankReconciliationItems] ([TenantId], [ReconciliationId], [Position], [Kind], [Class], [Amount], [Count])
            VALUES (@a_tenant, @rec, 1, 0, 0, 5, 1);
            """),
        ("FK_BankReconciliationItems_BankStatementLines_TenantId_StatementLineId", "BankStatementLines", """
            INSERT INTO [ClientMoneyAccounts]
                ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy], [BankConnection])
            VALUES (NEWID(), @a_tenant, 'USD', N'Simulator Bank', N'Trust', CONVERT(nvarchar(34), LEFT(REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''), 30)), 1, SYSDATETIMEOFFSET(), N'test', N'simulator');
            DECLARE @cma int = SCOPE_IDENTITY();
            INSERT INTO [BankReconciliations]
                ([PublicId], [TenantId], [ClientMoneyAccountId], [Currency], [Trigger], [RanAt], [RanByUserId], [Status], [CustodialCash],
                 [BreakCount], [BreakTotal], [TimingCount], [TimingTotal], [KnownCount], [KnownTotal], [ReservedCash],
                 [PaymentsInFlightCount], [PaymentsInFlightAmount], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @cma, 'USD', 0, SYSDATETIMEOFFSET(), NULL, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, SYSDATETIMEOFFSET(), N'test');
            DECLARE @rec int = SCOPE_IDENTITY();
            INSERT INTO [ClientMoneyAccounts]
                ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy], [BankConnection])
            VALUES (NEWID(), @{p}_tenant, 'NZD', N'Simulator Bank', N'Trust', CONVERT(nvarchar(34), LEFT(REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''), 30)), 1, SYSDATETIMEOFFSET(), N'test', N'simulator');
            DECLARE @pcma int = SCOPE_IDENTITY();
            INSERT INTO [BankStatementLines]
                ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [OutsideId], [ValueDate], [Direction], [Amount], [Currency],
                 [ReceivedAt], [Status], [Created], [CreatedBy])
            VALUES (NEWID(), @{p}_tenant, @pcma, N'simulator', CONVERT(nvarchar(100), NEWID()), '2026-11-21', 0, 5, 'USD',
                    SYSDATETIMEOFFSET(), 2, SYSDATETIMEOFFSET(), N'test');
            DECLARE @line int = SCOPE_IDENTITY();
            INSERT INTO [BankReconciliationItems]
                ([TenantId], [ReconciliationId], [Position], [Kind], [Class], [Amount], [Count], [StatementLineId], [Direction], [ValueDate], [LineStatus])
            VALUES (@a_tenant, @rec, 1, 2, 0, 5, 1, @line, 0, '2026-11-21', 2);
            """),
        ("FK_AccountClosures_Accounts_TenantId_AccountId", "Accounts", """
            INSERT INTO [AccountClosures]
                ([PublicId], [TenantId], [AccountId], [Status], [Memo], [RequestedByUserId], [RequestedAt], [Currency], [CashBalanceAtRequest],
                 [PayoutAmount], [NominatedBankAccountId], [WithdrawalId], [WriteOffAmount], [WriteOffTransactionId], [CompletedAt], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @{p}_bank, 1, N'Moving', @a_adviser, SYSDATETIMEOFFSET(), 'USD', 0,
                    0, NULL, NULL, NULL, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_AccountClosures_Users_TenantId_RequestedByUserId", "Users", """
            INSERT INTO [AccountClosures]
                ([PublicId], [TenantId], [AccountId], [Status], [Memo], [RequestedByUserId], [RequestedAt], [Currency], [CashBalanceAtRequest],
                 [PayoutAmount], [NominatedBankAccountId], [WithdrawalId], [WriteOffAmount], [WriteOffTransactionId], [CompletedAt], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_bank, 1, N'Moving', @{p}_adviser, SYSDATETIMEOFFSET(), 'USD', 0,
                    0, NULL, NULL, NULL, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_AccountClosures_NominatedBankAccounts_TenantId_NominatedBankAccountId", "NominatedBankAccounts", """
            INSERT INTO [NominatedBankAccounts]
                ([PublicId], [TenantId], [CustomerId], [Currency], [BankName], [AccountName], [AccountNumber], [AccountNumberKey], [Status],
                 [AddedByUserId], [AddedAt], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_customer, 'USD', N'ANZ', N'J Smith', N'0601', N'0601', 0, @a_adviser, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            DECLARE @anba int = SCOPE_IDENTITY();
            INSERT INTO [Withdrawals]
                ([PublicId], [TenantId], [AccountId], [NominatedBankAccountId], [Amount], [Currency], [Reference], [Status],
                 [RequestedByUserId], [RequestedAt], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_bank, @anba, 300, 'USD', N'REF', 0, @a_adviser, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            DECLARE @w int = SCOPE_IDENTITY();
            INSERT INTO [NominatedBankAccounts]
                ([PublicId], [TenantId], [CustomerId], [Currency], [BankName], [AccountName], [AccountNumber], [AccountNumberKey], [Status],
                 [AddedByUserId], [AddedAt], [Created], [CreatedBy])
            VALUES (NEWID(), @{p}_tenant, @{p}_customer, 'USD', N'ANZ', N'J Smith', N'0602', N'0602', 0, @{p}_adviser, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            DECLARE @pnba int = SCOPE_IDENTITY();
            INSERT INTO [AccountClosures]
                ([PublicId], [TenantId], [AccountId], [Status], [Memo], [RequestedByUserId], [RequestedAt], [Currency], [CashBalanceAtRequest],
                 [PayoutAmount], [NominatedBankAccountId], [WithdrawalId], [WriteOffAmount], [WriteOffTransactionId], [CompletedAt], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_bank, 0, N'Moving', @a_adviser, SYSDATETIMEOFFSET(), 'USD', 300,
                    300, @pnba, @w, NULL, NULL, NULL, SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_AccountClosures_Withdrawals_TenantId_WithdrawalId", "Withdrawals", """
            INSERT INTO [NominatedBankAccounts]
                ([PublicId], [TenantId], [CustomerId], [Currency], [BankName], [AccountName], [AccountNumber], [AccountNumberKey], [Status],
                 [AddedByUserId], [AddedAt], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_customer, 'USD', N'ANZ', N'J Smith', N'0601', N'0601', 0, @a_adviser, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            DECLARE @anba int = SCOPE_IDENTITY();
            INSERT INTO [NominatedBankAccounts]
                ([PublicId], [TenantId], [CustomerId], [Currency], [BankName], [AccountName], [AccountNumber], [AccountNumberKey], [Status],
                 [AddedByUserId], [AddedAt], [Created], [CreatedBy])
            VALUES (NEWID(), @{p}_tenant, @{p}_customer, 'USD', N'ANZ', N'J Smith', N'0602', N'0602', 0, @{p}_adviser, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            DECLARE @pnba int = SCOPE_IDENTITY();
            INSERT INTO [Withdrawals]
                ([PublicId], [TenantId], [AccountId], [NominatedBankAccountId], [Amount], [Currency], [Reference], [Status],
                 [RequestedByUserId], [RequestedAt], [Created], [CreatedBy])
            VALUES (NEWID(), @{p}_tenant, @{p}_bank, @pnba, 300, 'USD', N'REF', 0, @{p}_adviser, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            DECLARE @pw int = SCOPE_IDENTITY();
            INSERT INTO [AccountClosures]
                ([PublicId], [TenantId], [AccountId], [Status], [Memo], [RequestedByUserId], [RequestedAt], [Currency], [CashBalanceAtRequest],
                 [PayoutAmount], [NominatedBankAccountId], [WithdrawalId], [WriteOffAmount], [WriteOffTransactionId], [CompletedAt], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_bank, 0, N'Moving', @a_adviser, SYSDATETIMEOFFSET(), 'USD', 300,
                    300, @anba, @pw, NULL, NULL, NULL, SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_AccountClosures_Transactions_TenantId_WriteOffTransactionId", "Transactions", """
            INSERT INTO [AccountClosures]
                ([PublicId], [TenantId], [AccountId], [Status], [Memo], [RequestedByUserId], [RequestedAt], [Currency], [CashBalanceAtRequest],
                 [PayoutAmount], [NominatedBankAccountId], [WithdrawalId], [WriteOffAmount], [WriteOffTransactionId], [CompletedAt], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_bank, 1, N'Moving', @a_adviser, SYSDATETIMEOFFSET(), 'USD', 0.004,
                    0, NULL, NULL, 0.004, @{p}_buy, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_CustomerTaxProfiles_Users_TenantId_UserId", "Users", """
            INSERT INTO [CustomerTaxProfiles]
                ([UserId], [TenantId], [TaxResidency], [IrdNumberHeld], [RwtRatePercent], [UpdatedByUserId], [UpdatedAt], [Created], [CreatedBy])
            VALUES (@{p}_customer, @a_tenant, 0, 0, NULL, @a_adviser, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_CustomerTaxProfiles_Users_TenantId_UpdatedByUserId", "Users", """
            INSERT INTO [CustomerTaxProfiles]
                ([UserId], [TenantId], [TaxResidency], [IrdNumberHeld], [RwtRatePercent], [UpdatedByUserId], [UpdatedAt], [Created], [CreatedBy])
            VALUES (@a_customer, @a_tenant, 0, 0, NULL, @{p}_adviser, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_InterestRateBands_InterestRateSchedules_TenantId_InterestRateScheduleId", "InterestRateSchedules", """
            INSERT INTO [InterestRateSchedules] ([PublicId], [TenantId], [Currency], [EffectiveFrom], [Created], [CreatedBy])
            VALUES (NEWID(), @{p}_tenant, 'USD', '2026-12-01', SYSDATETIMEOFFSET(), N'test');
            DECLARE @{p}_rate int = SCOPE_IDENTITY();
            INSERT INTO [InterestRateBands] ([TenantId], [InterestRateScheduleId], [FromAmount], [RatePercent])
            VALUES (@a_tenant, @{p}_rate, 0, 3);
            """),
        ("FK_InterestAccruals_Accounts_TenantId_AccountId", "Accounts", """
            INSERT INTO [InterestRateSchedules] ([PublicId], [TenantId], [Currency], [EffectiveFrom], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, 'USD', '2026-12-01', SYSDATETIMEOFFSET(), N'test');
            DECLARE @rate int = SCOPE_IDENTITY();
            INSERT INTO [InterestAccruals] ([TenantId], [AccountId], [AccrualDate], [Balance], [InterestRateScheduleId], [EffectiveRatePercent], [Amount])
            VALUES (@a_tenant, @{p}_bank, '2026-12-01', 5000, @rate, 3, 0.41095890);
            """),
        ("FK_InterestAccruals_InterestRateSchedules_TenantId_InterestRateScheduleId", "InterestRateSchedules", """
            INSERT INTO [InterestRateSchedules] ([PublicId], [TenantId], [Currency], [EffectiveFrom], [Created], [CreatedBy])
            VALUES (NEWID(), @{p}_tenant, 'USD', '2026-12-01', SYSDATETIMEOFFSET(), N'test');
            DECLARE @{p}_rate int = SCOPE_IDENTITY();
            INSERT INTO [InterestAccruals] ([TenantId], [AccountId], [AccrualDate], [Balance], [InterestRateScheduleId], [EffectiveRatePercent], [Amount])
            VALUES (@a_tenant, @a_bank, '2026-12-01', 5000, @{p}_rate, 3, 0.41095890);
            """),
        ("FK_InterestAccruals_Transactions_TenantId_PostedTransactionId", "Transactions", """
            INSERT INTO [InterestRateSchedules] ([PublicId], [TenantId], [Currency], [EffectiveFrom], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, 'USD', '2026-12-01', SYSDATETIMEOFFSET(), N'test');
            DECLARE @rate int = SCOPE_IDENTITY();
            INSERT INTO [InterestAccruals]
                ([TenantId], [AccountId], [AccrualDate], [Balance], [InterestRateScheduleId], [EffectiveRatePercent], [Amount], [PostedTransactionId])
            VALUES (@a_tenant, @a_bank, '2026-12-01', 5000, @rate, 3, 0.41095890, @{p}_buy);
            """),
        ("FK_InterestRuns_Accounts_TenantId_AccountId", "Accounts", """
            INSERT INTO [InterestRuns]
                ([PublicId], [TenantId], [AsOfDate], [Trigger], [RanByUserId], [AccountId], [StartedAt], [AccountsProcessed],
                 [AccountsFailed], [PostingCount], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, '2026-12-31', 2, @a_adviser, @{p}_bank, SYSDATETIMEOFFSET(), 1, 0, 1, SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_TransactionTaxDetails_TransactionCashLegs_TenantId_CashLegId", "TransactionCashLegs", """
            INSERT INTO [TransactionCashLegs] ([TenantId], [TransactionId], [AccountId], [Role], [Amount], [Currency], [Created], [CreatedBy])
            VALUES (@{p}_tenant, @{p}_buy, @{p}_bank, 2, -2.16, 'USD', SYSDATETIMEOFFSET(), N'test');
            DECLARE @taxleg int = SCOPE_IDENTITY();
            INSERT INTO [TransactionTaxDetails] ([CashLegId], [TenantId], [TransactionId], [Kind], [RatePercent], [BasisAmount], [TaxAmount], [Currency])
            VALUES (@taxleg, @a_tenant, @a_buy, 0, 17.5, 12.33, 2.16, 'USD');
            """),
        ("FK_TransactionTaxDetails_Transactions_TenantId_TransactionId", "Transactions", """
            INSERT INTO [TransactionCashLegs] ([TenantId], [TransactionId], [AccountId], [Role], [Amount], [Currency], [Created], [CreatedBy])
            VALUES (@a_tenant, @a_buy, @a_bank, 2, -2.16, 'USD', SYSDATETIMEOFFSET(), N'test');
            DECLARE @taxleg int = SCOPE_IDENTITY();
            INSERT INTO [TransactionTaxDetails] ([CashLegId], [TenantId], [TransactionId], [Kind], [RatePercent], [BasisAmount], [TaxAmount], [Currency])
            VALUES (@taxleg, @a_tenant, @{p}_buy, 0, 17.5, 12.33, 2.16, 'USD');
            """),
        ("FK_TransactionInterestDetails_Transactions_TenantId_TransactionId", "Transactions", """
            INSERT INTO [TransactionInterestDetails] ([TransactionId], [TenantId], [PeriodFrom], [PeriodTo], [Days], [AccruedAmount], [GrossAmount])
            VALUES (@{p}_buy, @a_tenant, '2026-12-01', '2026-12-31', 31, 12.32876700, 12.33);
            """),
        ("FK_CorporateActions_Instruments_TenantId_InstrumentId", "Instruments", """
            INSERT INTO [CorporateActions] ([PublicId], [TenantId], [InstrumentId], [Kind], [RatioNew], [RatioHeld], [ExDate], [Origin], [Status], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @{p}_instrument, 0, 2, 1, '2026-12-01', 0, 0, SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_CorporateActions_Users_TenantId_EffectiveByUserId", "Users", """
            INSERT INTO [CorporateActions]
                ([PublicId], [TenantId], [InstrumentId], [Kind], [RatioNew], [RatioHeld], [ExDate], [Origin], [Status], [EffectiveAt], [EffectiveByUserId], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_instrument, 0, 2, 1, '2026-12-01', 0, 1, SYSDATETIMEOFFSET(), @{p}_adviser, SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_CorporateActions_Users_TenantId_CancelledByUserId", "Users", """
            INSERT INTO [CorporateActions]
                ([PublicId], [TenantId], [InstrumentId], [Kind], [RatioNew], [RatioHeld], [ExDate], [Origin], [Status], [CancelledAt], [CancelledByUserId], [CancelNote], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_instrument, 0, 2, 1, '2026-12-01', 0, 3, SYSDATETIMEOFFSET(), @{p}_adviser, N'No', SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_CorporateActionEntitlements_CorporateActions_TenantId_CorporateActionId", "CorporateActions", """
            INSERT INTO [CorporateActions] ([PublicId], [TenantId], [InstrumentId], [Kind], [RatioNew], [RatioHeld], [ExDate], [Origin], [Status], [EffectiveAt], [Created], [CreatedBy])
            VALUES (NEWID(), @{p}_tenant, @{p}_instrument, 0, 2, 1, '2026-12-01', 0, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            DECLARE @{p}_action int = SCOPE_IDENTITY();
            INSERT INTO [CorporateActionEntitlements] ([TenantId], [CorporateActionId], [AccountId], [Status])
            VALUES (@a_tenant, @{p}_action, @a_brokerage, 0);
            """),
        ("FK_CorporateActionEntitlements_Accounts_TenantId_AccountId", "Accounts", """
            INSERT INTO [CorporateActions] ([PublicId], [TenantId], [InstrumentId], [Kind], [RatioNew], [RatioHeld], [ExDate], [Origin], [Status], [EffectiveAt], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_instrument, 0, 2, 1, '2026-12-01', 0, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            DECLARE @action int = SCOPE_IDENTITY();
            INSERT INTO [CorporateActionEntitlements] ([TenantId], [CorporateActionId], [AccountId], [Status])
            VALUES (@a_tenant, @action, @{p}_brokerage, 0);
            """),
        ("FK_CorporateActionEntitlements_Transactions_TenantId_TransactionId", "Transactions", """
            INSERT INTO [CorporateActions] ([PublicId], [TenantId], [InstrumentId], [Kind], [RatioNew], [RatioHeld], [ExDate], [Origin], [Status], [EffectiveAt], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @a_instrument, 0, 2, 1, '2026-12-01', 0, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            DECLARE @action int = SCOPE_IDENTITY();
            INSERT INTO [CorporateActionEntitlements]
                ([TenantId], [CorporateActionId], [AccountId], [Status], [QuantityBefore], [QuantityAfter], [TransactionId], [PostedAt])
            VALUES (@a_tenant, @action, @a_brokerage, 1, 1, 2, @{p}_buy, SYSDATETIMEOFFSET());
            """),
        ("FK_TransactionCorporateActionDetails_Transactions_TenantId_TransactionId", "Transactions", """
            INSERT INTO [TransactionCorporateActionDetails] ([TransactionId], [TenantId], [Kind], [RatioNew], [RatioHeld], [ExDate], [QuantityBefore], [QuantityAfter])
            VALUES (@{p}_buy, @a_tenant, 0, 2, 1, '2026-12-01', 1, 2);
            """),
        ("FK_Orders_Accounts_TenantId_AccountId", "Accounts", OrderRow(account: "@{p}_brokerage")),
        ("FK_Orders_Instruments_TenantId_InstrumentId", "Instruments", OrderRow(instrument: "@{p}_instrument")),
        ("FK_Orders_TradeFeeSchedules_TenantId_TradeFeeScheduleId", "TradeFeeSchedules", OrderRow(schedule: "@{p}_feeschedule")),
        ("FK_Orders_Users_TenantId_PlacedByUserId", "Users", OrderRow(placedBy: "@{p}_adviser")),
        ("FK_Orders_Users_TenantId_CancelRequestedByUserId", "Users", OrderRow(cancelledBy: "@{p}_adviser")),
        ("FK_OrderFills_Orders_TenantId_OrderId", "Orders", OrderRow(tenant: "@{p}_tenant", account: "@{p}_brokerage",
            instrument: "@{p}_instrument", placedBy: "@{p}_adviser") + """
            DECLARE @order int = SCOPE_IDENTITY();
            INSERT INTO [ExecutionReports] ([PublicId], [Connection], [OutsideId], [Kind], [ReceivedAt], [Status])
            VALUES (NEWID(), N'simulator', CONVERT(nvarchar(100), NEWID()), 0, SYSDATETIMEOFFSET(), 0);
            INSERT INTO [OrderFills] ([PublicId], [TenantId], [OrderId], [ExecutionReportId], [Quantity], [Price], [TradeDate], [SettlementDate], [Status])
            VALUES (NEWID(), @a_tenant, @order, SCOPE_IDENTITY(), 4, 100, '2026-11-23', '2026-11-25', 0);
            """),
        ("FK_OrderFills_Transactions_TenantId_TransactionId", "Transactions", OrderRow() + """
            DECLARE @order int = SCOPE_IDENTITY();
            INSERT INTO [ExecutionReports] ([PublicId], [Connection], [OutsideId], [Kind], [ReceivedAt], [Status])
            VALUES (NEWID(), N'simulator', CONVERT(nvarchar(100), NEWID()), 0, SYSDATETIMEOFFSET(), 0);
            INSERT INTO [OrderFills]
                ([PublicId], [TenantId], [OrderId], [ExecutionReportId], [Quantity], [Price], [TradeDate], [SettlementDate], [Status],
                 [TransactionId], [NetCash], [PostedAt])
            VALUES (NEWID(), @a_tenant, @order, SCOPE_IDENTITY(), 4, 100, '2026-11-23', '2026-11-25', 1, @{p}_buy, -400, SYSDATETIMEOFFSET());
            """),
        ("FK_TransactionExecutionDetails_Transactions_TenantId_TransactionId", "Transactions", """
            INSERT INTO [TransactionExecutionDetails] ([TransactionId], [TenantId], [FillPublicId], [Price], [TradeDate], [SettlementDate])
            VALUES (@{p}_buy, @a_tenant, NEWID(), 100, '2026-11-23', '2026-11-25');
            """),
        ("FK_TradeSettlements_ClientMoneyAccounts_TenantId_ClientMoneyAccountId", "ClientMoneyAccounts",
            ClientMoney("@{p}_tenant", "pcma") + Report() + Settlement("@a_tenant", "@pcma")),
        ("FK_TradeSettlements_Users_TenantId_SetAsideByUserId", "Users",
            ClientMoney("@a_tenant", "cma") + Report() + Settlement("@a_tenant", "@cma", setAsideBy: "@{p}_adviser")),
        ("FK_Orders_ClientMoneyAccounts_TenantId_ClientMoneyAccountId", "ClientMoneyAccounts",
            ClientMoney("@{p}_tenant", "pcma") + OrderRow() + """
            UPDATE [Orders] SET [ClientMoneyAccountId] = @pcma, [SettlementAccountNumber] = N'SIM' WHERE [Id] = SCOPE_IDENTITY();
            """),
        ("FK_OrderFills_TradeSettlements_TenantId_TradeSettlementId", "TradeSettlements",
            ClientMoney("@{p}_tenant", "pcma") + Report() + Settlement("@{p}_tenant", "@pcma") + """
            DECLARE @pts int = SCOPE_IDENTITY();
            """ + OrderRow() + """
            DECLARE @order int = SCOPE_IDENTITY();
            """ + Report() + """
            INSERT INTO [OrderFills] ([PublicId], [TenantId], [OrderId], [ExecutionReportId], [Quantity], [Price], [TradeDate], [SettlementDate], [Status], [TradeSettlementId])
            VALUES (NEWID(), @a_tenant, @order, SCOPE_IDENTITY(), 4, 100, '2026-11-23', '2026-11-25', 0, @pts);
            """),
        ("FK_OrderFills_Transactions_TenantId_ReversalTransactionId", "Transactions",
            BrokenFill(outcome: "0", reversal: "@{p}_buy", holdReason: "NULL", resolvedBy: "NULL")),
        ("FK_OrderFills_Users_TenantId_BreakResolvedByUserId", "Users",
            BrokenFill(outcome: "3", reversal: "NULL", holdReason: "1", resolvedBy: "@{p}_adviser")),
        ("FK_OutgoingPayments_TradeSettlements_TenantId_TradeSettlementId", "TradeSettlements",
            ClientMoney("@a_tenant", "cma") + ClientMoney("@{p}_tenant", "pcma", "NZD") + Report() + Settlement("@{p}_tenant", "@pcma") + """
            DECLARE @pts int = SCOPE_IDENTITY();
            INSERT INTO [OutgoingPayments]
                ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [Kind], [InstructionPublicId], [TradeSettlementId], [Amount], [Currency], [Status], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @cma, N'simulator', 2, NEWID(), @pts, 5, 'USD', 0, SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_BankStatementLines_TradeSettlements_TenantId_TradeSettlementId", "TradeSettlements",
            ClientMoney("@a_tenant", "cma") + ClientMoney("@{p}_tenant", "pcma", "NZD") + Report() + Settlement("@{p}_tenant", "@pcma") + """
            DECLARE @pts int = SCOPE_IDENTITY();
            INSERT INTO [BankStatementLines]
                ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [OutsideId], [ValueDate], [Direction], [Amount], [Currency],
                 [ReceivedAt], [Status], [MatchMethod], [MatchedAt], [TradeSettlementId], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @cma, N'simulator', CONVERT(nvarchar(100), NEWID()), '2026-11-25', 1, 5, 'USD',
                    SYSDATETIMEOFFSET(), 1, 5, SYSDATETIMEOFFSET(), @pts, SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_Rebalances_Accounts_TenantId_AccountId", "Accounts", Assignment("a") + RebalanceRow(account: "@{p}_brokerage")),
        ("FK_Rebalances_Portfolios_TenantId_PortfolioId", "Portfolios", Assignment("a") + RebalanceRow(portfolio: "@{p}_portfolio")),
        ("FK_Rebalances_AccountPortfolioAssignments_TenantId_AssignmentId", "AccountPortfolioAssignments",
            Assignment("{p}") + RebalanceRow(assignment: "@{p}_assignment")),
        ("FK_Rebalances_Users_TenantId_ProposedByUserId", "Users", Assignment("a") + RebalanceRow(proposedBy: "@{p}_adviser")),
        ("FK_Rebalances_Users_TenantId_ConfirmedByUserId", "Users", Assignment("a") + RebalanceRow(confirmedBy: "@{p}_adviser")),
        ("FK_Rebalances_Users_TenantId_CancelRequestedByUserId", "Users", Assignment("a") + RebalanceRow(cancelledBy: "@{p}_adviser")),
        ("FK_RebalanceTargets_Rebalances_TenantId_RebalanceId", "Rebalances", Assignment("{p}") + RebalanceRow(
            tenant: "@{p}_tenant", account: "@{p}_brokerage", portfolio: "@{p}_portfolio", assignment: "@{p}_assignment",
            proposedBy: "NULL") + """
            INSERT INTO [RebalanceTargets] ([TenantId], [RebalanceId], [InstrumentId], [TargetWeight], [SortOrder])
            VALUES (@a_tenant, SCOPE_IDENTITY(), @a_instrument, 100, 0);
            """),
        ("FK_RebalanceTargets_Instruments_TenantId_InstrumentId", "Instruments", Assignment("a") + RebalanceRow() + """
            INSERT INTO [RebalanceTargets] ([TenantId], [RebalanceId], [InstrumentId], [TargetWeight], [SortOrder])
            VALUES (@a_tenant, SCOPE_IDENTITY(), @{p}_instrument, 100, 0);
            """),
        ("FK_RebalanceLines_Rebalances_TenantId_RebalanceId", "Rebalances", Assignment("{p}") + RebalanceRow(
            tenant: "@{p}_tenant", account: "@{p}_brokerage", portfolio: "@{p}_portfolio", assignment: "@{p}_assignment",
            proposedBy: "NULL") + """
            INSERT INTO [RebalanceLines] ([TenantId], [RebalanceId], [InstrumentId], [Quantity], [Weight], [Currency])
            VALUES (@a_tenant, SCOPE_IDENTITY(), @a_instrument, 1, 10, 'USD');
            """),
        ("FK_RebalanceLines_Instruments_TenantId_InstrumentId", "Instruments", Assignment("a") + RebalanceRow() + """
            INSERT INTO [RebalanceLines] ([TenantId], [RebalanceId], [InstrumentId], [Quantity], [Weight], [Currency])
            VALUES (@a_tenant, SCOPE_IDENTITY(), @{p}_instrument, 1, 10, 'USD');
            """),
        ("FK_Orders_Rebalances_TenantId_RebalanceId", "Rebalances", Assignment("{p}") + RebalanceRow(
            tenant: "@{p}_tenant", account: "@{p}_brokerage", portfolio: "@{p}_portfolio", assignment: "@{p}_assignment",
            proposedBy: "NULL") + """
            DECLARE @prebalance int = SCOPE_IDENTITY();
            """ + OrderRow() + """
            UPDATE [Orders] SET [RebalanceId] = @prebalance WHERE [Id] = SCOPE_IDENTITY();
            """),
        ("FK_MigrationBatches_Users_TenantId_SubmittedByUserId", "Users", BatchRow(status: "1", submittedBy: "@{p}_adviser")),
        ("FK_MigrationBatches_Users_TenantId_ReturnedByUserId", "Users", BatchRow(returnedBy: "@{p}_adviser")),
        ("FK_MigrationBatches_Users_TenantId_PostedByUserId", "Users",
            BatchRow(status: "2", submittedBy: "@a_adviser", postedBy: "@{p}_customer")),
        ("FK_MigrationBatches_Users_TenantId_CancelledByUserId", "Users", BatchRow(status: "3", cancelledBy: "@{p}_adviser")),
        ("FK_MigrationBatchControlTotals_MigrationBatches_TenantId_MigrationBatchId", "MigrationBatches",
            BatchRow(tenant: "@{p}_tenant") + """
            INSERT INTO [MigrationBatchControlTotals] ([TenantId], [MigrationBatchId], [Kind], [Currency], [Amount])
            VALUES (@a_tenant, @pbatch, 0, 'USD', 1);
            """),
        ("FK_MigrationBatchControlTotals_Instruments_TenantId_InstrumentId", "Instruments", BatchRow() + """
            INSERT INTO [MigrationBatchControlTotals] ([TenantId], [MigrationBatchId], [Kind], [InstrumentId], [Amount])
            VALUES (@a_tenant, @pbatch, 1, @{p}_instrument, 1);
            """),
        ("FK_MigrationBatchEntries_MigrationBatches_TenantId_MigrationBatchId", "MigrationBatches",
            BatchRow(tenant: "@{p}_tenant") + EntryRow(batch: "@pbatch")),
        ("FK_MigrationBatchEntries_Accounts_TenantId_AccountId", "Accounts", BatchRow() + EntryRow(account: "@{p}_brokerage")),
        ("FK_MigrationBatchEntries_Transactions_TenantId_OpeningTransactionId", "Transactions",
            BatchRow() + EntryRow(opening: "@{p}_buy")),
        ("FK_MigrationBatchEntries_Transactions_TenantId_OpeningHoldingsTransactionId", "Transactions",
            BatchRow() + EntryRow(openingHoldings: "@{p}_buy")),
        ("FK_MigrationBatchHoldings_MigrationBatchEntries_TenantId_MigrationBatchEntryId", "MigrationBatchEntries",
            BatchRow(tenant: "@{p}_tenant") + EntryRow(tenant: "@{p}_tenant", account: "@{p}_brokerage") + """
            INSERT INTO [MigrationBatchHoldings] ([TenantId], [MigrationBatchEntryId], [InstrumentId], [Quantity], [Cost])
            VALUES (@a_tenant, @pentry, @a_instrument, 1, 1);
            """),
        ("FK_MigrationBatchHoldings_Instruments_TenantId_InstrumentId", "Instruments", BatchRow() + EntryRow() + """
            INSERT INTO [MigrationBatchHoldings] ([TenantId], [MigrationBatchEntryId], [InstrumentId], [Quantity], [Cost])
            VALUES (@a_tenant, @pentry, @{p}_instrument, 1, 1);
            """),
        ("FK_TransactionMigrationDetails_Transactions_TenantId_TransactionId", "Transactions", """
            INSERT INTO [TransactionMigrationDetails] ([TransactionId], [TenantId], [AsOfDate], [PreviousProvider])
            VALUES (@{p}_buy, @a_tenant, '2026-11-20', N'Acme Wealth');
            """),
        ("FK_BankStatementLines_MigrationBatches_TenantId_MigrationBatchId", "MigrationBatches",
            ClientMoney("@a_tenant", "cma") + BatchRow(tenant: "@{p}_tenant") + """
            INSERT INTO [BankStatementLines]
                ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [OutsideId], [ValueDate], [Direction], [Amount], [Currency],
                 [ReceivedAt], [Status], [MatchMethod], [MatchedAt], [MigrationBatchId], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @cma, N'simulator', CONVERT(nvarchar(100), NEWID()), '2026-11-23', 0, 5, 'USD',
                    SYSDATETIMEOFFSET(), 1, 6, SYSDATETIMEOFFSET(), @pbatch, SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_CustodianFeedCursors_SafekeepingAccounts_TenantId_SafekeepingAccountId", "SafekeepingAccounts",
            SafekeepingRow("{p}") + """
            INSERT INTO [CustodianFeedCursors] ([SafekeepingAccountId], [TenantId]) VALUES (@{p}_safekeeping, @a_tenant);
            """),
        ("FK_CustodianPositions_SafekeepingAccounts_TenantId_SafekeepingAccountId", "SafekeepingAccounts",
            SafekeepingRow("{p}") + """
            INSERT INTO [CustodianPositions] ([TenantId], [SafekeepingAccountId], [Symbol], [Quantity])
            VALUES (@a_tenant, @{p}_safekeeping, N'VTI', 1);
            """),
        ("FK_InKindTransfers_Accounts_TenantId_AccountId", "Accounts", TransferRow(account: "@{p}_brokerage")),
        ("FK_InKindTransfers_Instruments_TenantId_InstrumentId", "Instruments", TransferRow(instrument: "@{p}_instrument")),
        ("FK_InKindTransfers_SafekeepingAccounts_TenantId_SafekeepingAccountId", "SafekeepingAccounts",
            SafekeepingRow("{p}") + TransferRow(status: "1", safekeeping: "@{p}_safekeeping", decidedBy: "@a_customer")),
        ("FK_InKindTransfers_Transactions_TenantId_TransactionId", "Transactions",
            SafekeepingRow("a") + TransferRow(status: "3", safekeeping: "@a_safekeeping", decidedBy: "@a_customer", transaction: "@{p}_buy")),
        ("FK_InKindTransfers_Users_TenantId_RequestedByUserId", "Users", TransferRow(requestedBy: "@{p}_adviser")),
        ("FK_InKindTransfers_Users_TenantId_DecisionByUserId", "Users", TransferRow(status: "4", decidedBy: "@{p}_customer")),
        ("FK_InKindTransfers_Users_TenantId_SetAsideByUserId", "Users",
            SafekeepingRow("a") + TransferRow(status: "7", safekeeping: "@a_safekeeping", decidedBy: "@a_customer", setAsideBy: "@{p}_customer")),
        ("FK_CustodyMovements_SafekeepingAccounts_TenantId_SafekeepingAccountId", "SafekeepingAccounts",
            SafekeepingRow("{p}") + MovementRow(safekeeping: "@{p}_safekeeping")),
        ("FK_CustodyMovements_Instruments_TenantId_InstrumentId", "Instruments",
            SafekeepingRow("a") + MovementRow(instrument: "@{p}_instrument")),
        ("FK_CustodyMovements_Accounts_TenantId_AccountId", "Accounts",
            SafekeepingRow("a") + TransferRow() + MovementRow(method: "0", account: "@{p}_brokerage", transaction: "@a_buy", transfer: "@transfer")),
        ("FK_CustodyMovements_Transactions_TenantId_TransactionId", "Transactions",
            SafekeepingRow("a") + TransferRow() + MovementRow(method: "0", account: "@a_brokerage", transaction: "@{p}_buy", transfer: "@transfer")),
        ("FK_CustodyMovements_InKindTransfers_TenantId_InKindTransferId", "InKindTransfers",
            SafekeepingRow("a") + TransferRow(tenant: "@{p}_tenant", account: "@{p}_brokerage", instrument: "@{p}_instrument", requestedBy: "@{p}_adviser")
            + MovementRow(method: "0", account: "@a_brokerage", transaction: "@a_buy", transfer: "@transfer")),
        ("FK_CustodyMovements_OrderFills_TenantId_OrderFillId", "OrderFills",
            SafekeepingRow("a") + FillRow("{p}") + MovementRow(method: "1", fill: "@{p}_fill")),
        ("FK_CustodyMovements_CorporateActions_TenantId_CorporateActionId", "CorporateActions", SafekeepingRow("a") + """
            INSERT INTO [CorporateActions] ([PublicId], [TenantId], [InstrumentId], [Kind], [RatioNew], [RatioHeld], [ExDate], [Origin], [Status], [EffectiveAt], [Created], [CreatedBy])
            VALUES (NEWID(), @{p}_tenant, @{p}_instrument, 0, 2, 1, '2026-12-01', 0, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'test');
            DECLARE @paction int = SCOPE_IDENTITY();
            """ + MovementRow(method: "2", corporateAction: "@paction")),
        ("FK_CustodyMovements_Users_TenantId_LastActionByUserId", "Users",
            SafekeepingRow("a") + MovementRow(status: "3", lastActionBy: "@{p}_customer")),
        ("FK_TransactionInKindTransferDetails_Transactions_TenantId_TransactionId", "Transactions", """
            INSERT INTO [TransactionInKindTransferDetails]
                ([TransactionId], [TenantId], [TransferPublicId], [CounterpartyName], [CounterpartyAccountMasked], [ChangesBeneficialOwner],
                 [SettlementDate], [CustodianReference])
            VALUES (@{p}_buy, @a_tenant, NEWID(), N'Harbour Brokers', N'****4172', 0, '2026-11-24', N'w-M1');
            """),
        ("FK_Orders_SafekeepingAccounts_TenantId_SafekeepingAccountId", "SafekeepingAccounts", SafekeepingRow("{p}") + OrderRow() + """
            UPDATE [Orders] SET [SafekeepingAccountId] = @{p}_safekeeping, [SafekeepingAccountNumber] = N'SK-1' WHERE [Id] = SCOPE_IDENTITY();
            """),
        ("FK_CustodianReconciliations_SafekeepingAccounts_TenantId_SafekeepingAccountId", "SafekeepingAccounts",
            SafekeepingRow("{p}") + CustodianRunRow(safekeeping: "@{p}_safekeeping")),
        ("FK_CustodianReconciliations_Users_TenantId_RanByUserId", "Users",
            SafekeepingRow("a") + CustodianRunRow(ranBy: "@{p}_adviser")),
        ("FK_CustodianReconciliationLines_CustodianReconciliations_TenantId_ReconciliationId", "CustodianReconciliations",
            SafekeepingRow("{p}") + CustodianRunRow(tenant: "@{p}_tenant", safekeeping: "@{p}_safekeeping") + """
            INSERT INTO [CustodianReconciliationLines]
                ([TenantId], [ReconciliationId], [Position], [Symbol], [CustodianQuantity], [PlatformQuantity], [Difference],
                 [NotProcessed], [UnsettledTrades], [Suspense], [UnbookedFills], [Unexplained], [Class])
            VALUES (@a_tenant, @crun, 1, N'VTI', 0, 0, 0, 0, 0, 0, 0, 0, 2);
            """),
        ("FK_CustodianReconciliationLines_Instruments_TenantId_InstrumentId", "Instruments",
            SafekeepingRow("a") + CustodianRunRow() + """
            INSERT INTO [CustodianReconciliationLines]
                ([TenantId], [ReconciliationId], [Position], [Symbol], [InstrumentId], [CustodianQuantity], [PlatformQuantity], [Difference],
                 [NotProcessed], [UnsettledTrades], [Suspense], [UnbookedFills], [Unexplained], [Class])
            VALUES (@a_tenant, @crun, 1, N'VTI', @{p}_instrument, 0, 0, 0, 0, 0, 0, 0, 0, 2);
            """),
        ("FK_CorporateActions_BankStatementLines_TenantId_ReceiptLineId", "BankStatementLines",
            ClientMoney("@{p}_tenant", "pcma") + """
            INSERT INTO [BankStatementLines]
                ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [OutsideId], [ValueDate], [Direction], [Amount], [Currency],
                 [ReceivedAt], [Status], [UnmatchedReason], [Created], [CreatedBy])
            VALUES (NEWID(), @{p}_tenant, @pcma, N'simulator', CONVERT(nvarchar(100), NEWID()), '2026-11-30', 0, 103, 'USD',
                    SYSDATETIMEOFFSET(), 2, 0, SYSDATETIMEOFFSET(), N'test');
            DECLARE @pline int = SCOPE_IDENTITY();
            """ + DividendRow(receiptLine: "@pline")),
        ("FK_CorporateActions_SafekeepingAccounts_TenantId_SafekeepingAccountId", "SafekeepingAccounts",
            SafekeepingRow("{p}") + DividendRow(safekeeping: "@{p}_safekeeping")),
        ("FK_CorporateActions_CustodyMovements_TenantId_SharesMovementId", "CustodyMovements", SafekeepingRow("{p}") + """
            INSERT INTO [CustodyMovements]
                ([PublicId], [TenantId], [SafekeepingAccountId], [Connection], [OutsideId], [Direction], [Kind], [Symbol], [Currency],
                 [Quantity], [SettlementDate], [ReceivedAt], [Status], [Created], [CreatedBy])
            VALUES (NEWID(), @{p}_tenant, @{p}_safekeeping, N'simulator', CONVERT(nvarchar(100), NEWID()), 0, 0, N'VTI', 'USD',
                    2.68, '2026-11-30', SYSDATETIMEOFFSET(), 0, SYSDATETIMEOFFSET(), N'test');
            DECLARE @pmovement int = SCOPE_IDENTITY();
            """ + DividendRow(movement: "@pmovement")),
        ("FK_CorporateActions_Users_TenantId_ReleasedByUserId", "Users", DividendRow(releasedBy: "@{p}_adviser")),
        ("FK_TransactionDividendDetails_Transactions_TenantId_TransactionId", "Transactions", DividendDetailRow(transaction: "@{p}_buy")),
        ("FK_TransactionDividendDetails_Instruments_TenantId_InstrumentId", "Instruments", DividendDetailRow(instrument: "@{p}_instrument")),
        ("FK_Accounts_Users_TenantId_ReinvestDividendsChangedByUserId", "Users", """
            UPDATE [Accounts] SET [ReinvestDividendsChangedByUserId] = @{p}_adviser, [ReinvestDividendsChangedAt] = SYSDATETIMEOFFSET()
            WHERE [Id] = @a_brokerage;
            """),
        ("FK_BankStatementLines_CorporateActions_TenantId_CorporateActionId", "CorporateActions",
            ClientMoney("@a_tenant", "cma") + DividendRow(tenant: "@{p}_tenant", instrument: "@{p}_instrument") + """
            DECLARE @pdividend int = SCOPE_IDENTITY();
            INSERT INTO [BankStatementLines]
                ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [OutsideId], [ValueDate], [Direction], [Amount], [Currency],
                 [ReceivedAt], [Status], [MatchMethod], [MatchedAt], [CorporateActionId], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, @cma, N'simulator', CONVERT(nvarchar(100), NEWID()), '2026-11-30', 0, 103, 'USD',
                    SYSDATETIMEOFFSET(), 1, 7, SYSDATETIMEOFFSET(), @pdividend, SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_ReportExecutions_ReportSchedules_TenantId_ScheduleId", "ReportSchedules", """
            INSERT INTO [ReportExecutions] ([PublicId], [ReportId], [ScheduleId], [TenantId], [TriggerKind], [Status], [ParametersJson], [Created], [CreatedBy])
            VALUES (NEWID(), @report, @{p}_schedule, @a_tenant, N'Scheduled', N'Queued', N'{}', SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_ReportExecutions_Users_TenantId_TriggeredByUserId", "Users", """
            INSERT INTO [ReportExecutions] ([PublicId], [ReportId], [TenantId], [TriggerKind], [Status], [ParametersJson], [TriggeredByUserId], [Created], [CreatedBy])
            VALUES (NEWID(), @report, @a_tenant, N'Manual', N'Queued', N'{}', @{p}_adviser, SYSDATETIMEOFFSET(), N'test');
            """),
        ("FK_AdjustmentRequests_Accounts_TenantId_AccountId", "Accounts", AdjustmentRequestRow(account: "@{p}_brokerage")),
        ("FK_AdjustmentRequests_Instruments_TenantId_InstrumentId", "Instruments",
            AdjustmentRequestRow(instrument: "@{p}_instrument")),
        ("FK_AdjustmentRequests_Transactions_TenantId_TransactionId", "Transactions",
            AdjustmentRequestRow(status: "1", decidedBy: "@a_customer", transaction: "@{p}_buy")),
        ("FK_AdjustmentRequests_Users_TenantId_RequestedByUserId", "Users", AdjustmentRequestRow(requestedBy: "@{p}_adviser")),
        ("FK_AdjustmentRequests_Users_TenantId_DecisionByUserId", "Users",
            AdjustmentRequestRow(status: "2", decidedBy: "@{p}_customer"))
    ];

    /// <summary>An adjustment request of tenant <c>a</c> (script 0058); one argument names a row of the other tenant.</summary>
    private static string AdjustmentRequestRow(
        string account = "@a_brokerage",
        string instrument = "NULL",
        string status = "0",
        string requestedBy = "@a_adviser",
        string decidedBy = "NULL",
        string transaction = "NULL") => $"""
        INSERT INTO [AdjustmentRequests]
            ([PublicId], [TenantId], [AccountId], [Reason], [Memo], [CashAmount], [InstrumentId], [QuantityChange], [CostChange],
             [Status], [RequestedByUserId], [RequestedAt], [DecisionAction], [DecisionByUserId], [DecisionAt], [TransactionId],
             [Created], [CreatedBy])
        VALUES (NEWID(), @a_tenant, {account}, 0, N'Correction', -5, {instrument},
                {(instrument == "NULL" ? "NULL" : "1")}, {(instrument == "NULL" ? "NULL" : "0")},
                {status}, {requestedBy}, SYSDATETIMEOFFSET(), {(status == "0" ? "NULL" : status)}, {decidedBy},
                {(decidedBy == "NULL" ? "NULL" : "SYSDATETIMEOFFSET()")}, {transaction}, SYSDATETIMEOFFSET(), N'test');

        """;

    private static readonly string[] ReplacedForeignKeys =
    [
        "FK_Users_Users_AdviserId",
        "FK_Accounts_Users_CustomerId",
        "FK_Holdings_Accounts_AccountId",
        "FK_Holdings_Instruments_InstrumentId",
        "FK_Transactions_Transactions_OriginalTransactionId",
        "FK_Transactions_Transactions_ReversedByTransactionId",
        "FK_TransactionCashLegs_Transactions_TransactionId",
        "FK_TransactionCashLegs_Accounts_AccountId",
        "FK_TransactionSecurityLegs_Transactions_TransactionId",
        "FK_TransactionSecurityLegs_Accounts_AccountId",
        "FK_TransactionSecurityLegs_Instruments_InstrumentId",
        "FK_TransactionFxDetails_Transactions_TransactionId",
        "FK_PortfolioLines_Portfolios_PortfolioId",
        "FK_PortfolioLines_Instruments_InstrumentId",
        "FK_AccountPortfolioAssignments_Accounts_AccountId",
        "FK_AccountPortfolioAssignments_Portfolios_PortfolioId",
        "FK_ReportExecutions_ReportSchedules_ScheduleId",
        "FK_ReportExecutions_Users_TriggeredByUserId"
    ];

    private static readonly string[] Parents =
        ["Users", "Accounts", "Instruments", "Transactions", "Portfolios", "ReportSchedules", "TradeFeeSchedules", "TransactionCashLegs", "NominatedBankAccounts", "Withdrawals", "BankStatementLines", "BankReconciliations", "InterestRateSchedules", "CorporateActions", "Orders", "ClientMoneyAccounts", "TradeSettlements", "AccountPortfolioAssignments", "Rebalances", "MigrationBatches", "MigrationBatchEntries", "SafekeepingAccounts", "OrderFills", "InKindTransfers", "CustodyMovements", "CustodianReconciliations", "AdjustmentRequests"];

    /// <summary>A safekeeping account of tenant <paramref name="prefix"/> in <c>@{prefix}_safekeeping</c> (script 0055).</summary>
    /// <summary>An effective Manual dividend; the optional columns point at the parent under test.</summary>
    private static string DividendRow(
        string tenant = "@a_tenant",
        string instrument = "@a_instrument",
        string receiptLine = "NULL",
        string safekeeping = "NULL",
        string movement = "NULL",
        string releasedBy = "NULL") => $"""
        INSERT INTO [CorporateActions]
            ([PublicId], [TenantId], [InstrumentId], [Kind], [AmountPerShare], [TaxSource], [ImputationCreditPerShare],
             [ForeignTaxRatePercent], [ExDate], [Origin], [Status], [EffectiveAt], [ReceiptLineId], [ReceivedAmount],
             [ElectionStatus], [SafekeepingAccountId], [SharesMovementId], [SharesReceived], [ReleasedByUserId], [ReleasedAt],
             [Created], [CreatedBy])
        VALUES (NEWID(), {tenant}, {instrument}, 3, 0.72, 0, 0, 0, '2026-12-01', 0, 1, SYSDATETIMEOFFSET(), {receiptLine},
                {(receiptLine == "NULL" ? "NULL" : "103")}, {(safekeeping == "NULL" && movement == "NULL" ? "NULL" : "2")},
                {safekeeping}, {movement}, {(movement == "NULL" ? "NULL" : "2.68")}, {releasedBy},
                {(releasedBy == "NULL" ? "NULL" : "SYSDATETIMEOFFSET()")}, SYSDATETIMEOFFSET(), N'test');

        """;

    private static string DividendDetailRow(string transaction = "@a_buy", string instrument = "@a_instrument") => $"""
        INSERT INTO [TransactionDividendDetails]
            ([TransactionId], [TenantId], [InstrumentId], [Quantity], [AmountPerShare], [Currency], [TaxSource], [ImputationCredit], [ExDate])
        VALUES ({transaction}, @a_tenant, {instrument}, 100, 0.72, 'USD', 0, 28, '2026-12-01');

        """;

    private static string SafekeepingRow(string prefix) => $"""
        INSERT INTO [SafekeepingAccounts]
            ([PublicId], [TenantId], [Currency], [CustodianName], [AccountName], [AccountNumber], [CustodianConnection], [IsActive], [Created], [CreatedBy])
        VALUES (NEWID(), @{prefix}_tenant, 'USD', N'Simulator Custody', N'Nominees', N'SK-' + LEFT(CONVERT(nvarchar(36), NEWID()), 12),
                N'simulator', 0, SYSDATETIMEOFFSET(), N'test');
        DECLARE @{prefix}_safekeeping int = SCOPE_IDENTITY();

        """;

    /// <summary>An in-kind transfer Out of tenant <c>a</c> in <c>@transfer</c> (script 0055); one argument names a row of
    /// the other tenant.</summary>
    private static string TransferRow(
        string tenant = "@a_tenant",
        string account = "@a_brokerage",
        string instrument = "@a_instrument",
        string status = "0",
        string requestedBy = "@a_adviser",
        string decidedBy = "NULL",
        string safekeeping = "NULL",
        string transaction = "NULL",
        string setAsideBy = "NULL") => $"""
        INSERT INTO [InKindTransfers]
            ([PublicId], [TenantId], [AccountId], [InstrumentId], [Direction], [Quantity], [Currency], [ChangesBeneficialOwner],
             [CounterpartyName], [CounterpartyAccount], [CounterpartyHolderName], [Status], [RequestedByUserId], [RequestedAt],
             [DecisionAction], [DecisionByUserId], [DecisionAt], [SafekeepingAccountId], [Attempts], [TransactionId], [CompletedAt],
             [SetAsideByUserId], [SetAsideAt], [SetAsideNote], [Created], [CreatedBy])
        VALUES (NEWID(), {tenant}, {account}, {instrument}, 1, 4, 'USD', 0,
                N'Harbour Brokers', N'HB-0044172', N'J Smith', {status}, {requestedBy}, SYSDATETIMEOFFSET(),
                {(decidedBy == "NULL" ? "NULL" : status == "4" ? "1" : "0")}, {decidedBy}, {(decidedBy == "NULL" ? "NULL" : "SYSDATETIMEOFFSET()")},
                {safekeeping}, 1, {transaction}, {(transaction == "NULL" ? "NULL" : "SYSDATETIMEOFFSET()")},
                {setAsideBy}, {(setAsideBy == "NULL" ? "NULL" : "SYSDATETIMEOFFSET()")}, {(setAsideBy == "NULL" ? "NULL" : "N'Never executed'")},
                SYSDATETIMEOFFSET(), N'test');
        DECLARE @transfer int = SCOPE_IDENTITY();

        """;

    /// <summary>An order of tenant <paramref name="prefix"/> with one fill in <c>@{prefix}_fill</c> (scripts 0051, 0055).</summary>
    private static string FillRow(string prefix) => OrderRow(tenant: $"@{prefix}_tenant", account: $"@{prefix}_brokerage",
        instrument: $"@{prefix}_instrument", placedBy: $"@{prefix}_adviser") + $"""
        DECLARE @{prefix}_order int = SCOPE_IDENTITY();
        INSERT INTO [ExecutionReports] ([PublicId], [Connection], [OutsideId], [Kind], [ReceivedAt], [Status])
        VALUES (NEWID(), N'simulator', CONVERT(nvarchar(100), NEWID()), 0, SYSDATETIMEOFFSET(), 0);
        INSERT INTO [OrderFills] ([PublicId], [TenantId], [OrderId], [ExecutionReportId], [Quantity], [Price], [TradeDate], [SettlementDate], [Status])
        VALUES (NEWID(), @{prefix}_tenant, @{prefix}_order, SCOPE_IDENTITY(), 4, 100, '2026-11-23', '2026-11-25', 0);
        DECLARE @{prefix}_fill bigint = SCOPE_IDENTITY();

        """;

    /// <summary>A custody movement of tenant <c>a</c> on <c>@a_safekeeping</c> (script 0055); one argument names a row of
    /// the other tenant. <paramref name="method"/> sets <c>Matched</c> with that method.</summary>
    private static string MovementRow(
        string safekeeping = "@a_safekeeping",
        string instrument = "@a_instrument",
        string status = "0",
        string method = "NULL",
        string account = "NULL",
        string transaction = "NULL",
        string transfer = "NULL",
        string fill = "NULL",
        string corporateAction = "NULL",
        string lastActionBy = "NULL")
    {
        var stored = method == "NULL" ? status : "1";
        var reason = stored is "2" or "3" ? "0" : "NULL";
        return $"""
            INSERT INTO [CustodyMovements]
                ([PublicId], [TenantId], [SafekeepingAccountId], [Connection], [OutsideId], [Direction], [Kind], [Symbol], [Currency],
                 [InstrumentId], [Quantity], [SettlementDate], [ReceivedAt], [Status], [MatchMethod], [UnmatchedReason], [AccountId],
                 [TransactionId], [InKindTransferId], [OrderFillId], [CorporateActionId], [LastAction], [LastActionByUserId], [LastActionAt],
                 [LastActionNote], [Created], [CreatedBy])
            VALUES (NEWID(), @a_tenant, {safekeeping}, N'simulator', CONVERT(nvarchar(100), NEWID()), 0, 0, N'VTI', 'USD',
                    {instrument}, 4, '2026-11-24', SYSDATETIMEOFFSET(), {stored}, {method}, {reason}, {account},
                    {transaction}, {transfer}, {fill}, {corporateAction}, {(lastActionBy == "NULL" ? "NULL" : "0")}, {lastActionBy},
                    {(lastActionBy == "NULL" ? "NULL" : "SYSDATETIMEOFFSET()")}, {(lastActionBy == "NULL" ? "NULL" : "N'Dealt with'")},
                    SYSDATETIMEOFFSET(), N'test');

            """;
    }

    /// <summary>A Manual custodian reconciliation run in <c>@crun</c> (script 0056).</summary>
    private static string CustodianRunRow(
        string tenant = "@a_tenant", string safekeeping = "@a_safekeeping", string ranBy = "@a_adviser") => $"""
        INSERT INTO [CustodianReconciliations]
            ([PublicId], [TenantId], [SafekeepingAccountId], [Currency], [Trigger], [RanAt], [RanByUserId], [Status], [PositionsAt],
             [BreakCount], [TimingCount], [AgreedCount], [TransfersInFlight], [FillsNotDelivered], [Created], [CreatedBy])
        VALUES (NEWID(), {tenant}, {safekeeping}, 'USD', 1, SYSDATETIMEOFFSET(), {ranBy}, 0, SYSDATETIMEOFFSET(), 0, 0, 1, 0, 0,
                SYSDATETIMEOFFSET(), N'test');
        DECLARE @crun int = SCOPE_IDENTITY();

        """;

    /// <summary>An order of tenant <c>a</c> (script 0051); one argument names a row of the other tenant.</summary>
    private static string OrderRow(
        string tenant = "@a_tenant",
        string account = "@a_brokerage",
        string instrument = "@a_instrument",
        string schedule = "NULL",
        string placedBy = "@a_adviser",
        string cancelledBy = "NULL") => $"""
        INSERT INTO [Orders]
            ([PublicId], [TenantId], [AccountId], [InstrumentId], [Side], [Type], [TimeInForce], [Quantity], [Currency],
             [ReferencePrice], [ReferencePriceDate], [PriceLimit], [TradeFeeScheduleId], [Connection], [Status], [FilledQuantity],
             [PostedGross], [FeeCharged], [ReservedCash], [ReservedQuantity], [PlacedByUserId], [PlacedAt], [Attempts],
             [CancelRequestedAt], [CancelRequestedByUserId], [Created], [CreatedBy])
        VALUES (NEWID(), {tenant}, {account}, {instrument}, 0, 0, 0, 10, 'USD',
                100, '2026-11-23', 105, {schedule}, N'simulator', 0, 0,
                0, 0, 0, 0, {placedBy}, SYSDATETIMEOFFSET(), 0,
                {(cancelledBy == "NULL" ? "NULL" : "SYSDATETIMEOFFSET()")}, {cancelledBy}, SYSDATETIMEOFFSET(), N'test');

        """;

    /// <summary>An Active assignment of the portfolio of <c>@{prefix}_*</c> on its brokerage account, in
    /// <c>@{prefix}_assignment</c> (script 0053 gives the table its tenant key).</summary>
    private static string Assignment(string prefix) => $"""
        INSERT INTO [AccountPortfolioAssignments] ([PublicId], [TenantId], [AccountId], [PortfolioId], [Status], [AssignedAt], [AssignedBy], [Created], [CreatedBy])
        VALUES (NEWID(), @{prefix}_tenant, @{prefix}_brokerage, @{prefix}_portfolio, 0, SYSDATETIMEOFFSET(), N'test', SYSDATETIMEOFFSET(), N'test');
        DECLARE @{prefix}_assignment int = SCOPE_IDENTITY();

        """;

    /// <summary>A migration batch in <c>@pbatch</c> (script 0054); a person column names a row of the other tenant.</summary>
    private static string BatchRow(
        string tenant = "@a_tenant",
        string status = "0",
        string submittedBy = "NULL",
        string returnedBy = "NULL",
        string postedBy = "NULL",
        string cancelledBy = "NULL") => $"""
        INSERT INTO [MigrationBatches]
            ([PublicId], [TenantId], [Status], [Name], [PreviousProvider], [AsOfDate], [ControlAccountCount],
             [SubmittedByUserId], [SubmittedAt], [ReturnedByUserId], [ReturnedAt], [ReturnNote], [PostedByUserId], [PostedAt],
             [CancelledByUserId], [CancelledAt], [CancelNote], [Created], [CreatedBy])
        VALUES (NEWID(), {tenant}, {status}, N'Acme', N'Acme Wealth', '2026-11-20', 1,
                {submittedBy}, {(submittedBy == "NULL" ? "NULL" : "SYSDATETIMEOFFSET()")},
                {returnedBy}, {(returnedBy == "NULL" ? "NULL" : "SYSDATETIMEOFFSET()")}, {(returnedBy == "NULL" ? "NULL" : "N'Note'")},
                {postedBy}, {(postedBy == "NULL" ? "NULL" : "SYSDATETIMEOFFSET()")},
                {cancelledBy}, {(cancelledBy == "NULL" ? "NULL" : "SYSDATETIMEOFFSET()")}, {(cancelledBy == "NULL" ? "NULL" : "N'Note'")},
                SYSDATETIMEOFFSET(), N'test');
        DECLARE @pbatch int = SCOPE_IDENTITY();

        """;

    /// <summary>An entry of <c>@pbatch</c> in <c>@pentry</c> (script 0054).</summary>
    private static string EntryRow(
        string tenant = "@a_tenant",
        string batch = "@pbatch",
        string account = "@a_brokerage",
        string opening = "NULL",
        string openingHoldings = "NULL") => $"""
        INSERT INTO [MigrationBatchEntries]
            ([TenantId], [MigrationBatchId], [Position], [AccountId], [CashAmount], [OpeningTransactionId], [OpeningHoldingsTransactionId])
        VALUES ({tenant}, {batch}, 0, {account}, 5, {opening}, {openingHoldings});
        DECLARE @pentry int = SCOPE_IDENTITY();

        """;

    /// <summary>A <c>Proposed</c> rebalance (script 0053).</summary>
    private static string RebalanceRow(
        string tenant = "@a_tenant",
        string account = "@a_brokerage",
        string portfolio = "@a_portfolio",
        string assignment = "@a_assignment",
        string proposedBy = "@a_adviser",
        string confirmedBy = "NULL",
        string cancelledBy = "NULL") => $"""
        INSERT INTO [Rebalances]
            ([PublicId], [TenantId], [AccountId], [PortfolioId], [AssignmentId], [Trigger], [Status], [BaseCurrency],
             [MinimumTrade], [CashResidualPercent], [Waves], [BuyWaves], [ProposedByUserId], [ProposedAt], [ExpiresAt],
             [ConfirmedByUserId], [ConfirmedAt], [CancelRequestedAt], [CancelRequestedByUserId], [Created], [CreatedBy])
        VALUES (NEWID(), {tenant}, {account}, {portfolio}, {assignment}, 1, 0, 'NZD',
                100, 1, 0, 0, {proposedBy}, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(),
                {confirmedBy}, {(confirmedBy == "NULL" ? "NULL" : "SYSDATETIMEOFFSET()")},
                {(cancelledBy == "NULL" ? "NULL" : "SYSDATETIMEOFFSET()")}, {cancelledBy}, SYSDATETIMEOFFSET(), N'test');

        """;

    /// <summary>A USD client money account of <paramref name="tenant"/> in <c>@{variable}</c> (script 0038).</summary>
    private static string ClientMoney(string tenant, string variable, string currency = "USD") => $"""
        INSERT INTO [ClientMoneyAccounts]
            ([PublicId], [TenantId], [Currency], [BankName], [AccountName], [AccountNumber], [IsActive], [Created], [CreatedBy], [BankConnection])
        VALUES (NEWID(), {tenant}, '{currency}', N'Simulator Bank', N'Trust', CONVERT(nvarchar(34), LEFT(REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''), 30)), 1, SYSDATETIMEOFFSET(), N'test', N'simulator');
        DECLARE @{variable} int = SCOPE_IDENTITY();

        """;

    /// <summary>An execution report, platform data; its Id is <c>SCOPE_IDENTITY()</c>.</summary>
    private static string Report() => """
        INSERT INTO [ExecutionReports] ([PublicId], [Connection], [OutsideId], [Kind], [ReceivedAt], [Status])
        VALUES (NEWID(), N'simulator', CONVERT(nvarchar(100), NEWID()), 5, SYSDATETIMEOFFSET(), 0);

        """;

    /// <summary>A trade settlement (script 0052) from the report just inserted.</summary>
    private static string Settlement(string tenant, string clientMoney, string setAsideBy = "NULL") => $"""
        INSERT INTO [TradeSettlements]
            ([PublicId], [TenantId], [ClientMoneyAccountId], [Connection], [BrokerSettlementId], [Currency], [SettledDate],
             [FillCount], [NetAmount], [BrokerFillCount], [BrokerNetAmount], [BrokerSettlementAccount], [ExecutionReportId],
             [Status], [SetAsideByUserId], [SetAsideAt], [SetAsideNote], [Created], [CreatedBy])
        VALUES (NEWID(), {tenant}, {clientMoney}, N'simulator', CONVERT(nvarchar(100), NEWID()), 'USD', '2026-11-25',
                1, -5, 1, -5, N'SIM', SCOPE_IDENTITY(), {(setAsideBy == "NULL" ? "0" : "5")}, {setAsideBy},
                {(setAsideBy == "NULL" ? "NULL" : "SYSDATETIMEOFFSET()")}, {(setAsideBy == "NULL" ? "NULL" : "N'Note'")},
                SYSDATETIMEOFFSET(), N'test');

        """;

    /// <summary>A broken posted fill of an order of tenant <c>a</c> (script 0052).</summary>
    private static string BrokenFill(string outcome, string reversal, string holdReason, string resolvedBy) => OrderRow() + $"""
        DECLARE @order int = SCOPE_IDENTITY();
        {Report()}
        DECLARE @fillReport bigint = SCOPE_IDENTITY();
        {Report()}
        INSERT INTO [OrderFills]
            ([PublicId], [TenantId], [OrderId], [ExecutionReportId], [Quantity], [Price], [TradeDate], [SettlementDate], [Status],
             [TransactionId], [NetCash], [PostedAt], [BrokenAt], [BreakReportId], [BreakOutcome], [BreakHoldReason],
             [ReversalTransactionId], [BreakResolvedByUserId], [BreakResolvedAt], [BreakNote])
        VALUES (NEWID(), @a_tenant, @order, @fillReport, 4, 100, '2026-11-23', '2026-11-25', 2,
                @a_buy, -400, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), SCOPE_IDENTITY(), {outcome}, {holdReason},
                {reversal}, {resolvedBy}, {(resolvedBy == "NULL" ? "NULL" : "SYSDATETIMEOFFSET()")},
                {(resolvedBy == "NULL" ? "NULL" : "N'Corrected'")});

        """;

    private static IEnumerable<TestCaseData> CompositeKeyCases() =>
        CompositeKeys.Select(key => new TestCaseData(key.ForeignKey).SetName($"Insert_ChildPointingAtOtherTenant_IsRejected({key.ForeignKey})"));

    private static IEnumerable<TestCaseData> SameTenantCases() =>
        CompositeKeys.Select(key => new TestCaseData(key.ForeignKey).SetName($"Insert_ChildPointingAtSameTenant_IsAccepted({key.ForeignKey})"));

    [Test]
    public void SchemaDirectory_Includes0028TenantCompositeKeysScript()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(TenantCompositeKeysScript);
    }

    [TestCaseSource(nameof(CompositeKeyCases))]
    public async Task Insert_ChildPointingAtOtherTenant_IsRejected(string foreignKey)
    {
        var exception = await Should.ThrowAsync<SqlException>(() => RunInRolledBackTransaction(foreignKey, "b"));

        exception.Number.ShouldBe(547);
        exception.Message.ShouldContain(foreignKey);
    }

    [TestCaseSource(nameof(SameTenantCases))]
    public async Task Insert_ChildPointingAtSameTenant_IsAccepted(string foreignKey)
    {
        await RunInRolledBackTransaction(foreignKey, "a");
    }

    [Test]
    public async Task Apply_EveryCompositeFkHasTheExpectedColumnsParentAndRestrict()
    {
        var rows = await ScratchSchemaDatabase.ReadAsync(SchemaDatabaseSetup.ConnectionString, """
            SELECT fk.[name], OBJECT_NAME(fk.[referenced_object_id]), fk.[delete_referential_action],
                   STRING_AGG(COL_NAME(fkc.[parent_object_id], fkc.[parent_column_id]), N',')
                       WITHIN GROUP (ORDER BY fkc.[constraint_column_id]),
                   STRING_AGG(COL_NAME(fkc.[referenced_object_id], fkc.[referenced_column_id]), N',')
                       WITHIN GROUP (ORDER BY fkc.[constraint_column_id])
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.[constraint_object_id] = fk.[object_id]
            WHERE fk.[name] LIKE N'FK[_]%[_]TenantId[_]%'
            GROUP BY fk.[name], fk.[referenced_object_id], fk.[delete_referential_action]
            ORDER BY fk.[name]
            """);

        var expected = CompositeKeys
            .OrderBy(key => key.ForeignKey, StringComparer.Ordinal)
            .Select(key => (key.ForeignKey, key.Parent, (byte)0, ChildColumns(key.ForeignKey), "TenantId,Id"))
            .ToList();
        rows.Select(row => ((string)row[0], (string)row[1], (byte)row[2], (string)row[3], (string)row[4]))
            .OrderBy(row => row.Item1, StringComparer.Ordinal)
            .ShouldBe(expected);
    }

    [Test]
    public async Task Apply_EveryParentHasAUniqueTenantIdIdKey()
    {
        var rows = await ScratchSchemaDatabase.ReadAsync(SchemaDatabaseSetup.ConnectionString, """
            SELECT kc.[name],
                   STRING_AGG(COL_NAME(ic.[object_id], ic.[column_id]), N',') WITHIN GROUP (ORDER BY ic.[key_ordinal])
            FROM sys.key_constraints kc
            JOIN sys.index_columns ic ON ic.[object_id] = kc.[parent_object_id] AND ic.[index_id] = kc.[unique_index_id]
            WHERE kc.[type] = 'UQ' AND kc.[name] LIKE N'UQ[_]%[_]TenantId[_]Id'
            GROUP BY kc.[name]
            ORDER BY kc.[name]
            """);

        rows.Select(row => ((string)row[0], (string)row[1]))
            .OrderBy(row => row.Item1, StringComparer.Ordinal)
            .ShouldBe(Parents
                .Select(parent => ($"UQ_{parent}_TenantId_Id", "TenantId,Id"))
                .OrderBy(key => key.Item1, StringComparer.Ordinal));
    }

    [Test]
    public async Task Apply_NoReplacedSingleColumnFkRemains()
    {
        var rows = await ScratchSchemaDatabase.ReadAsync(SchemaDatabaseSetup.ConnectionString,
            "SELECT [name] FROM sys.foreign_keys ORDER BY [name]");

        rows.Select(row => (string)row[0]).ShouldNotContain(name => ReplacedForeignKeys.Contains(name));
    }

    [Test]
    public async Task Apply0028_WithACrossTenantRow_ThrowsAndChangesNothing()
    {
        new FileSchemaScriptSource(ScratchSchemaDatabase.SchemaDirectory()).GetScripts()
            .Select(script => script.Name)
            .ShouldContain(TenantCompositeKeysScript);

        await using var db = await ScratchSchemaDatabase.CreateAsync("TenantCompositeKeysPrecheck");
        await db.ApplyBeforeAsync(TenantCompositeKeysScript);
        await db.ExecuteAsync(
            ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12], custodyMode: false, transactionSource: false)
            + ScratchSchemaDatabase.TenantRows("b", Guid.NewGuid().ToString("N")[..12], custodyMode: false, transactionSource: false)
            + """
              INSERT INTO [Holdings] ([PublicId], [TenantId], [AccountId], [InstrumentId], [Quantity], [CostAmount], [CostCurrency], [Created], [CreatedBy])
              VALUES (NEWID(), @a_tenant, @b_brokerage, @a_instrument, 1, 10, 'USD', SYSDATETIMEOFFSET(), N'test');
              """);

        var exception = await Should.ThrowAsync<SqlException>(db.ApplyAllAsync);

        exception.Message.ShouldContain("FK_Holdings_Accounts_TenantId_AccountId=1");
        var foreignKeys = (await db.ReadAsync("SELECT [name] FROM sys.foreign_keys"))
            .Select(row => (string)row[0])
            .ToList();
        foreignKeys.ShouldContain("FK_Holdings_Accounts_AccountId");
        foreignKeys.ShouldNotContain(name => name.Contains("_TenantId_"));
        (await db.ReadAsync("SELECT [name] FROM sys.key_constraints WHERE [name] LIKE N'UQ[_]%[_]TenantId[_]Id'"))
            .ShouldBeEmpty();
        (await db.ReadAsync($"SELECT 1 FROM [SchemaVersions] WHERE [ScriptName] = N'{TenantCompositeKeysScript}'"))
            .ShouldBeEmpty();
    }

    private static string ChildColumns(string foreignKey) =>
        "TenantId," + foreignKey[(foreignKey.LastIndexOf("_TenantId_", StringComparison.Ordinal) + "_TenantId_".Length)..];

    private static async Task RunInRolledBackTransaction(string foreignKey, string parentPrefix)
    {
        var statement = CompositeKeys.Single(key => key.ForeignKey == foreignKey).Statement
            .Replace("{p}", parentPrefix, StringComparison.Ordinal);

        await using var connection = new SqlConnection(SchemaDatabaseSetup.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                ScratchSchemaDatabase.TenantRows("a", Guid.NewGuid().ToString("N")[..12])
                + ScratchSchemaDatabase.TenantRows("b", Guid.NewGuid().ToString("N")[..12])
                + ScratchSchemaDatabase.FeeRows("a")
                + ScratchSchemaDatabase.FeeRows("b")
                + $"""
                   DECLARE @identity nvarchar(450) = CONVERT(nvarchar(450), NEWID());
                   DECLARE @report int = (SELECT [Id] FROM [Reports] WHERE [Code] = N'adviser-balances');
                   {statement}
                   """;
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
