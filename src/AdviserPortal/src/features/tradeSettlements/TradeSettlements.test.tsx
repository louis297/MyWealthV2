import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { OrderDetailPage } from "@/features/orders/OrderDetailPage";
import { TradeSettlementDetailPage } from "@/features/tradeSettlements/TradeSettlementDetailPage";
import { TradeSettlementsListPage } from "@/features/tradeSettlements/TradeSettlementsListPage";
import { TransactionDetailPage } from "@/features/transactions/TransactionDetailPage";
import { ShellLayout } from "@/layouts/ShellLayout";
import { chooseOption } from "@/test/chooseOption";
import { adviser, feeUser, json, renderFees, requestsTo, stubBrowser } from "@/test/fees";

const settlementId = "55555555-0000-0000-0000-000000000001";
const orderId = "99999999-0000-0000-0000-000000000001";
const accountId = "aaaaaaaa-0000-0000-0000-000000000001";
const customerId = "cccccccc-0000-0000-0000-000000000001";
const lineId = "3f2c0000-0000-0000-0000-0000000000e1";
const reversalId = "88888888-0000-0000-0000-000000000009";
const me = feeUser();

function settlement(overrides: Record<string, unknown> = {}) {
  return {
    id: settlementId,
    clientMoneyAccount: { id: "bbbbbbbb-0000-0000-0000-000000000001", currency: "USD", accountNumber: "SIM-DEMO-USD" },
    connection: "simulator",
    brokerSettlementId: "0f8fad5b-S3",
    settledDate: "2026-11-25",
    fillCount: 3,
    netAmount: -1001.5,
    broker: { fillCount: 3, netAmount: -1001.5, settlementAccount: "SIM-DEMO-USD" },
    status: "AwaitingPayment",
    failure: null,
    failureDetail: null,
    completedAt: null,
    statementLineId: null,
    setAside: null,
    payments: [{ status: "Accepted", attempts: 1, lastFailure: null, createdAt: "2026-11-25T02:00:04Z" }],
    fills: [
      { id: "ffffffff-0000-0000-0000-000000000001", orderId, accountId, side: "Buy", quantity: 4, price: 100.12, gross: 400.48, status: "Posted" },
    ],
    rowVersion: "AAAB",
    ...overrides,
  };
}

const page = (items: unknown[]) => json({ items, page: 1, pageSize: 20, totalCount: items.length });

async function body(request: Request) {
  return JSON.parse(await request.clone().text());
}

describe("Trade settlements navigation (trade-settlement D76)", () => {
  beforeEach(() => stubBrowser("/dashboard"));

  it("shows Trade settlements to a TenantAdmin", async () => {
    renderFees(me, [{ path: "/dashboard", element: <ShellLayout /> }], "/dashboard");
    expect(await screen.findByRole("link", { name: /^Trade settlements/ })).toHaveAttribute("href", "/trade-settlements");
  });

  it("hides Trade settlements from an Adviser", async () => {
    renderFees(adviser(), [{ path: "/dashboard", element: <ShellLayout /> }], "/dashboard");
    expect(await screen.findByRole("link", { name: /^Orders/ })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /^Trade settlements/ })).not.toBeInTheDocument();
  });
});

