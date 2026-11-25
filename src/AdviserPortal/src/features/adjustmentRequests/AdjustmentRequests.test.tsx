import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { AccountDetailPage } from "@/features/accounts/AccountDetailPage";
import { CloseAccountPage } from "@/features/accountClosures/CloseAccountPage";
import { AdjustmentDetailPage } from "@/features/adjustmentRequests/AdjustmentDetailPage";
import { AdjustmentsListPage } from "@/features/adjustmentRequests/AdjustmentsListPage";
import { TransactionDetailPage } from "@/features/transactions/TransactionDetailPage";
import { ShellLayout } from "@/layouts/ShellLayout";
import { chooseOption } from "@/test/chooseOption";
import { adviser, feeUser, json, problem, renderFees, requestsTo, stubBrowser } from "@/test/fees";

const accountId = "aaaaaaaa-0000-0000-0000-000000000001";
const customerId = "cccccccc-0000-0000-0000-000000000001";
const requestId = "99999999-0000-0000-0000-000000000001";
const aaplId = "dddddddd-0000-0000-0000-000000000001";
const transactionId = "77777777-0000-0000-0000-000000000001";
const me = feeUser();

const account = {
  id: accountId,
  tenantId: "22222222-2222-2222-2222-222222222222",
  customerId,
  name: "Managed portfolio",
  type: "Brokerage",
  custodyMode: "Custodial",
  currency: "USD",
  cashBalance: 1000,
  reservedCash: 0,
  availableCash: 1000,
  status: "Open",
  isActive: true,
  rowVersion: "AAAA",
};

function adjustment(overrides: Record<string, unknown> = {}) {
  return {
    id: requestId,
    accountId,
    customerId,
    reason: "EntryError",
    memo: "Buy was filled at 25.40, not 25.10",
    reference: null,
    amount: -0.6,
    currency: "USD",
    security: {
      instrument: { id: aaplId, symbol: "AAPL", name: "Apple" },
      quantityChange: 10,
      costChange: 250,
      costCurrency: "USD",
    },
    status: "Requested",
    requestedBy: { id: "eeeeeeee-0000-0000-0000-000000000001", name: "Tessa Admin" },
    requestedAt: "2026-11-25T02:00:00Z",
    decision: null,
    transactionId: null,
    rowVersion: "AAAA",
    ...overrides,
  };
}

const page = (items: unknown[], totalCount = items.length) => json({ items, page: 1, pageSize: 20, totalCount });

async function body(request: Request) {
  return JSON.parse(await request.clone().text());
}

describe("Adjustments navigation (adjustment-review D103)", () => {
  beforeEach(() => stubBrowser("/dashboard"));

  function serve(request: Request) {
    return new URL(request.url).pathname === "/api/adjustment-requests" ? page([], 4) : undefined;
  }

  it("shows a TenantAdmin Adjustments with the count waiting for approval", async () => {
    const { requests } = renderFees(me, [{ path: "/dashboard", element: <ShellLayout /> }], "/dashboard", serve);

    expect(await screen.findByRole("link", { name: /^Adjustments/ })).toHaveAttribute("href", "/adjustments");
    await waitFor(() => expect(screen.getByRole("link", { name: /^Adjustments/ })).toHaveTextContent("4"));
    expect(new URL(requestsTo(requests, "GET", "/adjustment-requests")[0].url).searchParams.get("status")).toBe("Requested");
  });

  it("shows an Adviser no Adjustments and asks for no count", async () => {
    const { requests } = renderFees(adviser(), [{ path: "/dashboard", element: <ShellLayout /> }], "/dashboard", serve);

    expect(await screen.findByRole("link", { name: /^Accounts/ })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /^Adjustments/ })).toBeNull();
    expect(requestsTo(requests, "GET", "/adjustment-requests")).toHaveLength(0);
  });
});

