using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Common.Security;

public static class RolePermissions
{
    private static readonly Dictionary<UserRole, HashSet<string>> Map = new()
    {
        [UserRole.SystemAdmin] =
        [
            Policies.TenantsManage, Policies.TenantsRead, Policies.TenantAdminsManage, Policies.UsersMe,
            Policies.InstrumentsRead, Policies.InstrumentsCreate, Policies.InstrumentsManage,
            Policies.AccountsRead, Policies.AccountsCreate, Policies.AccountsManage,
            Policies.TransactionsRead, Policies.TransactionsCreate,
            Policies.PortfoliosRead, Policies.PortfoliosManage,
            Policies.FeesRead, Policies.FeesManage,
            Policies.InterestRunsRead, Policies.InterestRunsStart
        ],
        [UserRole.TenantAdmin] =
        [
            Policies.TenantsRead, Policies.AdvisersManage, Policies.CustomersManage, Policies.UsersMe,
            Policies.InstrumentsRead, Policies.InstrumentsCreate, Policies.InstrumentsManage,
            Policies.AccountsRead, Policies.AccountsCreate, Policies.AccountsManage,
            Policies.TransactionsRead, Policies.TransactionsCreate, Policies.TransactionsAdjust,
            Policies.StatementLinesManage, Policies.BankReconciliationsManage,
            Policies.NominatedBankAccountsRead, Policies.NominatedBankAccountsManage, Policies.NominatedBankAccountsVerify,
            Policies.WithdrawalsRead, Policies.WithdrawalsRequest, Policies.WithdrawalsApprove,
            Policies.AccountClosuresRead, Policies.AccountClosuresRequest,
            Policies.PortfoliosRead, Policies.PortfoliosManage,
            Policies.FeesRead, Policies.FeesManage,
            Policies.InterestRatesRead, Policies.InterestRatesManage, Policies.InterestRunsRead,
            Policies.CorporateActionsRead, Policies.CorporateActionsManage,
            Policies.OrdersRead, Policies.OrdersPlace,
            Policies.TradeSettlementsManage,
            Policies.RebalancesRead, Policies.RebalancesConfirm,
            Policies.ReportsCustomerRead, Policies.ReportsCustomerCreate,
            Policies.ReportsBookRead, Policies.ReportsBookCreate,
            Policies.ReportsTenantRead, Policies.ReportsTenantCreate
        ],
        [UserRole.Adviser] =
        [
            Policies.TenantsRead, Policies.CustomersManageOwn, Policies.UsersMe,
            Policies.InstrumentsRead, Policies.InstrumentsCreate,
            Policies.AccountsRead, Policies.AccountsCreate, Policies.AccountsManage,
            Policies.TransactionsRead, Policies.TransactionsCreate,
            Policies.NominatedBankAccountsRead, Policies.NominatedBankAccountsManage,
            Policies.WithdrawalsRead, Policies.WithdrawalsRequest,
            Policies.AccountClosuresRead, Policies.AccountClosuresRequest,
            Policies.PortfoliosRead, Policies.PortfoliosManage,
            Policies.FeesRead,
            Policies.InterestRatesRead,
            Policies.CorporateActionsRead,
            Policies.OrdersRead, Policies.OrdersPlace,
            Policies.RebalancesRead, Policies.RebalancesConfirm,
            Policies.ReportsCustomerRead, Policies.ReportsCustomerCreate,
            Policies.ReportsBookRead, Policies.ReportsBookCreate
        ],
        [UserRole.Customer] =
        [
            Policies.UsersMe,
            Policies.AccountsRead,
            Policies.TransactionsRead,
            Policies.NominatedBankAccountsRead,
            Policies.WithdrawalsRead,
            Policies.AccountClosuresRead,
            Policies.OrdersRead,
            Policies.ReportsCustomerRead,
            Policies.ReportsCustomerCreate
        ]
    };

    public static bool Has(UserRole role, string policy) =>
        Map.TryGetValue(role, out var policies) && policies.Contains(policy);
}
