import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { CloseAccountPage } from "@/features/accountClosures/CloseAccountPage";
import { AccountDetailPage } from "@/features/accounts/AccountDetailPage";
import { TransactionDetailPage } from "@/features/transactions/TransactionDetailPage";
import { WithdrawalDetailPage } from "@/features/withdrawals/WithdrawalDetailPage";
import { WithdrawalsListPage } from "@/features/withdrawals/WithdrawalsListPage";
import { chooseOption } from "@/test/chooseOption";
import { adviser, feeUser, json, problem, renderFees, requestsTo, stubBrowser } from "@/test/fees";

const accountId = "aaaaaaaa-0000-0000-0000-000000000001";
const customerId = "cccccccc-0000-0000-0000-000000000001";
const closureId = "99999999-0000-0000-0000-000000000001";
const withdrawalId = "77777777-0000-0000-0000-000000000001";
const payeeId = "bbbbbbbb-0000-0000-0000-000000000001";

function account(overrides: Record<string, unknown> = {}) {
  return {
    id: accountId,
    tenantId: "22222222-2222-2222-2222-222222222222",
    customerId,
    name: "Managed portfolio",
    type: "Brokerage",
    custodyMode: "Custodial",
    currency: "USD",
    depositReference: "K7M3Q9PXB",
    cashBalance: 300.004,
    reservedCash: 0,
    availableCash: 300.004,
    status: "Open",
    isActive: true,
    rowVersion: "AAAA",
    closure: null,
    ...overrides,
  };
}

const payee = {
  id: payeeId,
  customerId,
  currency: "USD",
  bankName: "ANZ",
  accountName: "J Smith",
  accountNumber: "06-0123-0456789-00",
  status: "Verified",
  added: { by: { id: "x", name: "Ann Adviser" }, at: "2026-11-20T03:00:00Z" },
  review: null,
  removal: null,
  rowVersion: "AAAA",
};

function withdrawal(overrides: Record<string, unknown> = {}) {
  return {
    id: withdrawalId,
    accountId,
    customerId,
    nominatedBankAccount: { id: payeeId, bankName: "ANZ", accountName: "J Smith", accountNumber: "****8900" },
    amount: 300,
    currency: "USD",
    reference: "K7M3Q9PXB",
    memo: "Moving away",
    status: "Requested",
    requestedBy: { id: "eeeeeeee-0000-0000-0000-000000000001", name: "Ann Adviser" },
    requestedAt: "2026-11-22T03:00:00Z",
    decision: null,
    payment: null,
    payoutTransactionId: null,
    returnTransactionId: null,
    failureReason: null,
    returnReason: null,
    accountClosureId: closureId,
    rowVersion: "AAAA",
    ...overrides,
  };
}

const page = (items: unknown[]) => json({ items, page: 1, pageSize: 20, totalCount: items.length });

function path(request: Request) {
  return new URL(request.url).pathname.replace(/^\/api/, "");
}

async function body(request: Request) {
  return JSON.parse(await request.clone().text());
}