describe("AdjustmentsListPage (adjustment-review D103)", () => {
  const routes = [{ path: "/adjustments", element: <AdjustmentsListPage /> }];

  beforeEach(() => stubBrowser("/adjustments"));

  function serve(request: Request) {
    const url = new URL(request.url);
    if (url.pathname === "/api/adjustment-requests") {
      return page([adjustment()]);
    }
    if (url.pathname === `/api/accounts/${accountId}`) {
      return json(account);
    }
    return undefined;
  }

  it("lists the requests waiting for approval by default, with the reason, the cash and the holding change", async () => {
    const { requests } = renderFees(me, routes, "/adjustments", serve);

    const table = await screen.findByRole("table", { name: "Adjustments" });
    expect(await within(table).findByText("Waiting for approval")).toBeInTheDocument();
    expect(within(table).getByText("Entry error")).toBeInTheDocument();
    expect(within(table).getByText("AAPL quantity +10, cost +250.00 USD")).toBeInTheDocument();
    expect(within(table).getByText("Tessa Admin")).toBeInTheDocument();
    expect(await within(table).findByText("Managed portfolio")).toBeInTheDocument();
    expect(within(table).getByRole("link", { name: /2026/ })).toHaveAttribute("href", `/adjustments/${requestId}`);
    expect(new URL(requestsTo(requests, "GET", "/adjustment-requests")[0].url).searchParams.get("status")).toBe("Requested");
  });

  it("filters by status, and All sends none", async () => {
    const { requests } = renderFees(me, routes, "/adjustments", serve);

    await screen.findByRole("table", { name: "Adjustments" });
    await chooseOption("Status", "Approved");
    await waitFor(() => expect(requestsTo(requests, "GET", "/adjustment-requests").some(
      (request) => new URL(request.url).searchParams.get("status") === "Approved")).toBe(true));
    await chooseOption("Status", "All");
    await waitFor(() => expect(requestsTo(requests, "GET", "/adjustment-requests").some(
      (request) => !new URL(request.url).searchParams.has("status"))).toBe(true));
  });

  it("tells an Adviser only a firm administrator can see adjustments", async () => {
    renderFees(adviser(), routes, "/adjustments", () => new Response("", { status: 403 }));

    expect(await screen.findByText("Only a firm administrator can see adjustments.")).toBeInTheDocument();
  });
});

