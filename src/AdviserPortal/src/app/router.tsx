import { createBrowserRouter, Navigate, type RouteObject } from "react-router";
import { AdviserDetailPage } from "@/features/advisers/AdviserDetailPage";
import { AdvisersListPage } from "@/features/advisers/AdvisersListPage";
import { CreateAdviserPage } from "@/features/advisers/CreateAdviserPage";
import { EditAdviserPage } from "@/features/advisers/EditAdviserPage";
import { AccountDetailPage } from "@/features/accounts/AccountDetailPage";
import { AccountsListPage } from "@/features/accounts/AccountsListPage";
import { CreateAccountPage } from "@/features/accounts/CreateAccountPage";
import { CreateCustomerPage } from "@/features/customers/CreateCustomerPage";
import { CustomerDetailPage } from "@/features/customers/CustomerDetailPage";
import { CustomersListPage } from "@/features/customers/CustomersListPage";
import { EditCustomerPage } from "@/features/customers/EditCustomerPage";
import { DashboardPage } from "@/features/dashboard/DashboardPage";
import { CreateTradeSchedulePage } from "@/features/fees/CreateTradeSchedulePage";
import { EditTradeSchedulePage } from "@/features/fees/EditTradeSchedulePage";
import { FeesTradePage } from "@/features/fees/FeesTradePage";
import { TradeScheduleDetailPage } from "@/features/fees/TradeScheduleDetailPage";
import { InterestRateFormPage } from "@/features/interest/InterestRateFormPage";
import { InterestRatesPage } from "@/features/interest/InterestRatesPage";
import { InterestRunPage } from "@/features/interest/InterestRunPage";
import { InterestRunsPage } from "@/features/interest/InterestRunsPage";
import { CreatePortfolioPage } from "@/features/portfolios/CreatePortfolioPage";
import { PortfolioDetailPage } from "@/features/portfolios/PortfolioDetailPage";
import { PortfoliosListPage } from "@/features/portfolios/PortfoliosListPage";
import { ReportsPage } from "@/features/reports/ReportsPage";
import { CreateAdjustmentPage } from "@/features/transactions/CreateAdjustmentPage";
import { CreateFxConversionPage } from "@/features/transactions/CreateFxConversionPage";
import { CreateInternalTransferPage } from "@/features/transactions/CreateInternalTransferPage";
import { CreateTransactionPage } from "@/features/transactions/CreateTransactionPage";
import { TransactionDetailPage } from "@/features/transactions/TransactionDetailPage";
import { TransactionsListPage } from "@/features/transactions/TransactionsListPage";
import { ProfilePage } from "@/features/profile/ProfilePage";
import { ForbiddenPage } from "@/features/session/ForbiddenPage";
import { HomePage } from "@/features/session/HomePage";
import { RequireSession } from "@/features/session/RequireSession";
import { RoleGate } from "@/features/session/RoleGate";
import { roles } from "@/features/session/roles";
import { SessionProbePage } from "@/features/session/SessionProbePage";
import { AddBankAccountPage } from "@/features/nominatedBankAccounts/AddBankAccountPage";
import { BankAccountChecksPage } from "@/features/nominatedBankAccounts/BankAccountChecksPage";
import { WithdrawalDetailPage } from "@/features/withdrawals/WithdrawalDetailPage";
import { WithdrawalsListPage } from "@/features/withdrawals/WithdrawalsListPage";
import { CloseAccountPage } from "@/features/accountClosures/CloseAccountPage";
import { WithdrawPage } from "@/features/withdrawals/WithdrawPage";
import { MatchStatementLinePage } from "@/features/statementLines/MatchStatementLinePage";
import { BankReconciliationDetailPage } from "@/features/bankReconciliations/BankReconciliationDetailPage";
import { BankReconciliationHistoryPage } from "@/features/bankReconciliations/BankReconciliationHistoryPage";
import { BankReconciliationOverviewPage } from "@/features/bankReconciliations/BankReconciliationOverviewPage";
import { StatementLineDetailPage } from "@/features/statementLines/StatementLineDetailPage";
import { StatementLinesListPage } from "@/features/statementLines/StatementLinesListPage";
import { ShellLayout } from "@/layouts/ShellLayout";

