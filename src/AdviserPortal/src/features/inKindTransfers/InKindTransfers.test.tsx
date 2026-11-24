import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { AccountDetailPage } from "@/features/accounts/AccountDetailPage";
import { TransferDetailPage } from "@/features/inKindTransfers/TransferDetailPage";
import { TransferSecuritiesPage } from "@/features/inKindTransfers/TransferSecuritiesPage";
import { TransfersListPage } from "@/features/inKindTransfers/TransfersListPage";
import { CreateTransactionPage } from "@/features/transactions/CreateTransactionPage";
import { TransactionDetailPage } from "@/features/transactions/TransactionDetailPage";
import { ShellLayout } from "@/layouts/ShellLayout";
import { chooseOption, openSelect, pickOption } from "@/test/chooseOption";
import { adviser, feeUser, json, problem, renderFees, requestsTo, stubBrowser } from "@/test/fees";

const accountId = "aaaaaaaa-0000-0000-0000-000000000001";
const customerId = "cccccccc-0000-0000-0000-000000000001";
const transferId = "99999999-0000-0000-0000-000000000001";
const acmeId = "dddddddd-0000-0000-0000-000000000001";
const kiwiId = "dddddddd-0000-0000-0000-000000000002";
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

const holding = {
  id: "hhhhhhhh-0000-0000-0000-000000000001",
  accountId,
  instrumentId: acmeId,
  symbol: "ACME.SIM",
  name: "Acme Simulated",
  quantity: 100,
  cost: 9500,
  costCurrency: "USD",
  reservedQuantity: 20,
  availableQuantity: 80,
  unsettledQuantity: 10,
  transferableQuantity: 70,
};

const instruments = [
  { id: acmeId, tenantId: account.tenantId, symbol: "ACME.SIM", name: "Acme Simulated", quoteCurrency: "USD", isActive: true, rowVersion: "AAAA" },
  { id: kiwiId, tenantId: account.tenantId, symbol: "KIWI.SIM", name: "Kiwi Simulated", quoteCurrency: "NZD", isActive: true, rowVersion: "AAAA" },
];

function transfer(overrides: Record<string, unknown> = {}) {
  return {
    id: transferId,
    accountId,
    customerId,
    direction: "In",
    instrument: { id: acmeId, symbol: "ACME.SIM", name: "Acme Simulated" },
    quantity: 100,
    currency: "USD",
    cost: 9500,
    acquiredOn: "2024-03-18",
    changesBeneficialOwner: false,
    counterparty: { name: "Harbour Brokers", account: "HB-0044172", holderName: "J Smith" },
    memo: "Moving the Harbour holding across",
    status: "Requested",
    requestedBy: { id: "eeeeeeee-0000-0000-0000-000000000001", name: "Ann Adviser" },
    requestedAt: "2026-11-24T01:00:00Z",
    decision: null,
    send: null,
    failureReason: null,
    setAside: null,
    transactionId: null,
    completedAt: null,
    rowVersion: "AAAA",
    ...overrides,
  };
}

const page = (items: unknown[], totalCount = items.length) => json({ items, page: 1, pageSize: 20, totalCount });

async function body(request: Request) {
  return JSON.parse(await request.clone().text());
}

