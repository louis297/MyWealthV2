import { configureStore } from "@reduxjs/toolkit";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AccountDetailPage } from "@/features/accounts/AccountDetailPage";
import { CreateTransactionPage } from "@/features/transactions/CreateTransactionPage";
import { sessionSlice, setCurrentUser, type CurrentUser } from "@/features/session/sessionSlice";
import { api } from "@/shared/api/api";
import { chooseOption, openSelect, pickOption } from "@/test/chooseOption";

const accountId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
const customerId = "cccccccccccccccc-cccc-cccc-cccc-cccccccccccc";

const account = {
  id: accountId,
  tenantId: "22222222-2222-2222-2222-222222222222",
  customerId,
  name: "Everyday spending",
  type: "Brokerage",
  custodyMode: "External",
  currency: "NZD",
  status: "Open",
  isActive: true,
  cashBalance: 1250.5,
  rowVersion: "AAAA",
};

// P13: portfolios are assigned to Custodial accounts only (custody-modes R14).
const custodialAccount = { ...account, custodyMode: "Custodial" };

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

const currencyCatalog = [
  { code: "NZD", name: "New Zealand dollar", decimalPlaces: 2, isActive: true },
  { code: "USD", name: "US dollar", decimalPlaces: 2, isActive: true },
];

function renderDetail(
  fetchImpl: (url: string, method: string, body?: string) => Response,
  me: CurrentUser = user(),
) {
  const store = configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });
  store.dispatch(setCurrentUser(me));

  const fetchMock = vi.mocked(fetch);
  fetchMock.mockImplementation(async (input, init) => {
    const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
    const method = init?.method ?? (input instanceof Request ? input.method : "GET");
    const body =
      typeof init?.body === "string"
        ? init.body
        : input instanceof Request
          ? await input.clone().text()
          : undefined;
    if (url.includes("/currencies") && method === "GET") {
      return new Response(JSON.stringify(currencyCatalog), { status: 200, headers: { "Content-Type": "application/json" } });
    }
    return fetchImpl(url, method, body);
  });

  const router = createMemoryRouter(
    [
      { path: "/accounts", element: <p>accounts list</p> },
      { path: "/accounts/:id", element: <AccountDetailPage /> },
      { path: "/rebalances/:id", element: <p>rebalance page</p> },
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
    expect(screen.getByText("Custody")).toBeInTheDocument();
    expect(screen.getByText("External")).toBeInTheDocument();
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
      type: "Buy",
      cash: [{ accountId, customerId, amount: -1250, currency: "NZD" }],
      bookedAt: "2026-10-10T21:00:00Z",
      memo: null,
      reference: null,
      originalTransactionId: null,
      reversedByTransactionId: null,
      rowVersion: "BBBB",
      security: [{ accountId, customerId, instrumentId, quantity: 50, cost: 750, costCurrency: "USD" }],
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

    await chooseOption("Instrument", "DEMO Demo Fund");
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "50" } });
    fireEvent.change(screen.getByLabelText("Price per unit (NZD)"), { target: { value: "25" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-11T10:00" },
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
              type: "Sell",
              cash: [{ accountId, customerId, amount: 1250, currency: "NZD" }],
              bookedAt: "2026-10-10T22:00:00Z",
              memo: null,
              reference: null,
              originalTransactionId: null,
              reversedByTransactionId: null,
              rowVersion: "BBBB",
              security: [{ accountId, customerId, instrumentId, quantity: -40, cost: 2000, costCurrency: "NZD" }],
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

    await chooseOption("Instrument", "DEMO Demo Fund");
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "40" } });
    fireEvent.change(screen.getByLabelText("Price per unit (NZD)"), { target: { value: "31.25" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-11T11:00" },
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
    expect(body.security).toEqual([{ instrumentId, quantity: 40, price: 31.25 }]);
    expect(body.security[0]).not.toHaveProperty("cost");
    expect(body).not.toHaveProperty("tenantId");
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it.each([
    ["Bank", "Open"],
    ["Cash", "Open"],
    ["Brokerage", "Closed"],
  ] as const)("hides security actions when the account is %s and %s", async (type, status) => {
    renderDetail((url, method) => {
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json({ ...account, type, status });
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByRole("heading", { name: "Everyday spending" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Buy" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Split" })).not.toBeInTheDocument();
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

  it.each(["Bank", "Cash", "Brokerage", "Other"] as const)(
    "links an open %s account to TransferIn and TransferOut",
    async (type) => {
      renderDetail((url, method) => {
        if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
          return json({ items: [] });
        }
        if (url.includes("/transactions") && method === "GET") {
          return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
        }
        if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
          return json({ ...account, type, status: "Open" });
        }
        return new Response("", { status: 404 });
      });

      expect(await screen.findByRole("heading", { name: "Everyday spending" })).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "TransferIn" })).toHaveAttribute(
        "href",
        `/accounts/${accountId}/transactions/new?type=TransferIn`,
      );
      expect(screen.getByRole("link", { name: "TransferOut" })).toHaveAttribute(
        "href",
        `/accounts/${accountId}/transactions/new?type=TransferOut`,
      );
      expect(screen.getByRole("link", { name: "Transfer" })).toHaveAttribute(
        "href",
        `/accounts/${accountId}/transfer`,
      );
      expect(screen.getByRole("link", { name: "Convert" })).toHaveAttribute(
        "href",
        `/accounts/${accountId}/convert`,
      );
      if (type === "Bank" || type === "Cash") {
        expect(screen.queryByRole("link", { name: "Buy" })).not.toBeInTheDocument();
        expect(screen.queryByRole("link", { name: "Sell" })).not.toBeInTheDocument();
      }
      expect(document.querySelector('input[type="password"]')).toBeNull();
    },
  );

  it("posts a TransferIn from an open Bank account and shows it in activity", async () => {
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
              type: "TransferIn",
              cash: [{ accountId, customerId, amount: 80, currency: "NZD" }],
              bookedAt: "2026-10-11T20:00:00Z",
              memo: "Salary",
              reference: null,
              originalTransactionId: null,
              reversedByTransactionId: null,
              rowVersion: "BBBB",
            }]
            : [],
          page: 1,
          pageSize: 20,
          totalCount: posted ? 1 : 0,
        });
      }
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json({ ...account, type: "Bank", cashBalance: posted ? 1330.5 : 1250.5 });
      }
      if (url.includes("/instruments") && method === "GET") {
        return json({ items: [], page: 1, pageSize: 100, totalCount: 0 });
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

    fireEvent.click(await screen.findByRole("link", { name: "TransferIn" }));
    fireEvent.change(await screen.findByLabelText("Amount"), { target: { value: "80" } });
    fireEvent.change(screen.getByLabelText("Memo"), { target: { value: "Salary" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-12T09:00" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(await screen.findByRole("heading", { name: "Everyday spending" })).toBeInTheDocument();
    expect(await screen.findByRole("link", { name: createdId })).toHaveAttribute(
      "href",
      `/transactions/${createdId}`,
    );
    expect(screen.getByText("1330.5")).toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/accounts/${accountId}`);

    const post = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.method === "POST");
    const body = JSON.parse(await post!.clone().text()) as Record<string, unknown>;
    expect(body).toEqual({
      accountId,
      type: "TransferIn",
      amount: 80,
      bookedAt: "2026-10-11T20:00:00.000Z",
      memo: "Salary",
    });
    expect(post?.headers.get("Idempotency-Key")).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i,
    );
    expect(body).not.toHaveProperty("security");
    expect(body).not.toHaveProperty("tenantId");
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it.each([
    // D39: deposits to a Custodial account come from the bank statement. D44 (withdrawals R45): money leaves it as a
    // withdrawal, not a hand TransferOut. D69 (orders R48): a trade on it is an order.
    ["Custodial", ["Place order", "Transfer", "Convert", "Adjust", "Withdraw"]],
    [
      "External",
      ["TransferIn", "TransferOut", "Buy", "Sell", "StockIn", "StockOut", "Split", "Bonus", "Scrip", "Transfer", "Convert"],
    ],
  ])("offers the manual entry matrix actions on a %s Brokerage account (D18)", async (custodyMode, expected) => {
    renderDetail((url, method) => {
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json({ ...account, custodyMode });
      }
      return new Response("", { status: 404 });
    });

    const nav = await screen.findByRole("navigation", { name: "Transaction actions" });
    expect(within(nav).getAllByRole("link").map((link) => link.textContent)).toEqual(expected);
  });

  it("hides transfer actions when the account is closed", async () => {
    renderDetail((url, method) => {
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json({ ...account, type: "Bank", status: "Closed" });
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByRole("heading", { name: "Everyday spending" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "TransferIn" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "TransferOut" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Transfer" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Convert" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Buy" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Close" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reopen" })).toBeInTheDocument();
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("shows the cash leg on this account in activity, negative on the source of a transfer", async () => {
    const otherAccountId = "b2222222-2222-2222-2222-222222222222";
    renderDetail((url, method) => {
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({
          items: [
            {
              id: "71111111-1111-1111-1111-111111111111",
              tenantId: account.tenantId,
              type: "InternalTransfer",
              bookedAt: "2026-11-09T20:00:00Z",
              memo: null,
              reference: null,
              originalTransactionId: null,
              reversedByTransactionId: null,
              rowVersion: "AAAA",
              cash: [
                { accountId: otherAccountId, customerId, amount: 500, currency: "NZD" },
                { accountId, customerId, amount: -500, currency: "NZD" },
              ],
              security: [],
            },
          ],
          page: 1,
          pageSize: 20,
          totalCount: 1,
        });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json({ ...account, type: "Bank", status: "Open" });
      }
      return new Response("", { status: 404 });
    });

    const activity = await screen.findByRole("table", { name: "Activity" });
    expect(within(activity).getByRole("columnheader", { name: "Amount" })).toBeInTheDocument();
    const row = within(activity).getByRole("row", { name: /InternalTransfer/ });
    expect(row).toHaveTextContent("-500 NZD");
    expect(row).not.toHaveTextContent(/(^|[^-])500 NZD/);
  });

  it("shows the sum of this account's cash legs and the fee included (adviser-portal-fees F13)", async () => {
    const fee = { kind: "Brokerage", scheduleId: "abababab-abab-abab-abab-abababababab", basisAmount: 1250, amount: 10, currency: "NZD" };
    renderDetail((url, method) => {
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.includes("/transactions") && method === "GET") {
        const base = {
          tenantId: account.tenantId,
          bookedAt: "2026-11-09T20:00:00Z",
          memo: null,
          reference: null,
          originalTransactionId: null,
          reversedByTransactionId: null,
          rowVersion: "AAAA",
          security: [],
        };
        return json({
          items: [
            {
              ...base,
              id: "73333333-3333-3333-3333-333333333333",
              type: "Buy",
              cash: [
                { accountId, customerId, role: "Principal", amount: -1250, currency: "NZD", fee: null },
                { accountId, customerId, role: "Fee", amount: -10, currency: "NZD", fee },
              ],
            },
            {
              ...base,
              id: "74444444-4444-4444-4444-444444444444",
              type: "Sell",
              cash: [{ accountId, customerId, role: "Principal", amount: 800, currency: "NZD", fee: null }],
            },
          ],
          page: 1,
          pageSize: 20,
          totalCount: 2,
        });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json({ ...account, type: "Brokerage", status: "Open" });
      }
      return new Response("", { status: 404 });
    });

    const activity = await screen.findByRole("table", { name: "Activity" });
    const buy = within(activity).getByRole("row", { name: /Buy/ });
    expect(buy).toHaveTextContent("-1260 NZD");
    expect(buy).not.toHaveTextContent("-1250 NZD");
    expect(within(buy).getByText("incl. fee 10.00 NZD")).toBeInTheDocument();
    const sell = within(activity).getByRole("row", { name: /Sell/ });
    expect(sell).toHaveTextContent("800 NZD");
    expect(sell).not.toHaveTextContent("incl. fee");
  });

  it("shows the leg on this account and the rate line for an FX conversion", async () => {
    const usdAccountId = "b3333333-3333-3333-3333-333333333333";
    renderDetail((url, method) => {
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({
          items: [
            {
              id: "72222222-2222-2222-2222-222222222222",
              tenantId: account.tenantId,
              type: "FxConversion",
              bookedAt: "2026-11-09T20:00:00Z",
              memo: null,
              reference: null,
              originalTransactionId: null,
              reversedByTransactionId: null,
              rowVersion: "AAAA",
              cash: [
                { accountId, customerId, amount: -845.74, currency: "NZD" },
                { accountId: usdAccountId, customerId, amount: 500, currency: "USD" },
              ],
              security: [],
              fx: { baseCurrency: "NZD", quoteCurrency: "USD", rate: 0.5912, rateAsOfDate: "2026-11-09" },
            },
          ],
          page: 1,
          pageSize: 20,
          totalCount: 1,
        });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json({ ...account, type: "Bank", status: "Open" });
      }
      return new Response("", { status: 404 });
    });

    const activity = await screen.findByRole("table", { name: "Activity" });
    const row = within(activity).getByRole("row", { name: /FxConversion/ });
    expect(row).toHaveTextContent("-845.74 NZD");
    expect(row).not.toHaveTextContent("500 USD");
    expect(row).toHaveTextContent("1 NZD = 0.5912 USD (as of 2026-11-09)");
    expect(row).toHaveTextContent("2026-11-10 09:00");
  });

  it("closes an open account with the current rowVersion and shows Closed", async () => {
    const posted: string[] = [];
    let closed = false;
    vi.spyOn(window, "confirm").mockReturnValue(true);
    const { fetchMock } = renderDetail((url, method, body) => {
      if (url.endsWith(`/accounts/${accountId}/close`) && method === "POST") {
        posted.push(body ?? "");
        closed = true;
        return new Response(null, { status: 204 });
      }
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json({
          ...account,
          status: closed ? "Closed" : "Open",
          isActive: !closed,
        });
      }
      return new Response("", { status: 404 });
    });

    fireEvent.click(await screen.findByRole("button", { name: "Close" }));

    expect(await screen.findByText("Closed")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Close" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reopen" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "TransferIn" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Buy" })).not.toBeInTheDocument();
    expect(posted).toEqual([JSON.stringify({ rowVersion: "AAAA" })]);
    expect(window.confirm).toHaveBeenCalledWith("Close this account?");

    const post = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.method === "POST");
    expect(post?.url).toBe(`http://localhost:3000/api/accounts/${accountId}/close`);
    expect(post?.credentials).toBe("include");
    expect(post?.headers.get("Authorization")).toBeNull();
    expect(post?.headers.get("X-MyWealth-Request")).toBe("1");
    expect(new URL(post?.url ?? "").searchParams.has("tenantId")).toBe(false);
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("does not close the account when confirm is dismissed", async () => {
    vi.spyOn(window, "confirm").mockReturnValue(false);
    const { fetchMock } = renderDetail((url, method) => {
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json(account);
      }
      return new Response("", { status: 404 });
    });

    fireEvent.click(await screen.findByRole("button", { name: "Close" }));

    expect(screen.getByText("Open")).toBeInTheDocument();
    const posts = fetchMock.mock.calls
      .map(([input]) => input)
      .filter((input): input is Request => input instanceof Request && input.method === "POST");
    expect(posts).toEqual([]);
  });

  it("stays on the page and shows a close rejection", async () => {
    const problem = "Cannot close an account while the cash balance is not zero.";
    vi.spyOn(window, "confirm").mockReturnValue(true);
    renderDetail((url, method) => {
      if (url.endsWith(`/accounts/${accountId}/close`) && method === "POST") {
        return json({
          title: "One or more validation errors occurred.",
          status: 400,
          errors: { "": [problem] },
        }, 400);
      }
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [holding] });
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json(account);
      }
      return new Response("", { status: 404 });
    });

    fireEvent.click(await screen.findByRole("button", { name: "Close" }));

    expect(await screen.findByText(problem)).toBeInTheDocument();
    expect(screen.getByText("Open")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Close" })).toBeInTheDocument();
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("asks the user to reload on 409 and does not replay the stale rowVersion", async () => {
    const posted: string[] = [];
    let gets = 0;
    vi.spyOn(window, "confirm").mockReturnValue(true);
    renderDetail((url, method, body) => {
      if (url.endsWith(`/accounts/${accountId}/close`) && method === "POST") {
        posted.push(body ?? "");
        return new Response("", { status: 409 });
      }
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        gets += 1;
        return json({ ...account, rowVersion: gets === 1 ? "AAAA" : "BBBB" });
      }
      return new Response("", { status: 404 });
    });

    fireEvent.click(await screen.findByRole("button", { name: "Close" }));

    expect(await screen.findByText("This row changed. Reload and try again.")).toBeInTheDocument();
    await waitFor(() => {
      expect(gets).toBeGreaterThan(1);
    });
    expect(posted).toEqual([JSON.stringify({ rowVersion: "AAAA" })]);
    expect(screen.getByText("Open")).toBeInTheDocument();
  });

  it("reopens a closed account with the current rowVersion and shows Open", async () => {
    const posted: string[] = [];
    let opened = false;
    vi.spyOn(window, "confirm").mockReturnValue(true);
    const { fetchMock } = renderDetail((url, method, body) => {
      if (url.endsWith(`/accounts/${accountId}/reopen`) && method === "POST") {
        posted.push(body ?? "");
        opened = true;
        return new Response(null, { status: 204 });
      }
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json({
          ...account,
          status: opened ? "Open" : "Closed",
          isActive: opened,
        });
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByRole("button", { name: "Reopen" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Close" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reopen" }));

    expect(await screen.findByText("Open")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reopen" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Close" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "TransferIn" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Buy" })).toBeInTheDocument();
    expect(posted).toEqual([JSON.stringify({ rowVersion: "AAAA" })]);
    expect(window.confirm).toHaveBeenCalledWith("Reopen this account?");

    const post = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.method === "POST");
    expect(post?.url).toBe(`http://localhost:3000/api/accounts/${accountId}/reopen`);
    expect(post?.credentials).toBe("include");
    expect(post?.headers.get("Authorization")).toBeNull();
    expect(post?.headers.get("X-MyWealth-Request")).toBe("1");
    expect(new URL(post?.url ?? "").searchParams.has("tenantId")).toBe(false);
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("does not reopen the account when confirm is dismissed", async () => {
    vi.spyOn(window, "confirm").mockReturnValue(false);
    const { fetchMock } = renderDetail((url, method) => {
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json({ ...account, status: "Closed", isActive: false });
      }
      return new Response("", { status: 404 });
    });

    fireEvent.click(await screen.findByRole("button", { name: "Reopen" }));

    expect(screen.getByText("Closed")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Close" })).not.toBeInTheDocument();
    const posts = fetchMock.mock.calls
      .map(([input]) => input)
      .filter((input): input is Request => input instanceof Request && input.method === "POST");
    expect(posts).toEqual([]);
  });

  it("stays closed and shows a reopen rejection", async () => {
    const problem = "Cannot reopen an account while the customer is disabled. Enable the customer first.";
    vi.spyOn(window, "confirm").mockReturnValue(true);
    renderDetail((url, method) => {
      if (url.endsWith(`/accounts/${accountId}/reopen`) && method === "POST") {
        return json({
          status: 400,
          title: "User is disabled",
          detail: problem,
          code: "disabled",
          target: "user",
        }, 400);
      }
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json({ ...account, status: "Closed", isActive: false });
      }
      return new Response("", { status: 404 });
    });

    fireEvent.click(await screen.findByRole("button", { name: "Reopen" }));

    expect(await screen.findByText(problem)).toBeInTheDocument();
    expect(screen.getByText("Closed")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reopen" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "TransferIn" })).not.toBeInTheDocument();
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("asks the user to reload when reopen hits a stale rowVersion", async () => {
    const posted: string[] = [];
    let gets = 0;
    vi.spyOn(window, "confirm").mockReturnValue(true);
    renderDetail((url, method, body) => {
      if (url.endsWith(`/accounts/${accountId}/reopen`) && method === "POST") {
        posted.push(body ?? "");
        return new Response("", { status: 409 });
      }
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        gets += 1;
        return json({
          ...account,
          status: "Closed",
          isActive: false,
          rowVersion: gets === 1 ? "AAAA" : "BBBB",
        });
      }
      return new Response("", { status: 404 });
    });

    fireEvent.click(await screen.findByRole("button", { name: "Reopen" }));

    expect(await screen.findByText("This row changed. Reload and try again.")).toBeInTheDocument();
    await waitFor(() => {
      expect(gets).toBeGreaterThan(1);
    });
    expect(posted).toEqual([JSON.stringify({ rowVersion: "AAAA" })]);
    expect(screen.getByText("Closed")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reopen" })).toBeInTheDocument();
  });

  it("stays on the URL and shows Not found for a missing account", async () => {
    const { router } = renderDetail(() => new Response("", { status: 404 }));

    expect(await screen.findByText("Not found")).toBeInTheDocument();
    expect(screen.queryByText("accounts list")).not.toBeInTheDocument();
    expect(screen.queryByText("Forbidden")).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/accounts/${accountId}`);
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("assigns an active portfolio and opens its proposal (P14)", async () => {
    const portfolioId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    const posted: string[] = [];
    const { fetchMock } = renderDetail((url, method, body) => {
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json(custodialAccount);
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      }
      if (url.includes("/api/portfolios?") && method === "GET") {
        return json({
          items: [
            { id: portfolioId, name: "Balanced Growth", baseCurrency: "NZD", isActive: true, lines: [], rowVersion: "AAAA", tenantId: account.tenantId, driftAbsolute: 1, driftRelative: 5, minimumTrade: 100, cashResidualPercent: 1 },
            { id: "ffffffff-ffff-ffff-ffff-ffffffffffff", name: "Retired", baseCurrency: "NZD", isActive: false, lines: [], rowVersion: "AAAA", tenantId: account.tenantId, driftAbsolute: 1, driftRelative: 5, minimumTrade: 100, cashResidualPercent: 1 },
          ],
          page: 1,
          pageSize: 100,
          totalCount: 2,
        });
      }
      if (url.endsWith(`/accounts/${accountId}/portfolio`) && method === "POST") {
        posted.push(body ?? "");
        return json({ status: "Proposed", rebalanceId: "12121212-0000-0000-0000-000000000001" });
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByRole("heading", { name: "Everyday spending" })).toBeInTheDocument();
    await openSelect("Portfolio");
    expect(screen.getByRole("option", { name: "Balanced Growth" })).toBeInTheDocument();
    expect(screen.queryByRole("option", { name: "Retired" })).not.toBeInTheDocument();
    await pickOption("Balanced Growth");
    fireEvent.click(screen.getByRole("button", { name: "Assign" }));

    expect(await screen.findByText("rebalance page")).toBeInTheDocument();
    expect(posted).toEqual([JSON.stringify({ portfolioId })]);
    const request = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.method === "POST" && input.url.includes("/portfolio"));
    expect(new URL(request!.url).pathname).toBe(`/api/accounts/${accountId}/portfolio`);
    expect(request?.headers.get("X-MyWealth-Request")).toBe("1");
    expect(request?.headers.get("Authorization")).toBeNull();
    expect(new URL(request!.url).searchParams.has("tenantId")).toBe(false);
  });

  it("hides the portfolio dropdown and Assign until Unassign", async () => {
    const portfolioId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    let assigned = false;
    renderDetail((url, method) => {
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json(custodialAccount);
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      }
      if (url.includes("/api/portfolios?") && method === "GET") {
        return json({
          items: [
            {
              id: portfolioId,
              name: "Balanced Growth",
              baseCurrency: "NZD",
              isActive: true,
              lines: [],
              rowVersion: "AAAA",
              tenantId: account.tenantId,
              driftAbsolute: 1,
              driftRelative: 5,
              minimumTrade: 100,
              cashResidualPercent: 1,
            },
          ],
          page: 1,
          pageSize: 100,
          totalCount: 1,
        });
      }
      if (url.endsWith(`/accounts/${accountId}/portfolio`) && method === "POST") {
        assigned = true;
        return json({ status: "NoChange", rebalanceId: "12121212-0000-0000-0000-000000000001" });
      }
      if (url.endsWith(`/accounts/${accountId}/portfolio`) && method === "DELETE") {
        assigned = false;
        return new Response(null, { status: 204 });
      }
      if (url.endsWith(`/accounts/${accountId}/portfolio`) && method === "GET") {
        return assigned
          ? json({
              accountId,
              portfolioId,
              status: "Active",
              rebalance: null,
            })
          : new Response("", { status: 404 });
      }
      return new Response("", { status: 404 });
    });

    await chooseOption("Portfolio", "Balanced Growth");
    fireEvent.click(screen.getByRole("button", { name: "Assign" }));
    await waitFor(() => {
      expect(screen.queryByLabelText("Portfolio")).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Assign" })).not.toBeInTheDocument();
    });
    expect(screen.getByRole("button", { name: "Unassign" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Unassign" }));
    expect(await screen.findByLabelText("Portfolio")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Assign" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Unassign" })).not.toBeInTheDocument();
  });

  it("says there is nothing to trade when assign proposes no trade (P14)", async () => {
    const portfolioId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    renderDetail((url, method) => {
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json(custodialAccount);
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      }
      if (url.includes("/api/portfolios?") && method === "GET") {
        return json({
          items: [{ id: portfolioId, name: "Balanced Growth", baseCurrency: "NZD", isActive: true, lines: [], rowVersion: "AAAA", tenantId: account.tenantId, driftAbsolute: 1, driftRelative: 5, minimumTrade: 100, cashResidualPercent: 1 }],
          page: 1,
          pageSize: 100,
          totalCount: 1,
        });
      }
      if (url.endsWith(`/accounts/${accountId}/portfolio`) && method === "POST") {
        return json({ status: "NoChange", rebalanceId: "12121212-0000-0000-0000-000000000001" });
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByLabelText("Portfolio")).toBeInTheDocument();
    await chooseOption("Portfolio", "Balanced Growth");
    fireEvent.click(screen.getByRole("button", { name: "Assign" }));
    expect(await screen.findByText("Nothing to trade. The account is in line with its portfolio.")).toBeInTheDocument();
  });

  it("does not offer assign on External or Closed accounts (P13)", async () => {
    for (const next of [
      { ...account, type: "Bank" },
      { ...account, type: "Cash" },
      { ...account, type: "Brokerage" },
      { ...account, type: "Other" },
      { ...custodialAccount, status: "Closed" },
    ]) {
      cleanup();
      renderDetail((url, method) => {
        if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
          return json({ items: [] });
        }
        if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
          return json(next);
        }
        if (url.includes("/transactions") && method === "GET") {
          return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
        }
        return new Response("", { status: 404 });
      });

      expect(await screen.findByRole("heading", { name: "Everyday spending" })).toBeInTheDocument();
      expect(screen.queryByLabelText("Portfolio")).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Assign" })).not.toBeInTheDocument();
    }
  });

  it("shows the server message when assign is rejected", async () => {
    const portfolioId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    renderDetail((url, method) => {
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json(custodialAccount);
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      }
      if (url.includes("/api/portfolios?") && method === "GET") {
        return json({
          items: [{ id: portfolioId, name: "Balanced Growth", baseCurrency: "NZD", isActive: true, lines: [], rowVersion: "AAAA", tenantId: account.tenantId, driftAbsolute: 1, driftRelative: 5, minimumTrade: 100, cashResidualPercent: 1 }],
          page: 1,
          pageSize: 100,
          totalCount: 1,
        });
      }
      if (url.endsWith(`/accounts/${accountId}/portfolio`) && method === "POST") {
        return json({ detail: "Missing price for VTI." }, 400);
      }
      return new Response("", { status: 404 });
    });

    await chooseOption("Portfolio", "Balanced Growth");
    fireEvent.click(screen.getByRole("button", { name: "Assign" }));
    expect(await screen.findByText("Missing price for VTI.")).toBeInTheDocument();
  });

  it("unassigns an active portfolio and restores security actions", async () => {
    let assigned = true;
    const deleted: string[] = [];
    renderDetail((url, method) => {
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [holding] });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json(custodialAccount);
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      }
      if (url.endsWith(`/accounts/${accountId}/portfolio`) && method === "DELETE") {
        deleted.push(url);
        assigned = false;
        return new Response(null, { status: 204 });
      }
      if (url.endsWith(`/accounts/${accountId}/portfolio`) && method === "GET") {
        return assigned
          ? json({
              accountId,
              portfolioId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
              status: "Active",
              rebalance: null,
            })
          : new Response("", { status: 404 });
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByRole("button", { name: "Unassign" })).toBeInTheDocument();
    // D69: an assigned account follows its portfolio, so no order can be placed.
    expect(screen.queryByRole("link", { name: "Place order" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Place order" })).toBeDisabled();
    expect(screen.getByText("This account follows a portfolio.")).toBeInTheDocument();
    // D44 (withdrawals R45): cash leaves a Custodial account as a withdrawal, also while a portfolio is assigned.
    expect(screen.getByRole("link", { name: "Withdraw" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Unassign" }));
    expect(await screen.findByRole("link", { name: "Place order" })).toHaveAttribute("href", `/accounts/${accountId}/order`);
    expect(deleted.some((url) => url.includes(`/api/accounts/${accountId}/portfolio`))).toBe(true);
    expect(screen.queryByRole("button", { name: "Unassign" })).not.toBeInTheDocument();
  });

  it("shows a failed rebalance with its message and proposes again (P14, P17)", async () => {
    const posted: string[] = [];
    renderDetail((url, method) => {
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json(custodialAccount);
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      }
      if (url.endsWith(`/accounts/${accountId}/rebalance`) && method === "POST") {
        posted.push(url);
        return json({ status: "Proposed", rebalanceId: "12121212-0000-0000-0000-000000000002" });
      }
      if (url.endsWith(`/accounts/${accountId}/portfolio`) && method === "GET") {
        return json({
          accountId,
          portfolioId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
          status: "Active",
          rebalance: {
            id: "12121212-0000-0000-0000-000000000001",
            status: "Failed",
            failure: { code: "missing_price", message: "Missing price for VTI." },
            proposedAt: "2026-11-02T00:00:00+00:00",
            endedAt: "2026-11-02T00:00:00+00:00",
          },
        });
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByText("Missing price for VTI.", { exact: false })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Could not be planned" })).toHaveAttribute(
      "href", "/rebalances/12121212-0000-0000-0000-000000000001");
    fireEvent.click(screen.getByRole("button", { name: "Rebalance" }));
    expect(await screen.findByText("rebalance page")).toBeInTheDocument();
    expect(posted.some((url) => url.includes(`/api/accounts/${accountId}/rebalance`))).toBe(true);
  });

  it("says when no rebalance has run", async () => {
    renderDetail((url, method) => {
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json(custodialAccount);
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      }
      if (url.endsWith(`/accounts/${accountId}/portfolio`) && method === "GET") {
        return json({
          accountId,
          portfolioId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
          status: "Active",
          rebalance: null,
        });
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByText("No rebalance yet.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Retry" })).not.toBeInTheDocument();
  });

  it("proposes a rebalance of an assigned account after cash moves", async () => {
    const { fetchMock } = renderDetail((url, method) => {
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json(custodialAccount);
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      }
      if (url.endsWith(`/accounts/${accountId}/rebalance`) && method === "POST") {
        return json({ status: "NoChange", rebalanceId: "12121212-0000-0000-0000-000000000001" });
      }
      if (url.endsWith(`/accounts/${accountId}/portfolio`) && method === "GET") {
        return json({
          accountId,
          portfolioId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
          status: "Active",
          rebalance: null,
        });
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByRole("button", { name: "Unassign" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Rebalance" }));

    const request = await vi.waitFor(() => {
      const found = fetchMock.mock.calls
        .map(([input]) => input)
        .find((input): input is Request => input instanceof Request && input.method === "POST" && input.url.includes("/rebalance"));
      expect(found).toBeDefined();
      return found!;
    });
    expect(new URL(request.url).pathname).toBe(`/api/accounts/${accountId}/rebalance`);
    expect(request.headers.get("X-MyWealth-Request")).toBe("1");
    expect(request.headers.get("Authorization")).toBeNull();
    expect(await screen.findByText("Nothing to trade. The account is in line with its portfolio.")).toBeInTheDocument();
  });

  it("shows current quantity beside the target weight", async () => {
    const portfolioId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    const otherId = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";
    renderDetail((url, method) => {
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [holding] });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json(custodialAccount);
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      }
      if (url.includes("/instruments") && method === "GET") {
        return json({
          items: [
            { id: holding.instrumentId, symbol: "DEMO", name: "Demo Fund", quoteCurrency: "NZD", isActive: true, tenantId: account.tenantId, rowVersion: "AAAA" },
            { id: otherId, symbol: "FPH", name: "Fisher", quoteCurrency: "NZD", isActive: true, tenantId: account.tenantId, rowVersion: "AAAA" },
          ],
          page: 1,
          pageSize: 100,
          totalCount: 2,
        });
      }
      if (url.endsWith(`/portfolios/${portfolioId}`) && method === "GET") {
        return json({
          id: portfolioId,
          name: "Balanced Growth",
          baseCurrency: "NZD",
          isActive: true,
          tenantId: account.tenantId,
          rowVersion: "AAAA",
          driftAbsolute: 1,
          driftRelative: 5,
          minimumTrade: 100,
          cashResidualPercent: 1,
          lines: [
            { instrumentId: holding.instrumentId, targetWeight: 60, sortOrder: 0 },
            { instrumentId: otherId, targetWeight: 40, sortOrder: 1 },
          ],
        });
      }
      if (url.endsWith(`/accounts/${accountId}/portfolio`) && method === "GET") {
        return json({
          accountId,
          portfolioId,
          status: "Active",
          rebalance: null,
        });
      }
      return new Response("", { status: 404 });
    });

    const table = await screen.findByRole("table", { name: "Holdings" });
    expect(await within(table).findByRole("columnheader", { name: "Target" })).toBeInTheDocument();
    const demo = within(table).getByText("DEMO").closest("tr");
    expect(demo).toHaveTextContent("100");
    expect(demo).toHaveTextContent("60");
    const fisher = within(table).getByText("FPH").closest("tr");
    expect(fisher).toHaveTextContent("—");
    expect(fisher).toHaveTextContent("40");
  });
});

describe("AccountDetailPage activity Reverse (adviser-portal-dashboard D27)", () => {
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

  function item(id: string, memo: string, source: { kind: string; id: string | null }, extra: object = {}) {
    return {
      id,
      tenantId: account.tenantId,
      type: "TransferIn",
      source,
      bookedAt: "2026-11-09T20:00:00Z",
      memo,
      reference: null,
      originalTransactionId: null,
      reversedByTransactionId: null,
      rowVersion: "AAAA",
      cash: [{ accountId, customerId, amount: 100, currency: "NZD" }],
      security: [],
      ...extra,
    };
  }

  const manualId = "71111111-1111-1111-1111-111111111111";
  const rebalanceId = "72222222-2222-2222-2222-222222222222";
  const seedId = "73333333-3333-3333-3333-333333333333";
  const reversedId = "74444444-4444-4444-4444-444444444444";
  const reversalId = "75555555-5555-5555-5555-555555555555";

  function serve(onReverse: (url: string) => Response = () => new Response("", { status: 404 })) {
    renderDetail((url, method) => {
      if (method === "POST" && url.endsWith("/reverse")) {
        return onReverse(url);
      }
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({
          items: [
            item(manualId, "manual-row", { kind: "Manual", id: null }),
            item(rebalanceId, "rebalance-row", { kind: "Rebalance", id: "12121212-1212-1212-1212-121212121212" }, {
              type: "Buy",
            }),
            item(seedId, "seed-row", { kind: "Seed", id: null }),
            item(reversedId, "reversed-row", { kind: "Manual", id: null }, { reversedByTransactionId: reversalId }),
            item(reversalId, "reversal-row", { kind: "Manual", id: null }, {
              type: "Reversal",
              originalTransactionId: reversedId,
            }),
          ],
          page: 1,
          pageSize: 20,
          totalCount: 5,
        });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json(custodialAccount);
      }
      return new Response("", { status: 404 });
    });
  }

  function row(id: string) {
    return within(screen.getByRole("link", { name: id }).closest("tr") as HTMLElement);
  }

  it("offers Reverse on Manual rows only, never on a Rebalance, Seed, reversed or Reversal row (D27, rebalances R28)", async () => {
    serve();

    const table = await screen.findByRole("table", { name: "Activity" });
    expect(within(table).getAllByRole("button", { name: "Reverse" })).toHaveLength(1);
    expect(row(manualId).getByRole("button", { name: "Reverse" })).toBeInTheDocument();
    expect(row(rebalanceId).queryByRole("button", { name: "Reverse" })).not.toBeInTheDocument();
    expect(row(seedId).queryByRole("button", { name: "Reverse" })).not.toBeInTheDocument();
    expect(row(reversedId).queryByRole("button", { name: "Reverse" })).not.toBeInTheDocument();
    expect(row(reversalId).queryByRole("button", { name: "Reverse" })).not.toBeInTheDocument();
  });

  it("reverses from the activity row and shows the sentence of a 400", async () => {
    const sentence = "This transaction was written by the platform and cannot be reversed by hand.";
    const reversed: string[] = [];
    serve((url) => {
      reversed.push(url);
      return json({ status: 400, errors: { "": [sentence] } }, 400);
    });

    await screen.findByRole("table", { name: "Activity" });
    fireEvent.click(row(manualId).getByRole("button", { name: "Reverse" }));

    expect(await screen.findByText(sentence)).toBeInTheDocument();
    expect(reversed).toEqual([`http://localhost:3000/api/transactions/${manualId}/reverse`]);
  });
  describe("Adjust action (D28)", () => {
    function serveAccount(served: object) {
      return (url: string, method: string) => {
        if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
          return json({ items: [] });
        }
        if (url.includes("/transactions") && method === "GET") {
          return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
        }
        if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
          return json(served);
        }
        return new Response("", { status: 404 });
      };
    }

    it("offers Adjust to a TenantAdmin on an Open Custodial account", async () => {
      renderDetail(serveAccount(custodialAccount));

      expect(await screen.findByRole("link", { name: "Adjust" })).toHaveAttribute(
        "href",
        `/accounts/${accountId}/adjust`,
      );
    });

    it.each([
      ["an Adviser on an Open Custodial account", custodialAccount, user({ role: "adviser", name: "Sam Reed" })],
      ["a TenantAdmin on an External account", account, user()],
    ])("does not offer Adjust to %s", async (_case, served, me) => {
      renderDetail(serveAccount(served), me);

      await screen.findByRole("navigation", { name: "Transaction actions" });
      expect(screen.queryByRole("link", { name: "Adjust" })).not.toBeInTheDocument();
    });

    it("does not offer Adjust on a Closed Custodial account", async () => {
      renderDetail(serveAccount({ ...custodialAccount, status: "Closed" }));

      expect(await screen.findByRole("button", { name: "Reopen" })).toBeInTheDocument();
      expect(screen.queryByRole("link", { name: "Adjust" })).not.toBeInTheDocument();
    });
  });

  describe("Reverse on an adjustment row (D31)", () => {
    const adjustmentId = "76666666-6666-6666-6666-666666666666";

    function serveAdjustment(me: CurrentUser) {
      renderDetail((url, method) => {
        if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
          return json({ items: [] });
        }
        if (url.includes("/transactions") && method === "GET") {
          return json({
            items: [
              {
                id: adjustmentId,
                tenantId: account.tenantId,
                type: "Adjustment",
                source: { kind: "Manual", id: null },
                adjustment: { reason: "EntryError" },
                bookedAt: "2026-11-19T03:00:00Z",
                memo: "Fill price was wrong",
                reference: null,
                originalTransactionId: null,
                reversedByTransactionId: null,
                rowVersion: "AAAA",
                cash: [{ accountId, customerId, amount: -0.6, currency: "NZD" }],
                security: [],
              },
            ],
            page: 1,
            pageSize: 20,
            totalCount: 1,
          });
        }
        if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
          return json(custodialAccount);
        }
        return new Response("", { status: 404 });
      }, me);
    }

    it("offers Reverse on an adjustment to a TenantAdmin", async () => {
      serveAdjustment(user());

      const table = await screen.findByRole("table", { name: "Activity" });
      expect(within(table).getByRole("button", { name: "Reverse" })).toBeInTheDocument();
    });

    it("hides Reverse on an adjustment from an Adviser", async () => {
      serveAdjustment(user({ role: "adviser", name: "Sam Reed" }));

      const table = await screen.findByRole("table", { name: "Activity" });
      expect(within(table).getByRole("link", { name: adjustmentId })).toBeInTheDocument();
      expect(within(table).queryByRole("button", { name: "Reverse" })).not.toBeInTheDocument();
    });
  });
});

// client-money-accounts D32: deposit reference and deposit instructions on a Custodial account.
describe("AccountDetailPage deposit instructions (client-money-accounts D32)", () => {
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

  const reference = "K7M3Q9PXB";
  const instructions = {
    accountId,
    currency: "USD",
    reference,
    bankName: "Simulator Bank",
    accountName: "North Advisory Ltd Client Funds Trust Account",
    accountNumber: "02-0100-0123456-00",
  };

  function detailValue(label: string) {
    const term = screen.getByText(label, { selector: "dt" });
    return term.nextElementSibling?.textContent;
  }

  function serve(item: unknown, instructionsResponse: () => Response) {
    return renderDetail((url, method) => {
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.endsWith(`/accounts/${accountId}/deposit-instructions`) && method === "GET") {
        return instructionsResponse();
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json(item);
      }
      return new Response("", { status: 404 });
    });
  }

  function instructionCalls(fetchMock: ReturnType<typeof vi.mocked<typeof fetch>>) {
    return fetchMock.mock.calls.filter(([input]) => {
      const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
      return url.includes("/deposit-instructions");
    });
  }

  it("shows the reference and the instructions panel on an Open Custodial account", async () => {
    serve({ ...custodialAccount, currency: "USD", depositReference: reference }, () => json(instructions));

    const panel = (await screen.findByRole("heading", { name: "Deposit instructions" })).closest("section")!;
    expect(detailValue("Deposit reference")).toBe(reference);
    expect(await within(panel).findByText("Simulator Bank")).toBeInTheDocument();
    expect(within(panel).getByText("North Advisory Ltd Client Funds Trust Account")).toBeInTheDocument();
    expect(within(panel).getByText("02-0100-0123456-00")).toBeInTheDocument();
    expect(within(panel).getByText(reference)).toBeInTheDocument();
    expect(within(panel).getByText("USD")).toBeInTheDocument();
    expect(
      within(panel).getByText("Deposits are credited from the bank statement when the customer pays in with this reference."),
    ).toBeInTheDocument();
  });

  it("shows the reference but no panel and calls no route on a Closed Custodial account", async () => {
    const { fetchMock } = serve(
      { ...custodialAccount, currency: "USD", status: "Closed", isActive: false, depositReference: reference },
      () => json(instructions),
    );

    expect(await screen.findByRole("heading", { name: "Everyday spending" })).toBeInTheDocument();
    expect(detailValue("Deposit reference")).toBe(reference);
    expect(screen.queryByRole("heading", { name: "Deposit instructions" })).not.toBeInTheDocument();
    expect(instructionCalls(fetchMock)).toHaveLength(0);
  });

  it("says to ask the platform operator when the instructions route returns 404", async () => {
    serve({ ...custodialAccount, currency: "USD", depositReference: reference }, () => new Response("", { status: 404 }));

    const panel = (await screen.findByRole("heading", { name: "Deposit instructions" })).closest("section")!;
    expect(
      await within(panel).findByText("No client money account in USD. Ask the platform operator to add one."),
    ).toBeInTheDocument();
  });

  it("shows neither the reference nor the panel on an External account", async () => {
    const { fetchMock } = serve({ ...account, depositReference: null }, () => json(instructions));

    expect(await screen.findByRole("heading", { name: "Everyday spending" })).toBeInTheDocument();
    await screen.findByText("Cash balance");
    expect(screen.queryByText("Deposit reference")).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Deposit instructions" })).not.toBeInTheDocument();
    expect(instructionCalls(fetchMock)).toHaveLength(0);
  });
});

