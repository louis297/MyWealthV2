namespace MyWealthV2.Application.Common.Security;

public static class Policies
{
    public const string TenantsManage = "tenants.manage";
    public const string TenantsRead = "tenants.read";
    public const string TenantAdminsManage = "tenant-admins.manage";
    public const string AdvisersManage = "advisers.manage";
    public const string CustomersManage = "customers.manage";
    public const string CustomersManageOwn = "customers.manage-own";
    public const string UsersMe = "users.me";
    public const string InstrumentsRead = "instruments.read";
    public const string InstrumentsCreate = "instruments.create";
    public const string InstrumentsManage = "instruments.manage";
    public const string AccountsRead = "accounts.read";
    public const string AccountsCreate = "accounts.create";
    public const string AccountsManage = "accounts.manage";
    public const string TransactionsRead = "transactions.read";
    public const string TransactionsCreate = "transactions.create";
    public const string TransactionsAdjust = "transactions.adjust";
    public const string StatementLinesManage = "statement-lines.manage";
    public const string BankReconciliationsManage = "bank-reconciliations.manage";
    public const string NominatedBankAccountsRead = "nominated-bank-accounts.read";
    public const string NominatedBankAccountsManage = "nominated-bank-accounts.manage";
    public const string NominatedBankAccountsVerify = "nominated-bank-accounts.verify";
    public const string WithdrawalsRead = "withdrawals.read";
    public const string WithdrawalsRequest = "withdrawals.request";
    public const string WithdrawalsApprove = "withdrawals.approve";
    public const string AccountClosuresRead = "account-closures.read";
    public const string AccountClosuresRequest = "account-closures.request";
    public const string PortfoliosRead = "portfolios.read";
    public const string PortfoliosManage = "portfolios.manage";
    public const string FeesRead = "fees.read";
    public const string FeesManage = "fees.manage";
    public const string InterestRatesRead = "interest-rates.read";
    public const string InterestRatesManage = "interest-rates.manage";
    public const string InterestRunsRead = "interest-runs.read";
    public const string InterestRunsStart = "interest-runs.start";
    public const string ValuationRunsRead = "valuation-runs.read";
    public const string ValuationRunsStart = "valuation-runs.start";
    public const string CorporateActionsRead = "corporate-actions.read";
    public const string CorporateActionsManage = "corporate-actions.manage";
    public const string OrdersRead = "orders.read";
    public const string OrdersPlace = "orders.place";
    public const string TradeSettlementsManage = "trade-settlements.manage";
    public const string RebalancesRead = "rebalances.read";
    public const string RebalancesConfirm = "rebalances.confirm";
    public const string MigrationBatchesManage = "migration-batches.manage";

    public const string InKindTransfersRead = "in-kind-transfers.read";

    public const string InKindTransfersRequest = "in-kind-transfers.request";

    public const string InKindTransfersApprove = "in-kind-transfers.approve";

    public const string CustodyMovementsManage = "custody-movements.manage";

    public const string CustodianReconciliationsManage = "custodian-reconciliations.manage";
    public const string ReportsCustomerRead = "reports.customer.read";
    public const string ReportsCustomerCreate = "reports.customer.create";
    public const string ReportsBookRead = "reports.book.read";
    public const string ReportsBookCreate = "reports.book.create";
    public const string ReportsTenantRead = "reports.tenant.read";
    public const string ReportsTenantCreate = "reports.tenant.create";
}