describe("Transfers navigation (in-kind-transfers D88, D90, D91)", () => {
  beforeEach(() => stubBrowser("/dashboard"));

  function serve(request: Request) {
    const url = new URL(request.url);
    if (url.pathname === "/api/in-kind-transfers") {
      return page([], 2);
    }
    if (url.pathname === "/api/custody-movements") {
      return page([], 3);
    }
    return undefined;
  }

  it("shows a TenantAdmin Transfers with the count waiting for approval, and the custody pages with the suspense count", async () => {
    const { requests } = renderFees(me, [{ path: "/dashboard", element: <ShellLayout /> }], "/dashboard", serve);

    expect(await screen.findByRole("link", { name: /^Transfers/ })).toHaveAttribute("href", "/transfers");
    await waitFor(() => expect(screen.getByRole("link", { name: /^Transfers/ })).toHaveTextContent("2"));
    expect(screen.getByRole("link", { name: /^Custody movements/ })).toHaveAttribute("href", "/custody-movements");
    await waitFor(() => expect(screen.getByRole("link", { name: /^Custody movements/ })).toHaveTextContent("3"));
    expect(screen.getByRole("link", { name: /^Custodian reconciliation/ })).toHaveAttribute("href", "/custodian-reconciliations");
    expect(new URL(requestsTo(requests, "GET", "/in-kind-transfers")[0].url).searchParams.get("status")).toBe("Requested");
    expect(new URL(requestsTo(requests, "GET", "/custody-movements")[0].url).searchParams.get("status")).toBe("Unmatched");
  });

  it("shows an Adviser Transfers without a count, and no custody pages", async () => {
    const { requests } = renderFees(adviser(), [{ path: "/dashboard", element: <ShellLayout /> }], "/dashboard", serve);

    expect(await screen.findByRole("link", { name: /^Transfers/ })).toHaveAttribute("href", "/transfers");
    expect(screen.queryByRole("link", { name: /^Custody movements/ })).toBeNull();
    expect(screen.queryByRole("link", { name: /^Custodian reconciliation/ })).toBeNull();
    expect(requestsTo(requests, "GET", "/in-kind-transfers")).toHaveLength(0);
    expect(requestsTo(requests, "GET", "/custody-movements")).toHaveLength(0);
  });
});

