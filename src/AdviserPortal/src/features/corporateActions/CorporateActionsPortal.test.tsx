import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { AccountDetailPage } from "@/features/accounts/AccountDetailPage";
import { CorporateActionFormPage } from "@/features/corporateActions/CorporateActionFormPage";
import { CorporateActionPage } from "@/features/corporateActions/CorporateActionPage";
import { CorporateActionsPage } from "@/features/corporateActions/CorporateActionsPage";
import type { CorporateAction, CorporateActionEntitlement } from "@/features/corporateActions/corporateActionsApi";
import { utcToday } from "@/features/corporateActions/corporateActionText";
import { TransactionDetailPage } from "@/features/transactions/TransactionDetailPage";
import { TransactionsListPage } from "@/features/transactions/TransactionsListPage";
import { chooseOption } from "@/test/chooseOption";
import { adviser, feeUser, json, problem, renderFees, requestsTo, stubBrowser } from "@/test/fees";

const actionId = "aaaaaaaa-1111-1111-1111-aaaaaaaaaaaa";
const instrumentId = "99999999-9999-9999-9999-999999999999";
const accountId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
const customerId = "cccccccc-cccc-cccc-cccc-cccccccccccc";
const postingId = "dddddddd-dddd-dddd-dddd-dddddddddddd";

function action(overrides: Partial<CorporateAction> = {}): CorporateAction {
  return {
    id: actionId,
    instrument: { id: instrumentId, symbol: "AAPL", name: "Apple Inc." },
    kind: "Split",
    ratioNew: 2,
    ratioHeld: 1,
    exDate: "2099-12-01",
    recordDate: null,
    paymentDate: null,
    origin: "Manual",
    status: "Announced",
    memo: null,
    createdBy: { id: "p1", name: "Pat Admin" },
    createdAt: "2026-11-22T03:15:02Z",
    effectiveAt: null,
    effectiveBy: null,
    appliedAt: null,
    cancelledAt: null,
    cancelledBy: null,
    cancelNote: null,
    entitlements: { total: 0, posted: 0, pending: 0, notApplied: 0 },
    rowVersion: "AAAA",
    ...overrides,
  };
}

function entitlement(overrides: Partial<CorporateActionEntitlement> = {}): CorporateActionEntitlement {
  return {
    accountId,
    accountName: "Demo custodial brokerage",
    customerId,
    customerName: "Casey Customer",
    status: "Posted",
    reason: null,
    quantityBefore: 100,
    quantityAfter: 200,
    transactionId: postingId,
    postedAt: "2026-11-22T03:41:02Z",
    ...overrides,
  };
}

const posting = {
  id: postingId,
  tenantId: "22222222-2222-2222-2222-222222222222",
  type: "Split",
  source: { kind: "CorporateAction", id: actionId },
  corporateAction: {
    id: actionId, kind: "Split", ratioNew: 2, ratioHeld: 1, exDate: "2026-11-24", recordDate: null, paymentDate: null,
    quantityBefore: 100, quantityAfter: 200,
  },
  cash: [],
  security: [{ accountId, customerId, instrumentId, quantity: 100, cost: 0, costCurrency: "USD" }],
  bookedAt: "2026-11-22T03:41:02Z",
  memo: "Split 2 for 1",
  reference: null,
  originalTransactionId: null,
  reversedByTransactionId: null,
  rowVersion: "AAAA",
};

function path(request: Request) {
  return new URL(request.url).pathname;
}

const page = <T,>(items: T[]) => json({ items, page: 1, pageSize: 20, totalCount: items.length });

