import { configureStore } from "@reduxjs/toolkit";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { appRoutes } from "@/app/router";
import { sessionSlice } from "@/features/session/sessionSlice";
import { api } from "@/shared/api/api";
import { chooseOption, openSelect, pickOption } from "@/test/chooseOption";

const me = {
  id: "cccccccc-cccc-cccc-cccc-cccccccccccc",
  name: "Cee Customer",
  email: "cee@firm.example",
  role: "customer",
  status: "Active",
  tenantId: "tttttttt-tttt-tttt-tttt-tttttttttttt",
  tenantCode: "firm",
  adviserId: null,
  rowVersion: "AAAA",
};

const netWorth = {
  asOf: "2026-11-02T00:00:00.000Z",
  currencies: [
    {
      currency: "NZD",
      assets: { cash: 100, holdings: 40, total: 140 },
      liabilities: { total: 0 },
      net: 140,
    },
  ],
  breakdowns: {
    byCustomer: [{ customerId: me.id, name: "Someone else", currencies: [] }],
    byCustodyMode: [],
    byAccountType: [
      {
        type: "Bank",
        currencies: [
          {
            currency: "NZD",
            assets: { cash: 100, holdings: 0, total: 100 },
            liabilities: { total: 0 },
            net: 100,
          },
        ],
      },
    ],
    topHoldings: [
      {
        instrumentId: "iiiiiiii-iiii-iiii-iiii-iiiiiiiiiiii",
        symbol: "DEMO",
        quantity: 2,
        marketValue: 40,
        currency: "NZD",
      },
    ],
  },
};

const reportId = "8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11";

function callOf(input: RequestInfo | URL, init?: RequestInit) {
  const request = input instanceof Request ? input : null;
  const url = request?.url ?? String(input);
  const method = (init?.method ?? request?.method ?? "GET").toUpperCase();
  const credentials = init?.credentials ?? request?.credentials;
  const headers = new Headers(init?.headers ?? request?.headers);
  return { url, method, credentials, headers };
}

type AccountItem = { id: string; name: string; type: string; custodyMode: string; currency: string; cashBalance: number; status: string };

function renderAt(
  path: string,
  person = me,
  activity: unknown[] = [],
  custodyMode = "External",
  options: {
    netWorth?: unknown;
    accounts?: AccountItem[];
    account?: Record<string, unknown>;
    depositInstructions?: () => Response;
    cash?: Record<string, unknown>;
    withdrawals?: unknown[];
    transfers?: unknown[];
    orders?: unknown[];
    holdings?: unknown[];
    nominated?: unknown[];
    taxProfile?: unknown;
    series?: unknown;
    valuations?: unknown;
  } = {},
) {
  const accounts = options.accounts ?? [
    { id: "account-1", name: "Everyday", type: "Bank", custodyMode, currency: "NZD", cashBalance: 100, status: "Open" },
  ];
  const fetchMock = vi.mocked(fetch);
  fetchMock.mockImplementation(async (input, init) => {
    const { url, method } = callOf(input, init);
    if (url.endsWith("/users/me") && method === "PUT") {
      return new Response(null, { status: 204 });
    }
    if (url.endsWith("/users/me/password") && method === "PUT") {
      return new Response(null, { status: 204 });
    }
    if (url.endsWith("/users/me/tax-profile")) {
      return json(options.taxProfile ?? {
        taxResidency: "NewZealand",
        irdNumberHeld: false,
        rwtRatePercent: null,
        withholding: { kind: "Rwt", ratePercent: 45, basis: "NonDeclaration" },
        updatedBy: null,
        updatedAt: null,
        rowVersion: null,
      });
    }
    if (url.endsWith("/users/me")) {
      return json(person);
    }
    if (url.includes("/net-worth/funds-under-management")) {
      return options.series ? json(options.series) : new Response("", { status: 404 });
    }
    if (url.includes("/accounts/account-1/valuations")) {
      return options.valuations ? json(options.valuations) : new Response("", { status: 404 });
    }
    if (url.includes(`/users/customers/${person.id}/net-worth`)) {
      return json(options.netWorth ?? netWorth);
    }
    if (url.includes("/accounts/") && url.endsWith("/cash-balance")) {
      return json({ accountId: "account-1", currency: "NZD", cashBalance: 100, ...options.cash });
    }
    if (url.includes("/nominated-bank-accounts")) {
      const items = options.nominated ?? [];
      return json({ items, page: 1, pageSize: 100, totalCount: items.length });
    }
    if (url.includes("/in-kind-transfers")) {
      const items = options.transfers ?? [];
      return json({ items, page: 1, pageSize: 100, totalCount: items.length });
    }
    if (url.includes("/withdrawals")) {
      const items = options.withdrawals ?? [];
      return json({ items, page: 1, pageSize: 100, totalCount: items.length });
    }
    if (url.includes("/orders?accountId=")) {
      const items = options.orders ?? [];
      return json({ items, page: 1, pageSize: 100, totalCount: items.length });
    }
    if (url.includes("/accounts/") && url.endsWith("/holdings")) {
      return json({ items: options.holdings ?? [] });
    }
    if (url.includes("/transactions?accountId=")) {
      return json({ items: activity, page: 1, pageSize: 20, totalCount: activity.length });
    }
    if (url.endsWith("/accounts/account-1/deposit-instructions")) {
      return options.depositInstructions?.() ?? new Response("", { status: 404 });
    }
    if (url.endsWith("/accounts/account-1")) {
      return json({
        id: "account-1",
        name: "Everyday",
        type: "Bank",
        custodyMode,
        currency: "NZD",
        status: "Open",
        ...options.account,
      });
    }
    if (url.includes("/accounts/missing")) {
      return new Response(JSON.stringify({ detail: "missing" }), { status: 404 });
    }
    if (url.includes("/accounts?")) {
      const mode = new URL(url, "http://localhost").searchParams.get("custodyMode");
      const items = mode ? accounts.filter((account) => account.custodyMode === mode) : accounts;
      return json({ items, page: 1, pageSize: 100, totalCount: items.length });
    }
    if (url.includes("/reports?scope=Customer")) {
      return json([
        { id: reportId, code: "customer-transactions", name: "Customer Transactions" },
        { id: "unknown-report", code: "mystery", name: "Mystery" },
      ]);
    }
    if (url.endsWith("/reports/request") && method === "POST") {
      return json({ id: "execution-1" }, 202);
    }
    if (url.includes("/reports/executions?")) {
      return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    }
    return new Response("", { status: 404 });
  });

  const store = configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] });
  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );
  return { fetchMock, router };
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function urls(fetchMock: ReturnType<typeof vi.mocked<typeof fetch>>) {
  return fetchMock.mock.calls.map(([input]) => (
    typeof input === "string" ? input : input instanceof Request ? input.url : String(input)
  ));
}

