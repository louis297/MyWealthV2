import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { CloseAccountPage } from "@/features/accountClosures/CloseAccountPage";
import { AccountDetailPage } from "@/features/accounts/AccountDetailPage";
import { OrderDetailPage } from "@/features/orders/OrderDetailPage";
import { OrdersListPage } from "@/features/orders/OrdersListPage";
import { PlaceOrderPage } from "@/features/orders/PlaceOrderPage";
import { CreateFxConversionPage } from "@/features/transactions/CreateFxConversionPage";
import { CreateInternalTransferPage } from "@/features/transactions/CreateInternalTransferPage";
import { TransactionDetailPage } from "@/features/transactions/TransactionDetailPage";
import { WithdrawPage } from "@/features/withdrawals/WithdrawPage";
import { ShellLayout } from "@/layouts/ShellLayout";
import { chooseOption, openSelect } from "@/test/chooseOption";
import { adviser, feeUser, json, problem, renderFees, requestsTo, stubBrowser } from "@/test/fees";

const accountId = "aaaaaaaa-0000-0000-0000-000000000001";
const customerId = "cccccccc-0000-0000-0000-000000000001";
const orderId = "99999999-0000-0000-0000-000000000001";
const acmeId = "11111111-0000-0000-0000-000000000001";
const kiwiId = "11111111-0000-0000-0000-000000000002";
const transactionId = "88888888-0000-0000-0000-000000000001";
const me = feeUser();

const account = {
  id: accountId,
  tenantId: "22222222-2222-2222-2222-222222222222",
  customerId,
  name: "Managed portfolio",
  type: "Brokerage",
  custodyMode: "Custodial",
  currency: "USD",
  depositReference: "K7M3Q9PXB",
  cashBalance: 6200,
  reservedCash: 1055.25,
  availableCash: 5144.75,
  unsettledCash: 2000,
  withdrawableCash: 3144.75,
  status: "Open",
  isActive: true,
  rowVersion: "AAAA",
};

const holding = {
  id: "hhhhhhhh-0000-0000-0000-000000000001",
  accountId,
  instrumentId: acmeId,
  symbol: "ACME.SIM",
  name: "Acme Holdings",
  quantity: 100,
  cost: 9000,
  costCurrency: "USD",
  reservedQuantity: 40,
  availableQuantity: 60,
};

const instruments = [
  { id: acmeId, tenantId: account.tenantId, symbol: "ACME.SIM", name: "Acme Holdings", quoteCurrency: "USD", isActive: true, rowVersion: "AAAA" },
  { id: kiwiId, tenantId: account.tenantId, symbol: "KIWI.SIM", name: "Kiwi Fund", quoteCurrency: "NZD", isActive: true, rowVersion: "AAAA" },
];

function order(overrides: Record<string, unknown> = {}) {
  return {
    id: orderId,
    accountId,
    customerId,
    instrument: { id: acmeId, symbol: "ACME.SIM", name: "Acme Holdings" },
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
    memo: "Monthly top-up",
    placedBy: { id: "eeeeeeee-0000-0000-0000-000000000001", name: "Ann Adviser" },
    placedAt: "2026-11-23T02:10:00Z",
    send: { attempts: 1, lastAttemptAt: "2026-11-23T02:11:00Z", lastFailure: null, acceptedAt: "2026-11-23T02:11:00Z" },
    rejectReason: null,
    cancelRequest: null,
    closedAt: null,
    closeReason: null,
    rowVersion: "AAAB",
    ...overrides,
  };
}

const fill = {
  id: "ffffffff-0000-0000-0000-000000000001",
  quantity: 4,
  price: 100.12,
  tradeDate: "2026-11-23",
  settlementDate: "2026-11-25",
  brokerReference: "CN-1",
  status: "Posted",
  holdReason: null,
  transactionId,
  postedAt: "2026-11-23T02:13:00Z",
  settledAt: null,
};

