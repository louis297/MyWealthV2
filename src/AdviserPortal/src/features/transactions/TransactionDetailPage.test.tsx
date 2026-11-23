import { configureStore } from "@reduxjs/toolkit";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { sessionSlice, setCurrentUser, type CurrentUser } from "@/features/session/sessionSlice";
import { TransactionDetailPage } from "@/features/transactions/TransactionDetailPage";
import { api } from "@/shared/api/api";
import { adviser, feeUser, renderFees, stubBrowser, type Handler } from "@/test/fees";

const originalId = "dddddddd-dddd-dddd-dddd-dddddddddddd";
const reverseId = "99999999-9999-9999-9999-999999999999";
const idempotencyKey = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

const original = {
  id: originalId,
  tenantId: "22222222-2222-2222-2222-222222222222",
  type: "Buy",
  source: { kind: "Manual", id: null },
  cash: [
    {
      accountId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      customerId: "cccccccccccccccc-cccc-cccc-cccc-cccccccccccc",
      amount: -1250,
      currency: "NZD",
    },
  ],
  bookedAt: "2026-10-11T10:00:00+13:00",
  memo: "Parcel",
  reference: null,
  originalTransactionId: null,
  reversedByTransactionId: null,
  rowVersion: "AAAA",
  security: [
    {
      accountId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      customerId: "cccccccccccccccc-cccc-cccc-cccc-cccccccccccc",
      instrumentId: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
      quantity: 50,
      cost: 750,
      costCurrency: "USD",
    },
  ],
};

function user(): CurrentUser {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    name: "Sam Reed",
    email: "sam@north.example",
    role: "adviser",
    status: "active",
    tenantId: original.tenantId,
    tenantCode: "north-advisory",
    adviserId: "ffffffff-ffff-ffff-ffff-ffffffffffff",
    rowVersion: "AAAA",
  };
}