describe("Customer Portal", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: "/",
      search: "",
      href: "http://localhost:3000/",
      assign: vi.fn(),
    });
  });

  it("lands a customer on the dashboard and reads their own net worth", async () => {
    const { fetchMock } = renderAt("/");

    expect(await screen.findByRole("heading", { name: "Dashboard" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Accounts" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Reports" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Profile" })).toBeInTheDocument();
    expect(screen.getByText("Cee Customer")).toBeInTheDocument();
    expect(await screen.findByRole("cell", { name: "NZD" })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "Bank" })).toBeInTheDocument();
    expect(screen.getByText(/DEMO/)).toBeInTheDocument();
    expect(screen.queryByText("Someone else")).not.toBeInTheDocument();
    expect(urls(fetchMock).some((url) => url.includes("/tenants"))).toBe(false);
    expect(urls(fetchMock).some((url) => url.includes(`/users/customers/${me.id}/net-worth`))).toBe(true);
    const meCall = fetchMock.mock.calls
      .map(([input, init]) => callOf(input, init))
      .find((call) => call.url.includes("/users/me") && call.method === "GET");
    expect(meCall?.credentials).toBe("include");
    expect(meCall?.headers.get("Authorization")).toBeNull();
  });

  it("sends a non-customer to forbidden only after the role is known", async () => {
    renderAt("/dashboard", { ...me, role: "tenantAdmin", name: "Tess" });
    expect(screen.queryByRole("heading", { name: "Forbidden" })).not.toBeInTheDocument();
    expect(await screen.findByRole("heading", { name: "Forbidden" })).toBeInTheDocument();
  });

  it("shows the custody label next to the type on the accounts list", async () => {
    renderAt("/accounts");

    expect(await screen.findByRole("link", { name: "Everyday" })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Custody" })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "Held elsewhere" })).toBeInTheDocument();
    expect(screen.queryByText("External")).not.toBeInTheDocument();
  });

  it.each([
    ["External", "Held elsewhere"],
    ["Custodial", "Held on platform"],
    ["Vault", "Vault"],
  ])("shows custody mode %s on the account page as %s", async (custodyMode, label) => {
    renderAt("/accounts/account-1", me, [], custodyMode);

    expect(await screen.findByRole("heading", { name: "Everyday" })).toBeInTheDocument();
    expect(screen.getByText("Custody")).toBeInTheDocument();
    expect(screen.getByText(label)).toBeInTheDocument();
  });

  const note = "Held elsewhere: recorded by your adviser from outside statements. The platform has not checked these figures.";

  function currency(code: string, cash: number, holdings: number) {
    return {
      currency: code,
      assets: { cash, holdings, total: cash + holdings },
      liabilities: { total: 0 },
      net: cash + holdings,
    };
  }

  function withCustodyModes(byCustodyMode: unknown[]) {
    return { ...netWorth, breakdowns: { ...netWorth.breakdowns, byCustodyMode } };
  }

  it("shows held on platform and held elsewhere on the dashboard with the note under held elsewhere", async () => {
    renderAt("/dashboard", me, [], "External", {
      netWorth: withCustodyModes([
        { custodyMode: "Custodial", currencies: [currency("NZD", 1000, 5000)] },
        { custodyMode: "External", currencies: [currency("NZD", 2500, 0), currency("USD", 0, 800)] },
      ]),
    });

    const heading = await screen.findByRole("heading", { name: "Held on platform and elsewhere" });
    const section = heading.closest("section")!;
    const rows = within(section).getAllByRole("row").slice(1);
    expect(rows.map((row) => within(row).getAllByRole("cell")[0].firstChild?.textContent)).toEqual([
      "Held on platform",
      "Held elsewhere",
    ]);
    expect(within(rows[0]).getByText("NZD 6000")).toBeInTheDocument();
    expect(within(rows[0]).queryByText(note)).not.toBeInTheDocument();
    expect(within(rows[1]).getByText("NZD 2500, USD 800")).toBeInTheDocument();
    expect(within(rows[1]).getByText(note)).toBeInTheDocument();

    const byType = screen.getByRole("heading", { name: "By account type" });
    expect(heading.compareDocumentPosition(byType) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("shows only the custody modes the customer has on the dashboard", async () => {
    renderAt("/dashboard", me, [], "External", {
      netWorth: withCustodyModes([{ custodyMode: "Custodial", currencies: [currency("NZD", 70, 0)] }]),
    });

    const section = (await screen.findByRole("heading", { name: "Held on platform and elsewhere" })).closest("section")!;
    expect(within(section).getByText("Held on platform")).toBeInTheDocument();
    expect(within(section).queryByText("Held elsewhere")).not.toBeInTheDocument();
    expect(screen.queryByText(note)).not.toBeInTheDocument();
  });

  it("shows no balances in the custody section when there is no entry", async () => {
    renderAt("/dashboard", me, [], "External", { netWorth: withCustodyModes([]) });

    const section = (await screen.findByRole("heading", { name: "Held on platform and elsewhere" })).closest("section")!;
    expect(within(section).getByText("No balances.")).toBeInTheDocument();
    expect(within(section).queryByRole("row")).not.toBeInTheDocument();
  });

  it("lists accounts in two groups from two filtered requests with the note under held elsewhere", async () => {
    const { fetchMock } = renderAt("/accounts", me, [], "External", {
      accounts: [
        { id: "account-1", name: "Everyday", type: "Bank", custodyMode: "External", currency: "NZD", cashBalance: 100, status: "Open" },
        { id: "account-2", name: "Managed", type: "Brokerage", custodyMode: "Custodial", currency: "USD", cashBalance: 70, status: "Open" },
      ],
    });

    const onPlatform = await screen.findByRole("heading", { name: "Held on platform" });
    const elsewhere = await screen.findByRole("heading", { name: "Held elsewhere" });
    expect(onPlatform.compareDocumentPosition(elsewhere) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    const platformSection = onPlatform.closest("section")!;
    const elsewhereSection = elsewhere.closest("section")!;
    expect(within(platformSection).getByRole("link", { name: "Managed" })).toBeInTheDocument();
    expect(within(platformSection).queryByRole("link", { name: "Everyday" })).not.toBeInTheDocument();
    expect(within(platformSection).queryByText(note)).not.toBeInTheDocument();
    expect(within(elsewhereSection).getByRole("link", { name: "Everyday" })).toBeInTheDocument();
    expect(within(elsewhereSection).getByText(note)).toBeInTheDocument();

    const accountUrls = urls(fetchMock).filter((url) => url.includes("/accounts?"));
    expect(accountUrls.some((url) => url.includes("custodyMode=Custodial") && url.includes("pageSize=100"))).toBe(true);
    expect(accountUrls.some((url) => url.includes("custodyMode=External") && url.includes("pageSize=100"))).toBe(true);
    expect(accountUrls.every((url) => url.includes("custodyMode="))).toBe(true);
  });

  it("hides an empty account group and shows no accounts when both are empty", async () => {
    renderAt("/accounts");

    expect(await screen.findByRole("heading", { name: "Held elsewhere" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Held on platform" })).not.toBeInTheDocument();
    cleanup();

    renderAt("/accounts", me, [], "External", { accounts: [] });
    expect(await screen.findByText("No accounts.")).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Held elsewhere" })).not.toBeInTheDocument();
  });

  it.each([
    ["External", true],
    ["Custodial", false],
  ])("shows the note on the account page of a %s account: %s", async (custodyMode, shown) => {
    renderAt("/accounts/account-1", me, [], custodyMode);

    expect(await screen.findByRole("heading", { name: "Everyday" })).toBeInTheDocument();
    if (shown) {
      expect(screen.getByText(note)).toBeInTheDocument();
    } else {
      expect(screen.queryByText(note)).not.toBeInTheDocument();
    }
  });

  it("shows not found for another account and no write controls", async () => {
    renderAt("/accounts/missing");
    expect(await screen.findByText("Not found")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /close/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /new account/i })).not.toBeInTheDocument();
  });

  it("shows an internal transfer as Transfer with the visible leg amount and no counterparty", async () => {
    renderAt("/accounts/account-1", me, [
      {
        id: "71111111-1111-1111-1111-111111111111",
        tenantId: "22222222-2222-2222-2222-222222222222",
        type: "InternalTransfer",
        bookedAt: "2026-11-09T20:00:00Z",
        memo: null,
        reference: null,
        originalTransactionId: null,
        reversedByTransactionId: null,
        rowVersion: "AAAA",
        cash: [{ accountId: "account-1", customerId: me.id, amount: 500, currency: "NZD" }],
        security: [],
      },
    ]);

    const row = await screen.findByRole("row", { name: /Transfer/ });
    expect(row).toHaveTextContent("Transfer");
    expect(row).not.toHaveTextContent("InternalTransfer");
    expect(row).toHaveTextContent("500 NZD");
    expect(row).toHaveTextContent("2026-11-10 09:00");
    expect(screen.getByRole("columnheader", { name: "Amount" })).toBeInTheDocument();
  });

  it("shows an FX conversion as Currency exchange with this account's leg and the rate line", async () => {
    renderAt("/accounts/account-1", me, [
      {
        id: "72222222-2222-2222-2222-222222222222",
        tenantId: "22222222-2222-2222-2222-222222222222",
        type: "FxConversion",
        bookedAt: "2026-11-09T20:00:00Z",
        memo: null,
        reference: null,
        originalTransactionId: null,
        reversedByTransactionId: null,
        rowVersion: "AAAA",
        cash: [
          { accountId: "account-1", customerId: me.id, amount: -845.74, currency: "NZD" },
          { accountId: "account-2", customerId: me.id, amount: 500, currency: "USD" },
        ],
        security: [],
        fx: { baseCurrency: "NZD", quoteCurrency: "USD", rate: 0.5912, rateAsOfDate: "2026-11-09" },
      },
    ]);

    const row = await screen.findByRole("row", { name: /Currency exchange/ });
    expect(row).not.toHaveTextContent("FxConversion");
    expect(row).toHaveTextContent("-845.74 NZD");
    expect(row).not.toHaveTextContent("500 USD");
    expect(row).toHaveTextContent("1 NZD = 0.5912 USD (as of 2026-11-09)");
  });

  it("shows an adjustment as Adjustment with its memo and no reason, and its reversal as Reversal (C18)", async () => {
    const adjustment = {
      id: "73333333-3333-3333-3333-333333333333",
      tenantId: "22222222-2222-2222-2222-222222222222",
      type: "Adjustment",
      bookedAt: "2026-11-19T03:00:00Z",
      memo: "Buy of 18 November was filled at 25.40",
      reference: null,
      originalTransactionId: null,
      reversedByTransactionId: null,
      rowVersion: "AAAA",
      cash: [{ accountId: "account-1", customerId: me.id, amount: -0.6, currency: "NZD" }],
      security: [],
      adjustment: { reason: "EntryError" },
    };
    renderAt("/accounts/account-1", me, [
      adjustment,
      {
        ...adjustment,
        id: "74444444-4444-4444-4444-444444444444",
        type: "Reversal",
        memo: null,
        originalTransactionId: adjustment.id,
        cash: [{ accountId: "account-1", customerId: me.id, amount: 0.6, currency: "NZD" }],
      },
    ]);

    const row = await screen.findByRole("row", { name: /^Adjustment/ });
    expect(row).toHaveTextContent("Buy of 18 November was filled at 25.40");
    expect(row).toHaveTextContent("-0.6 NZD");
    expect(row).not.toHaveTextContent("Entry error");
    expect(row).not.toHaveTextContent("EntryError");
    expect(screen.getByRole("row", { name: /^Reversal/ })).toHaveTextContent("0.6 NZD");
  });

  it("labels a bank deposit as Deposit with its value date and no payer details, and keeps Reversal and TransferIn (C21)", async () => {
    const deposit = {
      id: "75555555-5555-5555-5555-555555555555",
      tenantId: "22222222-2222-2222-2222-222222222222",
      type: "TransferIn",
      bookedAt: "2026-11-20T02:01:08Z",
      memo: "Bank deposit",
      reference: null,
      originalTransactionId: null,
      reversedByTransactionId: null,
      rowVersion: "AAAA",
      source: { kind: "BankFeed", id: "3f2c0000-0000-0000-0000-0000000000e1" },
      cash: [{ accountId: "account-1", customerId: me.id, amount: 500, currency: "NZD" }],
      security: [],
      bankReceipt: { valueDate: "2026-11-19", reference: "K7M3Q9PXB", payerName: "J SMITH", payerAccount: "****4200" },
    };
    renderAt("/accounts/account-1", me, [
      deposit,
      {
        ...deposit,
        id: "76666666-6666-6666-6666-666666666666",
        type: "Reversal",
        originalTransactionId: deposit.id,
        cash: [{ accountId: "account-1", customerId: me.id, amount: -500, currency: "NZD" }],
      },
      {
        ...deposit,
        id: "77777777-7777-7777-7777-777777777777",
        memo: null,
        source: { kind: "Manual", id: null },
        bankReceipt: null,
        cash: [{ accountId: "account-1", customerId: me.id, amount: 20, currency: "NZD" }],
      },
    ]);

    const row = await screen.findByRole("row", { name: /^Deposit/ });
    expect(row).toHaveTextContent("Value date 2026-11-19");
    expect(row).toHaveTextContent("500 NZD");
    expect(row).not.toHaveTextContent("J SMITH");
    expect(row).not.toHaveTextContent("****4200");
    expect(screen.getByRole("row", { name: /^Reversal/ })).not.toHaveTextContent("Deposit");
    expect(screen.getByRole("row", { name: /^TransferIn/ })).toHaveTextContent("20 NZD");
  });

  it("generates a customer report with the caller's id and no schedule controls", async () => {
    const { fetchMock } = renderAt("/reports");
    await openSelect("Report");
    expect(screen.getByRole("option", { name: "Customer Transactions" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /schedule/i })).not.toBeInTheDocument();
    await pickOption("Mystery");
    expect(screen.getByText("parameters not available in this version")).toBeInTheDocument();
    await chooseOption("Report", "Customer Transactions");
    fireEvent.change(screen.getByLabelText("From"), { target: { value: "2026-01-01" } });
    fireEvent.change(screen.getByLabelText("To"), { target: { value: "2026-03-31" } });
    fireEvent.submit(screen.getByRole("button", { name: "Generate" }).closest("form")!);

    await waitFor(() => {
      expect(fetchMock.mock.calls.some(([input, init]) => callOf(input, init).url.endsWith("/reports/request"))).toBe(true);
    });
    const requestCall = fetchMock.mock.calls.find(([input, init]) => callOf(input, init).url.endsWith("/reports/request"));
    expect(requestCall).toBeTruthy();
    const request = callOf(requestCall![0], requestCall![1]);
    const raw = requestCall![0] instanceof Request
      ? await requestCall![0].clone().text()
      : String(requestCall![1]?.body);
    const body = JSON.parse(raw);
    expect(body.parameters.customerId).toBe(me.id);
    expect(request.headers.get("X-MyWealth-Request")).toBe("1");
    expect(request.headers.get("Authorization")).toBeNull();
    expect(request.credentials).toBe("include");
  });

  it("starts login again after a password change", async () => {
    renderAt("/profile");
    fireEvent.change(await screen.findByLabelText("Current password"), { target: { value: "Password1!" } });
    fireEvent.change(screen.getByLabelText("New password"), { target: { value: "Password2!" } });
    fireEvent.submit(screen.getByRole("button", { name: "Change password" }).closest("form")!);
    await waitFor(() => {
      expect(window.location.assign).toHaveBeenCalledWith(expect.stringContaining("/bff/login?returnUrl="));
    });
  });
});

// client-money-accounts C19, C20: "How to pay in" on a Custodial account.
describe("Customer Portal deposit instructions", () => {
  const reference = "K7M3Q9PXB";
  const custodialUsd = { type: "Brokerage", currency: "USD", depositReference: reference };
  const instructions = {
    accountId: "account-1",
    currency: "USD",
    reference,
    bankName: "Simulator Bank",
    accountName: "North Advisory Ltd Client Funds Trust Account",
    accountNumber: "02-0100-0123456-00",
  };
  const payInSentence = "Pay in USD only, and quote the reference exactly so the payment is matched to this account.";
  const notAvailable = "Deposit instructions are not available yet. Contact your adviser.";

  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: "/accounts/account-1",
      search: "",
      href: "http://localhost:3000/accounts/account-1",
      assign: vi.fn(),
    });
  });

  function instructionUrls(fetchMock: ReturnType<typeof vi.mocked<typeof fetch>>) {
    return urls(fetchMock).filter((url) => url.includes("/deposit-instructions"));
  }

  it("shows where to pay and which reference to quote on an Open Custodial account", async () => {
    renderAt("/accounts/account-1", me, [], "Custodial", {
      account: custodialUsd,
      depositInstructions: () => json(instructions),
    });

    const section = (await screen.findByRole("heading", { name: "How to pay in" })).closest("section")!;
    expect(await within(section).findByText("Simulator Bank")).toBeInTheDocument();
    expect(within(section).getByText("North Advisory Ltd Client Funds Trust Account")).toBeInTheDocument();
    expect(within(section).getByText("02-0100-0123456-00")).toBeInTheDocument();
    expect(within(section).getByText(reference)).toBeInTheDocument();
    expect(within(section).getByText(payInSentence)).toBeInTheDocument();
  });

  it("says the instructions are not available yet when the route returns 404", async () => {
    renderAt("/accounts/account-1", me, [], "Custodial", {
      account: custodialUsd,
      depositInstructions: () => new Response("", { status: 404 }),
    });

    const section = (await screen.findByRole("heading", { name: "How to pay in" })).closest("section")!;
    expect(await within(section).findByText(notAvailable)).toBeInTheDocument();
  });

  it("shows no section and calls no route on a Closed Custodial account", async () => {
    const { fetchMock } = renderAt("/accounts/account-1", me, [], "Custodial", {
      account: { ...custodialUsd, status: "Closed" },
      depositInstructions: () => json(instructions),
    });

    expect(await screen.findByRole("heading", { name: "Holdings" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "How to pay in" })).not.toBeInTheDocument();
    expect(screen.queryByText(notAvailable)).not.toBeInTheDocument();
    expect(instructionUrls(fetchMock)).toEqual([]);
  });

  it("shows no section and calls no route on an External account", async () => {
    const { fetchMock } = renderAt("/accounts/account-1", me, [], "External", {
      depositInstructions: () => json(instructions),
    });

    expect(await screen.findByRole("heading", { name: "Holdings" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "How to pay in" })).not.toBeInTheDocument();
    expect(instructionUrls(fetchMock)).toEqual([]);
  });
});

describe("Customer Portal bank accounts and withdrawals (withdrawals C22–C25)", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: "/accounts/account-1",
      search: "",
      href: "http://localhost:3000/accounts/account-1",
      assign: vi.fn(),
    });
  });

  const custodialUsd = { type: "Brokerage", currency: "USD", depositReference: "K7M3Q9PXB" };

  function payee(status: string, accountNumber: string) {
    return {
      id: `payee-${accountNumber}`,
      customerId: me.id,
      currency: "USD",
      bankName: "ANZ",
      accountName: "J Smith",
      accountNumber,
      status,
      added: { by: { id: "a", name: "Ann Adviser" }, at: "2026-11-20T03:00:00Z" },
      review: null,
      removal: null,
      rowVersion: "AAAA",
    };
  }

  function withdrawal(status: string, requestedAt: string, id: string) {
    return {
      id,
      accountId: "account-1",
      customerId: me.id,
      nominatedBankAccount: { id: "payee", bankName: "ANZ", accountName: "J Smith", accountNumber: "****8900" },
      amount: 300,
      currency: "USD",
      reference: "K7M3Q9PXB",
      memo: null,
      status,
      requestedBy: { id: "a", name: "Ann Adviser" },
      requestedAt,
      decision: null,
      payment: null,
      payoutTransactionId: null,
      returnTransactionId: null,
      failureReason: null,
      returnReason: null,
      rowVersion: "AAAA",
    };
  }

  it("lists Pending and Verified bank accounts on the profile, with no add or remove control (C22)", async () => {
    renderAt("/profile", me, [], "External", {
      nominated: [payee("Verified", "06-0123-0456789-00"), payee("Pending", "111"), payee("Removed", "222")],
    });

    const section = (await screen.findByRole("heading", { name: "Bank accounts" })).closest("section")!;
    expect(await within(section).findByText("06-0123-0456789-00")).toBeInTheDocument();
    expect(within(section).getByText("Verified")).toBeInTheDocument();
    expect(within(section).getByText("Being checked")).toBeInTheDocument();
    expect(within(section).queryByText("222")).not.toBeInTheDocument();
    expect(within(section).getByText("To change your bank accounts, contact your adviser.")).toBeInTheDocument();
    expect(within(section).queryByRole("button")).not.toBeInTheDocument();
  });

  it("shows Available to withdraw, and Reserved when cash is reserved (C23, C33)", async () => {
    renderAt("/accounts/account-1", me, [], "Custodial", {
      account: custodialUsd,
      cash: { currency: "USD", cashBalance: 1000, reservedCash: 300, availableCash: 700 },
    });

    expect(await screen.findByText("Available to withdraw")).toBeInTheDocument();
    expect(screen.getByText("700 USD")).toBeInTheDocument();
    expect(screen.getByText("Reserved")).toBeInTheDocument();
    expect(screen.getByText("300 USD")).toBeInTheDocument();
    expect(screen.getByText("Cash")).toBeInTheDocument();
    expect(screen.queryByText("From sales, not settled yet")).not.toBeInTheDocument();
  });

  it("shows the cash from sales that has not settled, and Available to withdraw without it (C33)", async () => {
    renderAt("/accounts/account-1", me, [], "Custodial", {
      account: custodialUsd,
      cash: { currency: "USD", cashBalance: 6200, reservedCash: 1055.25, availableCash: 5144.75, unsettledCash: 2000, withdrawableCash: 3144.75 },
    });

    expect(await screen.findByText("From sales, not settled yet")).toBeInTheDocument();
    expect(screen.getByText("2000 USD")).toBeInTheDocument();
    expect(screen.getByText("1055.25 USD")).toBeInTheDocument();
    expect(screen.getByText("3144.75 USD")).toBeInTheDocument();
    expect(screen.queryByText("5144.75 USD")).not.toBeInTheDocument();
  });

  it("lists open orders and those that ended in the last 30 days, in the Customer's words, with no action (C32)", async () => {
    const days = (count: number) => new Date(Date.now() - count * 86400000).toISOString();
    const order = (id: string, overrides: Record<string, unknown>) => ({
      id,
      instrument: { id: "i-1", symbol: "ACME.SIM", name: "Acme Holdings" },
      side: "Buy",
      quantity: 10,
      filledQuantity: 0,
      status: "Working",
      placedAt: "2026-11-23T02:10:00Z",
      closedAt: null,
      ...overrides,
    });
    const { fetchMock } = renderAt("/accounts/account-1", me, [], "Custodial", {
      account: custodialUsd,
      orders: [
        order("o-1", { status: "Placed" }),
        order("o-2", { status: "PartlyFilled", filledQuantity: 4, side: "Sell", quantity: 10 }),
        order("o-3", { status: "Filled", filledQuantity: 10, closedAt: days(2) }),
        order("o-4", { status: "Rejected", closedAt: days(3), instrument: { id: "i-2", symbol: "BETA.SIM", name: "Beta" } }),
        order("o-5", { status: "Cancelled", closedAt: days(4) }),
        order("o-6", { status: "Expired", closedAt: days(5) }),
        order("o-7", { status: "Filled", closedAt: days(40), instrument: { id: "i-3", symbol: "OLD.SIM", name: "Old" } }),
      ],
    });

    const table = await screen.findByRole("table", { name: "Orders" });
    const rows = within(table).getAllByRole("row").slice(1).map((row) =>
      within(row).getAllByRole("cell").slice(0, 2).map((cell) => cell.textContent));
    expect(rows).toEqual([
      ["Buy 10 ACME.SIM", "With the broker"],
      ["Sell 10 ACME.SIM", "Partly filled (4 of 10)"],
      ["Buy 10 ACME.SIM", "Done"],
      ["Buy 10 BETA.SIM", "Not accepted"],
      ["Buy 10 ACME.SIM", "Cancelled"],
      ["Buy 10 ACME.SIM", "Expired"],
    ]);
    expect(within(table).queryByText(/OLD\.SIM/)).not.toBeInTheDocument();
    expect(within(table).queryByRole("button")).not.toBeInTheDocument();
    expect(within(table).queryByRole("link")).not.toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([input]) => String(input instanceof Request ? input.url : input).includes("/orders?accountId=account-1")))
      .toBe(true);
  });

  it("labels the order of a rebalance with no link and no action (C37)", async () => {
    const order = (id: string, rebalanceId: string | null) => ({
      id,
      instrument: { id: "i-1", symbol: "ACME.SIM", name: "Acme Holdings" },
      side: "Buy",
      quantity: 10,
      filledQuantity: 0,
      status: "Working",
      placedAt: "2026-11-23T02:10:00Z",
      closedAt: null,
      rebalanceId,
    });
    renderAt("/accounts/account-1", me, [], "Custodial", {
      account: custodialUsd,
      orders: [order("o-1", "12121212-0000-0000-0000-000000000001"), order("o-2", null)],
    });

    const table = await screen.findByRole("table", { name: "Orders" });
    const rows = within(table).getAllByRole("row").slice(1);
    expect(within(rows[0]).getByText("Rebalance")).toBeInTheDocument();
    expect(within(rows[1]).queryByText("Rebalance")).not.toBeInTheDocument();
    expect(within(table).queryByRole("link")).not.toBeInTheDocument();
    expect(within(table).queryByRole("button")).not.toBeInTheDocument();
  });

  it("labels a migrated opening balance and holdings with where they came from (C38)", async () => {
    const migration = { asOfDate: "2026-11-20", previousProvider: "Acme Wealth" };
    const opening = {
      id: "t-1", type: "Opening", bookedAt: "2026-11-24T02:15:00Z", memo: "Opening balance",
      cash: [{ accountId: "account-1", amount: 150000, currency: "USD" }], security: [],
      source: { kind: "Migration", id: "b-1" }, migration,
    };
    const holdings = {
      id: "t-2", type: "OpeningHoldings", bookedAt: "2026-11-24T02:15:00Z", memo: "Opening balance", cash: [],
      security: [{ accountId: "account-1", instrumentId: "i-1", quantity: 800 }],
      source: { kind: "Migration", id: "b-1" }, migration,
    };
    const typed = { id: "t-3", type: "Opening", bookedAt: "2026-10-01T02:13:00Z", memo: null, cash: [], security: [], source: { kind: "Seed", id: null } };
    renderAt("/accounts/account-1", me, [opening, holdings, typed], "Custodial", { account: custodialUsd, orders: [] });

    expect(await screen.findByText("Opening balance")).toBeInTheDocument();
    expect(screen.getByText("Holdings transferred in")).toBeInTheDocument();
    expect(screen.getAllByText("From Acme Wealth, as at 2026-11-20")).toHaveLength(2);
    expect(screen.getByText("Opening")).toBeInTheDocument();
  });

  it("asks for no orders on an External account (C32)", async () => {
    const { fetchMock } = renderAt("/accounts/account-1");

    expect(await screen.findByText("No holdings.")).toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([input]) => String(input instanceof Request ? input.url : input).includes("/orders")))
      .toBe(false);
    expect(screen.queryByRole("table", { name: "Orders" })).not.toBeInTheDocument();
  });

  it("labels a fill by what was bought or sold, with its trade date, settlement and contract note (C34)", async () => {
    const fill = (id: string, type: string, quantity: number, settledAt: string | null) => ({
      id,
      type,
      bookedAt: "2026-11-23T02:13:00Z",
      memo: null,
      cash: [{ accountId: "account-1", amount: type === "Buy" ? -400.48 : 400.48, currency: "USD" }],
      security: [{ accountId: "account-1", instrumentId: "i-1", quantity }],
      source: { kind: "OrderFill", id: "o-1" },
      execution: { orderId: "o-1", price: 100.12, tradeDate: "2026-11-23", settlementDate: "2026-11-25", brokerReference: "CN-1", settledAt },
    });
    const hand = {
      id: "t-3", type: "Buy", bookedAt: "2026-10-01T02:13:00Z", memo: null, cash: [], security: [], source: { kind: "Rebalance", id: "r" },
    };
    renderAt("/accounts/account-1", me, [fill("t-1", "Buy", 4, null), fill("t-2", "Sell", -4, "2026-11-25T00:10:00Z"), hand], "Custodial", {
      account: custodialUsd,
      orders: [{
        id: "o-1", instrument: { id: "i-1", symbol: "ACME.SIM", name: "Acme Holdings" }, side: "Buy", quantity: 4, filledQuantity: 4,
        status: "Filled", placedAt: "2026-11-23T02:10:00Z", closedAt: "2026-11-23T02:13:00Z",
      }],
    });

    expect(await screen.findByText("Bought 4 ACME.SIM at 100.12")).toBeInTheDocument();
    expect(screen.getByText("Sold 4 ACME.SIM at 100.12")).toBeInTheDocument();
    expect(screen.getByText("Settles 25 Nov")).toBeInTheDocument();
    expect(screen.getByText("Settled")).toBeInTheDocument();
    expect(screen.getAllByText("Trade date 2026-11-23")).toHaveLength(2);
    expect(screen.getAllByText("Contract note CN-1")).toHaveLength(2);
    // A trade that is not a fill keeps its row.
    expect(screen.getByText("Buy")).toBeInTheDocument();
  });

  it("labels the Reversal of a broken fill, and shows Cancelled on the fill in place of the settlement (C36)", async () => {
    const execution = {
      orderId: "o-1", price: 100.12, tradeDate: "2026-11-23", settlementDate: "2026-11-25", brokerReference: "CN-1", settledAt: null,
      brokenAt: "2026-11-24T04:10:02Z",
    };
    const fill = {
      id: "t-1", type: "Buy", bookedAt: "2026-11-23T02:13:00Z", memo: null,
      cash: [{ accountId: "account-1", amount: -405.48, currency: "USD" }],
      security: [{ accountId: "account-1", instrumentId: "i-1", quantity: 4 }],
      source: { kind: "OrderFill", id: "o-1" },
      execution,
    };
    const reversal = {
      ...fill, id: "t-2", type: "Reversal", bookedAt: "2026-11-24T04:10:03Z",
      cash: [{ accountId: "account-1", amount: 405.48, currency: "USD" }],
      security: [{ accountId: "account-1", instrumentId: "i-1", quantity: -4 }],
    };
    renderAt("/accounts/account-1", me, [reversal, fill], "Custodial", {
      account: custodialUsd,
      orders: [{
        id: "o-1", instrument: { id: "i-1", symbol: "ACME.SIM", name: "Acme Holdings" }, side: "Buy", quantity: 4, filledQuantity: 0,
        status: "Cancelled", placedAt: "2026-11-23T02:10:00Z", closedAt: "2026-11-24T04:10:03Z",
      }],
    });

    expect(await screen.findByText("Trade cancelled by the broker: Bought 4 ACME.SIM at 100.12")).toBeInTheDocument();
    const fillRow = screen.getByText("Bought 4 ACME.SIM at 100.12").closest("tr")!;
    expect(within(fillRow).getByText("Cancelled")).toBeInTheDocument();
    expect(screen.queryByText("Settles 25 Nov")).not.toBeInTheDocument();
  });

  it("shows the quantity a sell order or a transfer out reserves under a holding (C35, in-kind-transfers C41)", async () => {
    renderAt("/accounts/account-1", me, [], "Custodial", {
      account: custodialUsd,
      holdings: [
        { id: "h-1", instrumentId: "i-1", symbol: "ACME.SIM", name: "Acme Holdings", quantity: 100, reservedQuantity: 40 },
        { id: "h-2", instrumentId: "i-2", symbol: "BETA.SIM", name: "Beta", quantity: 5, reservedQuantity: 0 },
      ],
    });

    expect(await screen.findByText("40 reserved for a sale or a transfer")).toBeInTheDocument();
    expect(screen.getAllByText(/reserved for a sale or a transfer/)).toHaveLength(1);
  });

  it("hides Reserved when nothing is reserved (C23)", async () => {
    renderAt("/accounts/account-1", me, [], "Custodial", {
      account: custodialUsd,
      cash: { currency: "USD", cashBalance: 1000, reservedCash: 0, availableCash: 1000 },
    });

    expect(await screen.findByText("Available to withdraw")).toBeInTheDocument();
    expect(screen.queryByText("Reserved")).not.toBeInTheDocument();
  });

  it("lists open withdrawals and those of the last 90 days, with no action (C24)", async () => {
    const recent = new Date(Date.now() - 5 * 86400000).toISOString();
    const old = new Date(Date.now() - 120 * 86400000).toISOString();
    renderAt("/accounts/account-1", me, [], "Custodial", {
      account: custodialUsd,
      withdrawals: [
        withdrawal("Requested", old, "w-open"),
        withdrawal("Paid", recent, "w-recent"),
        withdrawal("Rejected", old, "w-old"),
      ],
    });

    const section = (await screen.findByRole("heading", { name: "Withdrawals" })).closest("section")!;
    expect(await within(section).findByText("Waiting for approval")).toBeInTheDocument();
    expect(within(section).getByText("Paid")).toBeInTheDocument();
    expect(within(section).queryByText("Rejected")).not.toBeInTheDocument();
    expect(within(section).getAllByText("****8900")).toHaveLength(2);
    expect(within(section).queryByRole("button")).not.toBeInTheDocument();
  });

  it("labels a payout as Withdrawal with its value date and keeps Reversal and TransferOut (C25)", async () => {
    const payout = {
      id: "75555555-5555-5555-5555-555555555555",
      tenantId: "22222222-2222-2222-2222-222222222222",
      type: "TransferOut",
      bookedAt: "2026-11-20T04:07:02Z",
      memo: "Withdrawal",
      reference: null,
      originalTransactionId: null,
      reversedByTransactionId: null,
      rowVersion: "AAAA",
      source: { kind: "Payment", id: "w" },
      cash: [{ accountId: "account-1", customerId: me.id, amount: -300, currency: "USD" }],
      security: [],
      bankReceipt: null,
      payment: { valueDate: "2026-11-20", reference: "K7M3Q9PXB", payeeName: "J Smith", payeeAccount: "****8900" },
    };
    renderAt("/accounts/account-1", me, [
      payout,
      { ...payout, id: "76666666-6666-6666-6666-666666666666", type: "Reversal", originalTransactionId: payout.id,
        cash: [{ accountId: "account-1", customerId: me.id, amount: 300, currency: "USD" }] },
      { ...payout, id: "77777777-7777-7777-7777-777777777777", source: { kind: "Manual", id: null }, payment: null, memo: null,
        cash: [{ accountId: "account-1", customerId: me.id, amount: -20, currency: "USD" }] },
    ], "Custodial", { account: custodialUsd });

    const row = await screen.findByRole("row", { name: /^Withdrawal/ });
    expect(row).toHaveTextContent("Value date 2026-11-20");
    expect(row).toHaveTextContent("-300 USD");
    expect(screen.getByRole("row", { name: /^Reversal/ })).not.toHaveTextContent("Withdrawal");
    expect(screen.getByRole("row", { name: /^TransferOut/ })).toHaveTextContent("-20 USD");
  });
});

