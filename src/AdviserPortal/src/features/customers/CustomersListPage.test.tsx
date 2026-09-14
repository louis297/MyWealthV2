import { configureStore } from "@reduxjs/toolkit";
import { render, screen, waitFor } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CustomersListPage } from "@/features/customers/CustomersListPage";
import { sessionSlice, setCurrentUser, setTokens, type CurrentUser } from "@/features/session/sessionSlice";
import { api } from "@/shared/api/api";

const startAuthorize = vi.fn();

vi.mock("@/features/session/oidc", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/session/oidc")>();
  return {
    ...actual,
    startAuthorize: () => startAuthorize(),
  };
});

const adviserId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const customerId = "cccccccccccccccc-cccc-cccc-cccc-cccccccccccc";

const customer = {
  id: customerId,
  tenantId: "22222222-2222-2222-2222-222222222222",
  adviserId,
  name: "Jordan Lee",
  email: "jordan@north.example",
  status: "active",
  rowVersion: "AAAA",
  created: "2026-09-14T00:00:00+00:00",
};

const adviser = {
  id: adviserId,
  tenantId: customer.tenantId,
  name: "Sam Reed",
  email: "sam@north.example",
  status: "active",
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
    tenantId: customer.tenantId,
    tenantCode: "north-advisory",
    adviserId: null,
    rowVersion: "AAAA",
    ...overrides,
  };
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

function renderList(me: CurrentUser, path = "/customers") {
  const store = configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });
  store.dispatch(setTokens({ accessToken: "access-1", refreshToken: "refresh-1" }));
  store.dispatch(setCurrentUser(me));

  const fetchMock = vi.mocked(fetch);
  fetchMock.mockImplementation(async (input) => {
    const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
    if (url.includes("/users/advisers")) {
      return new Response(
        JSON.stringify({ items: [adviser], page: 1, pageSize: 100, totalCount: 1 }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    if (url.includes("/users/customers")) {
      return new Response(
        JSON.stringify({ items: [customer], page: 1, pageSize: 20, totalCount: 1 }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    return new Response("", { status: 404 });
  });

  const router = createMemoryRouter(
    [
      { path: "/customers", element: <CustomersListPage /> },
      { path: "/customers/new", element: <p>new customer</p> },
      { path: "/customers/:id", element: <p>customer detail</p> },
    ],
    { initialEntries: [path] },
  );

  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );

  return { fetchMock };
}

describe("CustomersListPage", () => {
  beforeEach(() => {
    startAuthorize.mockReset();
    sessionStorage.clear();
    vi.stubEnv("VITE_WEBAPI_BASE_URL", "https://webapi.test");
    vi.stubGlobal("fetch", vi.fn());
  });

  it("joins adviser names from GET /users/advisers for TenantAdmin", async () => {
    const { fetchMock } = renderList(user());

    expect(await screen.findByText("Jordan Lee")).toBeInTheDocument();
    expect(screen.getByText("jordan@north.example")).toBeInTheDocument();
    expect(screen.getByText("Sam Reed")).toBeInTheDocument();
    expect(screen.getByText("2026-09-14")).toBeInTheDocument();
    expect(screen.queryByText(adviserId)).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Jordan Lee" })).toHaveAttribute(
      "href",
      `/customers/${customerId}`,
    );
    expect(screen.getByRole("link", { name: "New customer" })).toHaveAttribute("href", "/customers/new");

    const urls = requestedUrls(fetchMock);
    expect(urls.some((url) => url.includes("/users/customers"))).toBe(true);
    expect(urls.some((url) => url.includes("/users/advisers"))).toBe(true);
  });

  it("does not request /users/advisers for an Adviser caller", async () => {
    const { fetchMock } = renderList(
      user({
        role: "adviser",
        name: "Sam Reed",
        id: adviserId,
        adviserId: null,
      }),
    );

    expect(await screen.findByText("Jordan Lee")).toBeInTheDocument();
    expect(screen.getByText("Sam Reed")).toBeInTheDocument();
    expect(screen.queryByLabelText("Adviser")).not.toBeInTheDocument();

    const urls = requestedUrls(fetchMock);
    expect(urls.some((url) => url.includes("/users/advisers"))).toBe(false);
  });

  it("reads list filters from the URL for TenantAdmin", async () => {
    const { fetchMock } = renderList(
      user(),
      `/customers?search=jordan&enabledOnly=true&adviserId=${adviserId}&page=2`,
    );

    await screen.findByText("Jordan Lee");

    const customersUrl = requestedUrls(fetchMock).find((url) => url.includes("/users/customers"));
    expect(customersUrl).toBeTruthy();
    const parsed = new URL(customersUrl!);
    expect(parsed.searchParams.get("search")).toBe("jordan");
    expect(parsed.searchParams.get("enabledOnly")).toBe("true");
    expect(parsed.searchParams.get("adviserId")).toBe(adviserId);
    expect(parsed.searchParams.get("page")).toBe("2");
    expect(parsed.searchParams.get("pageSize")).toBe("20");
  });
});