describe("TradeSettlementsListPage (trade-settlement D76)", () => {
  const routes = [{ path: "/trade-settlements", element: <TradeSettlementsListPage /> }];

  beforeEach(() => stubBrowser("/trade-settlements"));

  it("lists the open settlements by default, with the columns, the net and the status labels", async () => {
    const receipt = settlement({ id: "55555555-0000-0000-0000-000000000002", brokerSettlementId: "0f8fad5b-S4", netAmount: 2340, status: "AwaitingReceipt" });
    const { requests } = renderFees(me, routes, "/trade-settlements", (request) =>
      new URL(request.url).pathname === "/api/trade-settlements" ? page([settlement(), receipt]) : undefined);

    expect(await screen.findByText("Pay broker 1,001.50")).toBeInTheDocument();
    expect(new URL(requestsTo(requests, "GET", "/trade-settlements")[0].url).searchParams.get("open")).toBe("true");
    const table = screen.getByRole("table", { name: "Trade settlements" });
    expect(within(table).getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual([
      "Settled date", "Broker reference", "Currency", "Trades", "Net", "Status",
    ]);
    const rows = within(table).getAllByRole("row").slice(1);
    expect(within(rows[0]).getByRole("link", { name: "2026-11-25" })).toHaveAttribute("href", `/trade-settlements/${settlementId}`);
    expect(within(rows[0]).getByText("Paying the broker")).toBeInTheDocument();
    expect(within(rows[1]).getByText("Receive 2,340.00")).toBeInTheDocument();
    expect(within(rows[1]).getByText("Waiting for the broker's payment")).toBeInTheDocument();
  });

  it("filters by a status", async () => {
    const { requests } = renderFees(me, routes, "/trade-settlements", (request) =>
      new URL(request.url).pathname === "/api/trade-settlements" ? page([settlement({ status: "Disputed" })]) : undefined);

    await chooseOption("Status", "Figures differ");

    await waitFor(() => {
      const last = new URL(requestsTo(requests, "GET", "/trade-settlements").at(-1)!.url);
      expect(last.searchParams.get("status")).toBe("Disputed");
      expect(last.searchParams.get("open")).toBeNull();
    });
  });
});

describe("TradeSettlementDetailPage (trade-settlement D76)", () => {
  const routes = [{ path: "/trade-settlements/:id", element: <TradeSettlementDetailPage /> }];
  const detailPath = `/trade-settlements/${settlementId}`;

  beforeEach(() => stubBrowser(detailPath));

  function serve(item: () => unknown) {
    return (request: Request) => {
      if (request.method === "POST") {
        return new Response(null, { status: 204 });
      }
      return new URL(request.url).pathname === `/api/trade-settlements/${settlementId}` ? json(item()) : undefined;
    };
  }

  it("shows the platform's and the broker's figures side by side, the fills and the payments", async () => {
    const disputed = settlement({ status: "Disputed", broker: { fillCount: 3, netAmount: -1001.49, settlementAccount: "SIM-DEMO-USD" }, payments: [] });
    renderFees(me, routes, detailPath, serve(() => disputed));

    expect(await screen.findByText("Figures differ")).toBeInTheDocument();
    const figures = screen.getByRole("table", { name: "Figures" });
    const net = within(figures).getAllByRole("row")[2];
    expect(within(net).getByText("-1,001.50 USD")).toBeInTheDocument();
    expect(within(net).getByText("-1,001.49 USD")).toHaveClass("text-destructive");
    const trades = screen.getByRole("table", { name: "Trades" });
    expect(within(trades).getByRole("link", { name: "Order" })).toHaveAttribute("href", `/orders/${orderId}`);
    expect(screen.getByText("No payment.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Set aside" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Pay again" })).not.toBeInTheDocument();
  });

  it("shows why a payment failed, and pays again with the row version", async () => {
    const failed = settlement({ status: "PaymentFailed", failure: "PaymentRejected", failureDetail: "InvalidAccount" });
    const { requests } = renderFees(me, routes, detailPath, serve(() => failed));

    expect(await screen.findByText(/^The bank refused the payment: /)).toBeInTheDocument();
    expect(screen.getByText("Sent to the bank")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Pay again" }));

    await waitFor(() => expect(requestsTo(requests, "POST", `/trade-settlements/${settlementId}/pay`)).toHaveLength(1));
    expect(await body(requestsTo(requests, "POST", `/trade-settlements/${settlementId}/pay`)[0])).toEqual({ rowVersion: "AAAB" });
  });

  it("sets aside with a required note and the sentence of D76", async () => {
    const { requests } = renderFees(me, routes, detailPath, serve(() => settlement({ status: "AwaitingReceipt", netAmount: 990 })));

    fireEvent.click(await screen.findByRole("button", { name: "Set aside" }));
    expect(await screen.findByText(/The platform stops waiting for this settlement\./)).toBeInTheDocument();
    fireEvent.change(screen.getByRole("textbox"), { target: { value: "Agreed with the broker by phone" } });
    fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Confirm" }));

    await waitFor(() => expect(requestsTo(requests, "POST", `/trade-settlements/${settlementId}/set-aside`)).toHaveLength(1));
    expect(await body(requestsTo(requests, "POST", `/trade-settlements/${settlementId}/set-aside`)[0]))
      .toEqual({ rowVersion: "AAAB", note: "Agreed with the broker by phone" });
  });

  it("links the matched statement line and shows the set-aside note", async () => {
    const done = settlement({
      status: "SetAside",
      statementLineId: lineId,
      setAside: { by: { id: "eeeeeeee-0000-0000-0000-000000000001", name: "Tia Admin" }, at: "2026-11-26T01:00:00Z", note: "Paid by hand" },
    });
    renderFees(me, routes, detailPath, serve(() => done));

    expect(await screen.findByRole("link", { name: "Bank line" })).toHaveAttribute("href", `/statement-lines/${lineId}`);
    expect(screen.getByText("Paid by hand")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Set aside" })).not.toBeInTheDocument();
  });
});

describe("A broken trade on the order page (trade-settlement D77)", () => {
  const routes = [{ path: "/orders/:id", element: <OrderDetailPage /> }];

  beforeEach(() => stubBrowser(`/orders/${orderId}`));

  const brokenFill = (outcome: string, extra: Record<string, unknown> = {}) => ({
    id: "ffffffff-0000-0000-0000-000000000001",
    quantity: 4,
    price: 100.12,
    tradeDate: "2026-11-23",
    settlementDate: "2026-11-25",
    brokerReference: "CN-1",
    status: "Broken",
    holdReason: null,
    transactionId: "88888888-0000-0000-0000-000000000001",
    postedAt: "2026-11-23T02:13:00Z",
    settledAt: null,
    brokerSettlementId: null,
    tradeSettlementId: null,
    break: {
      at: "2026-11-24T04:10:02Z",
      reason: "Broken by the exchange",
      outcome,
      holdReason: null,
      reversalTransactionId: null,
      resolvedBy: null,
      resolvedAt: null,
      note: null,
      ...extra,
    },
  });

  function item(fill: unknown) {
    return {
      id: orderId,
      accountId,
      customerId,
      instrument: { id: "11111111-0000-0000-0000-000000000001", symbol: "ACME.SIM", name: "Acme Holdings" },
      side: "Buy",
      type: "Market",
      timeInForce: "Day",
      quantity: 10,
      filledQuantity: 0,
      openQuantity: 10,
      currency: "USD",
      referencePrice: 100,
      referencePriceDate: "2026-11-23",
      priceLimit: 105,
      reservedCash: 1055.25,
      reservedQuantity: 0,
      averagePrice: null,
      status: "Working",
      memo: null,
      placedBy: { id: "eeeeeeee-0000-0000-0000-000000000001", name: "Ann Adviser" },
      placedAt: "2026-11-23T02:10:00Z",
      send: null,
      rejectReason: null,
      cancelRequest: null,
      closedAt: null,
      closeReason: null,
      fills: [fill],
      rowVersion: "AAAC",
    };
  }

  function serve(fill: unknown) {
    return (request: Request) => {
      if (request.method === "POST") {
        return new Response(null, { status: 204 });
      }
      const p = new URL(request.url).pathname;
      if (p === `/api/orders/${orderId}`) {
        return json(item(fill));
      }
      if (p === `/api/accounts/${accountId}`) {
        return json({ id: accountId, name: "Managed portfolio", currency: "USD" });
      }
      return undefined;
    };
  }

  it("strikes the fill through, says the broker cancelled it and links the Reversal", async () => {
    renderFees(me, routes, `/orders/${orderId}`, serve(brokenFill("Reversed", { reversalTransactionId: reversalId })));

    expect(await screen.findByText("Cancelled by the broker 24 Nov: Broken by the exchange")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Reversed" })).toHaveAttribute("href", `/transactions/${reversalId}`);
    const row = within(screen.getByRole("table", { name: "Fills" })).getAllByRole("row")[1];
    expect(within(row).getByText("2026-11-23")).toHaveClass("line-through");
  });

  it.each([
    ["NotPosted", {}, "Was not booked"],
    ["ResolvedByHand", { holdReason: "LaterActivity", resolvedBy: { id: "x", name: "Tia Admin" }, resolvedAt: "2026-11-25T00:00:00Z" }, "Resolved by Tia Admin"],
  ])("shows %s", async (outcome, extra, label) => {
    renderFees(me, routes, `/orders/${orderId}`, serve(brokenFill(outcome, extra)));

    expect(await screen.findByText(label)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Mark resolved" })).not.toBeInTheDocument();
  });

  it("asks a TenantAdmin to correct the account and mark a break that could not be reversed as resolved", async () => {
    const { requests } = renderFees(me, routes, `/orders/${orderId}`, serve(brokenFill("NotApplied", { holdReason: "InsufficientCash" })));

    expect(await screen.findByText("Could not be reversed: The proceeds have been spent")).toBeInTheDocument();
    expect(screen.getByText("Needs attention")).toBeInTheDocument();
    expect(screen.getByText("Correct the account with an adjustment, then mark this resolved.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Mark resolved" }));
    fireEvent.change(await screen.findByRole("textbox"), { target: { value: "Corrected with adjustment 2026-11-24" } });
    fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Confirm" }));

    const path = `/orders/${orderId}/fills/ffffffff-0000-0000-0000-000000000001/resolve-break`;
    await waitFor(() => expect(requestsTo(requests, "POST", path)).toHaveLength(1));
    expect(await body(requestsTo(requests, "POST", path)[0])).toEqual({ note: "Corrected with adjustment 2026-11-24", rowVersion: "AAAC" });
  });

  it("does not offer Mark resolved to an Adviser", async () => {
    renderFees(adviser(), routes, `/orders/${orderId}`, serve(brokenFill("NotApplied", { holdReason: "LaterActivity" })));

    expect(await screen.findByText("Could not be reversed: Later activity on this holding")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Mark resolved" })).not.toBeInTheDocument();
  });
});

describe("The transaction page of a broken fill (trade-settlement D77)", () => {
  const transactionId = "88888888-0000-0000-0000-000000000001";

  beforeEach(() => stubBrowser(`/transactions/${transactionId}`));

  it("says the broker cancelled the trade in the Trade section", async () => {
    renderFees(me, [{ path: "/transactions/:id", element: <TransactionDetailPage /> }], `/transactions/${transactionId}`, (request) =>
      new URL(request.url).pathname === `/api/transactions/${transactionId}`
        ? json({
          id: transactionId,
          tenantId: "22222222-2222-2222-2222-222222222222",
          type: "Buy",
          source: { kind: "OrderFill", id: orderId },
          cash: [],
          bookedAt: "2026-11-23T02:13:00Z",
          rowVersion: "AAAA",
          security: [],
          execution: {
            orderId, fillId: "ffffffff-0000-0000-0000-000000000001", price: 100.12, tradeDate: "2026-11-23", settlementDate: "2026-11-25",
            brokerReference: "CN-1", settledAt: null, brokenAt: "2026-11-24T04:10:02Z",
          },
        })
        : undefined);

    expect(await screen.findByText("Cancelled by the broker 24 Nov")).toBeInTheDocument();
  });
});