describe("Customer Portal account closure (account-closure C26, C27)", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: "/accounts/account-1",
      search: "",
      href: "http://localhost:3000/accounts/account-1",
      assign: vi.fn(),
    });
  });

  const closure = {
    id: "closure-1",
    status: "InProgress",
    payoutAmount: 300,
    withdrawalId: "w-closure",
    requestedAt: "2026-11-22T03:00:00Z",
    completedAt: null,
  };
  const custodialUsd = { type: "Brokerage", currency: "USD", depositReference: "K7M3Q9PXB" };
  const closureWithdrawal = {
    id: "w-closure",
    accountId: "account-1",
    customerId: me.id,
    nominatedBankAccount: { id: "payee", bankName: "ANZ", accountName: "J Smith", accountNumber: "****8900" },
    amount: 300,
    currency: "USD",
    reference: "K7M3Q9PXB",
    memo: null,
    status: "Requested",
    requestedBy: { id: "a", name: "Ann Adviser" },
    requestedAt: "2026-11-22T03:00:00Z",
    decision: null,
    payment: null,
    payoutTransactionId: null,
    returnTransactionId: null,
    failureReason: null,
    returnReason: null,
    accountClosureId: "closure-1",
    rowVersion: "AAAA",
  };

  it("notes that a closing account is being closed, what is paid and to which bank account (C26)", async () => {
    renderAt("/accounts/account-1", me, [], "Custodial", {
      account: { ...custodialUsd, closure },
      cash: { currency: "USD", cashBalance: 300.004, reservedCash: 300, availableCash: 0.004 },
      withdrawals: [closureWithdrawal],
    });

    expect(await screen.findByText("This account is being closed. 300.00 USD is being paid to your bank account ****8900."))
      .toBeInTheDocument();
  });

  it("notes when a Closed account was closed by its closure (C26)", async () => {
    renderAt("/accounts/account-1", me, [], "Custodial", {
      account: { ...custodialUsd, status: "Closed", closure: { ...closure, status: "Completed", completedAt: "2026-11-23T03:00:00Z" } },
      withdrawals: [{ ...closureWithdrawal, status: "Paid" }],
    });

    expect(await screen.findByText("Closed on 2026-11-23.")).toBeInTheDocument();
    expect(screen.queryByText(/is being closed/)).not.toBeInTheDocument();
  });

  it("shows no note for an account without a closure, or with a Cancelled one (C26)", async () => {
    renderAt("/accounts/account-1", me, [], "Custodial", {
      account: { ...custodialUsd, closure: { ...closure, status: "Cancelled" } },
    });

    expect(await screen.findByText("Available to withdraw")).toBeInTheDocument();
    expect(screen.queryByText(/is being closed/)).not.toBeInTheDocument();
    expect(screen.queryByText(/^Closed on/)).not.toBeInTheDocument();
  });

  it("labels the closure's write-off Rounding write-off, and keeps the label of another CloseOut (C27)", async () => {
    const writeOff = {
      id: "78888888-8888-8888-8888-888888888888",
      tenantId: "22222222-2222-2222-2222-222222222222",
      type: "CloseOut",
      bookedAt: "2026-11-23T03:00:00Z",
      memo: "Account closure remainder",
      reference: null,
      originalTransactionId: null,
      reversedByTransactionId: null,
      rowVersion: "AAAA",
      source: { kind: "AccountClosure", id: "closure-1" },
      cash: [{ accountId: "account-1", customerId: me.id, amount: -0.004, currency: "USD" }],
      security: [],
      bankReceipt: null,
      payment: null,
    };
    renderAt("/accounts/account-1", me, [
      writeOff,
      { ...writeOff, id: "79999999-9999-9999-9999-999999999999", source: { kind: "Manual", id: null }, memo: null },
    ], "Custodial", { account: custodialUsd });

    expect(await screen.findByRole("row", { name: /^Rounding write-off/ })).toHaveTextContent("-0.004 USD");
    expect(screen.getByRole("row", { name: /^CloseOut/ })).toBeInTheDocument();
  });
});