const preview = {
  currency: "USD",
  referencePrice: 100,
  referencePriceDate: "2026-11-23",
  priceLimit: 105,
  estimatedGross: 1000,
  estimatedFee: 5,
  maximumGross: 1050,
  maximumFee: 5.25,
  maximumCost: 1055.25,
  availableCash: 5000,
  availableQuantity: null,
};

const page = (items: unknown[]) => json({ items, page: 1, pageSize: 20, totalCount: items.length });

async function body(request: Request) {
  return JSON.parse(await request.clone().text());
}

describe("Orders navigation (orders D71)", () => {
  beforeEach(() => stubBrowser("/dashboard"));

  it.each([["a TenantAdmin", feeUser()], ["an Adviser", adviser()]])("shows Orders to %s", async (_, user) => {
    renderFees(user, [{ path: "/dashboard", element: <ShellLayout /> }], "/dashboard");
    expect(await screen.findByRole("link", { name: /^Orders/ })).toHaveAttribute("href", "/orders");
  });
});

describe("OrdersListPage (orders D71, D73)", () => {
  const routes = [{ path: "/orders", element: <OrdersListPage /> }];

  beforeEach(() => stubBrowser("/orders"));

  function serve(items: unknown[], waiting: unknown[] = [], cancel?: () => Response) {
    return (request: Request) => {
      const url = new URL(request.url);
      if (request.method === "POST") {
        return cancel?.() ?? new Response(null, { status: 204 });
      }
      if (url.pathname === "/api/orders") {
        return page(url.searchParams.get("pendingFill") === "true" ? waiting : items);
      }
      if (url.pathname === `/api/accounts/${accountId}`) {
        return json(account);
      }
      return undefined;
    };
  }

  it("lists the open orders by default, with the labels, Cancelling and Needs attention", async () => {
    const partly = order({ id: "99999999-0000-0000-0000-000000000002", status: "PartlyFilled", filledQuantity: 4, side: "Sell" });
    const cancelling = order({
      id: "99999999-0000-0000-0000-000000000003",
      status: "Placed",
      cancelRequest: { by: null, at: "2026-11-23T03:00:00Z", note: null },
    });
    const { requests } = renderFees(me, routes, "/orders", serve([order(), partly, cancelling], [partly]));

    expect(await screen.findByText("At the broker")).toBeInTheDocument();
    const list = new URL(requestsTo(requests, "GET", "/orders")[0].url);
    expect(list.searchParams.get("status")).toBe("Placed,Working,PartlyFilled");
    expect(list.searchParams.get("pendingFill")).toBeNull();
    const table = screen.getByRole("table", { name: "Orders" });
    expect(within(table).getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual([
      "Placed", "Customer", "Account", "Side", "Instrument", "Quantity", "Filled", "Status", "Placed by", "Actions",
    ]);
    const rows = within(table).getAllByRole("row").slice(1);
    expect(within(rows[0]).getByRole("link")).toHaveAttribute("href", `/orders/${orderId}`);
    expect(within(rows[0]).getByText("Ann Adviser")).toBeInTheDocument();
    expect(await within(rows[0]).findByText("Managed portfolio")).toBeInTheDocument();
    expect(within(rows[1]).getByText("Partly filled")).toBeInTheDocument();
    expect(await within(rows[1]).findByText("Needs attention")).toBeInTheDocument();
    expect(within(rows[0]).queryByText("Needs attention")).not.toBeInTheDocument();
    expect(within(rows[2]).getByText("Sending to the broker")).toBeInTheDocument();
    expect(within(rows[2]).getByText("Cancelling")).toBeInTheDocument();
    // D73: Cancel is offered on an open order with no cancel request.
    expect(within(rows[0]).getByRole("button", { name: "Cancel" })).toBeInTheDocument();
    expect(within(rows[2]).queryByRole("button", { name: "Cancel" })).not.toBeInTheDocument();
  });

  it.each([
    ["Needs attention", null, "true"],
    ["All", null, null],
    ["Rejected", "Rejected", null],
  ])("filters by %s", async (label, status, pendingFill) => {
    const rejected = order({ status: "Rejected" });
    const { requests } = renderFees(me, routes, "/orders", serve([rejected], [rejected]));

    await chooseOption("Status", label);

    await waitFor(() => {
      const last = new URL(requestsTo(requests, "GET", "/orders").filter(
        (request) => (new URL(request.url).searchParams.get("pendingFill") === "true") === (pendingFill === "true"),
      ).at(-1)!.url);
      expect(last.searchParams.get("status")).toBe(status);
      expect(last.searchParams.get("pendingFill")).toBe(pendingFill);
    });
    expect(await screen.findByText("Refused by the broker")).toBeInTheDocument();
  });

  it("offers every filter of D71", async () => {
    renderFees(me, routes, "/orders", serve([]));

    await openSelect("Status");
    expect(screen.getAllByRole("option").map((option) => option.textContent?.trim()))
      .toEqual(["Open", "Filled", "Cancelled", "Rejected", "Expired", "Needs attention", "All"]);
  });

  it("cancels from the list with the order's rowVersion", async () => {
    const { requests } = renderFees(me, routes, "/orders", serve([order()]));

    fireEvent.click(await screen.findByRole("button", { name: "Cancel" }));
    expect(await screen.findByText("If the broker has already filled part of this order, that part stays.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Cancel order" }));

    await waitFor(() => expect(requestsTo(requests, "POST", `/orders/${orderId}/cancel`)).toHaveLength(1));
    expect(await body(requestsTo(requests, "POST", `/orders/${orderId}/cancel`)[0])).toEqual({ note: null, rowVersion: "AAAB" });
  });
});

