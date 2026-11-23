import { cleanup, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { OrderDetailPage } from "@/features/orders/OrderDetailPage";
import { OrdersListPage } from "@/features/orders/OrdersListPage";
import { PortfolioDetailPage } from "@/features/portfolios/PortfolioDetailPage";
import { RebalanceDetailPage } from "@/features/rebalances/RebalanceDetailPage";
import { RebalancesListPage } from "@/features/rebalances/RebalancesListPage";
import { ShellLayout } from "@/layouts/ShellLayout";
import { chooseOption } from "@/test/chooseOption";
import { adviser, feeUser, json, problem, renderFees, requestsTo, stubBrowser } from "@/test/fees";

const rebalanceId = "12121212-0000-0000-0000-000000000001";
const accountId = "aaaaaaaa-0000-0000-0000-000000000001";
const customerId = "cccccccc-0000-0000-0000-000000000001";
const portfolioId = "pppppppp-0000-0000-0000-000000000001";
const acme = { id: "11111111-0000-0000-0000-000000000001", symbol: "ACME.SIM", name: "Acme Simulated Holdings" };
const beta = { id: "11111111-0000-0000-0000-000000000002", symbol: "BETA.SIM", name: "Beta Simulated Group" };
const old = { id: "11111111-0000-0000-0000-000000000003", symbol: "OLD.SIM", name: "Old Holdings" };
const orderId = "99999999-0000-0000-0000-000000000001";
const me = feeUser();

function rebalance(overrides: Record<string, unknown> = {}) {
  return {
    id: rebalanceId,
    accountId,
    customerId,
    portfolio: { id: portfolioId, name: "Demo Growth" },
    trigger: "Account",
    status: "Proposed",
    baseCurrency: "USD",
    totalValue: 10000,
    cash: 9000,
    minimumTrade: 100,
    cashResidualPercent: 1,
    targets: [{ instrument: acme, targetWeight: 60 }, { instrument: beta, targetWeight: 40 }],
    lines: [
      { instrument: old, quantity: 40, weight: 10, targetWeight: null, side: "Sell", tradeQuantity: 40, referencePrice: 25, referencePriceDate: "2026-11-23", estimatedGross: 1000, estimatedFee: 5, currency: "USD" },
      { instrument: acme, quantity: 0, weight: 0, targetWeight: 60, side: "Buy", tradeQuantity: 59.4, referencePrice: 100, referencePriceDate: "2026-11-23", estimatedGross: 5940, estimatedFee: 29.7, currency: "USD" },
      { instrument: beta, quantity: 3, weight: 1.5, targetWeight: 40, side: null, tradeQuantity: null, referencePrice: null, referencePriceDate: null, estimatedGross: null, estimatedFee: null, currency: "USD" },
    ],
    orders: [],
    waves: 0,
    failure: null,
    proposedBy: { id: "dddddddd-0000-0000-0000-000000000001", name: "Avery Adviser" },
    proposedAt: "2026-11-23T02:10:00Z",
    expiresAt: new Date(Date.now() + 3600_000).toISOString(),
    confirmedBy: null,
    confirmedAt: null,
    cancelRequest: null,
    cancelReason: null,
    followUp: false,
    endedAt: null,
    rowVersion: "AAAB",
    ...overrides,
  };
}

const page = (items: unknown[]) => json({ items, page: 1, pageSize: 20, totalCount: items.length });

async function body(request: Request) {
  return JSON.parse(await request.clone().text());
}

describe("Rebalances navigation (rebalances P19)", () => {
  beforeEach(() => stubBrowser("/dashboard"));

  it("shows Rebalances to a TenantAdmin and an Adviser", async () => {
    for (const user of [me, adviser()]) {
      cleanup();
      renderFees(user, [{ path: "/dashboard", element: <ShellLayout /> }], "/dashboard");
      expect(await screen.findByRole("link", { name: /^Rebalances/ })).toHaveAttribute("href", "/rebalances");
    }
  });
});

describe("RebalancesListPage (rebalances P19)", () => {
  const routes = [{ path: "/rebalances", element: <RebalancesListPage /> }];

  beforeEach(() => stubBrowser("/rebalances"));

  it("lists the open rebalances by default, with the columns, the status and what started them", async () => {
    const running = rebalance({ id: "12121212-0000-0000-0000-000000000002", status: "Buying", trigger: "Portfolio", cancelRequest: { by: null, at: "2026-11-23T03:00:00Z", note: null } });
    const { requests } = renderFees(me, routes, "/rebalances", (request) =>
      new URL(request.url).pathname === "/api/rebalances" ? page([rebalance(), running]) : undefined);

    expect(await screen.findByText("Waiting for confirmation")).toBeInTheDocument();
    expect(new URL(requestsTo(requests, "GET", "/rebalances")[0].url).searchParams.get("open")).toBe("true");
    const table = screen.getByRole("table", { name: "Rebalances" });
    expect(within(table).getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual([
      "Proposed", "Customer", "Account", "Portfolio", "Status", "Started by",
    ]);
    expect(within(table).getByText("Request")).toBeInTheDocument();
    expect(within(table).getByText("Portfolio change")).toBeInTheDocument();
    expect(within(table).getByText("Cancelling")).toBeInTheDocument();
    expect(within(table).getAllByText("Demo Growth")).toHaveLength(2);
    expect(within(table).getAllByRole("link")[0]).toHaveAttribute("href", `/rebalances/${rebalanceId}`);
  });

  it("filters by status", async () => {
    const { requests } = renderFees(me, routes, "/rebalances", (request) =>
      new URL(request.url).pathname === "/api/rebalances" ? page([]) : undefined);

    expect(await screen.findByText("No rebalances.")).toBeInTheDocument();
    await chooseOption("Status", "Ended early");
    await waitFor(() => {
      const last = requestsTo(requests, "GET", "/rebalances").at(-1)!;
      expect(new URL(last.url).searchParams.get("status")).toBe("Incomplete");
    });
    await chooseOption("Status", "Waiting for confirmation");
    await waitFor(() => {
      const last = requestsTo(requests, "GET", "/rebalances").at(-1)!;
      expect(new URL(last.url).searchParams.get("status")).toBe("Proposed");
    });
  });
});

describe("RebalanceDetailPage (rebalances P15–P18)", () => {
  const routes = [
    { path: "/rebalances/:id", element: <RebalanceDetailPage /> },
  ];

  beforeEach(() => stubBrowser(`/rebalances/${rebalanceId}`));

  function serve(item: Record<string, unknown>, extra?: (request: Request) => Response | undefined) {
    return (request: Request) => {
      const path = new URL(request.url).pathname;
      if (path === `/api/rebalances/${rebalanceId}` && request.method === "GET") {
        return json(item);
      }
      if (path === `/api/accounts/${accountId}` && request.method === "GET") {
        return json({ id: accountId, name: "Growth account" });
      }
      return extra?.(request);
    };
  }

  it("shows a proposal with its lines, the sentence, and Confirm and Cancel", async () => {
    renderFees(me, routes, `/rebalances/${rebalanceId}`, serve(rebalance()));

    const table = await screen.findByRole("table", { name: "Proposal" });
    expect(within(table).getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual([
      "Instrument", "Holding", "Now %", "Target %", "Trade", "About", "Fee",
    ]);
    const rows = within(table).getAllByRole("row").slice(1).map((row) => within(row).getAllByRole("cell").map((cell) => cell.textContent));
    expect(rows).toEqual([
      ["OLD.SIM", "40", "10.00", "—", "Sell 40", "1,000.00 USD", "5.00 USD"],
      ["ACME.SIM", "0", "0.00", "60.00", "Buy 59.4", "5,940.00 USD", "29.70 USD"],
      ["BETA.SIM", "3", "1.50", "40.00", "—", "—", "—"],
    ]);
    expect(screen.getByText(/Sales are placed first\. Purchases are placed when the sales are filled/)).toBeInTheDocument();
    expect(screen.getByText(/by Avery Adviser/)).toBeInTheDocument();
    expect(screen.getByText("Waiting for confirmation")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Confirm" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Cancel" })).toBeInTheDocument();
  });

  it("says a proposal by the job was proposed by the platform", async () => {
    renderFees(me, routes, `/rebalances/${rebalanceId}`, serve(rebalance({ proposedBy: null, trigger: "Portfolio" })));

    expect(await screen.findByText(/by the platform/)).toBeInTheDocument();
    expect(screen.getByText("Portfolio change")).toBeInTheDocument();
  });

  it("confirms with the row version", async () => {
    const { requests } = renderFees(me, routes, `/rebalances/${rebalanceId}`, serve(rebalance(), (request) =>
      request.method === "POST" ? new Response(null, { status: 204 }) : undefined));

    fireEvent.click(await screen.findByRole("button", { name: "Confirm" }));

    await waitFor(() => expect(requestsTo(requests, "POST", `/rebalances/${rebalanceId}/confirm`)).toHaveLength(1));
    expect(await body(requestsTo(requests, "POST", `/rebalances/${rebalanceId}/confirm`)[0])).toEqual({ rowVersion: "AAAB" });
  });

  it("shows the server message of a 400 and says when the rebalance has changed", async () => {
    let answer = problem(400, { title: "Rebalance failed", detail: "The latest price of BETA.SIM is from 2026-11-18. An order needs a price from the last 4 days.", code: "stale_price" });
    renderFees(me, routes, `/rebalances/${rebalanceId}`, serve(rebalance(), (request) => (request.method === "POST" ? answer : undefined)));

    fireEvent.click(await screen.findByRole("button", { name: "Confirm" }));
    expect(await screen.findByText(/The latest price of BETA\.SIM is from 2026-11-18/)).toBeInTheDocument();

    answer = problem(409, { title: "Conflict", code: "stale_row_version" });
    fireEvent.click(screen.getByRole("button", { name: "Confirm" }));
    expect(await screen.findByText("This rebalance has changed.")).toBeInTheDocument();
  });

  it("offers Propose again in place of Confirm on an expired proposal", async () => {
    const newer = "12121212-0000-0000-0000-000000000009";
    const { requests, router } = renderFees(me, [
      ...routes,
    ], `/rebalances/${rebalanceId}`, serve(rebalance({ expiresAt: "2026-11-01T00:00:00Z" }), (request) =>
      request.method === "POST" && new URL(request.url).pathname === `/api/accounts/${accountId}/rebalance`
        ? json({ status: "Proposed", rebalanceId: newer })
        : undefined));

    expect(await screen.findByText("This proposal has expired.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Confirm" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Propose again" }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/rebalances/${newer}`));
    expect(requestsTo(requests, "POST", `/accounts/${accountId}/rebalance`)).toHaveLength(1);
  });

  it("shows the orders by wave, each linked to its order, after confirm", async () => {
    renderFees(me, routes, `/rebalances/${rebalanceId}`, serve(rebalance({
      status: "Buying",
      waves: 2,
      confirmedAt: "2026-11-23T02:15:00Z",
      confirmedBy: { id: "dddddddd-0000-0000-0000-000000000001", name: "Avery Adviser" },
      orders: [
        { id: orderId, wave: 1, side: "Sell", instrument: old, quantity: 40, filledQuantity: 40, status: "Filled" },
        { id: "99999999-0000-0000-0000-000000000002", wave: 2, side: "Buy", instrument: acme, quantity: 56.86, filledQuantity: 0, status: "Working" },
      ],
    })));

    const table = await screen.findByRole("table", { name: "Rebalance orders" });
    expect(within(table).getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual([
      "Wave", "Side", "Instrument", "Quantity", "Filled", "Status",
    ]);
    expect(within(table).getByRole("link", { name: "1" })).toHaveAttribute("href", `/orders/${orderId}`);
    expect(within(table).getByText("At the broker")).toBeInTheDocument();
    expect(screen.queryByRole("table", { name: "Proposal" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Confirm" })).not.toBeInTheDocument();
    expect(screen.getByText("Buying")).toBeInTheDocument();
  });

  it.each([
    [{ status: "Completed", endedAt: "2026-11-23T05:00:00Z" }, "Completed"],
    [{ status: "Incomplete", failure: { code: "order_not_filled", message: "1 of 2 orders were not filled in full." }, endedAt: "2026-11-23T05:00:00Z" }, "Ended early"],
    [{ status: "Cancelled", cancelReason: "Superseded", endedAt: "2026-11-23T05:00:00Z" }, "Cancelled: replaced by a newer proposal"],
    [{ status: "Cancelled", cancelReason: "Unassigned", endedAt: "2026-11-23T05:00:00Z" }, "Cancelled: the portfolio was unassigned"],
    [{ status: "NoChange", lines: [], endedAt: "2026-11-23T05:00:00Z" }, "Nothing to trade"],
    [{ status: "Failed", lines: [], failure: { code: "missing_price", message: "Price is not available for BETA.SIM." }, endedAt: "2026-11-23T05:00:00Z" }, "Could not be planned"],
  ])("labels a final rebalance (P17): %o", async (overrides, label) => {
    renderFees(me, routes, `/rebalances/${rebalanceId}`, serve(rebalance(overrides)));

    expect(await screen.findByText(label)).toBeInTheDocument();
    if ("failure" in overrides && overrides.failure) {
      expect(screen.getByText(overrides.failure.message)).toBeInTheDocument();
    }
    expect(screen.queryByRole("button", { name: "Cancel" })).not.toBeInTheDocument();
  });

  it("shows Cancelling and the follow-up line on a rebalance in progress", async () => {
    renderFees(me, routes, `/rebalances/${rebalanceId}`, serve(rebalance({
      status: "Selling",
      followUp: true,
      cancelRequest: { by: { id: "dddddddd-0000-0000-0000-000000000001", name: "Avery Adviser" }, at: "2026-11-23T03:00:00Z", note: "Withdrawing" },
      orders: [],
    })));

    expect(await screen.findByText("Cancelling")).toBeInTheDocument();
    expect(screen.getByText("A new proposal follows when this one ends.")).toBeInTheDocument();
    expect(screen.getByText("Withdrawing")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Cancel" })).not.toBeInTheDocument();
  });

  it("cancels a rebalance in progress with a note, saying what was filled stays", async () => {
    const { requests } = renderFees(me, routes, `/rebalances/${rebalanceId}`, serve(rebalance({ status: "Buying", orders: [] }), (request) =>
      request.method === "POST" ? new Response(null, { status: 204 }) : undefined));

    fireEvent.click(await screen.findByRole("button", { name: "Cancel" }));
    expect(await screen.findByText("Orders the broker has already filled stay.")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Note"), { target: { value: " Customer is withdrawing next week " } });
    fireEvent.click(screen.getByRole("button", { name: "Cancel rebalance" }));

    await waitFor(() => expect(requestsTo(requests, "POST", `/rebalances/${rebalanceId}/cancel`)).toHaveLength(1));
    expect(await body(requestsTo(requests, "POST", `/rebalances/${rebalanceId}/cancel`)[0]))
      .toEqual({ rowVersion: "AAAB", note: "Customer is withdrawing next week" });
  });

  it("does not say that filled orders stay when cancelling a proposal", async () => {
    renderFees(me, routes, `/rebalances/${rebalanceId}`, serve(rebalance()));

    fireEvent.click(await screen.findByRole("button", { name: "Cancel" }));
    expect(await screen.findByRole("button", { name: "Cancel rebalance" })).toBeInTheDocument();
    expect(screen.queryByText("Orders the broker has already filled stay.")).not.toBeInTheDocument();
  });
});

describe("Rebalance orders on the Orders pages (rebalances D79)", () => {
  const order = {
    id: orderId,
    accountId,
    customerId,
    instrument: acme,
    side: "Buy",
    type: "Market",
    timeInForce: "Day",
    quantity: 56.86,
    filledQuantity: 0,
    openQuantity: 56.86,
    currency: "USD",
    referencePrice: 100,
    referencePriceDate: "2026-11-23",
    priceLimit: 105,
    reservedCash: 6000,
    reservedQuantity: 0,
    averagePrice: null,
    status: "Working",
    memo: "Rebalance",
    placedBy: { id: "dddddddd-0000-0000-0000-000000000001", name: "Avery Adviser" },
    placedAt: "2026-11-23T02:15:00Z",
    send: { attempts: 1, lastAttemptAt: "2026-11-23T02:16:00Z", lastFailure: null, acceptedAt: "2026-11-23T02:16:00Z" },
    rejectReason: null,
    cancelRequest: null,
    closedAt: null,
    closeReason: null,
    rebalanceId,
    fills: [],
    rowVersion: "AAAC",
  };

  it("labels a rebalance order on the list and offers no Cancel on it", async () => {
    stubBrowser("/orders");
    renderFees(me, [{ path: "/orders", element: <OrdersListPage /> }], "/orders", (request) =>
      new URL(request.url).pathname === "/api/orders" ? page([order]) : undefined);

    expect(await screen.findByRole("link", { name: "Rebalance" })).toHaveAttribute("href", `/rebalances/${rebalanceId}`);
    expect(screen.queryByRole("button", { name: "Cancel" })).not.toBeInTheDocument();
  });

  it("says on the order page that the rebalance is cancelled to cancel it", async () => {
    stubBrowser(`/orders/${orderId}`);
    renderFees(me, [{ path: "/orders/:id", element: <OrderDetailPage /> }], `/orders/${orderId}`, (request) =>
      new URL(request.url).pathname === `/api/orders/${orderId}` ? json(order) : undefined);

    expect(await screen.findByText("This order belongs to a rebalance. Cancel the rebalance to cancel it.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Rebalance" })).toHaveAttribute("href", `/rebalances/${rebalanceId}`);
    expect(screen.queryByRole("button", { name: "Cancel order" })).not.toBeInTheDocument();
  });
});

describe("Confirm proposals on the portfolio page (rebalances P20)", () => {
  beforeEach(() => stubBrowser(`/portfolios/${portfolioId}`));

  it("confirms each proposal of the portfolio in turn and shows how many were confirmed and each refusal", async () => {
    vi.stubGlobal("confirm", vi.fn(() => true));
    const second = rebalance({ id: "12121212-0000-0000-0000-000000000002", rowVersion: "AAAD" });
    const { requests } = renderFees(me, [{ path: "/portfolios/:id", element: <PortfolioDetailPage /> }], `/portfolios/${portfolioId}`, (request) => {
      const url = new URL(request.url);
      if (url.pathname === `/api/portfolios/${portfolioId}`) {
        return json({ id: portfolioId, tenantId: "t", name: "Demo Growth", baseCurrency: "USD", isActive: true, driftAbsolute: 1, driftRelative: 5, minimumTrade: 100, cashResidualPercent: 1, rowVersion: "AAAA", lines: [{ instrumentId: acme.id, targetWeight: 100, sortOrder: 0 }] });
      }
      if (url.pathname === "/api/instruments") {
        return json({ items: [{ ...acme, quoteCurrency: "USD", isActive: true }], page: 1, pageSize: 100, totalCount: 1 });
      }
      if (url.pathname === "/api/accounts") {
        return page([]);
      }
      if (url.pathname === "/api/rebalances") {
        return page([rebalance(), second]);
      }
      if (url.pathname === `/api/rebalances/${rebalanceId}/confirm`) {
        return new Response(null, { status: 204 });
      }
      if (url.pathname === `/api/rebalances/${second.id}/confirm`) {
        return problem(400, { title: "Rebalance failed", detail: "The firm has no broker connection, so a rebalance cannot place orders.", code: "no_broker_connection" });
      }
      return undefined;
    });

    fireEvent.click(await screen.findByRole("button", { name: "Confirm proposals" }));

    expect(await screen.findByText("1 of 2 proposals confirmed.")).toBeInTheDocument();
    expect(screen.getByText("The firm has no broker connection, so a rebalance cannot place orders.")).toBeInTheDocument();
    expect(window.confirm).toHaveBeenCalledWith("Confirm 2 proposals?");
    const listed = new URL(requestsTo(requests, "GET", "/rebalances")[0].url).searchParams;
    expect(listed.get("portfolioId")).toBe(portfolioId);
    expect(listed.get("status")).toBe("Proposed");
    expect(await body(requestsTo(requests, "POST", `/rebalances/${second.id}/confirm`)[0])).toEqual({ rowVersion: "AAAD" });
  });
});