describe("Customer Portal interest (interest-run C28–C30)", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: "/profile",
      search: "",
      href: "http://localhost:3000/profile",
      assign: vi.fn(),
    });
  });

  const custodialUsd = { type: "Brokerage", currency: "USD", depositReference: "K7M3Q9PXB" };
  const posting = {
    id: "71111111-1111-1111-1111-111111111111",
    tenantId: "22222222-2222-2222-2222-222222222222",
    type: "Interest",
    bookedAt: "2027-01-01T00:30:04Z",
    memo: "Interest",
    reference: null,
    originalTransactionId: null,
    reversedByTransactionId: null,
    rowVersion: "AAAA",
    source: { kind: "InterestRun", id: "run-1" },
    interest: { periodFrom: "2026-12-02", periodTo: "2026-12-31", days: 30, accruedAmount: 12.328767, grossAmount: 12.33 },
    cash: [
      { accountId: "account-1", customerId: me.id, role: "Principal", amount: 12.33, currency: "USD", tax: null },
      {
        accountId: "account-1", customerId: me.id, role: "Tax", amount: -2.16, currency: "USD",
        tax: { kind: "Rwt", ratePercent: 17.5, basisAmount: 12.33, amount: 2.16, currency: "USD" },
      },
    ],
    security: [],
    bankReceipt: null,
    payment: null,
  };

  it("shows the Customer's tax details, read only (C28)", async () => {
    renderAt("/profile", me, [], "External", {
      taxProfile: {
        taxResidency: "NewZealand",
        irdNumberHeld: true,
        rwtRatePercent: 17.5,
        withholding: { kind: "Rwt", ratePercent: 17.5, basis: "Elected" },
        updatedBy: null,
        updatedAt: "2026-11-22T03:00:00Z",
        rowVersion: "AAAB",
      },
    });

    expect(await screen.findByText("Tax details")).toBeInTheDocument();
    expect(screen.getByText("New Zealand")).toBeInTheDocument();
    expect(screen.getByText("On file")).toBeInTheDocument();
    expect(screen.getByText("17.5 %")).toBeInTheDocument();
    expect(screen.getByText("Tax taken from your interest: 17.5 %")).toBeInTheDocument();
    expect(screen.getByText("To change these details, contact your adviser.")).toBeInTheDocument();
  });

  it("shows Not on file and the non-declaration rate when nothing is saved (C28)", async () => {
    renderAt("/profile", me);

    expect(await screen.findByText("Not on file")).toBeInTheDocument();
    expect(screen.getByText("Not chosen")).toBeInTheDocument();
    expect(screen.getByText("Tax taken from your interest: 45 %")).toBeInTheDocument();
  });

  it("shows the interest earned so far on a Custodial account, rounded down to the cent (C29)", async () => {
    renderAt("/accounts/account-1", me, [], "Custodial", {
      account: { ...custodialUsd, accruedInterest: 4.5295479, interestAccruedThrough: "2026-12-12" },
      cash: { currency: "USD", cashBalance: 5000, reservedCash: 0, availableCash: 5000 },
    });

    expect(await screen.findByText("Interest earned so far this period: 4.52 USD")).toBeInTheDocument();
    expect(screen.getByText("Paid after the end of the month.")).toBeInTheDocument();
  });

  it("shows no interest line without accrued interest (C29)", async () => {
    renderAt("/accounts/account-1", me, [], "Custodial", {
      account: { ...custodialUsd, accruedInterest: 0, interestAccruedThrough: null },
      cash: { currency: "USD", cashBalance: 5000, reservedCash: 0, availableCash: 5000 },
    });

    expect(await screen.findByText("Available to withdraw")).toBeInTheDocument();
    expect(screen.queryByText(/Interest earned so far/)).not.toBeInTheDocument();
  });

  it("labels an interest posting with its period, gross, tax withheld and net (C30)", async () => {
    const handInterest = {
      ...posting,
      id: "72222222-2222-2222-2222-222222222222",
      source: { kind: "Manual", id: null },
      interest: null,
      cash: [{ accountId: "account-1", customerId: me.id, role: "Principal", amount: 5, currency: "USD", tax: null }],
    };
    renderAt("/accounts/account-1", me, [posting, handInterest], "Custodial", {
      account: custodialUsd,
      cash: { currency: "USD", cashBalance: 5000, reservedCash: 0, availableCash: 5000 },
    });

    const row = await screen.findByRole("row", { name: /2026-12-02 to 2026-12-31/ });
    expect(row).toHaveTextContent("Interest");
    expect(row).toHaveTextContent("Gross 12.33 USD");
    expect(row).toHaveTextContent("Tax withheld (RWT 17.5 %) 2.16 USD");
    expect(row).toHaveTextContent("10.17 USD");
    const hand = screen.getAllByRole("row").find((item) => item.textContent?.includes("5 USD"));
    expect(hand).toBeDefined();
    expect(hand).not.toHaveTextContent("Tax withheld");
  });
});