describe("Close action on the account page (account-closure D53)", () => {
  beforeEach(() => stubBrowser(`/accounts/${accountId}`));

  function serve(data: Record<string, unknown>) {
    return (request: Request) => {
      const route = path(request);
      if (route === `/accounts/${accountId}`) {
        return json(data);
      }
      if (route === `/accounts/${accountId}/holdings`) {
        return json({ items: [] });
      }
      if (route.startsWith("/transactions") || route.startsWith("/withdrawals") || route.startsWith("/portfolios")) {
        return page([]);
      }
      return undefined;
    };
  }

  it("opens the Close account page on an Open Custodial account instead of closing it", async () => {
    const routes = [{ path: "/accounts/:id", element: <AccountDetailPage /> }];
    const { requests } = renderFees(feeUser(), routes, `/accounts/${accountId}`, serve(account()));

    expect(await screen.findByRole("link", { name: "Close" })).toHaveAttribute("href", `/accounts/${accountId}/close`);
    expect(screen.queryByRole("button", { name: "Close" })).not.toBeInTheDocument();
    expect(requestsTo(requests, "POST", `/accounts/${accountId}/close`)).toHaveLength(0);
  });

  it("keeps the Close button on an External account", async () => {
    const routes = [{ path: "/accounts/:id", element: <AccountDetailPage /> }];
    renderFees(feeUser(), routes, `/accounts/${accountId}`, serve(account({ custodyMode: "External", depositReference: null })));

    expect(await screen.findByRole("button", { name: "Close" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Close" })).not.toBeInTheDocument();
  });

  it("does not offer CloseOut on a Custodial account, even for a remainder below the minor unit", async () => {
    const routes = [{ path: "/accounts/:id", element: <AccountDetailPage /> }];
    renderFees(feeUser(), routes, `/accounts/${accountId}`, serve(account({ cashBalance: 0.004, availableCash: 0.004 })));

    const nav = await screen.findByRole("navigation", { name: "Transaction actions" });
    expect(within(nav).queryByRole("link", { name: "CloseOut" })).not.toBeInTheDocument();
  });
});

describe("A closing account (account-closure D55)", () => {
  beforeEach(() => stubBrowser(`/accounts/${accountId}`));

  const routes = [{ path: "/accounts/:id", element: <AccountDetailPage /> }];

  function serve(data: Record<string, unknown>) {
    return (request: Request) => {
      const route = path(request);
      if (route === `/accounts/${accountId}`) {
        return json(data);
      }
      if (route === `/accounts/${accountId}/holdings`) {
        return json({ items: [] });
      }
      if (route === `/withdrawals/${withdrawalId}`) {
        return json(withdrawal());
      }
      if (route.startsWith("/transactions") || route.startsWith("/withdrawals") || route.startsWith("/portfolios")) {
        return page([]);
      }
      return undefined;
    };
  }

  const inProgress = {
    id: closureId,
    status: "InProgress",
    payoutAmount: 300,
    withdrawalId,
    requestedAt: "2026-11-22T03:00:00Z",
    completedAt: null,
  };

  it("shows the banner with the payout and its withdrawal status, and hides every action", async () => {
    renderFees(feeUser(), routes, `/accounts/${accountId}`, serve(account({ reservedCash: 300, availableCash: 0.004, closure: inProgress })));

    const banner = await screen.findByText(
      "Being closed. 300.00 USD is being paid out (Waiting for approval). The account accepts no new activity.",
    );
    expect(banner).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "View the withdrawal" })).toHaveAttribute("href", `/withdrawals/${withdrawalId}`);
    expect(screen.queryByRole("navigation", { name: "Transaction actions" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Withdraw" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Close" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Close" })).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Portfolio" })).not.toBeInTheDocument();
  });

  it("shows when a Closed account was closed by an account closure", async () => {
    renderFees(feeUser(), routes, `/accounts/${accountId}`, serve(account({
      status: "Closed",
      cashBalance: 0,
      closure: { ...inProgress, status: "Completed", completedAt: "2026-11-23T03:00:00Z" },
    })));

    expect(await screen.findByText("Closed by account closure on 2026-11-23")).toBeInTheDocument();
  });

  it("shows nothing for a Cancelled closure", async () => {
    renderFees(feeUser(), routes, `/accounts/${accountId}`, serve(account({ closure: { ...inProgress, status: "Cancelled" } })));

    expect(await screen.findByRole("link", { name: "Close" })).toBeInTheDocument();
    expect(screen.queryByText(/Being closed/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Closed by account closure/)).not.toBeInTheDocument();
  });
});

