import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AccountDetailPage } from "@/features/accounts/AccountDetailPage";
import { CloseAccountPage } from "@/features/accountClosures/CloseAccountPage";
import { kindLabel } from "@/features/bankReconciliations/bankReconciliationLabels";
import { InterestRateFormPage } from "@/features/interest/InterestRateFormPage";
import { InterestRatesPage } from "@/features/interest/InterestRatesPage";
import { InterestRunPage } from "@/features/interest/InterestRunPage";
import { InterestRunsPage } from "@/features/interest/InterestRunsPage";
import { TaxDetailsSection } from "@/features/interest/TaxDetailsSection";
import type { InterestRateSchedule, InterestRun, TaxProfile } from "@/features/interest/interestApi";
import { StatementLinesListPage } from "@/features/statementLines/StatementLinesListPage";
import { TransactionDetailPage } from "@/features/transactions/TransactionDetailPage";
import { TransactionsListPage } from "@/features/transactions/TransactionsListPage";
import { chooseOption } from "@/test/chooseOption";
import { adviser, feeUser, json, problem, renderFees, requestsTo, stubBrowser } from "@/test/fees";

const customerId = "cccccccc-cccc-cccc-cccc-cccccccccccc";
const accountId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
const postingId = "dddddddd-dddd-dddd-dddd-dddddddddddd";
const runId = "77777777-7777-7777-7777-777777777777";

function profile(overrides: Partial<TaxProfile> = {}): TaxProfile {
  return {
    taxResidency: "NewZealand",
    irdNumberHeld: true,
    rwtRatePercent: 17.5,
    withholding: { kind: "Rwt", ratePercent: 17.5, basis: "Elected" },
    updatedBy: { id: "aaaa", name: "Pat Admin" },
    updatedAt: "2026-11-22T03:00:00Z",
    rowVersion: "AAAB",
    ...overrides,
  };
}

const notProvided = profile({
  irdNumberHeld: false,
  rwtRatePercent: null,
  withholding: { kind: "Rwt", ratePercent: 45, basis: "NonDeclaration" },
  updatedBy: null,
  updatedAt: null,
  rowVersion: null,
});

function rate(overrides: Partial<InterestRateSchedule> = {}): InterestRateSchedule {
  return {
    id: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
    currency: "USD",
    effectiveFrom: "2026-12-01",
    bands: [
      { from: 0, ratePercent: 3 },
      { from: 100000, ratePercent: 2 },
    ],
    inEffect: false,
    rowVersion: "AAAA",
    ...overrides,
  };
}

function run(overrides: Partial<InterestRun> = {}): InterestRun {
  return {
    id: runId,
    asOfDate: "2026-12-31",
    postingDate: "2026-12-31",
    trigger: "Scheduled",
    ranBy: null,
    accountId: null,
    startedAt: "2027-01-01T00:30:02Z",
    completedAt: "2027-01-01T00:30:09Z",
    accountsProcessed: 42,
    accountsFailed: 1,
    postingCount: 37,
    totals: [
      { currency: "NZD", postingCount: 12, grossAmount: 96.12, taxAmount: 21.07 },
      { currency: "USD", postingCount: 25, grossAmount: 410.55, taxAmount: 93.4 },
    ],
    ...overrides,
  };
}

const posting = {
  id: postingId,
  tenantId: "22222222-2222-2222-2222-222222222222",
  type: "Interest",
  source: { kind: "InterestRun", id: runId },
  interest: { periodFrom: "2026-12-02", periodTo: "2026-12-31", days: 30, accruedAmount: 12.328767, grossAmount: 12.33 },
  cash: [
    { accountId, customerId, role: "Principal", amount: 12.33, currency: "USD", fee: null, tax: null },
    {
      accountId,
      customerId,
      role: "Tax",
      amount: -2.16,
      currency: "USD",
      fee: null,
      tax: { kind: "Rwt", ratePercent: 17.5, basisAmount: 12.33, amount: 2.16, currency: "USD" },
    },
  ],
  bookedAt: "2027-01-01T00:30:04Z",
  memo: "Interest",
  reference: null,
  originalTransactionId: null,
  reversedByTransactionId: null,
  rowVersion: "AAAA",
  security: [],
};

