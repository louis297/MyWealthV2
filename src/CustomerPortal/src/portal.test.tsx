import { configureStore } from "@reduxjs/toolkit";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
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
    nominated?: unknown[];
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
    if (url.endsWith("/users/me")) {
      return json(person);
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
    if (url.includes("/withdrawals")) {
      const items = options.withdrawals ?? [];
      return json({ items, page: 1, pageSize: 100, totalCount: items.length });
    }
    if (url.includes("/accounts/") && url.endsWith("/holdings")) {
      return json({ items: [] });
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

  it("shows Available to withdraw, and Withdrawals in progress when cash is reserved (C23)", async () => {
    renderAt("/accounts/account-1", me, [], "Custodial", {
      account: custodialUsd,
      cash: { currency: "USD", cashBalance: 1000, reservedCash: 300, availableCash: 700 },
    });

    expect(await screen.findByText("Available to withdraw")).toBeInTheDocument();
    expect(screen.getByText("700 USD")).toBeInTheDocument();
    expect(screen.getByText("Withdrawals in progress")).toBeInTheDocument();
    expect(screen.getByText("300 USD")).toBeInTheDocument();
  });

  it("hides Withdrawals in progress when nothing is reserved (C23)", async () => {
    renderAt("/accounts/account-1", me, [], "Custodial", {
      account: custodialUsd,
      cash: { currency: "USD", cashBalance: 1000, reservedCash: 0, availableCash: 1000 },
    });

    expect(await screen.findByText("Available to withdraw")).toBeInTheDocument();
    expect(screen.queryByText("Withdrawals in progress")).not.toBeInTheDocument();
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
