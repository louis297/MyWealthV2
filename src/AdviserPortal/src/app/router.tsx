import { createBrowserRouter, type RouteObject } from "react-router";
import { AdviserDetailPage } from "@/features/advisers/AdviserDetailPage";
import { AdvisersListPage } from "@/features/advisers/AdvisersListPage";
import { CreateAdviserPage } from "@/features/advisers/CreateAdviserPage";
import { EditAdviserPage } from "@/features/advisers/EditAdviserPage";
import { CreateCustomerPage } from "@/features/customers/CreateCustomerPage";
import { CustomerDetailPage } from "@/features/customers/CustomerDetailPage";
import { CustomersListPage } from "@/features/customers/CustomersListPage";
import { EditCustomerPage } from "@/features/customers/EditCustomerPage";
import { ProfilePage } from "@/features/profile/ProfilePage";
import { CallbackPage } from "@/features/session/CallbackPage";
import { ForbiddenPage } from "@/features/session/ForbiddenPage";
import { HomePage } from "@/features/session/HomePage";
import { RequireSession } from "@/features/session/RequireSession";
import { RoleGate } from "@/features/session/RoleGate";
import { roles } from "@/features/session/roles";
import { SessionProbePage } from "@/features/session/SessionProbePage";
import { ShellLayout } from "@/layouts/ShellLayout";

export const appRoutes: RouteObject[] = [
  {
    path: "/callback",
    element: <CallbackPage />,
  },
  {
    element: <RequireSession />,
    children: [
      {
        element: <ShellLayout />,
        children: [
          { path: "/", element: <HomePage /> },
          { path: "/session", element: <SessionProbePage /> },
          { path: "/forbidden", element: <ForbiddenPage /> },
          {
            path: "/profile",
            element: <RoleGate allow={[roles.tenantAdmin, roles.adviser, roles.systemAdmin]} />,
            children: [{ index: true, element: <ProfilePage /> }],
          },
          {
            path: "/customers",
            element: <RoleGate allow={[roles.tenantAdmin, roles.adviser]} />,
            children: [
              { index: true, element: <CustomersListPage /> },
              { path: "new", element: <CreateCustomerPage /> },
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
