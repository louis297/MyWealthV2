import { configureStore } from "@reduxjs/toolkit";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AccountDetailPage } from "@/features/accounts/AccountDetailPage";
import { CreateTransactionPage } from "@/features/transactions/CreateTransactionPage";
import { sessionSlice, setCurrentUser, type CurrentUser } from "@/features/session/sessionSlice";
import { api } from "@/shared/api/api";

const accountId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
const customerId = "cccccccccccccccc-cccc-cccc-cccc-cccccccccccc";

const account = {
  id: accountId,
  tenantId: "22222222-2222-2222-2222-222222222222",
  customerId,
  name: "Everyday spending",
  type: "Brokerage",
  currency: "NZD",
  status: "Open",
  isActive: true,
  cashBalance: 1250.5,
  rowVersion: "AAAA",
};

const holding = {
  id: "hhhhhhhh-hhhh-hhhh-hhhh-hhhhhhhhhhhh",
  accountId,
  instrumentId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
  symbol: "DEMO",
  name: "Demo Fund",
  quantity: 100,
  cost: 5000,
  costCurrency: "NZD",
};

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

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

function renderDetail(
  fetchImpl: (url: string, method: string) => Response,
) {
  const store = configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });
  store.dispatch(setCurrentUser(user()));

  const fetchMock = vi.mocked(fetch);
  fetchMock.mockImplementation(async (input, init) => {
    const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
    const method = init?.method ?? (input instanceof Request ? input.method : "GET");
    return fetchImpl(url, method);
  });

  const router = createMemoryRouter(
    [
      { path: "/accounts", element: <p>accounts list</p> },
      { path: "/accounts/:id", element: <AccountDetailPage /> },
    ],
    { initialEntries: [`/accounts/${accountId}`] },
  );

  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );

  return { fetchMock, router };
}

