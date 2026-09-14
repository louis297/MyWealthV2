import { configureStore } from "@reduxjs/toolkit";
import { render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { appRoutes } from "@/app/router";
import { sessionSlice, setTokens, type CurrentUser } from "@/features/session/sessionSlice";
import { api } from "@/shared/api/api";

const startAuthorize = vi.fn();

vi.mock("@/features/session/oidc", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/session/oidc")>();
  return {
    ...actual,
    startAuthorize: () => startAuthorize(),
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
  store.dispatch(setTokens({ accessToken: "access-1", refreshToken: "refresh-1" }));

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
    startAuthorize.mockReset();
    sessionStorage.clear();
    vi.stubEnv("VITE_WEBAPI_BASE_URL", "https://webapi.test");
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
});