describe("Customer Portal corporate actions (corporate-actions C31)", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: "/accounts/account-1",
      search: "",
      href: "http://localhost:3000/accounts/account-1",
      assign: vi.fn(),
    });
  });

  const custodialUsd = { type: "Brokerage", currency: "USD", depositReference: "K7M3Q9PXB" };

  function entitlementPosting(id: string, kind: string, ratioNew: number, ratioHeld: number, before: number, after: number) {
    return {
      id,
      tenantId: "22222222-2222-2222-2222-222222222222",
      type: kind === "Bonus" ? "Bonus" : "Split",
      bookedAt: "2026-11-24T03:41:02Z",
      memo: `${kind} ${ratioNew} for ${ratioHeld}`,
      reference: null,
      originalTransactionId: null,
      reversedByTransactionId: null,
      rowVersion: "AAAA",
      source: { kind: "CorporateAction", id: "ca-1" },
      corporateAction: {
        id: "ca-1", kind, ratioNew, ratioHeld, exDate: "2026-11-24", recordDate: null, paymentDate: null,
        quantityBefore: before, quantityAfter: after,
      },
      cash: [],
      security: [{ accountId: "account-1", customerId: me.id, instrumentId: "i-1", quantity: after - before, cost: 0, costCurrency: "USD" }],
      bankReceipt: null,
      payment: null,
      interest: null,
    };
  }

  it("labels an entitlement posting with its kind and ratio, the quantities and the ex-date, and no cash amount", async () => {
    const handSplit = {
      ...entitlementPosting("74444444-4444-4444-4444-444444444444", "Split", 2, 1, 100, 200),
      source: { kind: "Manual", id: null },
      corporateAction: null,
      memo: "Hand split",
    };
    renderAt("/accounts/account-1", me, [
      entitlementPosting("71111111-1111-1111-1111-111111111111", "Split", 2, 1, 100, 200),
      entitlementPosting("72222222-2222-2222-2222-222222222222", "Consolidation", 1, 10, 105, 10.5),
      entitlementPosting("73333333-3333-3333-3333-333333333333", "Bonus", 1, 5, 12.34567891, 14.81481469),
      handSplit,
    ], "Custodial", {
      account: custodialUsd,
      cash: { currency: "USD", cashBalance: 0, reservedCash: 0, availableCash: 0 },
    });

    const split = await screen.findByRole("row", { name: /Share split 2 for 1/ });
    expect(split).toHaveTextContent("100 → 200 shares");
    expect(split).toHaveTextContent("Ex-date 2026-11-24");
    expect(split).not.toHaveTextContent("USD");
    expect(screen.getByRole("row", { name: /Share consolidation 1 for 10/ })).toHaveTextContent("105 → 10.5 shares");
    expect(screen.getByRole("row", { name: /Bonus issue 1 for 5/ })).toHaveTextContent("12.34567891 → 14.81481469 shares");
    const hand = screen.getAllByRole("row").filter((row) => row.textContent?.startsWith("Split"));
    expect(hand).toHaveLength(1);
    expect(hand[0]).not.toHaveTextContent("Ex-date");
  });
});