describe("AccountDetailPage", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: `/accounts/${accountId}`,
      search: "",
      href: `http://localhost:3000/accounts/${accountId}`,
      assign: vi.fn(),
    });
  });

  it("shows cash balance and holdings for the account", async () => {
    const { fetchMock } = renderDetail((url, method) => {
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [holding] });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json(account);
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByRole("heading", { name: "Everyday spending" })).toBeInTheDocument();
    expect(screen.getByText("1250.5")).toBeInTheDocument();
    expect(screen.getByText("Brokerage")).toBeInTheDocument();
    expect(screen.queryByText("Forbidden")).not.toBeInTheDocument();
    expect(document.querySelector('input[type="password"]')).toBeNull();

    const holdings = screen.getByRole("table", { name: "Holdings" });
    expect(within(holdings).getByText("DEMO")).toBeInTheDocument();
    expect(within(holdings).getByText("Demo Fund")).toBeInTheDocument();
    expect(within(holdings).getByText("100")).toBeInTheDocument();
    expect(within(holdings).getByText("5000")).toBeInTheDocument();
    expect(within(holdings).getByText("NZD")).toBeInTheDocument();

    const requests = fetchMock.mock.calls.map(([input]) => input).filter((input): input is Request => input instanceof Request);
    const accountRequest = requests.find((request) => request.url.endsWith(`/accounts/${accountId}`));
    const holdingsRequest = requests.find((request) => request.url.endsWith(`/accounts/${accountId}/holdings`));
    expect(accountRequest?.url).toBe(`http://localhost:3000/api/accounts/${accountId}`);
    expect(holdingsRequest?.url).toBe(`http://localhost:3000/api/accounts/${accountId}/holdings`);
    for (const request of [accountRequest, holdingsRequest]) {
      expect(request?.method).toBe("GET");
      expect(request?.headers.get("Authorization")).toBeNull();
      expect(request?.credentials).toBe("include");
      expect(new URL(request?.url ?? "").searchParams.has("tenantId")).toBe(false);
    }
  });

  it("posts a Buy and shows the updated cash, quantity, and new row", async () => {
    const instrumentId = holding.instrumentId;
    const createdId = "99999999-9999-9999-9999-999999999999";
    const postedBuy = {
      id: createdId,
      tenantId: account.tenantId,
      accountId,
      customerId,
      type: "Buy",
      cash: { amount: -1250, currency: "NZD" },
      bookedAt: "2026-10-11T10:00:00+13:00",
      memo: null,
      reference: null,
      originalTransactionId: null,
      rowVersion: "BBBB",
      security: [{ instrumentId, quantity: 50, cost: 750, costCurrency: "USD" }],
    };
    let posted = false;

    const store = configureStore({
      reducer: {
        session: sessionSlice.reducer,
        [api.reducerPath]: api.reducer,
      },
      middleware: (getDefault) => getDefault().concat(api.middleware),
    });
    store.dispatch(setCurrentUser(user({ role: "adviser", name: "Sam Reed" })));

    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
      const method = init?.method ?? (input instanceof Request ? input.method : "GET");
      if (url.includes("/instruments") && method === "GET") {
        return json({
          items: [{
            id: instrumentId,
            tenantId: account.tenantId,
            symbol: "DEMO",
            name: "Demo Fund",
            quoteCurrency: "NZD",
            isActive: true,
            rowVersion: "AAAA",
          }],
          page: 1,
          pageSize: 100,
          totalCount: 1,
        });
      }
      if (url.endsWith("/transactions") && method === "POST") {
        posted = true;
        return json({ id: createdId }, 201);
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({
          items: posted ? [postedBuy] : [],
          page: 1,
          pageSize: 20,
          totalCount: posted ? 1 : 0,
        });
      }
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [{ ...holding, quantity: posted ? 150 : 100 }] });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json({ ...account, cashBalance: posted ? 0.5 : 1250.5 });
      }
      return new Response("", { status: 404 });
    });

    const router = createMemoryRouter(
      [
        { path: "/accounts/:id", element: <AccountDetailPage /> },
        { path: "/accounts/:id/transactions/new", element: <CreateTransactionPage /> },
      ],
      { initialEntries: [`/accounts/${accountId}`] },
    );

    render(
      <Provider store={store}>
        <RouterProvider router={router} />
      </Provider>,
    );

    expect(await screen.findByText("1250.5")).toBeInTheDocument();
    expect(screen.getByText("100")).toBeInTheDocument();
    const buy = screen.getByRole("link", { name: "Buy" });
    expect(buy).toHaveAttribute("href", `/accounts/${accountId}/transactions/new?type=Buy`);
    fireEvent.click(buy);

    await screen.findByRole("option", { name: "DEMO Demo Fund" });
    fireEvent.change(screen.getByLabelText("Instrument"), { target: { value: instrumentId } });
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "50" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-11T10:00:00+13:00" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(await screen.findByRole("heading", { name: "Everyday spending" })).toBeInTheDocument();
    expect(await screen.findByRole("link", { name: createdId })).toHaveAttribute(
      "href",
      `/transactions/${createdId}`,
    );
    expect(screen.getByText("0.5")).toBeInTheDocument();
    expect(screen.getByText("150")).toBeInTheDocument();
    expect(screen.queryByText("1250.5")).not.toBeInTheDocument();
    expect(screen.queryByText("100")).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/accounts/${accountId}`);
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("posts a Sell and shows higher cash and lower quantity", async () => {
    const instrumentId = holding.instrumentId;
    const createdId = "99999999-9999-9999-9999-999999999999";
    let posted = false;

    const store = configureStore({
      reducer: {
        session: sessionSlice.reducer,
        [api.reducerPath]: api.reducer,
      },
      middleware: (getDefault) => getDefault().concat(api.middleware),
    });
    store.dispatch(setCurrentUser(user({ role: "adviser", name: "Sam Reed" })));

    const fetchMock = vi.mocked(fetch);
    fetchMock.mockImplementation(async (input, init) => {
      const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
      const method = init?.method ?? (input instanceof Request ? input.method : "GET");
      if (url.includes("/instruments") && method === "GET") {
        return json({
          items: [{
            id: instrumentId,
            tenantId: account.tenantId,
            symbol: "DEMO",
            name: "Demo Fund",
            quoteCurrency: "NZD",
            isActive: true,
            rowVersion: "AAAA",
          }],
          page: 1,
          pageSize: 100,
          totalCount: 1,
        });
      }
      if (url.endsWith("/transactions") && method === "POST") {
        posted = true;
        return json({ id: createdId }, 201);
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({
          items: posted
            ? [{
              id: createdId,
              tenantId: account.tenantId,
              accountId,
              customerId,
              type: "Sell",
              cash: { amount: 1250, currency: "NZD" },
              bookedAt: "2026-10-11T11:00:00+13:00",
              memo: null,
              reference: null,
              originalTransactionId: null,
              rowVersion: "BBBB",
              security: [{ instrumentId, quantity: -40, cost: 2000, costCurrency: "NZD" }],
            }]
            : [],
          page: 1,
          pageSize: 20,
          totalCount: posted ? 1 : 0,
        });
      }
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [{ ...holding, quantity: posted ? 60 : 100, cost: posted ? 3000 : 5000 }] });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json({ ...account, cashBalance: posted ? 2500.5 : 1250.5 });
      }
      return new Response("", { status: 404 });
    });

    const router = createMemoryRouter(
      [
        { path: "/accounts/:id", element: <AccountDetailPage /> },
        { path: "/accounts/:id/transactions/new", element: <CreateTransactionPage /> },
      ],
      { initialEntries: [`/accounts/${accountId}`] },
    );

    render(
      <Provider store={store}>
        <RouterProvider router={router} />
      </Provider>,
    );

    expect(await screen.findByText("1250.5")).toBeInTheDocument();
    expect(screen.getByText("100")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("link", { name: "Sell" }));

    await screen.findByRole("option", { name: "DEMO Demo Fund" });
    fireEvent.change(screen.getByLabelText("Instrument"), { target: { value: instrumentId } });
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "40" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-11T11:00:00+13:00" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(await screen.findByRole("heading", { name: "Everyday spending" })).toBeInTheDocument();
    expect(await screen.findByText("2500.5")).toBeInTheDocument();
    expect(screen.getByText("60")).toBeInTheDocument();
    expect(screen.getByText("3000")).toBeInTheDocument();
    expect(screen.queryByText("1250.5")).not.toBeInTheDocument();
    expect(screen.queryByText("100")).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/accounts/${accountId}`);

    const post = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.method === "POST");
    const body = JSON.parse(await post!.clone().text()) as {
      type: string;
      security: Record<string, unknown>[];
    };
    expect(body.type).toBe("Sell");
    expect(body.security).toEqual([{ instrumentId, quantity: 40 }]);
    expect(body.security[0]).not.toHaveProperty("cost");
    expect(body).not.toHaveProperty("tenantId");
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("shows no holdings when the projection is empty", async () => {
    renderDetail((url, method) => {
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json(account);
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByText("1250.5")).toBeInTheDocument();
    expect(screen.getByText("No holdings.")).toBeInTheDocument();
  });

  it("stays on the URL and shows Not found for a missing account", async () => {
    const { router } = renderDetail(() => new Response("", { status: 404 }));

    expect(await screen.findByText("Not found")).toBeInTheDocument();
    expect(screen.queryByText("accounts list")).not.toBeInTheDocument();
    expect(screen.queryByText("Forbidden")).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/accounts/${accountId}`);
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });
});
