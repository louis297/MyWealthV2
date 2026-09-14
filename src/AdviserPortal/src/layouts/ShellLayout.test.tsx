import { configureStore } from "@reduxjs/toolkit";
import { fireEvent, render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

const startEndSession = vi.fn();

vi.mock("@/features/session/oidc", () => ({
  startEndSession: () => startEndSession(),
}));
import {
  sessionSlice,
  setCurrentUser,
  setTenant,
  type CurrentUser,
} from "@/features/session/sessionSlice";
import { ShellLayout } from "@/layouts/ShellLayout";
import { api } from "@/shared/api/api";
import type { Tenant } from "@/shared/types/tenant";

const tenant: Tenant = {
  id: "22222222-2222-2222-2222-222222222222",
  name: "North Advisory",
  code: "north-advisory",
  reportingCurrency: "GBP",
  isEnabled: true,
  rowVersion: "AAAA",
  created: "2026-09-14T00:00:00+00:00",
};

function user(overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    name: "Pat Admin",
    email: "pat@north.example",
    role: "tenantAdmin",
    status: "active",
    tenantId: tenant.id,
    tenantCode: tenant.code,
    adviserId: null,
    rowVersion: "AAAA",
    ...overrides,
  };
}

function renderShell(me: CurrentUser, withTenant = true) {
  const store = configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });
  store.dispatch(setCurrentUser(me));
  if (withTenant && me.tenantCode) {
    store.dispatch(setTenant(tenant));
  }

  const router = createMemoryRouter(
    [
      {
        element: <ShellLayout />,
        children: [
          { path: "/", element: <p>home</p> },
          { path: "/customers", element: <p>customers</p> },
          { path: "/advisers", element: <p>advisers</p> },
          { path: "/profile", element: <p>profile</p> },
          { path: "/session", element: <p>session probe</p> },
        ],
      },
    ],
    { initialEntries: ["/"] },
  );

  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );
}

describe("ShellLayout", () => {
  beforeEach(() => {
    sessionStorage.clear();
    startEndSession.mockReset();
  });

  it("shows the firm Name from by-code and tenantCode as secondary for TenantAdmin", () => {
    renderShell(user());

    expect(screen.getByText("North Advisory")).toBeInTheDocument();
    expect(screen.getByText("north-advisory")).toBeInTheDocument();
    expect(screen.getByText("Pat Admin")).toBeInTheDocument();
    expect(screen.getByText("tenantAdmin")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Customers" })).toHaveAttribute("href", "/customers");
    expect(screen.getByRole("link", { name: "Advisers" })).toHaveAttribute("href", "/advisers");
    expect(screen.getByRole("link", { name: "Profile" })).toHaveAttribute("href", "/profile");
    expect(screen.queryByRole("link", { name: /session/i })).not.toBeInTheDocument();
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("hides Advisers for an Adviser and still shows Customers and Profile", () => {
    renderShell(user({ role: "adviser", name: "Sam Reed" }));

    expect(screen.getByText("North Advisory")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Customers" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Profile" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Advisers" })).not.toBeInTheDocument();
  });

  it("shows Platform for SystemAdmin with Profile only", () => {
    renderShell(
      user({
        role: "systemAdmin",
        name: "System Admin",
        tenantId: null,
        tenantCode: null,
      }),
      false,
    );

    expect(screen.getByText("Platform")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Profile" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Customers" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Advisers" })).not.toBeInTheDocument();
  });

  it("starts end-session when Sign out is clicked", () => {
    renderShell(user());

    fireEvent.click(screen.getByRole("button", { name: "Sign out" }));

    expect(startEndSession).toHaveBeenCalledOnce();
  });
});
