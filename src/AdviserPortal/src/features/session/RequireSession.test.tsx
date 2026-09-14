import { configureStore } from "@reduxjs/toolkit";
import { render, screen, waitFor } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { RequireSession } from "@/features/session/RequireSession";
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

function user(overrides: Partial<CurrentUser>): CurrentUser {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    name: "Pat Admin",
    email: "pat@north.example",
    role: "tenantAdmin",
    status: "active",
    tenantId: "22222222-2222-2222-2222-222222222222",
    tenantCode: "north-advisory",
    adviserId: null,
    rowVersion: "AAAA",
    ...overrides,
  };
}

function renderSession(me: CurrentUser, path = "/") {
  const store = configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });
  store.dispatch(setTokens({ accessToken: "access-1", refreshToken: "refresh-1" }));

  const fetchMock = vi.mocked(fetch);
  fetchMock.mockImplementation(async (input) => {
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

  const router = createMemoryRouter(
    [
      {
        element: <RequireSession />,
        children: [
          { path: "/", element: <p>home</p> },
          { path: "/forbidden", element: <p>forbidden</p> },
        ],
      },
    ],
    { initialEntries: [path] },
  );

  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );

  return { fetchMock, store };
}

function requestedUrls(fetchMock: ReturnType<typeof vi.mocked<typeof fetch>>) {
  return fetchMock.mock.calls.map(([input]) => {
    if (typeof input === "string") {
      return input;
    }
    if (input instanceof Request) {
      return input.url;
    }
    return String(input);
  });
}

describe("RequireSession", () => {
  beforeEach(() => {
    startAuthorize.mockReset();
    sessionStorage.clear();
    vi.stubEnv("VITE_WEBAPI_BASE_URL", "https://webapi.test");
    vi.stubGlobal("fetch", vi.fn());
  });

  it("loads GET /tenants/by-code/{code} for a TenantAdmin with a tenant", async () => {
    const { fetchMock } = renderSession(user({ role: "tenantAdmin" }));

    expect(await screen.findByText("home")).toBeInTheDocument();
    await waitFor(() => {
      expect(requestedUrls(fetchMock)).toContain(
        "https://webapi.test/tenants/by-code/north-advisory",
      );
    });
  });

  it("loads GET /tenants/by-code/{code} for an Adviser with a tenant", async () => {
    const { fetchMock } = renderSession(user({ role: "adviser", name: "Sam Reed" }));

    expect(await screen.findByText("home")).toBeInTheDocument();
    await waitFor(() => {
      expect(requestedUrls(fetchMock)).toContain(
        "https://webapi.test/tenants/by-code/north-advisory",
      );
    });
  });

  it("does not call by-code for SystemAdmin", async () => {
    const { fetchMock } = renderSession(
      user({
        role: "systemAdmin",
        name: "System Admin",
        tenantId: null,
        tenantCode: null,
      }),
    );

    expect(await screen.findByText("home")).toBeInTheDocument();
    expect(requestedUrls(fetchMock).some((url) => url.includes("/tenants/by-code/"))).toBe(
      false,
    );
  });

  it("starts authorize when there is no access token", () => {
    const store = configureStore({
      reducer: {
        session: sessionSlice.reducer,
        [api.reducerPath]: api.reducer,
      },
      middleware: (getDefault) => getDefault().concat(api.middleware),
    });

    const router = createMemoryRouter(
      [
        {
          element: <RequireSession />,
          children: [{ path: "/customers", element: <p>customers</p> }],
        },
      ],
      { initialEntries: ["/customers"] },
    );

    render(
      <Provider store={store}>
        <RouterProvider router={router} />
      </Provider>,
    );

    expect(startAuthorize).toHaveBeenCalledOnce();
    expect(screen.queryByText("customers")).not.toBeInTheDocument();
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("sends a leftover Customer session to /forbidden and does not call by-code", async () => {
    const { fetchMock } = renderSession(user({ role: "customer", name: "Jordan Lee" }));

    expect(await screen.findByText("forbidden")).toBeInTheDocument();
    expect(screen.queryByText("home")).not.toBeInTheDocument();
    expect(requestedUrls(fetchMock).some((url) => url.includes("/tenants/by-code/"))).toBe(
      false,
    );
  });
});