describe("CloseAccountPage (account-closure D54)", () => {
  const routes = [
    { path: "/accounts/:id/close", element: <CloseAccountPage /> },
    { path: "/accounts/:id", element: <p>Account page</p> },
  ];

  beforeEach(() => stubBrowser(`/accounts/${accountId}/close`));

  type Serve = {
    data?: Record<string, unknown>;
    holdings?: unknown[];
    assignment?: Record<string, unknown> | null;
    open?: unknown[];
    payees?: unknown[];
    post?: () => Response;
  };

  function serve({ data = account(), holdings = [], assignment = null, open = [], payees = [payee], post }: Serve = {}) {
    return (request: Request) => {
      const route = path(request);
      if (request.method === "POST") {
        return post?.() ?? json({ id: closureId }, 201);
      }
      if (route === `/accounts/${accountId}`) {
        return json(data);
      }
      if (route === `/accounts/${accountId}/holdings`) {
        return json({ items: holdings });
      }
      if (route === `/accounts/${accountId}/portfolio`) {
        return assignment ? json(assignment) : problem(404, { title: "Not found" });
      }
      if (route === "/withdrawals") {
        return page(open);
      }
      if (route === "/nominated-bank-accounts") {
        return page(payees);
      }
      return undefined;
    };
  }

  it("ticks every check, shows the amounts, reviews and posts the closure with one key", async () => {
    const { requests } = renderFees(adviser(), routes, `/accounts/${accountId}/close`, serve());

    expect(await screen.findByText("No holdings")).toBeInTheDocument();
    expect(screen.getByText("No portfolio")).toBeInTheDocument();
    expect(screen.getByText("No open withdrawals")).toBeInTheDocument();
    expect(screen.getByText("300.004 USD")).toBeInTheDocument();
    expect(screen.getByText("300.00 USD")).toBeInTheDocument();
    expect(screen.getByText("0.004 USD")).toBeInTheDocument();
    expect(screen.getByText("Written off")).toBeInTheDocument();
    const open = new URL(requestsTo(requests, "GET", "/withdrawals")[0].url);
    expect(open.searchParams.get("accountId")).toBe(accountId);
    expect(open.searchParams.get("status")).toBe("Requested,Approved,Sent");
    const lookup = new URL(requestsTo(requests, "GET", "/nominated-bank-accounts")[0].url);
    expect(lookup.searchParams.get("customerId")).toBe(customerId);
    expect(lookup.searchParams.get("status")).toBe("Verified");
    expect(lookup.searchParams.get("currency")).toBe("USD");

    await chooseOption("To", "ANZ J Smith ****8900");
    fireEvent.change(screen.getByLabelText("Memo"), { target: { value: "Moving to another provider" } });
    fireEvent.click(screen.getByRole("button", { name: "Review" }));

    expect(await screen.findByText(
      "Pay 300.00 USD to ANZ J Smith ****8900 and close the account when the bank has paid. A firm administrator must approve the payment. Until then the account accepts no new activity.",
    )).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Confirm" }));

    expect(await screen.findByText("Account page")).toBeInTheDocument();
    const post = requestsTo(requests, "POST", `/accounts/${accountId}/closures`)[0];
    expect(post.headers.get("Idempotency-Key")).toMatch(/^[0-9a-f-]{36}$/i);
    expect(await body(post)).toEqual({ nominatedBankAccountId: payeeId, memo: "Moving to another provider" });
  });

  it("closes at once with nothing to pay out, and says what is written off", async () => {
    const { requests } = renderFees(adviser(), routes, `/accounts/${accountId}/close`,
      serve({ data: account({ cashBalance: 0.004, availableCash: 0.004 }) }));

    expect(await screen.findByText("No holdings")).toBeInTheDocument();
    expect(screen.queryByLabelText("To")).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Memo"), { target: { value: "Flat" } });
    fireEvent.click(screen.getByRole("button", { name: "Review" }));

    expect(await screen.findByText("Close the account now.")).toBeInTheDocument();
    expect(screen.getByText("0.004 USD is written off.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Confirm" }));

    expect(await screen.findByText("Account page")).toBeInTheDocument();
    expect(await body(requestsTo(requests, "POST", `/accounts/${accountId}/closures`)[0]))
      .toEqual({ nominatedBankAccountId: null, memo: "Flat" });
  });

  it("does not show Written off when there is no remainder", async () => {
    renderFees(adviser(), routes, `/accounts/${accountId}/close`, serve({ data: account({ cashBalance: 300, availableCash: 300 }) }));

    expect(await screen.findByText("No holdings")).toBeInTheDocument();
    expect(screen.queryByText("Written off")).not.toBeInTheDocument();
  });

  it("shows each failed check with its sentence and link, and disables Confirm", async () => {
    renderFees(adviser(), routes, `/accounts/${accountId}/close`, serve({
      holdings: [{ id: "h1", accountId, instrumentId: "i1", symbol: "VTI", name: "Vanguard", quantity: 1, cost: 25, costCurrency: "USD" }],
      assignment: { accountId, portfolioId: "pppppppp-0000-0000-0000-000000000001", status: "Active", lastRebalanceAt: null, lastRebalanceStatus: null, lastRebalanceMessage: null },
      open: [withdrawal({ accountClosureId: null })],
    }));

    expect(await screen.findByText("Sell every holding before closing the account.")).toBeInTheDocument();
    expect(await screen.findByText("Unassign the portfolio before closing the account.")).toBeInTheDocument();
    expect(await screen.findByText("Open withdrawals must be paid, rejected or cancelled before the account can be closed.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "View the holdings" })).toHaveAttribute("href", `/accounts/${accountId}`);
    expect(screen.getByRole("link", { name: "View the portfolio" })).toHaveAttribute("href", "/portfolios/pppppppp-0000-0000-0000-000000000001");
    expect(screen.getByRole("link", { name: "View the withdrawal" })).toHaveAttribute("href", `/withdrawals/${withdrawalId}`);
    fireEvent.change(screen.getByLabelText("Memo"), { target: { value: "Moving" } });
    expect(screen.getByRole("button", { name: "Review" })).toBeDisabled();
    expect(screen.queryByRole("button", { name: "Confirm" })).not.toBeInTheDocument();
  });

  it("says how to add a bank account when the Customer has none in the currency", async () => {
    renderFees(adviser(), routes, `/accounts/${accountId}/close`, serve({ payees: [] }));

    expect(await screen.findByText(/No verified bank account in USD\. Add one on the/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "customer page" })).toHaveAttribute("href", `/customers/${customerId}`);
    expect(screen.getByRole("button", { name: "Review" })).toBeDisabled();
  });

  it("shows a 400 at its field and keeps one key per form open", async () => {
    let calls = 0;
    const { requests } = renderFees(adviser(), routes, `/accounts/${accountId}/close`, serve({
      post: () => {
        calls += 1;
        return calls === 1
          ? problem(400, { title: "Validation", errors: { Memo: ["Memo is required and must be 1 to 200 characters."] } })
          : json({ id: closureId }, 201);
      },
    }));

    await screen.findByText("No holdings");
    await chooseOption("To", "ANZ J Smith ****8900");
    fireEvent.change(screen.getByLabelText("Memo"), { target: { value: "x" } });
    fireEvent.click(screen.getByRole("button", { name: "Review" }));
    fireEvent.click(await screen.findByRole("button", { name: "Confirm" }));
    expect(await screen.findByText("Memo is required and must be 1 to 200 characters.")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Review" }));
    fireEvent.click(await screen.findByRole("button", { name: "Confirm" }));
    expect(await screen.findByText("Account page")).toBeInTheDocument();
    const posts = requestsTo(requests, "POST", `/accounts/${accountId}/closures`);
    expect(posts).toHaveLength(2);
    expect(posts[1].headers.get("Idempotency-Key")).toBe(posts[0].headers.get("Idempotency-Key"));
  });
});

describe("A closure's withdrawal (account-closure D56)", () => {
  beforeEach(() => stubBrowser("/withdrawals"));

  function serve(item: Record<string, unknown>) {
    return (request: Request) => {
      const route = path(request);
      if (request.method === "POST") {
        return new Response(null, { status: 204 });
      }
      if (route === "/withdrawals") {
        return page([item]);
      }
      if (route === `/withdrawals/${withdrawalId}`) {
        return json(item);
      }
      if (route === `/accounts/${accountId}`) {
        return json(account());
      }
      return undefined;
    };
  }

  it("shows Account closure under To on the list", async () => {
    renderFees(feeUser(), [{ path: "/withdrawals", element: <WithdrawalsListPage /> }], "/withdrawals", serve(withdrawal()));

    const table = await screen.findByRole("table", { name: "Withdrawals" });
    expect(await within(table).findByText("Account closure")).toBeInTheDocument();
  });

  it("says the withdrawal pays out a closure, and the Reject dialog warns it stops the closure", async () => {
    renderFees(feeUser(), [{ path: "/withdrawals/:id", element: <WithdrawalDetailPage /> }], `/withdrawals/${withdrawalId}`,
      serve(withdrawal()));

    expect(await screen.findByText("This withdrawal pays out an account closure.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reject" }));
    expect(await screen.findByText("This also stops the account closure. The account stays open.")).toBeInTheDocument();
  });

  it("asks before Cancel, with the same warning", async () => {
    const { requests } = renderFees(adviser(), [{ path: "/withdrawals/:id", element: <WithdrawalDetailPage /> }],
      `/withdrawals/${withdrawalId}`, serve(withdrawal()));

    fireEvent.click(await screen.findByRole("button", { name: "Cancel withdrawal" }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("This also stops the account closure. The account stays open.")).toBeInTheDocument();
    expect(requestsTo(requests, "POST", `/withdrawals/${withdrawalId}/cancel`)).toHaveLength(0);
    fireEvent.click(within(dialog).getByRole("button", { name: "Cancel withdrawal" }));
    await waitFor(() => expect(requestsTo(requests, "POST", `/withdrawals/${withdrawalId}/cancel`)).toHaveLength(1));
  });

  it("cancels an ordinary withdrawal without a dialog", async () => {
    const { requests } = renderFees(adviser(), [{ path: "/withdrawals/:id", element: <WithdrawalDetailPage /> }],
      `/withdrawals/${withdrawalId}`, serve(withdrawal({ accountClosureId: null })));

    fireEvent.click(await screen.findByRole("button", { name: "Cancel withdrawal" }));
    await waitFor(() => expect(requestsTo(requests, "POST", `/withdrawals/${withdrawalId}/cancel`)).toHaveLength(1));
    expect(screen.queryByText("This withdrawal pays out an account closure.")).not.toBeInTheDocument();
  });
});

describe("The remainder write-off (account-closure D57)", () => {
  beforeEach(() => stubBrowser("/transactions/t1"));

  it("shows the Account closure source with a link to the account, the rounding type, and no Reverse", async () => {
    renderFees(feeUser(), [{ path: "/transactions/:id", element: <TransactionDetailPage /> }], "/transactions/t1", (request) =>
      path(request) === "/transactions/t1"
        ? json({
          id: "t1",
          type: "CloseOut",
          bookedAt: "2026-11-22T03:12:40Z",
          memo: "Account closure remainder",
          reference: null,
          source: { kind: "AccountClosure", id: closureId },
          reversedByTransactionId: null,
          cash: [{ accountId, amount: -0.004, currency: "USD", role: "Principal" }],
          security: [],
        })
        : undefined);

    expect(await screen.findByText("Source: Account closure")).toBeInTheDocument();
    expect(screen.getByText("Close out (rounding)")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Account closure" })).toHaveAttribute("href", `/accounts/${accountId}`);
    expect(screen.queryByRole("button", { name: "Reverse" })).not.toBeInTheDocument();
  });
});
