import { configureStore } from "@reduxjs/toolkit";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { sessionSlice, setCurrentUser, type CurrentUser } from "@/features/session/sessionSlice";
import { TransactionsListPage } from "@/features/transactions/TransactionsListPage";
import { api } from "@/shared/api/api";

const transactionId = "dddddddd-dddd-dddd-dddd-dddddddddddd";
const accountId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
const customerId = "cccccccccccccccc-cccc-cccc-cccc-cccccccccccc";

const transaction = {
  id: transactionId,
  tenantId: "22222222-2222-2222-2222-222222222222",
  accountId,
  customerId,
  type: "Buy",
  cash: { amount: -1250, currency: "NZD" },
  bookedAt: "2026-10-11T10:00:00+13:00",
  memo: "Parcel",
  reference: null,
  originalTransactionId: null,
  rowVersion: "AAAA",
  security: [
    {
      instrumentId: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
      quantity: 50,
      cost: 750,
      costCurrency: "USD",
    },
  ],
};

function user(overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    name: "Pat Admin",
    email: "pat@north.example",
    role: "tenantAdmin",
    status: "active",
    tenantId: transaction.tenantId,
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

function renderList(me: CurrentUser, path = "/transactions", status = 200) {
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
    new Response(
      status === 200
        ? JSON.stringify({ items: [transaction], page: 1, pageSize: 20, totalCount: 1 })
        : "",
      {
        status,
        headers: status === 200 ? { "Content-Type": "application/json" } : undefined,
      },
    ),
  );

  const router = createMemoryRouter(
    [
      { path: "/transactions", element: <TransactionsListPage /> },
      { path: "/forbidden", element: <h1>Forbidden</h1> },
    ],
    { initialEntries: [path] },
  );

  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );

  return { fetchMock, router };
}

describe("TransactionsListPage", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: "/transactions",
      search: "",
      href: "http://localhost:3000/transactions",
      assign: vi.fn(),
    });
  });

  it("lists activity from GET /transactions without tenantId", async () => {
    const { fetchMock } = renderList(user());

    expect(await screen.findByText("Buy")).toBeInTheDocument();
    expect(screen.getByText("2026-10-11T10:00:00+13:00")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: accountId })).toHaveAttribute("href", `/accounts/${accountId}`);
    expect(screen.getByRole("link", { name: customerId })).toHaveAttribute("href", `/customers/${customerId}`);
    expect(screen.getByText("-1250 NZD")).toBeInTheDocument();
    expect(screen.getByText("50 750 USD")).toBeInTheDocument();
    expect(screen.getByText("Parcel")).toBeInTheDocument();
    expect(screen.getByText("1 / 1")).toBeInTheDocument();
    expect(document.querySelector('input[type="password"]')).toBeNull();

    const request = fetchMock.mock.calls[0][0] as Request;
    const url = new URL(request.url);
    expect(url.origin + url.pathname).toBe("http://localhost:3000/api/transactions");
    expect(url.searchParams.get("page")).toBe("1");
    expect(url.searchParams.get("pageSize")).toBe("20");
    expect(url.searchParams.has("tenantId")).toBe(false);
    expect(url.searchParams.has("accountId")).toBe(false);
    expect(url.searchParams.has("customerId")).toBe(false);
    expect(request.method).toBe("GET");
    expect(request.headers.get("Authorization")).toBeNull();
    expect(request.credentials).toBe("include");
    expect(requestedUrls(fetchMock).some((item) => item.includes("/connect/"))).toBe(false);
  });

  it("uses the same unscoped list for an Adviser", async () => {
    const { fetchMock } = renderList(user({ role: "adviser", name: "Sam Reed" }));

    expect(await screen.findByText("Buy")).toBeInTheDocument();
    const url = new URL((fetchMock.mock.calls[0][0] as Request).url);
    expect(url.pathname).toBe("/api/transactions");
    expect(url.searchParams.has("tenantId")).toBe(false);
  });

  it("forwards accountId and customerId from the URL and resets page when a filter changes", async () => {
    const { fetchMock, router } = renderList(
      user(),
      `/transactions?accountId=${accountId}&customerId=${customerId}&page=2`,
    );

    expect(await screen.findByText("Buy")).toBeInTheDocument();
    expect(screen.getByLabelText("Account")).toHaveValue(accountId);
    expect(screen.getByLabelText("Customer")).toHaveValue(customerId);

    const first = new URL((fetchMock.mock.calls[0][0] as Request).url);
    expect(first.pathname).toBe("/api/transactions");
    expect(first.searchParams.get("accountId")).toBe(accountId);
    expect(first.searchParams.get("customerId")).toBe(customerId);
    expect(first.searchParams.get("page")).toBe("2");
    expect(first.searchParams.get("pageSize")).toBe("20");
    expect(first.searchParams.has("tenantId")).toBe(false);

    const nextAccount = "ffffffff-ffff-ffff-ffff-ffffffffffff";
    fireEvent.change(screen.getByLabelText("Account"), { target: { value: nextAccount } });

    await waitFor(() => {
      expect(router.state.location.pathname).toBe("/transactions");
      expect(router.state.location.search).toContain(`accountId=${nextAccount}`);
      expect(router.state.location.search).toContain("page=1");
      expect(router.state.location.search).toContain(`customerId=${customerId}`);
    });

    const refetched = requestedUrls(fetchMock)
      .map((item) => new URL(item))
      .find((url) => url.searchParams.get("accountId") === nextAccount);
    expect(refetched?.pathname).toBe("/api/transactions");
    expect(refetched?.searchParams.get("page")).toBe("1");
    expect(refetched?.searchParams.get("customerId")).toBe(customerId);
    expect(refetched?.searchParams.has("tenantId")).toBe(false);
  });

  it("offers Reverse on a posted Buy and not on a reversal", async () => {
    const reversalId = "99999999-9999-9999-9999-999999999999";
    const store = configureStore({
      reducer: {
        session: sessionSlice.reducer,
        [api.reducerPath]: api.reducer,
      },
      middleware: (getDefault) => getDefault().concat(api.middleware),
    });
    store.dispatch(setCurrentUser(user()));
    vi.mocked(fetch).mockImplementation(async () =>
      new Response(
        JSON.stringify({
          items: [
            transaction,
            {
              ...transaction,
              id: reversalId,
              type: "Reversal",
              originalTransactionId: transactionId,
            },
          ],
          page: 1,
          pageSize: 20,
          totalCount: 2,
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      ),
    );

    const router = createMemoryRouter(
      [{ path: "/transactions", element: <TransactionsListPage /> }],
      { initialEntries: ["/transactions"] },
    );
    render(
      <Provider store={store}>
        <RouterProvider router={router} />
      </Provider>,
    );

    expect(await screen.findByRole("button", { name: "Reverse" })).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: "Reverse" })).toHaveLength(1);
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("stays on /transactions when the filtered list is not found", async () => {
    const { router } = renderList(user(), `/transactions?accountId=${accountId}`, 404);

    expect(await screen.findByText("Not found")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Transactions" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Forbidden" })).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/transactions");
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });
});
