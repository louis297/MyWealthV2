import { createBrowserRouter, type RouteObject } from "react-router";
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
            children: [{ index: true, element: <p>profile</p> }],
          },
          {
            path: "/customers",
            element: <RoleGate allow={[roles.tenantAdmin, roles.adviser]} />,
            children: [
              { index: true, element: <p>customers</p> },
              { path: "new", element: <p>new customer</p> },
              { path: ":id", element: <p>customer detail</p> },
              { path: ":id/edit", element: <p>edit customer</p> },
            ],
          },
          {
            path: "/advisers",
            element: <RoleGate allow={[roles.tenantAdmin]} />,
            children: [
              { index: true, element: <p>advisers</p> },
              { path: "new", element: <p>new adviser</p> },
              { path: ":id", element: <p>adviser detail</p> },
              { path: ":id/edit", element: <p>edit adviser</p> },
            ],
          },
        ],
      },
    ],
  },
];

export const router = createBrowserRouter(appRoutes);