describe("Corporate actions (corporate-actions D64–D68)", () => {
  beforeEach(() => {
    stubBrowser("/");
  });

  describe("D64 the list", () => {
    const routes = [
      { path: "/corporate-actions", element: <CorporateActionsPage /> },
      { path: "/corporate-actions/new", element: <p>form</p> },
    ];

    it("lists each action with its labels, and the not-applied count in amber", async () => {
      const rows = [
        action({ entitlements: { total: 3, posted: 2, pending: 0, notApplied: 1 }, status: "Applied" }),
        action({ id: "x2", kind: "Consolidation", ratioNew: 1, ratioHeld: 10, origin: "Feed", status: "Effective" }),
        action({ id: "x3", kind: "Bonus", ratioNew: 1, ratioHeld: 5, status: "Cancelled" }),
      ];
      renderFees(feeUser(), routes, "/corporate-actions", (request) =>
        path(request) === "/api/corporate-actions" ? page(rows) : undefined);

      const table = await screen.findByRole("table", { name: "Corporate actions" });
      expect(await within(table).findByText("Share split")).toBeInTheDocument();
      expect(within(table).getByText("Share consolidation")).toBeInTheDocument();
      expect(within(table).getByText("Bonus issue")).toBeInTheDocument();
      expect(within(table).getByText("2 for 1")).toBeInTheDocument();
      expect(within(table).getByText("1 for 10")).toBeInTheDocument();
      expect(within(table).getByText("Market data")).toBeInTheDocument();
      expect(within(table).getAllByText("Entered by hand")).toHaveLength(2);
      expect(within(table).getByText("Applied")).toBeInTheDocument();
      expect(within(table).getByText("Being applied")).toBeInTheDocument();
      expect(within(table).getByText("Cancelled")).toBeInTheDocument();
      expect(within(table).getByText("2 of 3")).toBeInTheDocument();
      expect(within(table).getByText("1 not applied")).toHaveClass("text-amber-600");
      expect(within(table).getAllByText("AAPL")).toHaveLength(3);
      expect(screen.getByRole("link", { name: "New corporate action" })).toBeInTheDocument();
    });

    it("filters by status, and an Adviser sees no New button", async () => {
      const { requests } = renderFees(adviser(), routes, "/corporate-actions", (request) =>
        path(request) === "/api/corporate-actions" ? page([action()]) : undefined);

      await screen.findByText("Share split");
      expect(screen.queryByRole("link", { name: "New corporate action" })).not.toBeInTheDocument();
      await chooseOption("Status", "Being applied");
      await waitFor(() =>
        expect(requestsTo(requests, "GET", "/corporate-actions").some((request) => request.url.includes("status=Effective"))).toBe(true));
    });
  });

  describe("D65, D67 one corporate action", () => {
    const routes = [
      { path: "/corporate-actions/:id", element: <CorporateActionPage /> },
      { path: "/corporate-actions/:id/edit", element: <p>edit form</p> },
    ];

    function handler(item: CorporateAction, rows: CorporateActionEntitlement[] = [entitlement()]) {
      return (request: Request) => {
        if (path(request) === `/api/corporate-actions/${actionId}` && request.method === "GET") {
          return json(item);
        }
        if (path(request) === `/api/corporate-actions/${actionId}/entitlements`) {
          return page(rows);
        }
        return undefined;
      };
    }

    it("shows the ratio sentence, who did what and the entitlements", async () => {
      renderFees(feeUser(), routes, `/corporate-actions/${actionId}`, handler(
        action({ status: "Applied", effectiveAt: "2026-11-22T03:41:01Z", effectiveBy: null, origin: "Feed", createdBy: null }),
        [entitlement(), entitlement({ accountId: "acc-2", accountName: "Other", status: "NotApplied", reason: "NoHolding",
          quantityBefore: null, quantityAfter: null, transactionId: null }),
        entitlement({ accountId: "acc-3", accountName: "Third", status: "Pending", quantityBefore: null, quantityAfter: null, transactionId: null }),
        entitlement({ accountId: "acc-4", accountName: "Fourth", status: "NotApplied", reason: "TooSmall",
          quantityBefore: null, quantityAfter: null, transactionId: null })]));

      expect(await screen.findByText("Every 1 shares become 2.")).toBeInTheDocument();
      expect(screen.getAllByText("The platform")).toHaveLength(2);
      const table = screen.getByRole("table", { name: "Entitlements" });
      expect(await within(table).findByText("Done")).toBeInTheDocument();
      expect(within(table).getByText("No longer held")).toBeInTheDocument();
      expect(within(table).getByText("Waiting")).toBeInTheDocument();
      expect(within(table).getByText("Holding too small to change")).toBeInTheDocument();
      expect(within(table).getAllByText("Casey Customer")).toHaveLength(4);
      expect(within(table).getByRole("link", { name: "View" })).toHaveAttribute("href", `/transactions/${postingId}`);
      expect(screen.queryByRole("button", { name: "Cancel" })).not.toBeInTheDocument();
      expect(screen.queryByText("Only your customers' accounts are listed.")).not.toBeInTheDocument();
    });

    it("words a bonus as new shares for every held, and an Adviser sees the scope note and no actions", async () => {
      renderFees(adviser(), routes, `/corporate-actions/${actionId}`, handler(action({ kind: "Bonus", ratioNew: 1, ratioHeld: 5 })));

      expect(await screen.findByText("1 new shares for every 5 held.")).toBeInTheDocument();
      expect(screen.getByText("Only your customers' accounts are listed.")).toBeInTheDocument();
      expect(screen.queryByRole("link", { name: "Edit" })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Cancel" })).not.toBeInTheDocument();
    });

    it("offers Edit and Cancel on an announced hand entry, and Apply now only once the ex-date has come", async () => {
      renderFees(feeUser(), routes, `/corporate-actions/${actionId}`, handler(action()));

      expect(await screen.findByRole("link", { name: "Edit" })).toHaveAttribute("href", `/corporate-actions/${actionId}/edit`);
      expect(screen.getByRole("button", { name: "Cancel" })).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Apply now" })).not.toBeInTheDocument();
    });

    it("offers none of the three on a feed action", async () => {
      renderFees(feeUser(), routes, `/corporate-actions/${actionId}`, handler(action({ origin: "Feed", exDate: "2020-01-01" })));

      await screen.findByText("Every 1 shares become 2.");
      expect(screen.queryByRole("link", { name: "Edit" })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Cancel" })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Apply now" })).not.toBeInTheDocument();
    });

    it("cancels with a required note, and applies now after the confirmation with the rowVersion read", async () => {
      const bodies: Record<string, unknown> = {};
      renderFees(feeUser(), routes, `/corporate-actions/${actionId}`, async (request) => {
        if (request.method === "POST") {
          bodies[path(request)] = await request.json();
          return new Response(null, { status: 204 });
        }
        return handler(action({ exDate: utcToday() }))(request);
      });

      fireEvent.click(await screen.findByRole("button", { name: "Cancel" }));
      fireEvent.click(await screen.findByRole("button", { name: "Confirm" }));
      expect(await screen.findByText("Enter a note.")).toBeInTheDocument();
      fireEvent.change(screen.getByLabelText("Note"), { target: { value: "Entered for the wrong instrument" } });
      fireEvent.click(screen.getByRole("button", { name: "Confirm" }));
      await waitFor(() => expect(bodies[`/api/corporate-actions/${actionId}/cancel`])
        .toEqual({ rowVersion: "AAAA", note: "Entered for the wrong instrument" }));

      fireEvent.click(screen.getByRole("button", { name: "Apply now" }));
      expect(await screen.findByText("Apply this to every holding now? Use this only when no price arrives for the instrument.")).toBeInTheDocument();
      fireEvent.click(screen.getByRole("button", { name: "Apply" }));
      await waitFor(() => expect(bodies[`/api/corporate-actions/${actionId}/apply`]).toEqual({ rowVersion: "AAAA" }));
    });

    it("shows a 400 sentence, and reloads on a 409 with the conflict sentence", async () => {
      let applies = 0;
      renderFees(feeUser(), routes, `/corporate-actions/${actionId}`, (request) => {
        if (request.method === "POST") {
          applies += 1;
          return applies === 1
            ? problem(400, { errors: { ExDate: ["A corporate action cannot be applied before its ex-date."] } })
            : problem(409, { code: "stale_row_version" });
        }
        return handler(action({ exDate: utcToday() }))(request);
      });

      fireEvent.click(await screen.findByRole("button", { name: "Apply now" }));
      fireEvent.click(await screen.findByRole("button", { name: "Apply" }));
      expect(await screen.findByText("A corporate action cannot be applied before its ex-date.")).toBeInTheDocument();
      fireEvent.click(screen.getByRole("button", { name: "Apply now" }));
      fireEvent.click(await screen.findByRole("button", { name: "Apply" }));
      expect(await screen.findByText("Someone else changed this corporate action.")).toBeInTheDocument();
    });
  });

  describe("D66 the form", () => {
    const routes = [
      { path: "/corporate-actions/new", element: <CorporateActionFormPage /> },
      { path: "/corporate-actions/:id/edit", element: <CorporateActionFormPage /> },
      { path: "/corporate-actions/:id", element: <p>detail page</p> },
    ];
    const catalog = (request: Request) =>
      path(request) === "/api/instruments"
        ? page([
          { id: instrumentId, symbol: "AAPL", name: "Apple Inc.", isActive: true, marketDataConnection: null },
          { id: "acme", symbol: "ACME.SIM", name: "Acme Simulated Holdings", isActive: true, marketDataConnection: "simulator" },
        ])
        : undefined;

    it("searches the catalog, lists a served instrument as disabled, previews the ratio and posts the body", async () => {
      let body: unknown = null;
      const { router } = renderFees(feeUser(), routes, "/corporate-actions/new", async (request) => {
        if (request.method === "POST" && path(request) === "/api/corporate-actions") {
          body = await request.json();
          return json({ id: actionId }, 201);
        }
        return catalog(request);
      });

      fireEvent.change(await screen.findByLabelText("Instrument"), { target: { value: "a" } });
      expect(await screen.findByRole("button", { name: "ACME.SIM · Acme Simulated Holdings (From market data)" })).toBeDisabled();
      fireEvent.click(screen.getByRole("button", { name: "AAPL · Apple Inc." }));
      expect(screen.getByText("Chosen: AAPL · Apple Inc.")).toBeInTheDocument();
      fireEvent.change(screen.getByLabelText("New shares"), { target: { value: "4" } });
      fireEvent.change(screen.getByLabelText("For every held"), { target: { value: "1" } });
      expect(screen.getByText("Every 1 shares become 4.")).toBeInTheDocument();
      fireEvent.change(screen.getByLabelText("Ex-date"), { target: { value: "2099-12-01" } });
      expect(screen.getByText("The change is applied when the first price from this date arrives.")).toBeInTheDocument();
      expect(screen.queryByText(/This date has passed/)).not.toBeInTheDocument();
      fireEvent.change(screen.getByLabelText("Memo"), { target: { value: "Announced by the issuer on 20 Nov" } });
      fireEvent.click(screen.getByRole("button", { name: "Save" }));

      await waitFor(() => expect(body).toEqual({
        instrumentId,
        kind: "Split",
        ratioNew: 4,
        ratioHeld: 1,
        exDate: "2099-12-01",
        recordDate: null,
        paymentDate: null,
        memo: "Announced by the issuer on 20 Nov",
      }));
      await waitFor(() => expect(router.state.location.pathname).toBe(`/corporate-actions/${actionId}`));
    });

    it("warns about a past ex-date, words the bonus preview, and shows a 400 at its field", async () => {
      renderFees(feeUser(), routes, "/corporate-actions/new", (request) =>
        request.method === "POST"
          ? problem(400, { errors: { RatioNew: ["The ratio is two whole numbers from 1 to 10000."] } })
          : catalog(request));

      await chooseOption("Kind", "Bonus issue");
      fireEvent.change(screen.getByLabelText("New shares"), { target: { value: "1" } });
      fireEvent.change(screen.getByLabelText("For every held"), { target: { value: "5" } });
      expect(screen.getByText("1 new shares for every 5 held.")).toBeInTheDocument();
      fireEvent.change(screen.getByLabelText("Ex-date"), { target: { value: "2020-01-01" } });
      expect(screen.getByText("This date has passed. The change is applied to today's holdings. Check trades booked since then.")).toBeInTheDocument();
      fireEvent.click(screen.getByRole("button", { name: "Save" }));
      expect(await screen.findByText("The ratio is two whole numbers from 1 to 10000.")).toBeInTheDocument();
    });

    it("edits with the rowVersion read and keeps the instrument", async () => {
      let body: unknown = null;
      renderFees(feeUser(), routes, `/corporate-actions/${actionId}/edit`, async (request) => {
        if (request.method === "PUT") {
          body = await request.json();
          return new Response(null, { status: 204 });
        }
        return path(request) === `/api/corporate-actions/${actionId}` ? json(action({ ratioNew: 4, memo: "First" })) : undefined;
      });

      expect(await screen.findByText("Instrument: AAPL · Apple Inc.")).toBeInTheDocument();
      await waitFor(() => expect(screen.getByLabelText("New shares")).toHaveValue("4"));
      fireEvent.change(screen.getByLabelText("New shares"), { target: { value: "3" } });
      fireEvent.click(screen.getByRole("button", { name: "Save" }));

      await waitFor(() => expect(body).toEqual({
        kind: "Split", ratioNew: 3, ratioHeld: 1, exDate: "2099-12-01", recordDate: null, paymentDate: null, memo: "First",
        rowVersion: "AAAA",
      }));
    });
  });

  describe("D68 the entitlement posting", () => {
    it("shows the kind and ratio, the ex-date, the quantities, a link and the Source line, and no Reverse", async () => {
      renderFees(feeUser(), [{ path: "/transactions/:id", element: <TransactionDetailPage /> }], `/transactions/${postingId}`,
        (request) => (path(request) === `/api/transactions/${postingId}` ? json(posting) : undefined));

      // The badge and the block title both read the corporate action.
      expect(await screen.findAllByText("Share split 2 for 1")).toHaveLength(2);
      expect(screen.getByText("Ex-date 2026-11-24")).toBeInTheDocument();
      expect(screen.getByText("100 → 200 shares")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Corporate action" })).toHaveAttribute("href", `/corporate-actions/${actionId}`);
      expect(screen.getByText("Source: Corporate action")).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Reverse" })).not.toBeInTheDocument();
    });

    it("labels the posting with the kind and ratio in the transactions list and the account activity", async () => {
      renderFees(feeUser(), [{ path: "/transactions", element: <TransactionsListPage /> }], "/transactions", (request) =>
        path(request) === "/api/transactions" ? page([posting]) : undefined);
      expect(await screen.findByRole("link", { name: "Share split 2 for 1" })).toBeInTheDocument();
    });

    it("labels the posting in the account activity", async () => {
      renderFees(feeUser(), [{ path: "/accounts/:id", element: <AccountDetailPage /> }], `/accounts/${accountId}`, (request) => {
        if (path(request) === `/api/accounts/${accountId}`) {
          return json({
            id: accountId, tenantId: "22222222-2222-2222-2222-222222222222", customerId, name: "Managed", type: "Brokerage",
            custodyMode: "Custodial", currency: "USD", depositReference: "K7M3Q9PXB", cashBalance: 0, reservedCash: 0,
            availableCash: 0, status: "Open", isActive: true, rowVersion: "AAAA", closure: null, accruedInterest: 0,
            interestAccruedThrough: null,
          });
        }
        if (path(request) === "/api/transactions") {
          return page([posting]);
        }
        if (path(request) === `/api/accounts/${accountId}/holdings`) {
          return json({ items: [] });
        }
        if (path(request) === "/api/withdrawals") {
          return page([]);
        }
        return undefined;
      });
      const activity = await screen.findByText("Share split 2 for 1");
      expect(activity).toBeInTheDocument();
    });
  });
});