export const appRoutes: RouteObject[] = [
  {
    element: <RequireSession />,
    children: [
      {
        element: <ShellLayout />,
        children: [
          { path: "/", element: <HomePage /> },
          {
            path: "/dashboard",
            element: <RoleGate allow={[roles.tenantAdmin, roles.adviser]} />,
            children: [{ index: true, element: <DashboardPage /> }],
          },
          {
            path: "/reports",
            element: <RoleGate allow={[roles.tenantAdmin, roles.adviser]} />,
            children: [{ index: true, element: <ReportsPage /> }],
          },
          {
            path: "/portfolios",
            element: <RoleGate allow={[roles.tenantAdmin, roles.adviser]} />,
            children: [
              { index: true, element: <PortfoliosListPage /> },
              { path: "new", element: <CreatePortfolioPage /> },
              { path: ":id", element: <PortfolioDetailPage /> },
            ],
          },
          {
            path: "/fees",
            element: <RoleGate allow={[roles.tenantAdmin, roles.adviser]} />,
            children: [
              { index: true, element: <Navigate to="/fees/trade" replace /> },
              { path: "trade", element: <FeesTradePage /> },
              { path: "trade/:id", element: <TradeScheduleDetailPage /> },
              {
                element: <RoleGate allow={[roles.tenantAdmin]} />,
                children: [
                  { path: "trade/new", element: <CreateTradeSchedulePage /> },
                  { path: "trade/:id/edit", element: <EditTradeSchedulePage /> },
                ],
              },
            ],
          },
          {
            // interest-run D59, D60: an Adviser reads rates; the forms and the runs are a TenantAdmin's.
            path: "/interest",
            element: <RoleGate allow={[roles.tenantAdmin, roles.adviser]} />,
            children: [
              { index: true, element: <Navigate to="/interest/rates" replace /> },
              { path: "rates", element: <InterestRatesPage /> },
              { path: "runs", element: <InterestRunsPage /> },
              { path: "runs/:id", element: <InterestRunPage /> },
              {
                element: <RoleGate allow={[roles.tenantAdmin]} />,
                children: [
                  { path: "rates/new", element: <InterestRateFormPage /> },
                  { path: "rates/:id/edit", element: <InterestRateFormPage /> },
                ],
              },
            ],
          },
          { path: "/session", element: <SessionProbePage /> },
          { path: "/forbidden", element: <ForbiddenPage /> },
          {
            path: "/profile",
            element: <RoleGate allow={[roles.tenantAdmin, roles.adviser]} />,
            children: [{ index: true, element: <ProfilePage /> }],
          },
          {
            path: "/accounts",
            element: <RoleGate allow={[roles.tenantAdmin, roles.adviser]} />,
            children: [
              { index: true, element: <AccountsListPage /> },
              { path: ":id", element: <AccountDetailPage /> },
              { path: ":id/transactions/new", element: <CreateTransactionPage /> },
              { path: ":id/transfer", element: <CreateInternalTransferPage /> },
              { path: ":id/convert", element: <CreateFxConversionPage /> },
              { path: ":id/withdraw", element: <WithdrawPage /> },
              { path: ":id/close", element: <CloseAccountPage /> },
              {
                element: <RoleGate allow={[roles.tenantAdmin]} />,
                children: [{ path: ":id/adjust", element: <CreateAdjustmentPage /> }],
              },
            ],
          },
          {
            path: "/transactions",
            element: <RoleGate allow={[roles.tenantAdmin, roles.adviser]} />,
            children: [
              { index: true, element: <TransactionsListPage /> },
              { path: ":id", element: <TransactionDetailPage /> },
            ],
          },
          {
            // D34: an Adviser who opens the route gets the firm administrator sentence from the 403.
            path: "/statement-lines",
            element: <RoleGate allow={[roles.tenantAdmin, roles.adviser]} />,
            children: [
              { index: true, element: <StatementLinesListPage /> },
              { path: ":id", element: <StatementLineDetailPage /> },
              { path: ":id/match", element: <MatchStatementLinePage /> },
            ],
          },
          {
            // D51: an Adviser who opens the route gets the firm administrator sentence from the 403.
            path: "/bank-reconciliations",
            element: <RoleGate allow={[roles.tenantAdmin, roles.adviser]} />,
            children: [
              { index: true, element: <BankReconciliationOverviewPage /> },
              { path: "history", element: <BankReconciliationHistoryPage /> },
              { path: ":id", element: <BankReconciliationDetailPage /> },
            ],
          },
          {
            path: "/withdrawals",
            element: <RoleGate allow={[roles.tenantAdmin, roles.adviser]} />,
            children: [
              { index: true, element: <WithdrawalsListPage /> },
              { path: ":id", element: <WithdrawalDetailPage /> },
            ],
          },
          {
            // D42: an Adviser who opens the route gets the firm administrator sentence from the 403.
            path: "/nominated-bank-accounts",
            element: <RoleGate allow={[roles.tenantAdmin, roles.adviser]} />,
            children: [{ index: true, element: <BankAccountChecksPage /> }],
          },
          {
            path: "/customers",
            element: <RoleGate allow={[roles.tenantAdmin, roles.adviser]} />,
            children: [
              { index: true, element: <CustomersListPage /> },
              { path: "new", element: <CreateCustomerPage /> },
              { path: ":id/accounts/new", element: <CreateAccountPage /> },
              { path: ":id/bank-accounts/new", element: <AddBankAccountPage /> },
              { path: ":id", element: <CustomerDetailPage /> },
              { path: ":id/edit", element: <EditCustomerPage /> },
            ],
          },
          {
            path: "/advisers",
            element: <RoleGate allow={[roles.tenantAdmin]} />,
            children: [
              { index: true, element: <AdvisersListPage /> },
              { path: "new", element: <CreateAdviserPage /> },
              { path: ":id", element: <AdviserDetailPage /> },
              { path: ":id/edit", element: <EditAdviserPage /> },
            ],
          },
        ],
      },
    ],
  },
];

export const router = createBrowserRouter(appRoutes);