describe("PlaceOrderPage (orders D70)", () => {
  const routes = [
    { path: "/accounts/:id/order", element: <PlaceOrderPage /> },
    { path: "/orders/:id", element: <p>Order page</p> },
  ];

  beforeEach(() => stubBrowser(`/accounts/${accountId}/order`));

  function serve(previewAnswer: () => Response, post?: () => Response) {
    return (request: Request) => {
      const url = new URL(request.url);
      if (request.method === "POST" && url.pathname === "/api/orders/preview") {
        return previewAnswer();
      }
      if (request.method === "POST") {
        return post?.() ?? json({ id: orderId }, 201);
      }
      if (url.pathname === `/api/accounts/${accountId}/holdings`) {
        return json({ items: [holding] });
      }
      if (url.pathname === `/api/accounts/${accountId}`) {
        return json(account);
      }
      if (url.pathname === "/api/instruments") {
        return page(instruments);
      }
      return undefined;
    };
  }

  it("previews a buy, reviews it with the price limit and posts with one key", async () => {
    const { requests } = renderFees(me, routes, `/accounts/${accountId}/order`, serve(() => json(preview)));

    await screen.findByLabelText("Side");
    expect(screen.queryByLabelText(/Price/)).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Booked at")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Review" })).toBeDisabled();
    await openSelect("Instrument");
    // An instrument quoted in another currency cannot be bought on this account.
    expect(await screen.findByRole("option", { name: "KIWI.SIM Kiwi Fund (Quoted in NZD)" })).toHaveAttribute("aria-disabled", "true");
    fireEvent.keyDown(document.activeElement ?? document.body, { key: "Escape" });
    await chooseOption("Instrument", "ACME.SIM Acme Holdings");
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "10" } });
    fireEvent.change(screen.getByLabelText("Memo"), { target: { value: "Monthly top-up" } });

    const sentence = "About 1,000.00 USD at the last price 100.00 (23 Nov), plus about 5.00 USD in fees. "
      + "At most 1,055.25 USD is reserved until the order is filled. Available 5,000.00 USD.";
    expect(await screen.findByText(sentence)).toBeInTheDocument();
    expect(await body(requestsTo(requests, "POST", "/orders/preview")[0]))
      .toEqual({ accountId, instrumentId: acmeId, side: "Buy", quantity: 10 });
    fireEvent.click(screen.getByRole("button", { name: "Review" }));

    expect(await screen.findByText("The broker fills at the market price, not above 105.00.")).toBeInTheDocument();
    expect(screen.getAllByText(sentence)).toHaveLength(2);
    fireEvent.click(screen.getByRole("button", { name: "Confirm order" }));

    expect(await screen.findByText("Order page")).toBeInTheDocument();
    const post = requestsTo(requests, "POST", "/orders")[0];
    expect(post.headers.get("Idempotency-Key")).toMatch(/^[0-9a-f-]{36}$/i);
    expect(await body(post)).toEqual({ accountId, instrumentId: acmeId, side: "Buy", quantity: 10, memo: "Monthly top-up" });
  });

  it("sells from the account's holdings with their available quantity", async () => {
    const sell = { ...preview, priceLimit: 95, maximumCost: null, availableQuantity: 60 };
    const { requests } = renderFees(me, routes, `/accounts/${accountId}/order`, serve(() => json(sell)));

    await chooseOption("Side", "Sell");
    await chooseOption("Instrument", "ACME.SIM Acme Holdings (60 available)");
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "10" } });

    expect(await screen.findByText("About 1,000.00 USD before fees. 60 available to sell.")).toBeInTheDocument();
    expect((await body(requestsTo(requests, "POST", "/orders/preview")[0])).side).toBe("Sell");
    fireEvent.click(screen.getByRole("button", { name: "Review" }));
    expect(await screen.findByText("The broker fills at the market price, not below 95.00.")).toBeInTheDocument();
  });

  it("shows a preview 400 at its field and blocks Review", async () => {
    const refusal = "This order can cost up to 10,552.50 USD with fees. The account's available cash is 5,000.00 USD.";
    renderFees(me, routes, `/accounts/${accountId}/order`, serve(() => problem(400, { errors: { Quantity: [refusal] } })));

    await chooseOption("Instrument", "ACME.SIM Acme Holdings");
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "100" } });

    expect(await screen.findByText(refusal)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Review" })).toBeDisabled();
    expect(screen.queryByRole("button", { name: "Confirm order" })).not.toBeInTheDocument();
  });

  it("shows the 400 of the order at its field and keeps the form", async () => {
    const refusal = "The latest price of ACME.SIM is from 2026-11-18. An order needs a price from the last 4 days.";
    renderFees(me, routes, `/accounts/${accountId}/order`,
      serve(() => json(preview), () => problem(400, { errors: { InstrumentId: [refusal] } })));

    await chooseOption("Instrument", "ACME.SIM Acme Holdings");
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "10" } });
    await screen.findByText(/^About 1,000.00 USD/);
    fireEvent.click(screen.getByRole("button", { name: "Review" }));
    fireEvent.click(await screen.findByRole("button", { name: "Confirm order" }));

    expect(await screen.findByText(refusal)).toBeInTheDocument();
    expect(screen.queryByText("Order page")).not.toBeInTheDocument();
  });
});