function account(overrides: Record<string, unknown> = {}) {
  return {
    id: accountId,
    tenantId: "22222222-2222-2222-2222-222222222222",
    customerId,
    name: "Managed",
    type: "Brokerage",
    custodyMode: "Custodial",
    currency: "USD",
    depositReference: "K7M3Q9PXB",
    cashBalance: 5000,
    reservedCash: 0,
    availableCash: 5000,
    status: "Open",
    isActive: true,
    rowVersion: "AAAA",
    closure: null,
    accruedInterest: 4.5205479,
    interestAccruedThrough: "2026-12-12",
    ...overrides,
  };
}

function path(request: Request) {
  return new URL(request.url).pathname;
}

/** What the account pages read besides the account: no holdings and no withdrawals. */
function accountPages(request: Request) {
  if (path(request) === `/api/accounts/${accountId}/holdings`) {
    return json({ items: [] });
  }
  if (path(request) === "/api/withdrawals") {
    return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
  }
  return undefined;
}

describe("Interest (interest-run D58–D63)", () => {
  beforeEach(() => {
    stubBrowser("/");
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  describe("D58 tax details on the customer page", () => {
    const routes = [{ path: "/customers/:id", element: <TaxDetailsSection customerId={customerId} /> }];

    it("shows the three attributes and the withholding line", async () => {
      renderFees(adviser(), routes, `/customers/${customerId}`, (request) =>
        path(request) === `/api/users/customers/${customerId}/tax-profile` ? json(profile()) : undefined);

      expect(await screen.findByText("Tax details")).toBeInTheDocument();
      expect(screen.getByText("New Zealand")).toBeInTheDocument();
      expect(screen.getByText("Held")).toBeInTheDocument();
      expect(screen.getByText("17.5 %")).toBeInTheDocument();
      expect(screen.getByText("Tax withheld from interest: RWT 17.5 % (chosen rate)")).toBeInTheDocument();
      expect(screen.queryByText("Not provided")).not.toBeInTheDocument();
    });

    it("says Not provided when nothing is saved, with the non-declaration rate", async () => {
      renderFees(adviser(), routes, `/customers/${customerId}`, (request) =>
        path(request) === `/api/users/customers/${customerId}/tax-profile` ? json(notProvided) : undefined);

      expect(await screen.findByText("Not provided")).toBeInTheDocument();
      expect(screen.getByText("Not held")).toBeInTheDocument();
      expect(screen.getByText("Not chosen")).toBeInTheDocument();
      expect(screen.getByText("Tax withheld from interest: RWT 45 % (no IRD number held)")).toBeInTheDocument();
    });

    it("edits with the rate select disabled unless the IRD number is held, and sends the rowVersion read", async () => {
      let body: unknown = null;
      renderFees(adviser(), routes, `/customers/${customerId}`, async (request) => {
        if (path(request) !== `/api/users/customers/${customerId}/tax-profile`) {
          return undefined;
        }
        if (request.method === "PUT") {
          body = await request.json();
          return new Response(null, { status: 204 });
        }
        return json(notProvided);
      });

      fireEvent.click(await screen.findByRole("button", { name: "Edit" }));
      expect(screen.getByText("We do not store the IRD number here.")).toBeInTheDocument();
      expect(screen.getByLabelText("RWT rate")).toBeDisabled();
      fireEvent.click(screen.getByLabelText("The firm holds the IRD number"));
      await waitFor(() => expect(screen.getByLabelText("RWT rate")).not.toBeDisabled());
      await chooseOption("RWT rate", "30 %");
      fireEvent.click(screen.getByRole("button", { name: "Save" }));

      await waitFor(() => expect(body).toEqual({
        taxResidency: "NewZealand",
        irdNumberHeld: true,
        rwtRatePercent: 30,
        rowVersion: null,
      }));
    });

    it("shows a 400 at its field and reloads on a 409", async () => {
      let puts = 0;
      renderFees(feeUser(), routes, `/customers/${customerId}`, (request) => {
        if (path(request) !== `/api/users/customers/${customerId}/tax-profile`) {
          return undefined;
        }
        if (request.method === "PUT") {
          puts += 1;
          return puts === 1
            ? problem(400, { errors: { RwtRatePercent: ["A rate can be chosen only when the IRD number is held."] } })
            : problem(409, { code: "stale_row_version" });
        }
        return json(profile());
      });

      fireEvent.click(await screen.findByRole("button", { name: "Edit" }));
      fireEvent.click(screen.getByRole("button", { name: "Save" }));
      expect(await screen.findByText("A rate can be chosen only when the IRD number is held.")).toBeInTheDocument();
      fireEvent.click(screen.getByRole("button", { name: "Save" }));
      expect(await screen.findByText("Someone else changed these details.")).toBeInTheDocument();
    });
  });

  describe("D59 interest rates", () => {
    const routes = [
      { path: "/interest/rates", element: <InterestRatesPage /> },
      { path: "/interest/rates/new", element: <InterestRateFormPage /> },
      { path: "/interest/rates/:id/edit", element: <InterestRateFormPage /> },
      { path: "/interest/runs", element: <p>runs page</p> },
    ];
    const rows = [
      rate({ id: "usd-1", currency: "USD", effectiveFrom: "2026-12-01", inEffect: true }),
      rate({ id: "usd-2", currency: "USD", effectiveFrom: "2027-02-01", inEffect: false, bands: [{ from: 0, ratePercent: 2.5 }] }),
      rate({ id: "nzd-1", currency: "NZD", effectiveFrom: "2026-12-01", inEffect: false, bands: [{ from: 0, ratePercent: 3.5 }] }),
    ];
    const list = (request: Request) =>
      path(request) === "/api/interest/rate-schedules" && request.method === "GET"
        ? json({ items: rows, page: 1, pageSize: 100, totalCount: rows.length })
        : undefined;

    it("lists the rates by currency with the band text and the state", async () => {
      renderFees(adviser(), routes, "/interest/rates", list);

      const usd = await screen.findByRole("table", { name: "USD interest rates" });
      expect(await within(usd).findByText("In effect")).toBeInTheDocument();
      expect(screen.getAllByRole("table").map((table) => table.getAttribute("aria-label"))).toEqual([
        "NZD interest rates",
        "USD interest rates",
      ]);
      expect(within(usd).getByText("3 % up to 100,000.00, then 2 %")).toBeInTheDocument();
      expect(within(usd).getByText("In effect")).toBeInTheDocument();
      expect(within(usd).getByText("Starts 2027-02-01")).toBeInTheDocument();
      expect(screen.getByText("Rates are per year. Interest is worked out on each day's closing balance and paid after the end of each month.")).toBeInTheDocument();
    });

    it("gives a TenantAdmin New, and Edit and Delete on a pending row; an Adviser sees none", async () => {
      renderFees(feeUser(), routes, "/interest/rates", list);
      const usd = await screen.findByRole("table", { name: "USD interest rates" });
      expect(screen.getByRole("link", { name: "New rate" })).toBeInTheDocument();
      expect(within(usd).getAllByRole("link", { name: "Edit" })).toHaveLength(1);
      expect(within(usd).getAllByRole("button", { name: "Delete" })).toHaveLength(1);
      expect(screen.getByRole("link", { name: "Interest runs" })).toBeInTheDocument();
    });

    it("hides New, Edit, Delete and the runs link from an Adviser", async () => {
      renderFees(adviser(), routes, "/interest/rates", list);
      await screen.findByRole("table", { name: "USD interest rates" });
      expect(screen.queryByRole("link", { name: "New rate" })).not.toBeInTheDocument();
      expect(screen.queryByRole("link", { name: "Edit" })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Delete" })).not.toBeInTheDocument();
      expect(screen.queryByRole("link", { name: "Interest runs" })).not.toBeInTheDocument();
    });

    it("shows the in-effect sentence when a delete is refused with interest_schedule_in_effect", async () => {
      renderFees(feeUser(), routes, "/interest/rates", (request) =>
        request.method === "DELETE" ? problem(409, { code: "interest_schedule_in_effect" }) : list(request));

      const usd = await screen.findByRole("table", { name: "USD interest rates" });
      fireEvent.click(within(usd).getByRole("button", { name: "Delete" }));
      expect(await screen.findByText("This rate has already been used and cannot be changed. Add a new one from a later date.")).toBeInTheDocument();
    });

    it("creates a rate with its bands and the UTC day note", async () => {
      let body: unknown = null;
      const { router } = renderFees(feeUser(), routes, "/interest/rates/new", async (request) => {
        if (path(request) === "/api/interest/rate-schedules" && request.method === "POST") {
          body = await request.json();
          return json({ id: "new-id" }, 201);
        }
        return list(request);
      });

      expect(await screen.findByText(/Dates are UTC days/)).toBeInTheDocument();
      await chooseOption("Currency", "USD");
      fireEvent.change(screen.getByLabelText("Effective from"), { target: { value: "2027-03-01" } });
      fireEvent.change(screen.getByLabelText("Band 1 rate %"), { target: { value: "3" } });
      fireEvent.click(screen.getByRole("button", { name: "Add band" }));
      fireEvent.change(screen.getByLabelText("Band 2 from"), { target: { value: "100000" } });
      fireEvent.change(screen.getByLabelText("Band 2 rate %"), { target: { value: "2" } });
      fireEvent.click(screen.getByRole("button", { name: "Save" }));

      await waitFor(() => expect(body).toEqual({
        currency: "USD",
        effectiveFrom: "2027-03-01",
        bands: [
          { from: 0, ratePercent: 3 },
          { from: 100000, ratePercent: 2 },
        ],
      }));
      await waitFor(() => expect(router.state.location.pathname).toBe("/interest/rates"));
    });

    it("edits a pending rate with its rowVersion", async () => {
      let body: unknown = null;
      renderFees(feeUser(), routes, "/interest/rates/usd-2/edit", async (request) => {
        if (path(request) === "/api/interest/rate-schedules/usd-2") {
          if (request.method === "PUT") {
            body = await request.json();
            return new Response(null, { status: 204 });
          }
          return json(rows[1]);
        }
        return list(request);
      });

      fireEvent.change(await screen.findByDisplayValue("2.5"), { target: { value: "2.75" } });
      fireEvent.click(screen.getByRole("button", { name: "Save" }));

      await waitFor(() => expect(body).toEqual({
        effectiveFrom: "2027-02-01",
        bands: [{ from: 0, ratePercent: 2.75 }],
        rowVersion: "AAAA",
      }));
    });
  });

  describe("D60 interest runs", () => {
    const routes = [
      { path: "/interest/runs", element: <InterestRunsPage /> },
      { path: "/interest/runs/:id", element: <InterestRunPage /> },
    ];

    it("lists the runs with the trigger label, counts and totals per currency, and no start button", async () => {
      renderFees(feeUser(), routes, "/interest/runs", (request) =>
        path(request) === "/api/interest/runs"
          ? json({ items: [run(), run({ id: "r2", trigger: "AccountClosure", accountsFailed: 0 })], page: 1, pageSize: 20, totalCount: 2 })
          : undefined);

      const table = await screen.findByRole("table", { name: "Interest runs" });
      expect(await within(table).findByText("Daily run")).toBeInTheDocument();
      expect(within(table).getByText("Account closure")).toBeInTheDocument();
      expect(within(table).getAllByText("42")).toHaveLength(2);
      expect(within(table).getAllByText("96.12 NZD, 410.55 USD")).toHaveLength(2);
      expect(within(table).getAllByText("21.07 NZD, 93.40 USD")).toHaveLength(2);
      expect(within(table).getByText("1").className).toContain("text-destructive");
      expect(screen.queryByRole("button", { name: /run/i })).not.toBeInTheDocument();
    });

    it("shows the firm administrator sentence on a 403", async () => {
      renderFees(adviser(), routes, "/interest/runs", (request) =>
        path(request) === "/api/interest/runs" ? problem(403, {}) : undefined);

      expect(await screen.findByText("Only a firm administrator can see interest runs.")).toBeInTheDocument();
    });

    it("shows one run with its posting date", async () => {
      renderFees(feeUser(), routes, `/interest/runs/${runId}`, (request) =>
        path(request) === `/api/interest/runs/${runId}` ? json(run({ trigger: "Manual", ranBy: { id: "s", name: "Sys Admin" } })) : undefined);

      expect(await screen.findByText("Run by the platform team")).toBeInTheDocument();
      expect(screen.getByText("Posting date")).toBeInTheDocument();
      expect(screen.getAllByText("2026-12-31").length).toBeGreaterThanOrEqual(2);
    });
  });

  describe("D61 an interest posting on the ledger pages", () => {
    it("shows the period, gross, tax and net, the run source, the daily table, and no Reverse", async () => {
      renderFees(feeUser(), [{ path: "/transactions/:id", element: <TransactionDetailPage /> }], `/transactions/${postingId}`, (request) => {
        if (path(request) === `/api/transactions/${postingId}`) {
          return json(posting);
        }
        if (path(request) === `/api/accounts/${accountId}/interest-accruals`) {
          return json({
            items: [{ date: "2026-12-31", balance: 5000, ratePercent: 3, scheduleId: "s", amount: 0.4109589, transactionId: postingId }],
            page: 1,
            pageSize: 100,
            totalCount: 1,
          });
        }
        return undefined;
      });

      expect(await screen.findByText("Interest 2026-12-02 to 2026-12-31 (30 days)")).toBeInTheDocument();
      expect(screen.getByText("Source: Interest run")).toBeInTheDocument();
      expect(screen.getByText("12.33 USD")).toBeInTheDocument();
      expect(screen.getByText("RWT 17.5 %: 2.16 USD")).toBeInTheDocument();
      expect(screen.getByText("10.17 USD")).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Reverse" })).not.toBeInTheDocument();
      fireEvent.click(screen.getByRole("button", { name: "Daily interest" }));
      const daily = await screen.findByRole("table", { name: "Daily interest" });
      expect(await within(daily).findByText("0.41095890")).toBeInTheDocument();
    });

    it("shows the net with the tax under it in the transactions list", async () => {
      renderFees(feeUser(), [{ path: "/transactions", element: <TransactionsListPage /> }], "/transactions", (request) =>
        path(request) === "/api/transactions" ? json({ items: [posting], page: 1, pageSize: 20, totalCount: 1 }) : undefined);

      expect(await screen.findByText(/tax -2\.16 USD/)).toBeInTheDocument();
    });
  });

  describe("D62 accrued interest", () => {
    it("shows the interest accrued and not yet paid on a Custodial account", async () => {
      renderFees(feeUser(), [{ path: "/accounts/:id", element: <AccountDetailPage /> }], `/accounts/${accountId}`, (request) =>
        path(request) === `/api/accounts/${accountId}` ? json(account()) : accountPages(request));

      expect(await screen.findByText("Interest accrued, not yet paid")).toBeInTheDocument();
      expect(screen.getByText("4.5205 USD up to 2026-12-12")).toBeInTheDocument();
    });

    it("shows the incl. tax line in the account activity", async () => {
      renderFees(feeUser(), [{ path: "/accounts/:id", element: <AccountDetailPage /> }], `/accounts/${accountId}`, (request) => {
        if (path(request) === `/api/accounts/${accountId}`) {
          return json(account({ accruedInterest: 0 }));
        }
        if (path(request) === "/api/transactions") {
          return json({ items: [posting], page: 1, pageSize: 20, totalCount: 1 });
        }
        return accountPages(request);
      });

      expect(await screen.findByText("incl. tax 2.16 USD")).toBeInTheDocument();
      expect(screen.queryByText("Interest accrued, not yet paid")).not.toBeInTheDocument();
    });

    it("warns on the Close account page that the accrued interest is added before the payout", async () => {
      renderFees(feeUser(), [{ path: "/accounts/:id/close", element: <CloseAccountPage /> }], `/accounts/${accountId}/close`, (request) =>
        path(request) === `/api/accounts/${accountId}` ? json(account()) : accountPages(request));

      expect(await screen.findByText("Interest accrued so far is added before the payout. The amount to pay out may be higher than shown.")).toBeInTheDocument();
    });
  });

  describe("D63 the bank's interest", () => {
    it("tags a line of kind Interest and filters by kind", async () => {
      const { requests } = renderFees(feeUser(), [{ path: "/statement-lines", element: <StatementLinesListPage /> }], "/statement-lines", (request) =>
        path(request) === "/api/statement-lines"
          ? json({
              items: [{
                id: "l1", clientMoneyAccountId: "c", currency: "USD", direction: "Credit", kind: "Interest", amount: 520,
                valueDate: "2026-12-31", payerName: "Bank interest", payerAccount: null, reference: "Interest", receivedAt: "2027-01-01T00:00:00Z",
                status: "Ignored", unmatchedReason: null, match: null, lastAction: null, rowVersion: "AAAA",
              }],
              page: 1,
              pageSize: 20,
              totalCount: 1,
            })
          : undefined);

      expect(await screen.findByText("Bank interest", { selector: "[data-slot=badge]" })).toBeInTheDocument();
      await chooseOption("Kind", "Bank interest");
      await waitFor(() =>
        expect(requestsTo(requests, "GET", "/statement-lines").some((request) => new URL(request.url).searchParams.get("kind") === "Interest")).toBe(true));
    });

    it("labels the three reconciliation kinds", () => {
      expect(kindLabel("BankInterest")).toBe("Interest paid by the bank");
      expect(kindLabel("InterestCredited")).toBe("Interest credited to customers");
      expect(kindLabel("TaxWithheld")).toBe("Tax withheld from customers' interest");
    });
  });
});