/** The page also loads the currency catalog for decimal places; mocks that answer every URL skip it. */
function isCurrencies(input: RequestInfo | URL) {
  return (input instanceof Request ? input.url : String(input)).includes("/currencies");
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

describe("TransactionDetailPage", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
    vi.spyOn(crypto, "randomUUID").mockReturnValue(idempotencyKey);
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: `/transactions/${originalId}`,
      search: "",
      href: `http://localhost:3000/transactions/${originalId}`,
      assign: vi.fn(),
    });
  });

  it("reverses a Buy once and keeps the original row", async () => {
    const store = configureStore({
      reducer: {
        session: sessionSlice.reducer,
        [api.reducerPath]: api.reducer,
      },
      middleware: (getDefault) => getDefault().concat(api.middleware),
    });
    store.dispatch(setCurrentUser(user()));

    let reversed = false;
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockImplementation(async (input, init) => {
      const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
      const method = init?.method ?? (input instanceof Request ? input.method : "GET");
      if (url.endsWith(`/transactions/${originalId}/reverse`) && method === "POST") {
        reversed = true;
        return json({ id: reverseId }, 201);
      }
      if (url.endsWith(`/transactions/${originalId}`) && method === "GET") {
        return json(reversed ? { ...original, reversedByTransactionId: reverseId } : original);
      }
      return new Response("", { status: 404 });
    });

    const router = createMemoryRouter(
      [{ path: "/transactions/:id", element: <TransactionDetailPage /> }],
      { initialEntries: [`/transactions/${originalId}`] },
    );

    render(
      <Provider store={store}>
        <RouterProvider router={router} />
      </Provider>,
    );

    expect(await screen.findByText(originalId)).toBeInTheDocument();
    expect(screen.getByText("Buy")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reverse" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reverse" }));
    await waitFor(() => {
      expect(screen.queryByRole("button", { name: "Reverse" })).not.toBeInTheDocument();
    });
    expect(screen.getByText(originalId)).toBeInTheDocument();
    expect(screen.getByText("Buy")).toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/transactions/${originalId}`);
    expect(document.querySelector('input[type="password"]')).toBeNull();

    const posts = fetchMock.mock.calls
      .map(([input]) => input)
      .filter((input): input is Request => input instanceof Request && input.method === "POST");
    expect(posts).toHaveLength(1);
    for (const post of posts) {
      expect(post.url).toBe(`http://localhost:3000/api/transactions/${originalId}/reverse`);
      expect(post.headers.get("Idempotency-Key")).toBe(idempotencyKey);
      expect(post.headers.get("X-MyWealth-Request")).toBe("1");
      expect(post.headers.get("Authorization")).toBeNull();
      expect(post.credentials).toBe("include");
      const text = await post.clone().text();
      expect(text === "" || text === "{}").toBe(true);
      expect(text).not.toContain("tenantId");
    }
  });

  it("hides Reverse when the loaded original is already reversed", async () => {
    const store = configureStore({
      reducer: {
        session: sessionSlice.reducer,
        [api.reducerPath]: api.reducer,
      },
      middleware: (getDefault) => getDefault().concat(api.middleware),
    });
    store.dispatch(setCurrentUser(user()));

    vi.mocked(fetch).mockImplementation(async (input) => isCurrencies(input) ? json([]) : json({
      ...original,
      reversedByTransactionId: reverseId,
    }));

    const router = createMemoryRouter(
      [{ path: "/transactions/:id", element: <TransactionDetailPage /> }],
      { initialEntries: [`/transactions/${originalId}`] },
    );

    render(
      <Provider store={store}>
        <RouterProvider router={router} />
      </Provider>,
    );

    expect(await screen.findByText(originalId)).toBeInTheDocument();
    expect(screen.getByText("Buy")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reverse" })).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/transactions/${originalId}`);
  });

  it("hides Reverse on a reversal", async () => {
    const store = configureStore({
      reducer: {
        session: sessionSlice.reducer,
        [api.reducerPath]: api.reducer,
      },
      middleware: (getDefault) => getDefault().concat(api.middleware),
    });
    store.dispatch(setCurrentUser(user()));

    vi.mocked(fetch).mockImplementation(async (input) => isCurrencies(input) ? json([]) : json({
      ...original,
      id: reverseId,
      type: "Reversal",
      originalTransactionId: originalId,
      reversedByTransactionId: null,
    }));

    render(
      <Provider store={store}>
        <RouterProvider router={createMemoryRouter(
          [{ path: "/transactions/:id", element: <TransactionDetailPage /> }],
          { initialEntries: [`/transactions/${reverseId}`] },
        )} />
      </Provider>,
    );

    expect(await screen.findByText("Reversal")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reverse" })).not.toBeInTheDocument();
  });

  it("stays on the original when it has already been reversed", async () => {
    const store = configureStore({
      reducer: {
        session: sessionSlice.reducer,
        [api.reducerPath]: api.reducer,
      },
      middleware: (getDefault) => getDefault().concat(api.middleware),
    });
    store.dispatch(setCurrentUser(user()));

    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
      const method = init?.method ?? (input instanceof Request ? input.method : "GET");
      if (url.endsWith(`/transactions/${originalId}/reverse`) && method === "POST") {
        return json({
          type: "https://tools.ietf.org/html/rfc9110#section-15.5.1",
          title: "One or more validation errors occurred.",
          status: 400,
          errors: { "": ["This transaction has already been reversed."] },
        }, 400);
      }
      if (url.endsWith(`/transactions/${originalId}`) && method === "GET") {
        return json(original);
      }
      return new Response("", { status: 404 });
    });

    const router = createMemoryRouter(
      [{ path: "/transactions/:id", element: <TransactionDetailPage /> }],
      { initialEntries: [`/transactions/${originalId}`] },
    );

    render(
      <Provider store={store}>
        <RouterProvider router={router} />
      </Provider>,
    );

    expect(await screen.findByText(originalId)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reverse" }));

    expect(await screen.findByText("This transaction has already been reversed.")).toBeInTheDocument();
    expect(screen.getByText(originalId)).toBeInTheDocument();
    expect(screen.getByText("Buy")).toBeInTheDocument();
    expect(screen.queryByText(reverseId)).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/transactions/${originalId}`);
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("shows a closed account when reverse is rejected", async () => {
    const store = configureStore({
      reducer: {
        session: sessionSlice.reducer,
        [api.reducerPath]: api.reducer,
      },
      middleware: (getDefault) => getDefault().concat(api.middleware),
    });
    store.dispatch(setCurrentUser(user()));
    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const method = init?.method ?? (input instanceof Request ? input.method : "GET");
      if (isCurrencies(input)) {
        return json([]);
      }
      if (method === "POST") {
        return json({
          status: 400,
          errors: { "": ["Cannot reverse a transaction while the account is closed. Reopen the account first."] },
        }, 400);
      }
      if (method === "GET") {
        return json(original);
      }
      return new Response("", { status: 404 });
    });
    render(
      <Provider store={store}>
        <RouterProvider router={createMemoryRouter(
          [{ path: "/transactions/:id", element: <TransactionDetailPage /> }],
          { initialEntries: [`/transactions/${originalId}`] },
        )} />
      </Provider>,
    );
    expect(await screen.findByRole("button", { name: "Reverse" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reverse" }));
    expect(
      await screen.findByText("Cannot reverse a transaction while the account is closed. Reopen the account first."),
    ).toBeInTheDocument();
    expect(screen.getByText(originalId)).toBeInTheDocument();
  });

  it("shows a disabled customer when reverse is rejected", async () => {
    const store = configureStore({
      reducer: {
        session: sessionSlice.reducer,
        [api.reducerPath]: api.reducer,
      },
      middleware: (getDefault) => getDefault().concat(api.middleware),
    });
    store.dispatch(setCurrentUser(user()));
    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const method = init?.method ?? (input instanceof Request ? input.method : "GET");
      if (isCurrencies(input)) {
        return json([]);
      }
      if (method === "POST") {
        return json({
          status: 400,
          title: "User is disabled",
          detail: "Cannot reverse a transaction while the customer is disabled. Enable the customer first.",
          code: "disabled",
          target: "user",
          targetId: original.cash[0].customerId,
        }, 400);
      }
      if (method === "GET") {
        return json(original);
      }
      return new Response("", { status: 404 });
    });
    render(
      <Provider store={store}>
        <RouterProvider router={createMemoryRouter(
          [{ path: "/transactions/:id", element: <TransactionDetailPage /> }],
          { initialEntries: [`/transactions/${originalId}`] },
        )} />
      </Provider>,
    );
    expect(await screen.findByRole("button", { name: "Reverse" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reverse" }));
    expect(
      await screen.findByText("Cannot reverse a transaction while the customer is disabled. Enable the customer first."),
    ).toBeInTheDocument();
    expect(screen.getByText(originalId)).toBeInTheDocument();
    expect(screen.queryByText(reverseId)).not.toBeInTheDocument();
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });
});

describe("TransactionDetailPage source (adviser-portal-dashboard D26, D27)", () => {
  const path = `/transactions/${originalId}`;
  const routes = [{ path: "/transactions/:id", element: <TransactionDetailPage /> }];
  const handle = "12121212-1212-1212-1212-121212121212";

  function serve(source: { kind: string; id: string | null }, onReverse?: () => Response): Handler {
    return (request) => {
      if (request.method === "POST" && request.url.endsWith(`${path}/reverse`) && onReverse) {
        return onReverse();
      }
      return request.method === "GET" && request.url.endsWith(path) ? json({ ...original, source }) : undefined;
    };
  }

  beforeEach(() => {
    stubBrowser(path);
  });

  it.each([
    ["Manual", null, "Source: Entered by hand"],
    ["Rebalance", handle, "Source: Rebalance"],
    ["Seed", null, "Source: Test seed"],
  ])("shows the Source line for a %s transaction without its id", async (kind, id, line) => {
    renderFees(user(), routes, path, serve({ kind, id }));

    expect(await screen.findByText(line)).toBeInTheDocument();
    expect(screen.queryByText(handle)).not.toBeInTheDocument();
  });

  it.each([
    ["Manual", null, true],
    // Rebalances R28: a rebalance trade is no longer reversed by hand.
    ["Rebalance", handle, false],
    ["Seed", null, false],
  ])("offers Reverse on a %s transaction (source id %s): %s", async (kind, id, offered) => {
    renderFees(user(), routes, path, serve({ kind, id }));

    expect(await screen.findByText(originalId)).toBeInTheDocument();
    if (offered) {
      expect(screen.getByRole("button", { name: "Reverse" })).toBeInTheDocument();
    } else {
      expect(screen.queryByRole("button", { name: "Reverse" })).not.toBeInTheDocument();
    }
  });

  it("shows a migrated opening balance with its batch, its provider and no Reverse (D85)", async () => {
    const batchId = "7b1ae0a4-0000-0000-0000-000000000001";
    renderFees({ ...user(), role: "tenantAdmin" }, routes, path, (request) =>
      request.method === "GET" && request.url.endsWith(path)
        ? json({ ...original, type: "Opening", source: { kind: "Migration", id: batchId }, migration: { asOfDate: "2026-11-20", previousProvider: "Acme Wealth" } })
        : undefined);

    expect(await screen.findByText("Source: Migration batch")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Migration batch" })).toHaveAttribute("href", `/migration-batches/${batchId}`);
    expect(screen.getByText("From Acme Wealth, balances as at 2026-11-20")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reverse" })).not.toBeInTheDocument();
  });

  it("shows the sentence of a 400 from reverse", async () => {
    const sentence = "This transaction was written by the platform and cannot be reversed by hand.";
    renderFees(user(), routes, path, serve({ kind: "Manual", id: null }, () => json({
      status: 400,
      errors: { "": [sentence] },
    }, 400)));

    fireEvent.click(await screen.findByRole("button", { name: "Reverse" }));

    expect(await screen.findByText(sentence)).toBeInTheDocument();
  });
});

describe("TransactionDetailPage cash legs (adviser-portal-fees F13)", () => {
  const accountId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
  const customerId = "cccccccc-cccc-cccc-cccc-cccccccccccc";
  const scheduleId = "abababab-abab-abab-abab-abababababab";
  const path = `/transactions/${originalId}`;
  const routes = [{ path: "/transactions/:id", element: <TransactionDetailPage /> }];

  function serve(cash: unknown[]): Handler {
    return (request) =>
      request.method === "GET" && request.url.endsWith(path) ? json({ ...original, cash }) : undefined;
  }

  beforeEach(() => {
    stubBrowser(path);
  });

  it("lists each visible cash leg with its account, role, amount and currency", async () => {
    renderFees(
      user(),
      routes,
      path,
      serve([{ accountId, customerId, role: "Principal", amount: -1250, currency: "NZD", fee: null }]),
    );

    const legs = within(await screen.findByRole("table", { name: "Cash legs" }));
    const row = within(legs.getByText("Principal").closest("tr") as HTMLElement);
    expect(row.getByRole("link", { name: accountId })).toHaveAttribute("href", `/accounts/${accountId}`);
    expect(row.getByText("-1,250.00")).toBeInTheDocument();
    expect(row.getByText("NZD")).toBeInTheDocument();
    expect(screen.queryByText(/Brokerage/)).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "View schedule" })).not.toBeInTheDocument();
  });

  it("shows a Fee leg's fee below its amount with a link to the schedule", async () => {
    renderFees(
      user(),
      routes,
      path,
      serve([
        { accountId, customerId, role: "Principal", amount: -1250, currency: "NZD", fee: null },
        {
          accountId,
          customerId,
          role: "Fee",
          amount: -16.92,
          currency: "NZD",
          fee: { kind: "Brokerage", scheduleId, basisAmount: 1250, amount: 10, currency: "USD" },
        },
      ]),
    );

    const legs = within(await screen.findByRole("table", { name: "Cash legs" }));
    const row = within(legs.getByText("Fee").closest("tr") as HTMLElement);
    expect(row.getByText("-16.92")).toBeInTheDocument();
    expect(row.getByText("Brokerage 10.00 USD on 1,250.00 USD")).toBeInTheDocument();
    expect(row.getByRole("link", { name: "View schedule" })).toHaveAttribute("href", `/fees/trade/${scheduleId}`);
  });

  it("shows a fee without a schedule link when the fee has no schedule", async () => {
    renderFees(
      user(),
      routes,
      path,
      serve([
        {
          accountId,
          customerId,
          role: "Fee",
          amount: -10,
          currency: "NZD",
          fee: { kind: "Brokerage", scheduleId: null, basisAmount: 1250, amount: 10, currency: "NZD" },
        },
      ]),
    );

    const legs = within(await screen.findByRole("table", { name: "Cash legs" }));
    expect(legs.getByText("Brokerage 10.00 NZD on 1,250.00 NZD")).toBeInTheDocument();
    expect(legs.queryByRole("link", { name: "View schedule" })).not.toBeInTheDocument();
  });
});

describe("TransactionDetailPage adjustment (adviser-portal-dashboard D30, D31)", () => {
  const path = `/transactions/${originalId}`;
  const routes = [{ path: "/transactions/:id", element: <TransactionDetailPage /> }];
  const adjustment = {
    ...original,
    type: "Adjustment",
    memo: "Fill price was wrong",
    security: [],
    adjustment: { reason: "EntryError" },
  };

  function serve(item: object, onReverse?: () => Response): Handler {
    return (request) => {
      if (request.method === "POST" && request.url.endsWith(`${path}/reverse`) && onReverse) {
        return onReverse();
      }
      return request.method === "GET" && request.url.endsWith(path) ? json(item) : undefined;
    };
  }

  beforeEach(() => {
    stubBrowser(path);
  });

  it("shows the Reason line with the label on an adjustment", async () => {
    renderFees(feeUser(), routes, path, serve(adjustment));

    expect(await screen.findByText("Reason: Entry error")).toBeInTheDocument();
    expect(screen.getByText("Source: Entered by hand")).toBeInTheDocument();
  });

  it.each([
    ["CorporateAction", "Reason: Corporate action"],
    ["ReconciliationBreak", "Reason: Reconciliation"],
    ["Other", "Reason: Other"],
  ])("labels the reason %s", async (reason, line) => {
    renderFees(feeUser(), routes, path, serve({ ...adjustment, adjustment: { reason } }));

    expect(await screen.findByText(line)).toBeInTheDocument();
  });

  it("shows the Reason line on the reversal of an adjustment", async () => {
    renderFees(feeUser(), routes, path, serve({
      ...adjustment,
      type: "Reversal",
      originalTransactionId: reverseId,
    }));

    expect(await screen.findByText("Reason: Entry error")).toBeInTheDocument();
  });

  it("shows no Reason line on another transaction", async () => {
    renderFees(feeUser(), routes, path, serve({ ...original, adjustment: null }));

    expect(await screen.findByText(originalId)).toBeInTheDocument();
    expect(screen.queryByText(/^Reason:/)).not.toBeInTheDocument();
  });

  it("offers Reverse on an adjustment to a TenantAdmin", async () => {
    renderFees(feeUser(), routes, path, serve(adjustment));

    expect(await screen.findByRole("button", { name: "Reverse" })).toBeInTheDocument();
  });

  it("hides Reverse on an adjustment from an Adviser", async () => {
    renderFees(adviser(), routes, path, serve(adjustment));

    expect(await screen.findByText("Reason: Entry error")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reverse" })).not.toBeInTheDocument();
  });

  it("shows the firm administrator sentence on a 403 from reverse", async () => {
    renderFees(feeUser(), routes, path, serve(adjustment, () => new Response("", { status: 403 })));

    fireEvent.click(await screen.findByRole("button", { name: "Reverse" }));

    expect(await screen.findByText("Only a firm administrator can reverse an adjustment.")).toBeInTheDocument();
  });
});

describe("TransactionDetailPage bank deposit (deposit-matching D38)", () => {
  const path = `/transactions/${originalId}`;
  const routes = [{ path: "/transactions/:id", element: <TransactionDetailPage /> }];
  const lineId = "3f2c0000-0000-0000-0000-0000000000e1";
  const deposit = {
    ...original,
    type: "TransferIn",
    memo: "Bank deposit",
    security: [],
    source: { kind: "BankFeed", id: lineId },
    bankReceipt: { valueDate: "2026-11-19", reference: "K7M3Q9PXB", payerName: "J SMITH", payerAccount: "****4200" },
  };

  function serve(item: object): Handler {
    return (request) => (request.method === "GET" && request.url.endsWith(path) ? json(item) : undefined);
  }

  beforeEach(() => {
    stubBrowser(path);
  });

  it("shows the Bank receipt section, the Bank feed source and the statement line link to a TenantAdmin", async () => {
    renderFees(feeUser(), routes, path, serve(deposit));

    const section = (await screen.findByRole("heading", { name: "Bank receipt" })).closest("section")!;
    expect(within(section).getByText("2026-11-19")).toBeInTheDocument();
    expect(within(section).getByText("K7M3Q9PXB")).toBeInTheDocument();
    expect(within(section).getByText("J SMITH")).toBeInTheDocument();
    expect(within(section).getByText("****4200")).toBeInTheDocument();
    expect(screen.getByText("Source: Bank feed")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Statement line" })).toHaveAttribute("href", `/statement-lines/${lineId}`);
    expect(screen.queryByRole("button", { name: "Reverse" })).not.toBeInTheDocument();
  });

  it("shows the section on the Reversal of a deposit too", async () => {
    renderFees(feeUser(), routes, path, serve({ ...deposit, type: "Reversal", originalTransactionId: reverseId }));

    expect(await screen.findByRole("heading", { name: "Bank receipt" })).toBeInTheDocument();
  });

  it("gives an Adviser no statement line link", async () => {
    renderFees(adviser(), routes, path, serve(deposit));

    expect(await screen.findByText("Source: Bank feed")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Statement line" })).not.toBeInTheDocument();
  });

  it("shows no Bank receipt section on another transaction", async () => {
    renderFees(feeUser(), routes, path, serve({ ...original, bankReceipt: null }));

    expect(await screen.findByText(originalId)).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Bank receipt" })).not.toBeInTheDocument();
  });
});

describe("TransactionDetailPage payout (withdrawals D48)", () => {
  const path = `/transactions/${originalId}`;
  const routes = [{ path: "/transactions/:id", element: <TransactionDetailPage /> }];
  const withdrawalId = "77777777-0000-0000-0000-000000000001";
  const payout = {
    ...original,
    type: "TransferOut",
    memo: "Withdrawal",
    security: [],
    source: { kind: "Payment", id: withdrawalId },
    bankReceipt: null,
    payment: { valueDate: "2026-11-20", reference: "K7M3Q9PXB", payeeName: "J Smith", payeeAccount: "****8900" },
  };

  beforeEach(() => {
    stubBrowser(path);
  });

  it("shows the Payment section, the Withdrawal source with its link, and no Reverse", async () => {
    renderFees(adviser(), routes, path, (request) =>
      request.method === "GET" && request.url.endsWith(path) ? json(payout) : undefined);

    const section = (await screen.findByRole("heading", { name: "Payment" })).closest("section")!;
    expect(within(section).getByText("2026-11-20")).toBeInTheDocument();
    expect(within(section).getByText("K7M3Q9PXB")).toBeInTheDocument();
    expect(within(section).getByText("J Smith")).toBeInTheDocument();
    expect(within(section).getByText("****8900")).toBeInTheDocument();
    expect(screen.getByText("Source: Withdrawal")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Withdrawal" })).toHaveAttribute("href", `/withdrawals/${withdrawalId}`);
    expect(screen.queryByRole("button", { name: "Reverse" })).not.toBeInTheDocument();
  });
});