describe("OrderDetailPage (orders D72, D73)", () => {
  const routes = [{ path: "/orders/:id", element: <OrderDetailPage /> }];

  beforeEach(() => stubBrowser(`/orders/${orderId}`));

  function serve(item: () => unknown, cancel?: () => Response) {
    return (request: Request) => {
      const url = new URL(request.url);
      if (request.method === "POST") {
        return cancel?.() ?? new Response(null, { status: 204 });
      }
      if (url.pathname === `/api/orders/${orderId}`) {
        return json(item());
      }
      if (url.pathname === `/api/accounts/${accountId}`) {
        return json(account);
      }
      return undefined;
    };
  }

  it("shows the price limit, what the order reserves, the send attempts and the fills", async () => {
    const settled = { ...fill, id: "ffffffff-0000-0000-0000-000000000002", quantity: 2, settledAt: "2026-11-25T00:10:00Z", brokerReference: "CN-2" };
    const item = order({
      status: "PartlyFilled",
      filledQuantity: 6,
      averagePrice: 100.12,
      send: { attempts: 2, lastAttemptAt: "2026-11-23T02:11:00Z", lastFailure: "Timeout", acceptedAt: null },
      fills: [fill, settled],
    });
    renderFees(me, routes, `/orders/${orderId}`, serve(() => item));

    expect(await screen.findByRole("heading", { name: "Buy 10 ACME.SIM" })).toBeInTheDocument();
    expect(screen.getByText("The broker may buy up to 105.00")).toBeInTheDocument();
    expect(screen.getByText("1,055.25 USD")).toBeInTheDocument();
    expect(screen.getByText("6 of 10")).toBeInTheDocument();
    expect(screen.getByText("2 attempts")).toBeInTheDocument();
    expect(screen.getByText(/^Last failure: Broker did not answer in time/)).toBeInTheDocument();
    expect(screen.getByText("Monthly top-up")).toBeInTheDocument();
    const fills = screen.getByRole("table", { name: "Fills" });
    expect(within(fills).getAllByRole("columnheader").map((cell) => cell.textContent))
      .toEqual(["Trade date", "Quantity", "Price", "Settlement", "Contract note", "Status"]);
    const rows = within(fills).getAllByRole("row").slice(1);
    expect(within(rows[0]).getByText("Settles 25 Nov")).toBeInTheDocument();
    expect(within(rows[0]).getByText("CN-1")).toBeInTheDocument();
    expect(within(rows[0]).getByRole("link", { name: "Booked" })).toHaveAttribute("href", `/transactions/${transactionId}`);
    expect(within(rows[1]).getByText("Settled 25 Nov")).toBeInTheDocument();
    expect(screen.queryByText(/Correct the account with an adjustment/)).not.toBeInTheDocument();
  });

  it("shows a sell's limit and reserved quantity, the reject reason and a cancel request by the platform", async () => {
    const item = order({
      side: "Sell",
      priceLimit: 95,
      reservedCash: 0,
      reservedQuantity: 10,
      status: "Rejected",
      rejectReason: "MarketClosed",
      cancelRequest: { by: null, at: "2026-11-23T03:00:00Z", note: "Corporate action" },
      fills: [],
    });
    renderFees(me, routes, `/orders/${orderId}`, serve(() => item));

    expect(await screen.findByText("The broker may sell down to 95.00")).toBeInTheDocument();
    expect(screen.getByText("10 ACME.SIM")).toBeInTheDocument();
    expect(screen.getByText("The market is closed")).toBeInTheDocument();
    expect(screen.getByText(/by the platform$/)).toBeInTheDocument();
    expect(screen.getByText("Corporate action")).toBeInTheDocument();
    expect(screen.getByText("No fills.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Cancel order" })).not.toBeInTheDocument();
  });

  it.each([
    ["a TenantAdmin", feeUser(), true],
    ["an Adviser", adviser(), false],
  ])("shows a waiting fill with its reason, and how to correct it to %s only when a firm administrator", async (_, user, hint) => {
    const waiting = { ...fill, status: "Pending", holdReason: "InsufficientCash", transactionId: null, postedAt: null };
    renderFees(user, routes, `/orders/${orderId}`, serve(() => order({ fills: [waiting] })));

    expect(await screen.findByText("Waiting: Not enough cash")).toBeInTheDocument();
    expect(screen.getByText("Needs attention")).toBeInTheDocument();
    const sentence = "Correct the account with an adjustment. The platform books the fill on its next run.";
    expect(screen.queryAllByText(sentence)).toHaveLength(hint ? 1 : 0);
  });

  it("cancels with a note, shows the sentence of a 400 and says so when the order has changed", async () => {
    let answer = problem(400, { errors: { Status: ["A cancel was already requested for this order."] } });
    const { requests } = renderFees(me, routes, `/orders/${orderId}`, serve(() => order({ fills: [] }), () => answer.clone()));

    fireEvent.click(await screen.findByRole("button", { name: "Cancel order" }));
    expect(await screen.findByText("If the broker has already filled part of this order, that part stays.")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Note"), { target: { value: "Wrong quantity" } });
    const confirm = within(screen.getByRole("dialog")).getByRole("button", { name: "Cancel order" });
    fireEvent.click(confirm);

    expect(await screen.findByText("A cancel was already requested for this order.")).toBeInTheDocument();
    expect(await body(requestsTo(requests, "POST", `/orders/${orderId}/cancel`)[0]))
      .toEqual({ note: "Wrong quantity", rowVersion: "AAAB" });

    answer = problem(409, { code: "stale_row_version" });
    fireEvent.click(confirm);
    expect(await screen.findByText("This order has changed.")).toBeInTheDocument();
  });
});

