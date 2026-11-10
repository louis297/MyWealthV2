import { createBrowserRouter, type RouteObject } from "react-router";
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
import { CreatePortfolioPage } from "@/features/portfolios/CreatePortfolioPage";
import { PortfolioDetailPage } from "@/features/portfolios/PortfolioDetailPage";
import { PortfoliosListPage } from "@/features/portfolios/PortfoliosListPage";
import { ReportsPage } from "@/features/reports/ReportsPage";
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
            path: "/customers",
            element: <RoleGate allow={[roles.tenantAdmin, roles.adviser]} />,
            children: [
              { index: true, element: <CustomersListPage /> },
              { path: "new", element: <CreateCustomerPage /> },
              { path: ":id/accounts/new", element: <CreateAccountPage /> },
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
