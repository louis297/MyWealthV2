import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { AccountDetailPage } from "@/features/accounts/AccountDetailPage";
import { kindLabel as reconciliationKindLabel } from "@/features/bankReconciliations/bankReconciliationLabels";
import { CorporateActionFormPage } from "@/features/corporateActions/CorporateActionFormPage";
import { CorporateActionPage } from "@/features/corporateActions/CorporateActionPage";
import { CorporateActionsPage } from "@/features/corporateActions/CorporateActionsPage";
import type { CorporateAction, CorporateActionEntitlement } from "@/features/corporateActions/corporateActionsApi";
import { MatchStatementLinePage } from "@/features/statementLines/MatchStatementLinePage";
import { StatementLineDetailPage } from "@/features/statementLines/StatementLineDetailPage";
import { reasonLabel } from "@/features/statementLines/statementLineLabels";
import { TransactionDetailPage } from "@/features/transactions/TransactionDetailPage";
import { chooseOption } from "@/test/chooseOption";
import { adviser, feeUser, json, renderFees, requestsTo, stubBrowser } from "@/test/fees";

const actionId = "aaaaaaaa-1111-1111-1111-aaaaaaaaaaaa";
const instrumentId = "99999999-9999-9999-9999-999999999999";
const accountId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
const customerId = "cccccccc-cccc-cccc-cccc-cccccccccccc";
const postingId = "dddddddd-dddd-dddd-dddd-dddddddddddd";
const lineId = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";

function dividend(overrides: Partial<CorporateAction> = {}): CorporateAction {
  return {
    id: actionId,
    instrument: { id: instrumentId, symbol: "ACME.SIM", name: "Acme Simulated Holdings" },
    kind: "Dividend",
    ratioNew: null,
    ratioHeld: null,
    exDate: "2026-11-26",
    recordDate: null,
    paymentDate: "2026-11-30",
    origin: "Feed",
    status: "Effective",
    memo: null,
    createdBy: null,
    createdAt: "2026-11-24T03:15:02Z",
    effectiveAt: "2026-11-26T00:00:02Z",
    effectiveBy: null,
    appliedAt: null,
    cancelledAt: null,
    cancelledBy: null,
    cancelNote: null,
    entitlements: { total: 2, posted: 0, pending: 2, notApplied: 0 },
    dividend: {
      amountPerShare: 1, currency: "USD", taxSource: "Foreign", imputationCreditPerShare: 0, foreignTaxRatePercent: 15,
      reinvestmentPrice: 25,
    },
    payment: { expectedAmount: 103, receivedAmount: 103.02, difference: 0.02, currency: "USD", statementLineId: lineId, paidAt: null },
    election: {
      status: "Accepted", quantity: 78.82352941, attempts: 1, reason: null, sharesReceived: null, releasedBy: null, releasedAt: null,
      note: null,
    },
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
    quantityBefore: null,
    quantityAfter: null,
    transactionId: postingId,
    postedAt: "2026-11-24T04:10:31Z",
    entitledQuantity: 100,
    grossAmount: 100,
    foreignTax: 15,
    currency: "USD",
    reinvests: true,
    participatingQuantity: 78.82352941,
    reinvestedQuantity: 2.68,
    netAmount: 67,
    netCurrency: "USD",
    ...overrides,
  };
}

function path(request: Request) {
  return new URL(request.url).pathname;
}

const page = <T,>(items: T[]) => json({ items, page: 1, pageSize: 20, totalCount: items.length });