describe("AdjustmentDetailPage (adjustment-review D104)", () => {
  const routes = [{ path: "/adjustments/:id", element: <AdjustmentDetailPage /> }];

  beforeEach(() => stubBrowser(`/adjustments/${requestId}`));

  function serve(item: Record<string, unknown>, action?: () => Response) {
    return (request: Request) => {
      const url = new URL(request.url);
      if (request.method === "POST") {
        return action?.() ?? new Response(null, { status: 204 });
      }
      if (url.pathname === `/api/adjustment-requests/${requestId}`) {
        return json(item);
      }
      if (url.pathname === `/api/accounts/${accountId}`) {
        return json(account);
      }
      return undefined;
    };
  }

  it("shows every field, the memo the customer sees, and lets another TenantAdmin approve with the rowVersion", async () => {
    const { requests } = renderFees(me, routes, `/adjustments/${requestId}`, serve(adjustment()));

    expect(await screen.findByText("Buy was filled at 25.40, not 25.10")).toBeInTheDocument();
    expect(screen.getByText("The customer sees this memo")).toBeInTheDocument();
    expect(screen.getByText("Entry error")).toBeInTheDocument();
    expect(screen.getByText("AAPL quantity +10, cost +250.00 USD")).toBeInTheDocument();
    expect(screen.getByText("Waiting for approval")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Approve" }));

    await waitFor(() => expect(requestsTo(requests, "POST", `/adjustment-requests/${requestId}/approve`)).toHaveLength(1));
    expect(await body(requestsTo(requests, "POST", `/adjustment-requests/${requestId}/approve`)[0])).toEqual({ note: null, rowVersion: "AAAA" });
  });

  it("disables Approve and Reject for the requester, who can still cancel", async () => {
    const { requests } = renderFees(me, routes, `/adjustments/${requestId}`,
      serve(adjustment({ requestedBy: { id: me.id, name: me.name } })));

    expect(await screen.findByRole("button", { name: "Approve" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Reject" })).toBeDisabled();
    expect(screen.getByText("Someone else must approve this adjustment.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Cancel adjustment" }));

    await waitFor(() => expect(requestsTo(requests, "POST", `/adjustment-requests/${requestId}/cancel`)).toHaveLength(1));
  });

  it("rejects with a required note", async () => {
    const { requests } = renderFees(me, routes, `/adjustments/${requestId}`, serve(adjustment()));

    fireEvent.click(await screen.findByRole("button", { name: "Reject" }));
    fireEvent.change(screen.getByLabelText("Note"), { target: { value: "Wrong account" } });
    fireEvent.click(screen.getByRole("button", { name: "Confirm" }));

    await waitFor(() => expect(requestsTo(requests, "POST", `/adjustment-requests/${requestId}/reject`)).toHaveLength(1));
    expect(await body(requestsTo(requests, "POST", `/adjustment-requests/${requestId}/reject`)[0]))
      .toEqual({ note: "Wrong account", rowVersion: "AAAA" });
  });

  it("shows the sentence of a 400 from approve and leaves the request as it is", async () => {
    renderFees(me, routes, `/adjustments/${requestId}`, serve(adjustment(), () => problem(400, {
      errors: { "": ["Available cash cannot become negative: 400.00 USD is reserved for withdrawals and orders."] },
    })));

    fireEvent.click(await screen.findByRole("button", { name: "Approve" }));

    expect(await screen.findByText("Available cash cannot become negative: 400.00 USD is reserved for withdrawals and orders."))
      .toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Approve" })).toBeEnabled();
  });

  it("reloads with a sentence on a 409", async () => {
    const { requests } = renderFees(me, routes, `/adjustments/${requestId}`,
      serve(adjustment(), () => problem(409, { code: "stale_row_version" })));

    fireEvent.click(await screen.findByRole("button", { name: "Approve" }));

    expect(await screen.findByText("This adjustment has changed.")).toBeInTheDocument();
    await waitFor(() => expect(requestsTo(requests, "GET", `/adjustment-requests/${requestId}`).length).toBeGreaterThan(1));
  });

  it("shows the decision with its note and a link to the posted Transaction, and no actions", async () => {
    renderFees(me, routes, `/adjustments/${requestId}`, serve(adjustment({
      status: "Approved",
      decision: { action: "Approved", by: { id: "f0000000-0000-0000-0000-000000000001", name: "Tom Admin" }, at: "2026-11-25T02:10:00Z", note: "Checked" },
      transactionId,
    })));

    expect(await screen.findByText("Tom Admin")).toBeInTheDocument();
    expect(screen.getByText("Checked")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "View the transaction" })).toHaveAttribute("href", `/transactions/${transactionId}`);
    expect(screen.queryByRole("button", { name: "Approve" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Cancel adjustment" })).toBeNull();
  });
});

describe("Open adjustment requests on the account page (adjustment-review D105)", () => {
  const routes = [{ path: "/accounts/:id", element: <AccountDetailPage /> }];

  beforeEach(() => stubBrowser(`/accounts/${accountId}`));

  function serve(request: Request) {
    const url = new URL(request.url);
    if (url.pathname === "/api/adjustment-requests") {
      return page([adjustment()]);
    }
    if (url.pathname === `/api/accounts/${accountId}`) {
      return json(account);
    }
    if (url.pathname === `/api/accounts/${accountId}/holdings`) {
      return json({ items: [] });
    }
    return undefined;
  }

  it("shows a TenantAdmin the account's open requests, each linking to the request", async () => {
    const { requests } = renderFees(me, routes, `/accounts/${accountId}`, serve);

    const table = await screen.findByRole("table", { name: "Adjustments" });
    expect(within(table).getByRole("link", { name: /2026/ })).toHaveAttribute("href", `/adjustments/${requestId}`);
    const asked = new URL(requestsTo(requests, "GET", "/adjustment-requests")[0].url).searchParams;
    expect(asked.get("accountId")).toBe(accountId);
    expect(asked.get("status")).toBe("Requested");
  });

  it("asks nothing for an Adviser", async () => {
    const { requests } = renderFees(adviser(), routes, `/accounts/${accountId}`, serve);

    expect(await screen.findByRole("heading", { name: "Managed portfolio" })).toBeInTheDocument();
    expect(requestsTo(requests, "GET", "/adjustment-requests")).toHaveLength(0);
    expect(screen.queryByRole("table", { name: "Adjustments" })).toBeNull();
  });
});

describe("Close account page with an open adjustment request (adjustment-review D105)", () => {
  const routes = [{ path: "/accounts/:id/close", element: <CloseAccountPage /> }];

  beforeEach(() => stubBrowser(`/accounts/${accountId}/close`));

  it("shows the open request among the failed checks, with a link to it", async () => {
    renderFees(me, routes, `/accounts/${accountId}/close`, (request) => {
      const url = new URL(request.url);
      if (url.pathname === "/api/adjustment-requests") {
        return page([adjustment()]);
      }
      if (url.pathname === `/api/accounts/${accountId}`) {
        return json(account);
      }
      if (url.pathname === `/api/accounts/${accountId}/holdings`) {
        return json({ items: [] });
      }
      if (url.pathname === `/api/accounts/${accountId}/portfolio`) {
        return new Response("", { status: 404 });
      }
      if (url.pathname === "/api/withdrawals" || url.pathname === "/api/orders" || url.pathname === "/api/nominated-bank-accounts") {
        return page([]);
      }
      return undefined;
    });

    expect(await screen.findByText("No open adjustment requests")).toBeInTheDocument();
    expect(screen.getByText(/Open adjustment requests must be approved, rejected or cancelled before the account can be closed\./))
      .toBeInTheDocument();
    expect(screen.getByRole("link", { name: "View the adjustment" })).toHaveAttribute("href", `/adjustments/${requestId}`);
  });
});

describe("An adjustment on the transaction page (adjustment-review D106)", () => {
  const path = `/transactions/${transactionId}`;
  const routes = [{ path: "/transactions/:id", element: <TransactionDetailPage /> }];
  const posted = {
    id: transactionId,
    tenantId: account.tenantId,
    type: "Adjustment",
    source: { kind: "AdjustmentRequest", id: requestId },
    adjustment: { reason: "EntryError" },
    bookedAt: "2026-11-25T02:10:00Z",
    memo: "Buy was filled at 25.40, not 25.10",
    reference: null,
    originalTransactionId: null,
    reversedByTransactionId: null,
    rowVersion: "AAAA",
    cash: [{ accountId, customerId, amount: -0.6, currency: "USD", role: "Principal" }],
    security: [],
  };

  beforeEach(() => stubBrowser(path));

  function serve(request: Request) {
    return new URL(request.url).pathname === `/api${path}` ? json(posted) : undefined;
  }

  it("names the source and links a TenantAdmin to the request, with no Reverse", async () => {
    renderFees(me, routes, path, serve);

    expect(await screen.findByText("Source: Adjustment request")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Adjustment request" })).toHaveAttribute("href", `/adjustments/${requestId}`);
    expect(screen.queryByRole("button", { name: "Reverse" })).toBeNull();
  });

  it("names the source for an Adviser without a link", async () => {
    renderFees(adviser(), routes, path, serve);

    expect(await screen.findByText("Source: Adjustment request")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Adjustment request" })).toBeNull();
  });
});