describe("Customer Portal dividends (dividends C42–C44)", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: "/accounts/account-1",
      search: "",
      href: "http://localhost:3000/accounts/account-1",
      assign: vi.fn(),
    });
  });

  const custodialUsd = { type: "Brokerage", currency: "USD", depositReference: "K7M3Q9PXB" };
  const usdCash = { currency: "USD", cashBalance: 0, reservedCash: 0, availableCash: 0 };

  function dividendPosting(reinvested: boolean) {
    const leg = (role: string, amount: number, tax: unknown = null) =>
      ({ accountId: "account-1", customerId: me.id, role, amount, currency: "USD", fee: null, tax });
    return {
      id: reinvested ? "75555555-5555-5555-5555-555555555555" : "76666666-6666-6666-6666-666666666666",
      tenantId: "22222222-2222-2222-2222-222222222222",
      type: "Dividend",
      bookedAt: "2026-11-24T04:10:31Z",
      memo: "Dividend ACME.SIM 1.00 per share",
      reference: null,
      originalTransactionId: null,
      reversedByTransactionId: null,
      rowVersion: "AAAA",
      source: { kind: "CorporateAction", id: "ca-1" },
      corporateAction: null,
      dividend: {
        corporateActionId: "ca-1", instrument: { id: "i-1", symbol: "ACME.SIM", name: "Acme" }, quantity: 100, amountPerShare: 1,
        currency: "USD", taxSource: "NewZealand", imputationCredit: 2.5,
        reinvestedQuantity: reinvested ? 2.68 : null, reinvestmentPrice: reinvested ? 25 : null,
        exDate: "2026-11-26", recordDate: null, paymentDate: "2026-11-30",
      },
      cash: [
        leg("Principal", 100),
        leg("Tax", -15, { kind: "ForeignWithholding", ratePercent: 15, basisAmount: 100, amount: 15, currency: "USD" }),
        leg("Tax", -18, { kind: "Rwt", ratePercent: 33, basisAmount: 100, amount: 18, currency: "USD" }),
        ...(reinvested ? [leg("Reinvestment", -67)] : []),
      ],
      security: reinvested ? [{ accountId: "account-1", customerId: me.id, instrumentId: "i-1", quantity: 2.68, cost: 67, costCurrency: "USD" }] : [],
      bankReceipt: null,
      payment: null,
      interest: null,
    };
  }

  it("labels a dividend with its shares, gross, taxes, credit and net (C42)", async () => {
    renderAt("/accounts/account-1", me, [dividendPosting(false)], "Custodial", { account: custodialUsd, cash: usdCash });

    const row = await screen.findByRole("row", { name: /Dividend ACME.SIM/ });
    expect(row).toHaveTextContent("100 shares × 1.00");
    expect(row).toHaveTextContent("Gross 100.00 USD");
    expect(row).toHaveTextContent("Foreign tax 15 % 15.00 USD");
    expect(row).toHaveTextContent("RWT 33 % 18.00 USD");
    expect(row).toHaveTextContent("Imputation credit 2.50 USD");
    expect(row).toHaveTextContent("67.00 USD");
    expect(row).not.toHaveTextContent("Reinvested");
  });

  it("shows the reinvestment of a reinvested dividend and a net of zero (C43)", async () => {
    renderAt("/accounts/account-1", me, [dividendPosting(true)], "Custodial", { account: custodialUsd, cash: usdCash });

    const row = await screen.findByRole("row", { name: /Dividend ACME.SIM/ });
    expect(row).toHaveTextContent("Reinvested in 2.68 shares at 25.00");
    expect(row).toHaveTextContent("0.00 USD");
  });

  it("says whether dividends are reinvested, read only (C44)", async () => {
    renderAt("/accounts/account-1", me, [], "Custodial", {
      account: { ...custodialUsd, reinvestDividends: true }, cash: usdCash,
    });

    expect(await screen.findByText("Dividends are reinvested")).toBeInTheDocument();
    expect(screen.getByText("Ask your adviser to change this.")).toBeInTheDocument();
    expect(screen.queryByRole("checkbox", { name: /Reinvest/ })).not.toBeInTheDocument();
  });

  it("says dividends are paid in cash (C44)", async () => {
    renderAt("/accounts/account-1", me, [], "Custodial", { account: { ...custodialUsd, reinvestDividends: false }, cash: usdCash });
    expect(await screen.findByText("Dividends are paid in cash")).toBeInTheDocument();
  });
});