describe("TransferSecuritiesPage (in-kind-transfers D87)", () => {
  const routes = [
    { path: "/accounts/:id/transfer-securities", element: <TransferSecuritiesPage /> },
    { path: "/transfers/:id", element: <p>Transfer page</p> },
  ];

  beforeEach(() => stubBrowser(`/accounts/${accountId}/transfer-securities`));

  function serve(post?: () => Response) {
    return (request: Request) => {
      const url = new URL(request.url);
      if (request.method === "POST") {
        return post?.() ?? json({ id: transferId }, 201);
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

  function fillCounterparty() {
    fireEvent.change(screen.getByLabelText("Broker or registry"), { target: { value: "Harbour Brokers" } });
    fireEvent.change(screen.getByLabelText("Account or holder number"), { target: { value: "HB-0044172" } });
    fireEvent.change(screen.getByLabelText("Name on the account"), { target: { value: "J Smith" } });
  }

  it("requests a transfer in: an instrument in another currency is disabled, review warns, and confirm posts with one key", async () => {
    const { requests, router } = renderFees(me, routes, `/accounts/${accountId}/transfer-securities`, serve());

    await chooseOption("Direction", "In");
    await openSelect("Instrument");
    expect(screen.getByRole("option", { name: "KIWI.SIM Kiwi Simulated (Quoted in NZD)" })).toHaveAttribute("aria-disabled", "true");
    await pickOption("ACME.SIM Acme Simulated");
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "100" } });
    expect(screen.getByText("What the customer paid for these shares in total, in USD")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Cost"), { target: { value: "9500" } });
    fireEvent.change(screen.getByLabelText("Acquired on (optional)"), { target: { value: "2024-03-18" } });
    fireEvent.click(screen.getByLabelText(/^No/));
    expect(screen.getByText("The customer owns these shares before and after")).toBeInTheDocument();
    fillCounterparty();
    fireEvent.change(screen.getByLabelText("Memo"), { target: { value: "Moving the Harbour holding across" } });
    fireEvent.click(screen.getByRole("button", { name: "Review" }));

    expect(await screen.findByText("A firm administrator must approve this transfer before it is sent to the custodian.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Confirm transfer" }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/transfers/${transferId}`));
    const [post] = requestsTo(requests, "POST", "/in-kind-transfers");
    expect(post.headers.get("Idempotency-Key")).toMatch(/^[0-9a-f-]{36}$/);
    expect(await body(post)).toEqual({
      accountId,
      direction: "In",
      instrumentId: acmeId,
      quantity: 100,
      cost: 9500,
      acquiredOn: "2024-03-18",
      changesBeneficialOwner: false,
      counterparty: { name: "Harbour Brokers", account: "HB-0044172", holderName: "J Smith" },
      memo: "Moving the Harbour holding across",
    });
  });

  it("requests a transfer out from a holding with what is available, and sends no cost", async () => {
    const { requests } = renderFees(me, routes, `/accounts/${accountId}/transfer-securities`, serve());

    await chooseOption("Direction", "Out");
    await chooseOption("Instrument", "ACME.SIM Acme Simulated (70 available to transfer)");
    expect(screen.queryByLabelText("Cost")).toBeNull();
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "50" } });
    fireEvent.click(screen.getByLabelText(/^Yes/));
    fillCounterparty();
    fireEvent.click(screen.getByRole("button", { name: "Review" }));
    fireEvent.click(await screen.findByRole("button", { name: "Confirm transfer" }));

    await waitFor(() => expect(requestsTo(requests, "POST", "/in-kind-transfers")).toHaveLength(1));
    const sent = await body(requestsTo(requests, "POST", "/in-kind-transfers")[0]);
    expect(sent).toMatchObject({ direction: "Out", instrumentId: acmeId, quantity: 50, changesBeneficialOwner: true, memo: null });
    expect(sent).not.toHaveProperty("cost");
    expect(sent).not.toHaveProperty("acquiredOn");
  });

  it("shows a 400 at its field", async () => {
    renderFees(me, routes, `/accounts/${accountId}/transfer-securities`,
      serve(() => problem(400, { errors: { Quantity: ["Only 70 ACME.SIM can be transferred out."] } })));

    await chooseOption("Direction", "Out");
    await chooseOption("Instrument", "ACME.SIM Acme Simulated (70 available to transfer)");
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "90" } });
    fireEvent.click(screen.getByLabelText(/^No/));
    fillCounterparty();
    fireEvent.click(screen.getByRole("button", { name: "Review" }));
    fireEvent.click(await screen.findByRole("button", { name: "Confirm transfer" }));

    expect(await screen.findByText("Only 70 ACME.SIM can be transferred out.")).toBeInTheDocument();
  });
});

describe("TransfersListPage (in-kind-transfers D88)", () => {
  const routes = [{ path: "/transfers", element: <TransfersListPage /> }];

  beforeEach(() => stubBrowser("/transfers"));

  it("lists the open transfers by default with their labels", async () => {
    const { requests } = renderFees(me, routes, "/transfers", (request) => {
      const url = new URL(request.url);
      if (url.pathname === "/api/in-kind-transfers") {
        return page([transfer(), transfer({ id: "99999999-0000-0000-0000-000000000002", status: "Sent", direction: "Out" })]);
      }
      if (url.pathname === `/api/accounts/${accountId}`) {
        return json(account);
      }
      return undefined;
    });

    const table = await screen.findByRole("table", { name: "Transfers" });
    expect(await within(table).findByText("Waiting for approval")).toBeInTheDocument();
    expect(within(table).getByText("With the custodian")).toBeInTheDocument();
    expect(within(table).getAllByText("Harbour Brokers")).toHaveLength(2);
    expect(new URL(requestsTo(requests, "GET", "/in-kind-transfers")[0].url).searchParams.get("status")).toBe("Requested,Approved,Sent");
  });
});

describe("TransferDetailPage (in-kind-transfers D89)", () => {
  const routes = [{ path: "/transfers/:id", element: <TransferDetailPage /> }];

  beforeEach(() => stubBrowser(`/transfers/${transferId}`));

  function serve(item: Record<string, unknown>, action?: () => Response) {
    return (request: Request) => {
      const url = new URL(request.url);
      if (request.method === "POST") {
        return action?.() ?? new Response(null, { status: 204 });
      }
      if (url.pathname === `/api/in-kind-transfers/${transferId}`) {
        return json(item);
      }
      if (url.pathname === `/api/accounts/${accountId}`) {
        return json(account);
      }
      return undefined;
    };
  }

  it("lets another TenantAdmin approve a requested transfer with the rowVersion", async () => {
    const { requests } = renderFees(me, routes, `/transfers/${transferId}`, serve(transfer()));

    expect(await screen.findByText("Owner changes: no")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Approve" }));

    await waitFor(() => expect(requestsTo(requests, "POST", `/in-kind-transfers/${transferId}/approve`)).toHaveLength(1));
    expect(await body(requestsTo(requests, "POST", `/in-kind-transfers/${transferId}/approve`)[0])).toEqual({ note: null, rowVersion: "AAAA" });
  });

  it("disables Approve and Reject for the requester", async () => {
    renderFees(me, routes, `/transfers/${transferId}`, serve(transfer({ requestedBy: { id: me.id, name: me.name } })));

    expect(await screen.findByRole("button", { name: "Approve" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Reject" })).toBeDisabled();
    expect(screen.getByText("Someone else must approve this transfer.")).toBeInTheDocument();
  });

  it("shows an Adviser no approval, but Cancel", async () => {
    renderFees(adviser(), routes, `/transfers/${transferId}`, serve(transfer()));

    expect(await screen.findByRole("button", { name: "Cancel transfer" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Approve" })).toBeNull();
  });

  it("sets a transfer the custodian was asked to make aside with a required note", async () => {
    const sent = transfer({ status: "Sent", send: { attempts: 1, lastAttemptAt: "2026-11-24T01:11:00Z", lastFailure: null, acceptedAt: "2026-11-24T01:11:00Z", custodianTransferId: "w-T3" } });
    const { requests } = renderFees(me, routes, `/transfers/${transferId}`, serve(sent));

    fireEvent.click(await screen.findByRole("button", { name: "Set aside" }));
    expect(screen.getByText("Setting a transfer aside books nothing. Correct the account with an adjustment if the custodian has moved the shares.")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Note"), { target: { value: "Custodian cannot find it" } });
    fireEvent.click(screen.getByRole("button", { name: "Confirm" }));

    await waitFor(() => expect(requestsTo(requests, "POST", `/in-kind-transfers/${transferId}/set-aside`)).toHaveLength(1));
    expect(await body(requestsTo(requests, "POST", `/in-kind-transfers/${transferId}/set-aside`)[0]))
      .toEqual({ note: "Custodian cannot find it", rowVersion: "AAAA" });
    expect(screen.queryByRole("button", { name: "Cancel transfer" })).toBeNull();
  });

  it("shows the custodian's reason on a failed transfer, and a link to the Transaction once completed", async () => {
    renderFees(me, routes, `/transfers/${transferId}`, serve(transfer({ status: "Failed", failureReason: "InsufficientPosition" })));
    expect(await screen.findByText("The custodian does not hold enough")).toBeInTheDocument();
    expect(screen.getByText("Refused by the custodian")).toBeInTheDocument();
  });

  it("links a completed transfer to its Transaction", async () => {
    renderFees(me, routes, `/transfers/${transferId}`, serve(transfer({ status: "Completed", transactionId })));
    expect(await screen.findByRole("link", { name: "View the transaction" })).toHaveAttribute("href", `/transactions/${transactionId}`);
  });

  it("reloads with a sentence on a 409, and shows a 400's sentence", async () => {
    renderFees(me, routes, `/transfers/${transferId}`, serve(transfer(), () => problem(409, { code: "stale_row_version" })));
    fireEvent.click(await screen.findByRole("button", { name: "Approve" }));
    expect(await screen.findByText("This transfer has changed.")).toBeInTheDocument();
  });
});

describe("AccountDetailPage with in-kind transfers (in-kind-transfers D86)", () => {
  const routes = [{ path: "/accounts/:id", element: <AccountDetailPage /> }];

  beforeEach(() => stubBrowser(`/accounts/${accountId}`));

  function serve(assignment: unknown) {
    return (request: Request) => {
      const url = new URL(request.url);
      if (url.pathname === `/api/accounts/${accountId}/holdings`) {
        return json({ items: [holding] });
      }
      if (url.pathname === `/api/accounts/${accountId}/portfolio`) {
        return assignment ? json(assignment) : new Response("", { status: 404 });
      }
      if (url.pathname === `/api/accounts/${accountId}`) {
        return json(account);
      }
      if (url.pathname === "/api/in-kind-transfers") {
        return page([transfer()]);
      }
      if (url.pathname === "/api/transactions") {
        return page([]);
      }
      return undefined;
    };
  }

  it("offers Transfer securities, shows the reserved and unsettled quantities, and lists the open transfers", async () => {
    const { requests } = renderFees(me, routes, `/accounts/${accountId}`, serve(null));

    expect(await screen.findByRole("link", { name: "Transfer securities" })).toHaveAttribute("href", `/accounts/${accountId}/transfer-securities`);
    expect(screen.getByText("20 reserved")).toBeInTheDocument();
    expect(screen.getByText("10 not settled")).toBeInTheDocument();
    const open = await screen.findByRole("table", { name: "Transfers" });
    expect(within(open).getByText("Waiting for approval")).toBeInTheDocument();
    const lookup = new URL(requestsTo(requests, "GET", "/in-kind-transfers")[0].url);
    expect(lookup.searchParams.get("accountId")).toBe(accountId);
    expect(lookup.searchParams.get("status")).toBe("Requested,Approved,Sent");
  });

  it("disables Transfer securities on an account that follows a portfolio", async () => {
    renderFees(me, routes, `/accounts/${accountId}`,
      serve({ accountId, portfolioId: "ffffffff-0000-0000-0000-000000000001", status: "Active", rowVersion: "AAAA" }));

    expect(await screen.findByRole("button", { name: "Transfer securities" })).toBeDisabled();
  });
});

describe("Securities on a Custodial account move by transfer (in-kind-transfers D86, R48)", () => {
  beforeEach(() => stubBrowser(`/accounts/${accountId}/transactions/new`));

  it("points the new transaction form to a transfer", async () => {
    renderFees(me, [{ path: "/accounts/:id/transactions/new", element: <CreateTransactionPage /> }],
      `/accounts/${accountId}/transactions/new?type=StockIn`, (request) =>
        new URL(request.url).pathname === `/api/accounts/${accountId}` ? json(account) : undefined);

    expect(await screen.findByText("Securities move in or out of a Custodial account by an in-kind transfer.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Request a transfer" })).toHaveAttribute("href", `/accounts/${accountId}/transfer-securities`);
  });
});

describe("TransactionDetailPage of a transfer (in-kind-transfers D93)", () => {
  beforeEach(() => stubBrowser(`/transactions/${transactionId}`));

  it("shows the source, the Transfer section, and no Reverse", async () => {
    renderFees(me, [{ path: "/transactions/:id", element: <TransactionDetailPage /> }], `/transactions/${transactionId}`, (request) =>
      new URL(request.url).pathname === `/api/transactions/${transactionId}`
        ? json({
            id: transactionId,
            tenantId: account.tenantId,
            type: "StockIn",
            bookedAt: "2026-11-24T01:12:00Z",
            memo: "Transfer in",
            reference: null,
            source: { kind: "InKindTransfer", id: transferId },
            inKindTransfer: {
              transferId,
              direction: "In",
              counterpartyName: "Harbour Brokers",
              counterpartyAccount: "****4172",
              changesBeneficialOwner: false,
              acquiredOn: "2024-03-18",
              settlementDate: "2026-11-24",
              custodianReference: "w-M12",
            },
            cash: [],
            security: [],
            rowVersion: "AAAA",
          })
        : undefined);

    expect(await screen.findByText("Source: In-kind transfer")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "In-kind transfer" })).toHaveAttribute("href", `/transfers/${transferId}`);
    expect(screen.getByText("Harbour Brokers ****4172")).toBeInTheDocument();
    expect(screen.getByText("Owner changes: no")).toBeInTheDocument();
    expect(screen.getByText("w-M12")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Reverse/ })).toBeNull();
  });
});
