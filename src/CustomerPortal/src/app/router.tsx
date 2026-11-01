import { createBrowserRouter, type RouteObject } from "react-router";
import { AccountDetailPage } from "@/features/accounts/AccountDetailPage";
import { AccountsListPage } from "@/features/accounts/AccountsListPage";
import { DashboardPage } from "@/features/dashboard/DashboardPage";
import { ProfilePage } from "@/features/profile/ProfilePage";
import { ReportsPage } from "@/features/reports/ReportsPage";
import { ForbiddenPage } from "@/features/session/ForbiddenPage";
import { HomePage } from "@/features/session/HomePage";
import { RequireSession } from "@/features/session/RequireSession";
import { RoleGate } from "@/features/session/RoleGate";
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
            element: <RoleGate><DashboardPage /></RoleGate>,
          },
          {
            path: "/accounts",
            element: <RoleGate><AccountsListPage /></RoleGate>,
          },
          {
            path: "/accounts/:id",
            element: <RoleGate><AccountDetailPage /></RoleGate>,
          },
          {
            path: "/reports",
            element: <RoleGate><ReportsPage /></RoleGate>,
          },
          {
            path: "/profile",
            element: <RoleGate><ProfilePage /></RoleGate>,
          },
          { path: "/forbidden", element: <ForbiddenPage /> },
        ],
      },
    ],
  },
];

export const router = createBrowserRouter(appRoutes);
