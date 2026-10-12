import { configureStore } from "@reduxjs/toolkit";
import { render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { appRoutes } from "@/app/router";
import { sessionSlice, type CurrentUser } from "@/features/session/sessionSlice";
import { api } from "@/shared/api/api";

const startLogin = vi.fn();

vi.mock("@/features/session/session", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/session/session")>();
  return {
    ...actual,
    startLogin: () => startLogin(),
  };
});

const tenant = {
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

function renderApp(me: CurrentUser, path: string) {
  const store = configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });

  vi.mocked(fetch).mockImplementation(async (input) => {
    const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
    if (url.endsWith("/users/me")) {
      return new Response(JSON.stringify(me), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      });
    }
    if (url.includes("/tenants/by-code/")) {
      return new Response(JSON.stringify(tenant), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      });
    }
    return new Response("", { status: 404 });
  });

  const router = createMemoryRouter(appRoutes, { initialEntries: [path] });
  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );
}

describe("app routes", () => {
  beforeEach(() => {
    startLogin.mockReset();
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
  });

  it("sends an Adviser on /advisers to /forbidden", async () => {
    renderApp(user({ role: "adviser", name: "Sam Reed" }), "/advisers");

    expect(await screen.findByRole("heading", { name: "Forbidden" })).toBeInTheDocument();
    expect(screen.queryByText("advisers")).not.toBeInTheDocument();
  });

  it("sends SystemAdmin on /customers to /forbidden and allows /profile", async () => {
    renderApp(
      user({
        role: "systemAdmin",
        name: "System Admin",
        tenantId: null,
        tenantCode: null,
      }),
      "/customers",
    );

    expect(await screen.findByRole("heading", { name: "Forbidden" })).toBeInTheDocument();
    expect(screen.queryByText("customers")).not.toBeInTheDocument();
  });

  it("allows TenantAdmin on /advisers", async () => {
    renderApp(user(), "/advisers");

    expect(await screen.findByRole("heading", { name: "Advisers" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Forbidden" })).not.toBeInTheDocument();
  });

  it("lets TenantAdmin open /dashboard", async () => {
    renderApp(user(), "/dashboard");

    expect(await screen.findByRole("heading", { name: "Dashboard" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Forbidden" })).not.toBeInTheDocument();
  });

  it("lets an Adviser open /dashboard", async () => {
    renderApp(user({ role: "adviser", name: "Sam Reed" }), "/dashboard");

    expect(await screen.findByRole("heading", { name: "Dashboard" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Forbidden" })).not.toBeInTheDocument();
  });

  it("sends SystemAdmin on /dashboard to /forbidden", async () => {
    renderApp(
      user({
        role: "systemAdmin",
        name: "System Admin",
        tenantId: null,
        tenantCode: null,
      }),
      "/dashboard",
    );

    expect(await screen.findByRole("heading", { name: "Forbidden" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Dashboard" })).not.toBeInTheDocument();
  });

  it("lets TenantAdmin open /accounts", async () => {
    renderApp(user(), "/accounts");

    expect(await screen.findByRole("heading", { name: "Accounts" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Forbidden" })).not.toBeInTheDocument();
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("lets an Adviser open /accounts", async () => {
    renderApp(user({ role: "adviser", name: "Sam Reed" }), "/accounts");

    expect(await screen.findByRole("heading", { name: "Accounts" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Forbidden" })).not.toBeInTheDocument();
  });

  it("sends SystemAdmin on /accounts to /forbidden", async () => {
    renderApp(
      user({
        role: "systemAdmin",
        name: "System Admin",
        tenantId: null,
        tenantCode: null,
      }),
      "/accounts",
    );

    expect(await screen.findByRole("heading", { name: "Forbidden" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Accounts" })).not.toBeInTheDocument();
  });

  it("lets TenantAdmin open an account without treating a missing id as Forbidden", async () => {
    renderApp(user(), "/accounts/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    expect(await screen.findByText("Not found")).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Forbidden" })).not.toBeInTheDocument();
  });

  it("sends SystemAdmin on an account to /forbidden", async () => {
    renderApp(
      user({
        role: "systemAdmin",
        name: "System Admin",
        tenantId: null,
        tenantCode: null,
      }),
      "/accounts/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
    );

    expect(await screen.findByRole("heading", { name: "Forbidden" })).toBeInTheDocument();
    expect(screen.queryByText("Not found")).not.toBeInTheDocument();
  });

  it("sends a Customer on /accounts to /forbidden", async () => {
    renderApp(user({ role: "customer", name: "Jordan Lee" }), "/accounts");

    expect(await screen.findByRole("heading", { name: "Forbidden" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Accounts" })).not.toBeInTheDocument();
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("lets TenantAdmin open /transactions", async () => {
    renderApp(user(), "/transactions");

    expect(await screen.findByRole("heading", { name: "Transactions" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Forbidden" })).not.toBeInTheDocument();
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("lets an Adviser open /transactions", async () => {
    renderApp(user({ role: "adviser", name: "Sam Reed" }), "/transactions");

    expect(await screen.findByRole("heading", { name: "Transactions" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Forbidden" })).not.toBeInTheDocument();
  });

  it("sends SystemAdmin on /transactions to /forbidden", async () => {
    renderApp(
      user({
        role: "systemAdmin",
        name: "System Admin",
        tenantId: null,
        tenantCode: null,
      }),
      "/transactions",
    );

    expect(await screen.findByRole("heading", { name: "Forbidden" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Transactions" })).not.toBeInTheDocument();
  });

  it("sends a Customer on /transactions to /forbidden", async () => {
    renderApp(user({ role: "customer", name: "Jordan Lee" }), "/transactions");

    expect(await screen.findByRole("heading", { name: "Forbidden" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Transactions" })).not.toBeInTheDocument();
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("sends SystemAdmin on / to Profile and not a Customers workspace", async () => {
    renderApp(
      user({
        role: "systemAdmin",
        name: "System Admin",
        tenantId: null,
        tenantCode: null,
      }),
      "/",
    );

    expect(await screen.findByRole("heading", { name: "Profile" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Customers" })).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Dashboard" })).not.toBeInTheDocument();
  });
});
