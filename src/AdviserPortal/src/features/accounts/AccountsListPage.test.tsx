import { configureStore } from "@reduxjs/toolkit";
import { render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AccountsListPage } from "@/features/accounts/AccountsListPage";
import { sessionSlice, setCurrentUser, type CurrentUser } from "@/features/session/sessionSlice";
import { api } from "@/shared/api/api";

const accountId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
const customerId = "cccccccccccccccc-cccc-cccc-cccc-cccccccccccc";

const account = {
  id: accountId,
  tenantId: "22222222-2222-2222-2222-222222222222",
  customerId,
  name: "Everyday spending",
  type: "Bank",
  currency: "NZD",
  status: "Open",
  isActive: true,
  cashBalance: 1250.5,
  rowVersion: "AAAA",
};

function user(overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    name: "Pat Admin",
    email: "pat@north.example",
    role: "tenantAdmin",
    status: "active",
    tenantId: account.tenantId,
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

function renderList(me: CurrentUser) {
  const store = configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });
  store.dispatch(setCurrentUser(me));

  const fetchMock = vi.mocked(fetch);
  fetchMock.mockImplementation(async () =>
    new Response(JSON.stringify({ items: [account], page: 1, pageSize: 20, totalCount: 1 }), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    }),
  );

  const router = createMemoryRouter(
    [
      { path: "/accounts", element: <AccountsListPage /> },
      { path: "/accounts/:id", element: <p>account detail</p> },
    ],
    { initialEntries: ["/accounts"] },
  );

  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );

  return { fetchMock };
}

describe("AccountsListPage", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: "/accounts",
      search: "",
      href: "http://localhost:3000/accounts",
      assign: vi.fn(),
    });
  });

  it("lists cash balances from GET /accounts without tenantId", async () => {
    const { fetchMock } = renderList(user());

    expect(await screen.findByRole("link", { name: "Everyday spending" })).toHaveAttribute(
      "href",
      `/accounts/${accountId}`,
    );
    expect(screen.getByText("1250.5")).toBeInTheDocument();
    expect(screen.getByText("Bank")).toBeInTheDocument();
    expect(screen.getByText("NZD")).toBeInTheDocument();
    expect(screen.getByText("Open")).toBeInTheDocument();
    expect(document.querySelector('input[type="password"]')).toBeNull();

    const request = fetchMock.mock.calls[0][0] as Request;
    const url = new URL(request.url);
    expect(url.origin + url.pathname).toBe("http://localhost:3000/api/accounts");
    expect(url.searchParams.get("page")).toBe("1");
    expect(url.searchParams.get("pageSize")).toBe("20");
    expect(url.searchParams.has("tenantId")).toBe(false);
    expect(request.method).toBe("GET");
    expect(request.headers.get("Authorization")).toBeNull();
    expect(request.credentials).toBe("include");
    expect(requestedUrls(fetchMock).some((item) => item.includes("/connect/"))).toBe(false);
  });

  it("uses the same unscoped list for an Adviser", async () => {
    const { fetchMock } = renderList(user({ role: "adviser", name: "Sam Reed" }));

    expect(await screen.findByText("1250.5")).toBeInTheDocument();
    const url = new URL((fetchMock.mock.calls[0][0] as Request).url);
    expect(url.pathname).toBe("/api/accounts");
    expect(url.searchParams.has("tenantId")).toBe(false);
  });
});