describe("Dividends (dividends D94–D101)", () => {
  beforeEach(() => {
    stubBrowser("/");
  });

  it("D94 lists a dividend with its amount per share and status, and filters by kind", async () => {
    const rows = [dividend(), dividend({ id: "x2", status: "Applied" }), dividend({ id: "x3", payment: { ...dividend().payment!, paidAt: "2026-11-30T04:10:02Z" } })];
    const { requests } = renderFees(feeUser(), [{ path: "/corporate-actions", element: <CorporateActionsPage /> }], "/corporate-actions",
      (request) => (path(request) === "/api/corporate-actions" ? page(rows) : undefined));

    const table = await screen.findByRole("table", { name: "Corporate actions" });
    expect(await within(table).findAllByText("Dividend")).toHaveLength(3);
    expect(within(table).getAllByText("1.00 USD per share")).toHaveLength(3);
    expect(within(table).getByText("Waiting for payment")).toBeInTheDocument();
    expect(within(table).getByText("Paid")).toBeInTheDocument();
    expect(within(table).getByText("Being paid")).toBeInTheDocument();
    await chooseOption("Kind", "Dividend");
    await waitFor(() =>
      expect(requestsTo(requests, "GET", "/corporate-actions").some((request) => request.url.includes("kind=Dividend"))).toBe(true));
  });

  describe("D95, D97 one dividend", () => {
    const routes = [{ path: "/corporate-actions/:id", element: <CorporateActionPage /> }];

    function handler(item: CorporateAction, rows: CorporateActionEntitlement[] = [entitlement()]) {
      return (request: Request) => {
        if (path(request) === `/api/corporate-actions/${actionId}` && request.method === "GET") {
          return json(item);
        }
        if (path(request) === `/api/corporate-actions/${actionId}/entitlements`) {
          return page(rows);
        }
        if (path(request) === `/api/corporate-actions/${actionId}/pay-in-cash`) {
          return new Response(null, { status: 204 });
        }
        return undefined;
      };
    }

    it("shows the terms, the payment with its difference in amber, the election and the entitlements", async () => {
      renderFees(feeUser(), routes, `/corporate-actions/${actionId}`, handler(dividend({ election: { ...dividend().election!, sharesReceived: 2.68 } })));

      expect(await screen.findByText("Foreign company")).toBeInTheDocument();
      expect(screen.getByText("15 %")).toBeInTheDocument();
      expect(screen.getByText("25.00 USD")).toBeInTheDocument();
      expect(screen.getByText("Customers are owed 103.00 USD")).toBeInTheDocument();
      expect(screen.getByText("The custodian paid 103.02 USD")).toBeInTheDocument();
      expect(screen.getByText("Difference 0.02 USD")).toHaveClass("text-amber-600");
      expect(screen.getByText("Accepted by the custodian")).toBeInTheDocument();
      expect(screen.getByText("2.68 shares received")).toBeInTheDocument();
      const table = screen.getByRole("table", { name: "Entitlements" });
      expect(await within(table).findByText("2.68 new shares")).toBeInTheDocument();
      expect(within(table).getByText("100.00 USD")).toBeInTheDocument();
      expect(within(table).getByText("15.00 USD")).toBeInTheDocument();
      expect(within(table).getByText("67.00 USD")).toBeInTheDocument();
      expect(screen.queryByText(/Every .* shares become/)).not.toBeInTheDocument();
    });

    it("shows a dividend with no plan and no receipt yet", async () => {
      renderFees(feeUser(), routes, `/corporate-actions/${actionId}`, handler(dividend({
        dividend: { ...dividend().dividend!, reinvestmentPrice: null, taxSource: "NewZealand", foreignTaxRatePercent: 0, imputationCreditPerShare: 0.28 },
        payment: { expectedAmount: 93.5, receivedAmount: null, difference: null, currency: "USD", statementLineId: null, paidAt: null },
        election: null,
      })));

      expect(await screen.findByText("New Zealand company")).toBeInTheDocument();
      expect(screen.getByText("No reinvestment plan")).toBeInTheDocument();
      expect(screen.getByText("Not received yet")).toBeInTheDocument();
    });

    it("offers Pay in cash on a dividend that waits for shares, with a required note", async () => {
      const { requests } = renderFees(feeUser(), routes, `/corporate-actions/${actionId}`, handler(dividend()));

      fireEvent.click(await screen.findByRole("button", { name: "Pay in cash" }));
      expect(screen.getByText("No new shares arrived for this dividend. Every customer is paid in cash.")).toBeInTheDocument();
      fireEvent.change(screen.getByLabelText("Note"), { target: { value: "The registry issued no shares" } });
      fireEvent.click(screen.getByRole("button", { name: "Confirm" }));

      await waitFor(() => expect(requestsTo(requests, "POST", `/corporate-actions/${actionId}/pay-in-cash`)).toHaveLength(1));
      const sent = await requestsTo(requests, "POST", `/corporate-actions/${actionId}/pay-in-cash`)[0].json();
      expect(sent).toEqual({ rowVersion: "AAAA", note: "The registry issued no shares" });
    });

    it("words Apply now on a hand-entered dividend as fixing the entitlements, and an Adviser gets no Pay in cash", async () => {
      renderFees(feeUser(), routes, `/corporate-actions/${actionId}`, handler(dividend({
        origin: "Manual", status: "Announced", exDate: "2020-01-01", effectiveAt: null, payment: null, election: null,
      })));

      expect(await screen.findByRole("button", { name: "Fix the entitlements now" })).toBeInTheDocument();
    });

    it("shows no Pay in cash to an Adviser", async () => {
      renderFees(adviser(), routes, `/corporate-actions/${actionId}`, handler(dividend()));

      await screen.findByText("Foreign company");
      expect(screen.queryByRole("button", { name: "Pay in cash" })).not.toBeInTheDocument();
    });
  });

  it("D96 enters a dividend with its terms and no ratio", async () => {
    const { requests } = renderFees(feeUser(), [{ path: "/corporate-actions/new", element: <CorporateActionFormPage /> },
      { path: "/corporate-actions/:id", element: <p>saved</p> }], "/corporate-actions/new", (request) => {
      if (path(request) === "/api/instruments") {
        return page([{ id: instrumentId, symbol: "AAPL", name: "Apple Inc.", isActive: true, marketDataConnection: null }]);
      }
      if (path(request) === "/api/corporate-actions" && request.method === "POST") {
        return json({ id: actionId }, 201);
      }
      return undefined;
    });

    fireEvent.change(await screen.findByLabelText("Instrument"), { target: { value: "AAPL" } });
    fireEvent.click(await screen.findByRole("button", { name: "AAPL · Apple Inc." }));
    await chooseOption("Kind", "Dividend");
    expect(screen.queryByLabelText("New shares")).not.toBeInTheDocument();
    expect(screen.getByText(/They are paid when you match the custodian's payment./)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Amount per share"), { target: { value: "0.26" } });
    await chooseOption("Company", "Foreign");
    fireEvent.change(screen.getByLabelText("Foreign tax rate"), { target: { value: "15" } });
    fireEvent.change(screen.getByLabelText("Ex-date"), { target: { value: "2099-12-08" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await screen.findByText("saved");
    const sent = await requestsTo(requests, "POST", "/corporate-actions")[0].json();
    expect(sent).toMatchObject({
      instrumentId, kind: "Dividend", amountPerShare: 0.26, taxSource: "Foreign", foreignTaxRatePercent: 15, imputationCreditPerShare: 0,
      exDate: "2099-12-08",
    });
    expect(sent.ratioNew).toBeUndefined();
  });

  describe("D98 a receipt of a dividend", () => {
    const line = {
      id: lineId, clientMoneyAccountId: "f1", direction: "Credit", kind: "Transfer", amount: 103.02, currency: "USD",
      valueDate: "2026-11-30", receivedAt: "2026-11-30T04:00:00Z", payerName: "Simulator Custody", payerAccount: null,
      reference: "w-CA3", status: "Unmatched", unmatchedReason: "DividendMismatch", match: null, lastAction: null, rowVersion: "AAAA",
    };

    it("labels a mismatch, and matches the receipt to a dividend that waits for its payment with a note", async () => {
      expect(reasonLabel("DividendMismatch")).toBe("Names a dividend but does not fit it");
      const waiting = dividend({ payment: { expectedAmount: 103, receivedAmount: null, difference: null, currency: "USD", statementLineId: null, paidAt: null } });
      const { requests } = renderFees(feeUser(), [{ path: "/statement-lines/:id/match", element: <MatchStatementLinePage /> },
        { path: "/statement-lines/:id", element: <p>matched</p> }], `/statement-lines/${lineId}/match`, (request) => {
        if (path(request) === `/api/statement-lines/${lineId}` && request.method === "GET") {
          return json(line);
        }
        if (path(request) === "/api/corporate-actions") {
          return page([waiting, dividend({ id: "nzd", dividend: { ...dividend().dividend!, currency: "NZD" } })]);
        }
        if (path(request) === `/api/statement-lines/${lineId}/match`) {
          return new Response(null, { status: 204 });
        }
        return undefined;
      });

      fireEvent.click(await screen.findByRole("button", { name: "To a dividend" }));
      fireEvent.click(await screen.findByRole("button", { name: /ACME.SIM · owed 103.00 USD/ }));
      expect(screen.getByText("The custodian paid 103.02 USD and the customers are owed 103.00 USD. Matching pays the customers in full."))
        .toBeInTheDocument();
      expect(screen.getAllByRole("button", { name: /ACME.SIM · owed/ })).toHaveLength(1);
      fireEvent.change(screen.getByLabelText("Note"), { target: { value: "Advice of 30 Nov" } });
      fireEvent.click(screen.getByRole("button", { name: "Confirm match" }));

      await screen.findByText("matched");
      const sent = await requestsTo(requests, "POST", `/statement-lines/${lineId}/match`)[0].json();
      expect(sent).toEqual({ corporateActionId: actionId, rowVersion: "AAAA", note: "Advice of 30 Nov" });
      expect(requestsTo(requests, "GET", "/corporate-actions").some((request) =>
        request.url.includes("kind=Dividend") && request.url.includes("status=Effective"))).toBe(true);
    });

    it("links a receipt matched to a dividend to it, and offers no action", async () => {
      renderFees(feeUser(), [{ path: "/statement-lines/:id", element: <StatementLineDetailPage /> }], `/statement-lines/${lineId}`,
        (request) => (path(request) === `/api/statement-lines/${lineId}`
          ? json({ ...line, status: "Matched", unmatchedReason: null, corporateActionId: actionId,
            match: { accountId: null, customerId: null, transactionId: null, method: "Dividend", at: "2026-11-30T04:10:02Z" } })
          : undefined));

      expect(await screen.findByRole("link", { name: "Dividend" })).toHaveAttribute("href", `/corporate-actions/${actionId}`);
      expect(screen.queryByRole("button", { name: "Unmatch" })).not.toBeInTheDocument();
    });
  });

  describe("D99 the account page", () => {
    const custodial = {
      id: accountId, tenantId: "t", customerId, name: "Demo custodial brokerage", type: "Brokerage", custodyMode: "Custodial",
      currency: "USD", depositReference: "K7M3Q9PXB", cashBalance: 100, status: "Open", isActive: true, rowVersion: "AAAA",
      reinvestDividends: false, pendingDividends: 1,
    };

    it("switches dividend reinvestment and counts the dividends to be paid", async () => {
      const { requests } = renderFees(feeUser(), [{ path: "/accounts/:id", element: <AccountDetailPage /> }], `/accounts/${accountId}`,
        (request) => {
          if (path(request) === `/api/accounts/${accountId}` && request.method === "GET") {
            return json(custodial);
          }
          if (path(request) === `/api/accounts/${accountId}/dividend-reinvestment`) {
            return new Response(null, { status: 204 });
          }
          return undefined;
        });

      expect(await screen.findByText("1 dividend(s) to be paid")).toBeInTheDocument();
      expect(screen.getByText("Applies to dividends that offer a reinvestment plan. Tax is still paid in cash.")).toBeInTheDocument();
      fireEvent.click(screen.getByRole("checkbox", { name: "Reinvest dividends" }));

      await waitFor(() => expect(requestsTo(requests, "PUT", `/accounts/${accountId}/dividend-reinvestment`)).toHaveLength(1));
      expect(await requestsTo(requests, "PUT", `/accounts/${accountId}/dividend-reinvestment`)[0].json()).toEqual({ reinvest: true, rowVersion: "AAAA" });
    });
  });

  it("D100 shows a dividend posting with its taxes, credit and reinvestment, and no Reverse", async () => {
    const posting = {
      id: postingId, tenantId: "t", type: "Dividend", source: { kind: "CorporateAction", id: actionId },
      dividend: {
        corporateActionId: actionId, instrument: { id: instrumentId, symbol: "ACME.SIM", name: "Acme" }, quantity: 100, amountPerShare: 1,
        currency: "USD", taxSource: "Foreign", imputationCredit: 2.5, reinvestedQuantity: 2.68, reinvestmentPrice: 25,
        exDate: "2026-11-26", recordDate: null, paymentDate: "2026-11-30",
      },
      cash: [
        { accountId, customerId, role: "Principal", amount: 100, currency: "USD", fee: null, tax: null },
        { accountId, customerId, role: "Tax", amount: -15, currency: "USD", fee: null,
          tax: { kind: "ForeignWithholding", ratePercent: 15, basisAmount: 100, amount: 15, currency: "USD" } },
        { accountId, customerId, role: "Tax", amount: -18, currency: "USD", fee: null,
          tax: { kind: "Rwt", ratePercent: 33, basisAmount: 100, amount: 18, currency: "USD" } },
        { accountId, customerId, role: "Reinvestment", amount: -67, currency: "USD", fee: null, tax: null },
      ],
      security: [{ accountId, customerId, instrumentId, quantity: 2.68, cost: 67, costCurrency: "USD" }],
      bookedAt: "2026-11-24T04:10:31Z", memo: "Dividend ACME.SIM 1.00 per share", reference: null,
      originalTransactionId: null, reversedByTransactionId: null, rowVersion: "AAAA",
    };
    renderFees(feeUser(), [{ path: "/transactions/:id", element: <TransactionDetailPage /> }], `/transactions/${postingId}`,
      (request) => (path(request) === `/api/transactions/${postingId}` ? json(posting) : undefined));

    expect(await screen.findByText("Dividend ACME.SIM")).toBeInTheDocument();
    expect(screen.getByText("100 shares × 1.00")).toBeInTheDocument();
    expect(screen.getByText("Foreign tax 15 %: 15.00 USD")).toBeInTheDocument();
    expect(screen.getByText("RWT 33 %: 18.00 USD")).toBeInTheDocument();
    expect(screen.getByText("Imputation credit 2.50 USD")).toBeInTheDocument();
    expect(screen.getByText("Reinvested: 2.68 shares at 25.00")).toBeInTheDocument();
    expect(screen.getByText("Net cash 0.00 USD")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Corporate action" })).toHaveAttribute("href", `/corporate-actions/${actionId}`);
    expect(screen.getByText("Source: Corporate action")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reverse" })).not.toBeInTheDocument();
  });

  it("D101 labels the two dividend items of a bank reconciliation", () => {
    expect(reconciliationKindLabel("DividendsNotPosted")).toBe("Dividends received, not paid to customers yet");
    expect(reconciliationKindLabel("DividendDifference")).toBe(
      "Dividends: difference between the custodian's payment and the customers' credits");
  });
});
