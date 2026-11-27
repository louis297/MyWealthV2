import { screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { TransactionDetailPage } from "@/features/transactions/TransactionDetailPage";
import { TransactionsListPage } from "@/features/transactions/TransactionsListPage";
import { feeUser, json, renderFees, stubBrowser } from "@/test/fees";

const accountId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
const customerId = "cccccccc-cccc-cccc-cccc-cccccccccccc";
const dividendId = "dddddddd-dddd-dddd-dddd-dddddddddddd";
const interestId = "dddddddd-dddd-dddd-dddd-111111111111";
const instrumentId = "99999999-9999-9999-9999-999999999999";

const enteredDividend = {
  id: dividendId,
  tenantId: "t",
  type: "Dividend",
  source: { kind: "Manual", id: null },
  dividend: {
    corporateActionId: null,
    instrument: { id: instrumentId, symbol: "AIA", name: "Auckland Airport" },
    quantity: 1000,
    amountPerShare: null,
    currency: "NZD",
    taxSource: "NewZealand",
    imputationCredit: 200,
    reinvestedQuantity: null,
    reinvestmentPrice: null,
    exDate: null,
    recordDate: null,
    paymentDate: "2026-11-13",
  },
  cash: [
    { accountId, customerId, role: "Principal", amount: 2000, currency: "NZD", fee: null, tax: null },
    {
      accountId, customerId, role: "Tax", amount: -526, currency: "NZD", fee: null,
      tax: { kind: "Rwt", ratePercent: 33, basisAmount: 2200, amount: 526, currency: "NZD" },
    },
  ],
  security: [],
  interest: null,
  bookedAt: "2026-11-20T00:00:00Z",
  memo: null,
  reference: null,
  originalTransactionId: null,
  reversedByTransactionId: null,
  rowVersion: "AAAA",
};

const enteredInterest = {
  ...enteredDividend,
  id: interestId,
  type: "Interest",
  dividend: null,
  cash: [
    { accountId, customerId, role: "Principal", amount: 12.34, currency: "NZD", fee: null, tax: null },
    {
      accountId, customerId, role: "Tax", amount: -4.07, currency: "NZD", fee: null,
      tax: { kind: "Rwt", ratePercent: 33, basisAmount: 12.34, amount: 4.07, currency: "NZD" },
    },
  ],
};

function detail(posting: typeof enteredDividend) {
  renderFees(feeUser({ role: "adviser" }), [{ path: "/transactions/:id", element: <TransactionDetailPage /> }],
    `/transactions/${posting.id}`,
    (request) => (new URL(request.url).pathname === `/api/transactions/${posting.id}` ? json(posting) : undefined));
}

describe("Income entered on an External account (external-income D113)", () => {
  beforeEach(() => stubBrowser("/transactions"));

  it("shows a hand-entered dividend with no corporate action, no amount per share, no missing date, and Reverse", async () => {
    detail(enteredDividend);

    expect(await screen.findByText("Dividend AIA")).toBeInTheDocument();
    expect(screen.getByText("Source: Entered by hand")).toBeInTheDocument();
    expect(screen.getByText("1000 shares")).toBeInTheDocument();
    expect(screen.queryByText(/shares ×/)).not.toBeInTheDocument();
    expect(screen.getByText("RWT 33 %: 526.00 NZD")).toBeInTheDocument();
    expect(screen.getByText("Imputation credit 200.00 NZD")).toBeInTheDocument();
    expect(screen.getByText("Net cash 1,474.00 NZD")).toBeInTheDocument();
    expect(screen.queryByText("Ex-date")).not.toBeInTheDocument();
    expect(screen.queryByText("Record date")).not.toBeInTheDocument();
    expect(screen.getByText("Payment date")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Corporate action" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reverse" })).toBeInTheDocument();
  });

  it("shows the amount per share when it was entered", async () => {
    detail({ ...enteredDividend, dividend: { ...enteredDividend.dividend, amountPerShare: 2 } } as typeof enteredDividend);

    expect(await screen.findByText("1000 shares × 2.00")).toBeInTheDocument();
  });

  it("shows a hand Interest with its gross, each tax with its rate, and the net", async () => {
    detail(enteredInterest as typeof enteredDividend);

    const block = within((await screen.findByRole("heading", { name: "Interest" })).closest("section")!);
    expect(block.getByText("12.34 NZD")).toBeInTheDocument();
    expect(block.getByText("RWT 33 %: 4.07 NZD")).toBeInTheDocument();
    expect(block.getByText("8.27 NZD")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Daily interest" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reverse" })).toBeInTheDocument();
  });

  it("lists a hand-entered dividend and interest with their net and the tax under it", async () => {
    renderFees(feeUser(), [{ path: "/transactions", element: <TransactionsListPage /> }], "/transactions",
      (request) => (new URL(request.url).pathname === "/api/transactions"
        ? json({ items: [enteredDividend, enteredInterest], page: 1, pageSize: 20, totalCount: 2 })
        : undefined));

    const dividendRow = within((await screen.findByRole("link", { name: "Dividend AIA" })).closest("tr")!);
    expect(dividendRow.getByText("1,474.00 NZD")).toBeInTheDocument();
    expect(dividendRow.getByText("incl. tax 526.00 NZD")).toBeInTheDocument();
    const interestRow = within(screen.getByRole("link", { name: "Interest" }).closest("tr")!);
    expect(interestRow.getByText("8.27 NZD")).toBeInTheDocument();
    expect(interestRow.getByText("incl. tax 4.07 NZD")).toBeInTheDocument();
  });
});
