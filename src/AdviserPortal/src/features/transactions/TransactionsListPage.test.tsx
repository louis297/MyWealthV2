import { configureStore } from "@reduxjs/toolkit";
import { render, screen } from "@testing-library/react";
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
    new Response(JSON.stringify({ items: [transaction], page: 1, pageSize: 20, totalCount: 1 }), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    }),
  );

  const router = createMemoryRouter([{ path: "/transactions", element: <TransactionsListPage /> }], {
    initialEntries: ["/transactions"],
  });

  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );

  return { fetchMock };
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
});