describe("Customer Portal in-kind transfers (in-kind-transfers C39–C41)", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: "/accounts/account-1",
      search: "",
      href: "http://localhost:3000/accounts/account-1",
      assign: vi.fn(),
    });
  });

  const custodialUsd = { type: "Brokerage", currency: "USD", depositReference: "K7M3Q9PXB" };

  function transfer(id: string, status: string, direction: string, requestedAt: string, completedAt: string | null = null) {
    return {
      id,
      accountId: "account-1",
      customerId: me.id,
      direction,
      instrument: { id: "i-1", symbol: "ACME.SIM", name: "Acme Simulated" },
      quantity: 100,
      currency: "USD",
      cost: null,
      acquiredOn: null,
      changesBeneficialOwner: false,
      counterparty: { name: "Harbour Brokers", account: "HB-0044172", holderName: "J Smith" },
      memo: null,
      status,
      requestedBy: { id: "a", name: "Ann Adviser" },
      requestedAt,
      decision: null,
      send: null,
      failureReason: null,
      setAside: null,
      transactionId: null,
      completedAt,
      rowVersion: "AAAA",
    };
  }

  it("lists open transfers and those that ended in the last 30 days, in the Customer's words, with no action (C39)", async () => {
    const recent = new Date(Date.now() - 5 * 86400000).toISOString();
    const old = new Date(Date.now() - 60 * 86400000).toISOString();
    const { fetchMock } = renderAt("/accounts/account-1", me, [], "Custodial", {
      account: custodialUsd,
      transfers: [
        transfer("t-open", "Sent", "In", old),
        transfer("t-done", "Completed", "Out", old, recent),
        transfer("t-old", "Completed", "In", old, old),
      ],
    });

    const section = (await screen.findByRole("heading", { name: "Transfers" })).closest("section")!;
    expect(await within(section).findByText("100 ACME.SIM from Harbour Brokers")).toBeInTheDocument();
    expect(within(section).getByText("On its way")).toBeInTheDocument();
    expect(within(section).getByText("100 ACME.SIM to Harbour Brokers")).toBeInTheDocument();
    expect(within(section).getAllByText("Done")).toHaveLength(1);
    expect(within(section).queryByRole("button")).not.toBeInTheDocument();
    const lookup = urls(fetchMock).find((url) => url.includes("/in-kind-transfers"))!;
    expect(new URL(lookup, "http://localhost").searchParams.get("accountId")).toBe("account-1");
  });

  it("labels the postings of a transfer, and keeps StockIn without one (C40)", async () => {
    const posting = (id: string, type: string, quantity: number, inKindTransfer: unknown) => ({
      id,
      type,
      bookedAt: "2026-11-24T01:12:00Z",
      memo: null,
      cash: [],
      security: [{ accountId: "account-1", instrumentId: "i-1", quantity, cost: 9500 }],
      inKindTransfer,
    });
    const detail = (direction: string) => ({
      transferId: "t-1",
      direction,
      counterpartyName: "Harbour Brokers",
      counterpartyAccount: "****4172",
      changesBeneficialOwner: false,
      acquiredOn: null,
      settlementDate: "2026-11-24",
      custodianReference: "w-M12",
    });
    renderAt("/accounts/account-1", me, [
      posting("tx-in", "StockIn", 100, detail("In")),
      posting("tx-out", "StockOut", -40, detail("Out")),
      posting("tx-hand", "StockIn", 5, null),
    ], "Custodial", {
      account: custodialUsd,
      holdings: [{ id: "h-1", instrumentId: "i-1", symbol: "ACME.SIM", name: "Acme Simulated", quantity: 65 }],
    });

    expect(await screen.findByText("Shares transferred in")).toBeInTheDocument();
    expect(screen.getByText("Shares transferred out")).toBeInTheDocument();
    expect(screen.getByText("100 ACME.SIM, Harbour Brokers")).toBeInTheDocument();
    expect(screen.getByText("40 ACME.SIM, Harbour Brokers")).toBeInTheDocument();
    expect(screen.getByText("StockIn")).toBeInTheDocument();
  });

  it("shows the quantity not settled yet under a holding (C41)", async () => {
    renderAt("/accounts/account-1", me, [], "Custodial", {
      account: custodialUsd,
      holdings: [{ id: "h-1", instrumentId: "i-1", symbol: "ACME.SIM", name: "Acme Simulated", quantity: 100, unsettledQuantity: 10 }],
    });

    expect(await screen.findByText("10 not settled yet")).toBeInTheDocument();
  });
});