describe("Account page with orders (orders D69, D74)", () => {
  const routes = [{ path: "/accounts/:id", element: <AccountDetailPage /> }];

  beforeEach(() => stubBrowser(`/accounts/${accountId}`));

  const trade = {
    id: transactionId,
    tenantId: account.tenantId,
    type: "Sell",
    source: { kind: "OrderFill", id: orderId },
    cash: [{ accountId, customerId, amount: 2000, currency: "USD", role: "Principal", fee: null }],
    bookedAt: "2026-11-23T02:13:00Z",
    rowVersion: "AAAA",
    security: [],
    execution: {
      orderId, fillId: fill.id, price: 100.12, tradeDate: "2026-11-23", settlementDate: "2026-11-25", brokerReference: "CN-1", settledAt: null,
    },
  };

  function serve(request: Request) {
    const url = new URL(request.url);
    if (url.pathname === `/api/accounts/${accountId}/holdings`) {
      return json({ items: [holding] });
    }
    if (url.pathname === `/api/accounts/${accountId}`) {
      return json(account);
    }
    if (url.pathname === "/api/orders") {
      return page(url.searchParams.get("pendingFill") === "true" ? [] : [order()]);
    }
    if (url.pathname === "/api/transactions") {
      return page([trade]);
    }
    return undefined;
  }

  it("shows the cash figures, the reserved quantity, the open orders and Place order", async () => {
    const { requests } = renderFees(me, routes, `/accounts/${accountId}`, serve);

    expect(await screen.findByRole("link", { name: "Place order" })).toHaveAttribute("href", `/accounts/${accountId}/order`);
    expect(screen.queryByRole("link", { name: "Buy" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Sell" })).not.toBeInTheDocument();
    for (const [label, value] of [
      ["Reserved", "1,055.25 USD"], ["Available", "5,144.75 USD"], ["Not settled", "2,000.00 USD"], ["Available to withdraw", "3,144.75 USD"],
    ]) {
      expect(screen.getByText(label)).toBeInTheDocument();
      expect(screen.getByText(value)).toBeInTheDocument();
    }
    expect(within(screen.getByRole("table", { name: "Holdings" })).getByText("40 reserved")).toBeInTheDocument();
    const orders = await screen.findByRole("table", { name: "Orders" });
    expect(within(orders).getByText("At the broker")).toBeInTheDocument();
    expect(within(orders).queryByText("Customer")).not.toBeInTheDocument();
    const list = new URL(requestsTo(requests, "GET", "/orders")[0].url);
    expect(list.searchParams.get("accountId")).toBe(accountId);
    // D74: a fill that has not settled, and no hand reverse of it.
    const activity = screen.getByRole("table", { name: "Activity" });
    expect(await within(activity).findByText("Settles 25 Nov")).toBeInTheDocument();
    expect(within(activity).queryByRole("button", { name: /Reverse/ })).not.toBeInTheDocument();
  });
});

describe("Transaction page of a fill (orders D74)", () => {
  beforeEach(() => stubBrowser(`/transactions/${transactionId}`));

  it.each([
    [null, "2026-11-25 (Not settled yet)"],
    ["2026-11-25T00:10:00Z", "2026-11-25 (Settled 25 Nov)"],
  ])("shows the Trade section, the source Order and no Reverse (settled at %s)", async (settledAt, settlement) => {
    renderFees(me, [{ path: "/transactions/:id", element: <TransactionDetailPage /> }], `/transactions/${transactionId}`, (request) =>
      new URL(request.url).pathname === `/api/transactions/${transactionId}`
        ? json({
          id: transactionId,
          tenantId: account.tenantId,
          type: "Buy",
          source: { kind: "OrderFill", id: orderId },
          cash: [],
          bookedAt: "2026-11-23T02:13:00Z",
          rowVersion: "AAAA",
          security: [],
          execution: {
            orderId, fillId: fill.id, price: 100.12, tradeDate: "2026-11-23", settlementDate: "2026-11-25", brokerReference: "CN-1", settledAt,
          },
        })
        : undefined);

    expect(await screen.findByText("Source: Order")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Trade" })).toBeInTheDocument();
    expect(screen.getByText("100.12")).toBeInTheDocument();
    expect(screen.getByText("2026-11-23")).toBeInTheDocument();
    expect(screen.getByText(settlement)).toBeInTheDocument();
    expect(screen.getByText("CN-1")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "View the order" })).toHaveAttribute("href", `/orders/${orderId}`);
    expect(screen.queryByRole("button", { name: /Reverse/ })).not.toBeInTheDocument();
  });
});

describe("Withdrawable cash (orders D75)", () => {
  beforeEach(() => stubBrowser(`/accounts/${accountId}/withdraw`));

  it("shows what can be withdrawn and the part that settles later on the Withdraw form", async () => {
    renderFees(me, [{ path: "/accounts/:id/withdraw", element: <WithdrawPage /> }], `/accounts/${accountId}/withdraw`, (request) => {
      const url = new URL(request.url);
      if (url.pathname === `/api/accounts/${accountId}`) {
        return json(account);
      }
      return url.pathname === "/api/nominated-bank-accounts" ? page([{ id: "p", bankName: "ANZ", accountName: "J Smith", accountNumber: "06-0123-0456789-00" }]) : undefined;
    });

    expect(await screen.findByText("Available to withdraw 3,144.75 USD")).toBeInTheDocument();
    expect(screen.getByText("2,000.00 USD from sales settles later.")).toBeInTheDocument();
  });

  it.each([
    ["transfer", <CreateInternalTransferPage key="transfer" />],
    ["convert", <CreateFxConversionPage key="convert" />],
  ])("shows the same figure on the %s form of a Custodial from account", async (path, element) => {
    renderFees(me, [{ path: `/accounts/:id/${path}`, element }], `/accounts/${accountId}/${path}`, (request) => {
      const url = new URL(request.url);
      if (url.pathname === `/api/accounts/${accountId}`) {
        return json(account);
      }
      return url.pathname === "/api/accounts" ? page([account]) : undefined;
    });

    expect(await screen.findByText("Available to withdraw 3,144.75 USD")).toBeInTheDocument();
    expect(screen.getByText("2,000.00 USD from sales settles later.")).toBeInTheDocument();
  });

  it("shows the two refusals of orders on the Close account page", async () => {
    renderFees(me, [{ path: "/accounts/:id/close", element: <CloseAccountPage /> }], `/accounts/${accountId}/close`, (request) => {
      const url = new URL(request.url);
      if (url.pathname === `/api/accounts/${accountId}/holdings`) {
        return json({ items: [] });
      }
      if (url.pathname === `/api/accounts/${accountId}`) {
        return json(account);
      }
      if (url.pathname === "/api/orders") {
        return page([order()]);
      }
      return url.pathname === "/api/withdrawals" || url.pathname === "/api/nominated-bank-accounts" ? page([]) : undefined;
    });

    expect(await screen.findByText("Open orders must be filled, cancelled or expired before the account can be closed.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "View the order" })).toHaveAttribute("href", `/orders/${orderId}`);
    expect(screen.getByText("Trades must settle before the account can be closed.")).toBeInTheDocument();
  });
});