// withdrawals D44: cash, reserved cash, available cash, Withdraw and the open withdrawals on a Custodial account.
describe("AccountDetailPage withdrawals (withdrawals D44)", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  function serve(override: Record<string, unknown>, withdrawals: unknown[] = []) {
    return (url: string, method: string) => {
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.includes("/withdrawals") && method === "GET") {
        return json({ items: withdrawals, page: 1, pageSize: 20, totalCount: withdrawals.length });
      }
      if (url.includes("/transactions") && method === "GET") {
        return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json({ ...custodialAccount, currency: "USD", ...override });
      }
      return new Response("", { status: 404 });
    };
  }

  it("shows Cash, Reserved and Available, links Withdraw and lists the open withdrawals", async () => {
    renderDetail(serve({ cashBalance: 1000, reservedCash: 300, availableCash: 700 }, [{
      id: "77777777-0000-0000-0000-000000000001",
      accountId,
      customerId,
      nominatedBankAccount: { id: "b", bankName: "ANZ", accountName: "J Smith", accountNumber: "****8900" },
      amount: 300,
      currency: "USD",
      reference: "REF",
      memo: null,
      status: "Requested",
      requestedBy: { id: "e", name: "Ann Adviser" },
      requestedAt: "2026-11-20T04:00:00Z",
      decision: null,
      payment: null,
      payoutTransactionId: null,
      returnTransactionId: null,
      failureReason: null,
      returnReason: null,
      rowVersion: "AAAA",
    }]));

    expect(await screen.findByText("Reserved")).toBeInTheDocument();
    expect(screen.getByText("Available")).toBeInTheDocument();
    expect(screen.getByText("Available to withdraw")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Withdraw" })).toHaveAttribute("href", `/accounts/${accountId}/withdraw`);
    expect(await screen.findByText("Waiting for approval")).toBeInTheDocument();
    expect(screen.getByText("ANZ ****8900")).toBeInTheDocument();
  });

  it("hides Reserved when nothing is reserved", async () => {
    renderDetail(serve({ cashBalance: 1000, reservedCash: 0, availableCash: 1000 }));

    expect(await screen.findByText("Available")).toBeInTheDocument();
    expect(screen.queryByText("Reserved")).not.toBeInTheDocument();
    expect(screen.queryByText("Not settled")).not.toBeInTheDocument();
  });
});