describe("Customer Portal valuation history (C45–C47)", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-03-13T10:00:00Z"));
    vi.stubGlobal("fetch", vi.fn());
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: "/",
      search: "",
      href: "http://localhost:3000/",
      assign: vi.fn(),
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  function day(date: string, total: number, accounts: number, complete = true) {
    return {
      date,
      currencies: accounts > 0 ? [{ currency: "USD", total: total / 1.728, status: "Folded", fx: { rate: 1.728, date, source: "Frankfurter" }, value: total }] : [],
      total,
      complete,
      accounts,
      incompleteAccounts: complete ? 0 : 1,
      missingAccounts: 0,
    };
  }

  const series = {
    reportingCurrency: "NZD",
    from: "2026-03-08",
    to: "2026-03-11",
    days: [day("2026-03-08", 0, 0), day("2026-03-09", 12975.43, 1), day("2026-03-10", 12960.43, 1), day("2026-03-11", 12982.93, 1, false)],
  };

  function folded(complete: boolean) {
    return {
      currency: "NZD",
      total: complete ? 9885.6 : 8500,
      complete,
      currencies: [
        ...(complete ? [] : [{ currency: "GBP", total: 50, status: "NoRate", fx: null, value: null }]),
        { currency: "NZD", total: 8500, status: "Folded", fx: null, value: 8500 },
        { currency: "USD", total: 800, status: "Folded", fx: { rate: 1.732, date: "2026-03-13", source: "Frankfurter" }, value: 1385.6 },
      ],
    };
  }

  function seriesUrls(fetchMock: ReturnType<typeof vi.mocked<typeof fetch>>) {
    return urls(fetchMock).filter((url) => url.includes("/net-worth/funds-under-management"));
  }

  it("charts what the platform holds over 90 days, or a year when chosen, under the custody section (C45)", async () => {
    const { fetchMock } = renderAt("/dashboard", me, [], "External", { series });

    const section = (await screen.findByRole("heading", { name: "Held on platform over time" })).closest("section")!;
    expect(seriesUrls(fetchMock)).toEqual(["http://localhost:3000/api/net-worth/funds-under-management?from=2025-12-12"]);
    expect(
      within(section).getByText("In NZD, at each day's exchange rate. The latest two days are added once their prices are final."),
    ).toBeInTheDocument();
    const points = within(section).getAllByRole("button", { name: /^2026-03-/ });
    expect(points.map((point) => point.getAttribute("aria-label"))).toEqual([
      "2026-03-09: 12,975.43 NZD",
      "2026-03-10: 12,960.43 NZD",
      "2026-03-11: 12,982.93 NZD. Some holdings could not be valued on this day.",
    ]);
    expect(points[2]).toHaveAttribute("fill", "none");
    const custody = screen.getByRole("heading", { name: "Held on platform and elsewhere" });
    const byType = screen.getByRole("heading", { name: "By account type" });
    const chart = screen.getByRole("heading", { name: "Held on platform over time" });
    expect(custody.compareDocumentPosition(chart) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(chart.compareDocumentPosition(byType) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(screen.queryByRole("button", { name: "30 days" })).not.toBeInTheDocument();

    fireEvent.click(within(section).getByRole("button", { name: "1 year" }));
    await waitFor(() =>
      expect(seriesUrls(fetchMock)).toContain("http://localhost:3000/api/net-worth/funds-under-management?from=2025-03-12"),
    );
  });

  it("shows no chart without a Custodial account in the range (C45)", async () => {
    renderAt("/dashboard", me, [], "External", {
      series: { ...series, days: series.days.map((item) => ({ ...item, accounts: 0, currencies: [], total: 0, complete: true })) },
    });

    expect(await screen.findByRole("heading", { name: "Held on platform and elsewhere" })).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByText(/^In NZD, at each day/)).not.toBeInTheDocument());
    expect(screen.queryByRole("heading", { name: "Held on platform over time" })).not.toBeInTheDocument();
  });

  it("shows the total in the reporting currency under the per-currency totals and what it leaves out (C46)", async () => {
    renderAt("/dashboard", me, [], "External", { netWorth: { ...netWorth, folded: folded(false) }, series });

    expect(await screen.findByText("Total in NZD: 8,500 at today's exchange rates")).toBeInTheDocument();
    expect(screen.getByText("Not counted: GBP (no exchange rate)")).toBeInTheDocument();
    const totals = screen.getByRole("table", { name: "Per currency" });
    const line = screen.getByText(/^Total in NZD/);
    expect(totals.compareDocumentPosition(line) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("hides the total when the only currency is the reporting currency (C46)", async () => {
    renderAt("/dashboard", me, [], "External", {
      netWorth: {
        ...netWorth,
        folded: { currency: "NZD", total: 140, complete: true, currencies: [{ currency: "NZD", total: 140, status: "Folded", fx: null, value: 140 }] },
      },
      series,
    });

    expect(await screen.findByRole("table", { name: "Per currency" })).toBeInTheDocument();
    expect(screen.queryByText(/^Total in NZD/)).not.toBeInTheDocument();
  });

  const valuations = {
    items: [
      { date: "2026-03-11", currency: "NZD", cash: 14356.6, holdingsValue: 20394.12, total: 34750.72, complete: false, unvaluedCount: 1 },
      { date: "2026-03-10", currency: "NZD", cash: 14356.6, holdingsValue: 28573.49, total: 42930.09, complete: true, unvaluedCount: 0 },
    ],
    page: 1,
    pageSize: 100,
    totalCount: 2,
  };

  function valuationUrls(fetchMock: ReturnType<typeof vi.mocked<typeof fetch>>) {
    return urls(fetchMock).filter((url) => url.includes("/valuations"));
  }

  it("charts the value of a Custodial account over its last 90 stored days (C47)", async () => {
    const { fetchMock } = renderAt("/accounts/account-1", me, [], "Custodial", {
      account: { valuedThrough: "2026-03-11" },
      valuations,
    });

    const section = (await screen.findByRole("heading", { name: "Value over the last 90 days" })).closest("section")!;
    const points = await within(section).findAllByRole("button", { name: /^2026-03-1/ });
    expect(points.map((point) => point.getAttribute("aria-label"))).toEqual([
      "2026-03-10: 42,930.09 NZD",
      "2026-03-11: 34,750.72 NZD. Some holdings could not be valued on this day.",
    ]);
    expect(valuationUrls(fetchMock)).toEqual([
      "http://localhost:3000/api/accounts/account-1/valuations?from=2025-12-12&to=2026-03-11&pageSize=100",
    ]);
  });

  it("says when a Custodial account is not valued yet (C47)", async () => {
    const { fetchMock } = renderAt("/accounts/account-1", me, [], "Custodial", { account: { valuedThrough: null }, valuations });

    expect(
      await screen.findByText("Not valued yet. The first value appears two days after the account was opened."),
    ).toBeInTheDocument();
    expect(valuationUrls(fetchMock)).toEqual([]);
  });

  it("shows no chart on an External account (C47)", async () => {
    const { fetchMock } = renderAt("/accounts/account-1", me, [], "External", { valuations });

    expect(await screen.findByRole("heading", { name: "Activity" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Value over the last 90 days" })).not.toBeInTheDocument();
    expect(valuationUrls(fetchMock)).toEqual([]);
  });
});
