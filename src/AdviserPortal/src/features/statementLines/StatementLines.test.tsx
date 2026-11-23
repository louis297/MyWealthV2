import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MatchStatementLinePage } from "@/features/statementLines/MatchStatementLinePage";
import { StatementLineDetailPage } from "@/features/statementLines/StatementLineDetailPage";
import { StatementLinesListPage } from "@/features/statementLines/StatementLinesListPage";
import { ShellLayout } from "@/layouts/ShellLayout";
import { chooseOption } from "@/test/chooseOption";
import { adviser, feeUser, json, problem, renderFees, requestsTo, stubBrowser, type Handler } from "@/test/fees";

const lineId = "3f2c0000-0000-0000-0000-0000000000e1";
const accountId = "aaaaaaaa-0000-0000-0000-000000000001";
const customerId = "cccccccc-0000-0000-0000-000000000001";
const transactionId = "dddddddd-0000-0000-0000-000000000001";

const unmatched = {
  id: lineId,
  clientMoneyAccountId: "bbbbbbbb-0000-0000-0000-000000000001",
  currency: "USD",
  direction: "Credit",
  amount: 500,
  valueDate: "2026-11-19",
  payerName: "J SMITH",
  payerAccount: "****4200",
  reference: "Rent",
  receivedAt: "2026-11-20T02:01:07Z",
  status: "Unmatched",
  unmatchedReason: "NoReference",
  match: null,
  lastAction: null,
  rowVersion: "AAAA",
};

const matched = {
  ...unmatched,
  status: "Matched",
  unmatchedReason: null,
  reference: "K7M3Q9PXB",
  match: { accountId, customerId, transactionId, method: "Reference", at: "2026-11-20T02:01:08Z" },
};

const account = {
  id: accountId,
  tenantId: "22222222-2222-2222-2222-222222222222",
  customerId,
  name: "Managed portfolio",
  type: "Brokerage",
  custodyMode: "Custodial",
  currency: "USD",
  depositReference: "K7M3Q9PXB",
  cashBalance: 0,
  status: "Open",
  isActive: true,
  rowVersion: "AAAA",
};

const customer = {
  id: customerId,
  tenantId: "22222222-2222-2222-2222-222222222222",
  adviserId: "eeeeeeee-0000-0000-0000-000000000001",
  name: "Ned North",
  email: "ned@north.example",
  status: "Active",
  rowVersion: "AAAA",
  created: "2026-11-01T00:00:00Z",
};

const page = (items: unknown[]) => json({ items, page: 1, pageSize: 20, totalCount: items.length });

function path(request: Request) {
  return new URL(request.url).pathname.replace(/^\/api/, "");
}

function serveLine(line: object, extra: Handler = () => undefined): Handler {
  return async (request) => {
    const answer = await extra(request);
    if (answer) {
      return answer;
    }
    const p = path(request);
    if (request.method === "GET" && p === `/statement-lines/${lineId}`) {
      return json(line);
    }
    if (request.method === "GET" && p === `/accounts/${accountId}`) {
      return json(account);
    }
    if (request.method === "GET" && p === `/users/customers/${customerId}`) {
      return json(customer);
    }
    return undefined;
  };
}

async function body(request: Request) {
  return JSON.parse(await request.clone().text());
}

describe("Bank receipts navigation (deposit-matching D34)", () => {
  beforeEach(() => stubBrowser("/dashboard"));

  it("shows Bank receipts to a TenantAdmin and not to an Adviser", async () => {
    const routes = [{ path: "/dashboard", element: <ShellLayout /> }];
    renderFees(feeUser(), routes, "/dashboard");
    expect(await screen.findByRole("link", { name: /Bank receipts/ })).toHaveAttribute("href", "/statement-lines");
  });

  it("hides Bank receipts from an Adviser", async () => {
    const routes = [{ path: "/dashboard", element: <ShellLayout /> }];
    renderFees(adviser(), routes, "/dashboard");
    expect(await screen.findByRole("link", { name: /Accounts/ })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Bank receipts/ })).not.toBeInTheDocument();
  });
});

describe("StatementLinesListPage (deposit-matching D34)", () => {
  const routes = [{ path: "/statement-lines", element: <StatementLinesListPage /> }];

  beforeEach(() => stubBrowser("/statement-lines"));

  it("lists unmatched receipts by default with the columns and the reason label", async () => {
    const { requests } = renderFees(feeUser(), routes, "/statement-lines", (request) =>
      path(request) === "/statement-lines" ? page([unmatched]) : undefined,
    );

    const row = await screen.findByRole("row", { name: /J SMITH/ });
    for (const header of ["Value date", "Received", "Amount", "Currency", "Payer", "Reference", "Status", "Reason"]) {
      expect(screen.getByRole("columnheader", { name: header })).toBeInTheDocument();
    }
    expect(row).toHaveTextContent("2026-11-19");
    expect(row).toHaveTextContent("500.00");
    expect(row).toHaveTextContent("USD");
    expect(row).toHaveTextContent("Rent");
    expect(row).toHaveTextContent("Unmatched");
    expect(row).toHaveTextContent("No valid reference");
    expect(within(row).getByRole("link")).toHaveAttribute("href", `/statement-lines/${lineId}`);
    const listed = requestsTo(requests, "GET", "/statement-lines");
    expect(new URL(listed[0].url).searchParams.get("status")).toBe("Unmatched");
  });

  it.each([
    ["NoReference", "No valid reference"],
    ["UnknownReference", "Reference not found"],
    ["SeveralReferences", "More than one reference"],
    ["CurrencyMismatch", "Account in another currency"],
    ["AccountClosed", "Account closed"],
    ["CustomerDisabled", "Customer disabled"],
    ["AmountPrecision", "Amount has too many decimals"],
    ["AccountClosing", "Account being closed"],
  ])("labels the reason %s", async (reason, label) => {
    renderFees(feeUser(), routes, "/statement-lines", (request) =>
      path(request) === "/statement-lines" ? page([{ ...unmatched, unmatchedReason: reason }]) : undefined,
    );

    expect(await screen.findByRole("row", { name: /J SMITH/ })).toHaveTextContent(label);
  });

  it("shows To be returned for a ReturnRequested line", async () => {
    renderFees(feeUser(), routes, "/statement-lines", (request) =>
      path(request) === "/statement-lines" ? page([{ ...unmatched, status: "ReturnRequested" }]) : undefined,
    );

    expect(await screen.findByRole("row", { name: /J SMITH/ })).toHaveTextContent("To be returned");
  });

  it.each([
    ["Matched", "Matched"],
    ["Ignored", "Ignored"],
    ["To be returned", "ReturnRequested"],
    ["Return sent", "ReturnSent"],
    ["Returned", "Returned"],
    ["All", "Unmatched,Matched,Ignored,ReturnRequested,ReturnSent,Returned"],
  ])("filters by %s", async (option, status) => {
    const { requests } = renderFees(feeUser(), routes, "/statement-lines", (request) =>
      path(request) === "/statement-lines" ? page([]) : undefined,
    );

    await chooseOption("Status", option);

    await waitFor(() => {
      const statuses = requestsTo(requests, "GET", "/statement-lines").map((request) =>
        new URL(request.url).searchParams.get("status"),
      );
      expect(statuses).toContain(status);
    });
  });

  it("shows the firm administrator sentence on a 403", async () => {
    renderFees(adviser(), routes, "/statement-lines", (request) =>
      path(request) === "/statement-lines" ? new Response("", { status: 403 }) : undefined,
    );

    expect(await screen.findByText("Only a firm administrator can see bank receipts.")).toBeInTheDocument();
  });
});

describe("StatementLineDetailPage (deposit-matching D35, D37)", () => {
  const routes = [
    { path: "/statement-lines/:id", element: <StatementLineDetailPage /> },
    { path: "/statement-lines/:id/match", element: <p>match form</p> },
  ];
  const detailPath = `/statement-lines/${lineId}`;

  beforeEach(() => {
    stubBrowser(detailPath);
    vi.restoreAllMocks();
  });

  it("shows a matched line with the account, the customer and the deposit, and offers only Undo match", async () => {
    renderFees(feeUser(), routes, detailPath, serveLine(matched));

    expect(await screen.findByText("Matched by reference")).toBeInTheDocument();
    expect(await screen.findByRole("link", { name: "Managed portfolio" })).toHaveAttribute("href", `/accounts/${accountId}`);
    expect(await screen.findByText("Ned North")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Deposit" })).toHaveAttribute("href", `/transactions/${transactionId}`);
    expect(screen.getByText("K7M3Q9PXB")).toBeInTheDocument();
    expect(screen.getByText("****4200")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Undo match" })).toBeInTheDocument();
    for (const name of ["Match", "Ignore", "Return to payer", "Reopen"]) {
      expect(screen.queryByRole("button", { name })).not.toBeInTheDocument();
      expect(screen.queryByRole("link", { name })).not.toBeInTheDocument();
    }
  });

  it("says Trade settlement for a line matched to one, links it and offers no action (trade-settlement D78)", async () => {
    const settlementId = "55555555-0000-0000-0000-000000000001";
    renderFees(feeUser(), routes, detailPath, serveLine({
      ...matched,
      match: { accountId: null, customerId: null, transactionId: null, method: "Settlement", at: "2026-11-25T02:01:08Z" },
      tradeSettlementId: settlementId,
    }));

    expect(await screen.findByText("Trade settlement", { selector: "p" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Trade settlement" })).toHaveAttribute("href", `/trade-settlements/${settlementId}`);
    expect(screen.queryByRole("button", { name: "Undo match" })).not.toBeInTheDocument();
  });

  it("says Migration batch for a line that funds one, links it and offers Undo match (D83)", async () => {
    const migrationBatchId = "7b1ae0a4-0000-0000-0000-000000000001";
    renderFees(feeUser(), routes, detailPath, serveLine({
      ...matched,
      match: { accountId: null, customerId: null, transactionId: null, method: "Migration", at: "2026-11-25T02:01:08Z" },
      migrationBatchId,
    }));

    expect(await screen.findByText("Migration batch", { selector: "p" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Migration batch" })).toHaveAttribute("href", `/migration-batches/${migrationBatchId}`);
    expect(screen.getByRole("button", { name: "Undo match" })).toBeInTheDocument();
  });

  it("explains a receipt that does not match its trade settlement (trade-settlement D78)", async () => {
    renderFees(feeUser(), routes, detailPath, serveLine({ ...unmatched, unmatchedReason: "SettlementMismatch" }));

    expect(await screen.findByText("Does not match the trade settlement")).toBeInTheDocument();
    expect(screen.getByText("Return it to the broker, or ignore it with a note.")).toBeInTheDocument();
  });

  it("says Matched by hand for a manual match", async () => {
    renderFees(feeUser(), routes, detailPath, serveLine({ ...matched, match: { ...matched.match, method: "Manual" } }));

    expect(await screen.findByText("Matched by hand")).toBeInTheDocument();
  });

  it("offers Match, Ignore and Return to payer on an unmatched line, with its reason", async () => {
    const { router } = renderFees(feeUser(), routes, detailPath, serveLine(unmatched));

    expect(await screen.findByText("No valid reference")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Ignore" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Return to payer" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Undo match" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("link", { name: "Match" }));
    expect(await screen.findByText("match form")).toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/statement-lines/${lineId}/match`);
  });

  it.each(["Ignored", "ReturnRequested"])("offers only Reopen on a %s line, and shows the last action", async (status) => {
    renderFees(feeUser(), routes, detailPath, serveLine({
      ...unmatched,
      status,
      lastAction: { action: status, note: "Bank interest", by: { id: "u1", name: "Pat Admin" }, at: "2026-11-20T03:00:00Z" },
    }));

    expect(await screen.findByRole("button", { name: "Reopen" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Ignore" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Match" })).not.toBeInTheDocument();
    expect(screen.getByText("Bank interest")).toBeInTheDocument();
    expect(screen.getByText("Pat Admin")).toBeInTheDocument();
  });

  it("asks for a note to ignore, refuses an empty one, then posts it with the row version", async () => {
    const { requests } = renderFees(feeUser(), routes, detailPath, serveLine(unmatched, (request) =>
      request.method === "POST" ? new Response(null, { status: 204 }) : undefined,
    ));

    fireEvent.click(await screen.findByRole("button", { name: "Ignore" }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirm" }));
    expect(await within(dialog).findByText("Enter a note.")).toBeInTheDocument();
    expect(requestsTo(requests, "POST", `/statement-lines/${lineId}/ignore`)).toHaveLength(0);

    fireEvent.change(within(dialog).getByLabelText("Note"), { target: { value: "Bank interest" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirm" }));

    await waitFor(() => expect(requestsTo(requests, "POST", `/statement-lines/${lineId}/ignore`)).toHaveLength(1));
    expect(await body(requestsTo(requests, "POST", `/statement-lines/${lineId}/ignore`)[0])).toEqual({
      note: "Bank interest",
      rowVersion: "AAAA",
    });
  });

  it("posts Return to payer with its note", async () => {
    const { requests } = renderFees(feeUser(), routes, detailPath, serveLine(unmatched, (request) =>
      request.method === "POST" ? new Response(null, { status: 204 }) : undefined,
    ));

    fireEvent.click(await screen.findByRole("button", { name: "Return to payer" }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.change(within(dialog).getByLabelText("Note"), { target: { value: "Not our customer" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirm" }));

    await waitFor(() => expect(requestsTo(requests, "POST", `/statement-lines/${lineId}/request-return`)).toHaveLength(1));
  });

  it("warns that Undo match posts a reversal, and shows the R30 refusal as returned", async () => {
    const refusal = "The account no longer holds this cash, so the deposit cannot be undone. Correct it with an adjustment.";
    renderFees(feeUser(), routes, detailPath, serveLine(matched, (request) =>
      request.method === "POST" ? problem(400, { errors: { "": [refusal] } }) : undefined,
    ));

    fireEvent.click(await screen.findByRole("button", { name: "Undo match" }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("This posts a reversal of the deposit on Managed portfolio.")).toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText("Note"), { target: { value: "Wrong customer" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirm" }));

    expect(await screen.findByText(refusal)).toBeInTheDocument();
  });

  it("asks only for confirmation to reopen", async () => {
    vi.spyOn(window, "confirm").mockReturnValue(true);
    const { requests } = renderFees(feeUser(), routes, detailPath, serveLine({ ...unmatched, status: "Ignored" }, (request) =>
      request.method === "POST" ? new Response(null, { status: 204 }) : undefined,
    ));

    fireEvent.click(await screen.findByRole("button", { name: "Reopen" }));

    expect(window.confirm).toHaveBeenCalled();
    await waitFor(() => expect(requestsTo(requests, "POST", `/statement-lines/${lineId}/reopen`)).toHaveLength(1));
    expect(await body(requestsTo(requests, "POST", `/statement-lines/${lineId}/reopen`)[0])).toEqual({ rowVersion: "AAAA" });
  });
});

describe("MatchStatementLinePage (deposit-matching D36)", () => {
  const matchPath = `/statement-lines/${lineId}/match`;
  const routes = [
    { path: "/statement-lines/:id/match", element: <MatchStatementLinePage /> },
    { path: "/statement-lines/:id", element: <p>line detail</p> },
  ];

  beforeEach(() => stubBrowser(matchPath));

  function serveMatch(onPost: () => Response, accounts: object[] = [account]): Handler {
    return serveLine(unmatched, (request) => {
      const p = path(request);
      if (request.method === "POST" && p === `/statement-lines/${lineId}/match`) {
        return onPost();
      }
      if (request.method === "GET" && p === "/accounts") {
        return page(accounts);
      }
      if (request.method === "GET" && p === "/users/customers") {
        return page([customer]);
      }
      return undefined;
    });
  }

  it("finds the account by its deposit reference, reviews and posts the match", async () => {
    const { requests, router } = renderFees(feeUser(), routes, matchPath, serveMatch(() => new Response(null, { status: 204 })));

    fireEvent.change(await screen.findByLabelText("Deposit reference"), { target: { value: "k7m3q9pxb" } });
    fireEvent.click(screen.getByRole("button", { name: "Find" }));
    fireEvent.click(await screen.findByRole("button", { name: /Managed portfolio/ }));
    fireEvent.change(screen.getByLabelText("Note"), { target: { value: "Confirmed by phone" } });

    expect(await screen.findByText("Credit 500.00 USD to Managed portfolio of Ned North.")).toBeInTheDocument();
    const lookup = requestsTo(requests, "GET", "/accounts")[0];
    expect(new URL(lookup.url).searchParams.get("depositReference")).toBe("k7m3q9pxb");
    fireEvent.click(screen.getByRole("button", { name: "Confirm match" }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/statement-lines/${lineId}`));
    expect(await body(requestsTo(requests, "POST", `/statement-lines/${lineId}/match`)[0])).toEqual({
      accountId,
      rowVersion: "AAAA",
      note: "Confirmed by phone",
    });
  });

  it("matches the receipt to a Draft migration batch (D83)", async () => {
    const batchId = "7b1ae0a4-0000-0000-0000-000000000001";
    const draft = {
      id: batchId, name: "Acme transfer", previousProvider: "Acme Wealth", asOfDate: "2026-11-20", status: "Draft",
      entryCount: 2, holdingCount: 2, createdBy: null, createdAt: "2026-11-24T01:00:00Z", submittedBy: null, submittedAt: null,
      postedBy: null, postedAt: null,
    };
    const { requests, router } = renderFees(feeUser(), routes, matchPath, serveLine(unmatched, (request) => {
      const p = path(request);
      if (request.method === "POST" && p === `/statement-lines/${lineId}/match`) {
        return new Response(null, { status: 204 });
      }
      return request.method === "GET" && p === "/migration-batches" ? page([draft]) : undefined;
    }));

    fireEvent.click(await screen.findByRole("button", { name: "To a migration batch" }));
    fireEvent.click(await screen.findByRole("button", { name: /Acme transfer/ }));
    fireEvent.click(screen.getByRole("button", { name: "Confirm match" }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/statement-lines/${lineId}`));
    expect(await body(requestsTo(requests, "POST", `/statement-lines/${lineId}/match`)[0])).toEqual({
      migrationBatchId: batchId,
      rowVersion: "AAAA",
      note: null,
    });
    expect(new URL(requestsTo(requests, "GET", "/migration-batches")[0].url).searchParams.get("status")).toBe("Draft");
  });

  it("finds the Customer's Open Custodial accounts in the line's currency", async () => {
    const other = { ...account, id: "aaaaaaaa-0000-0000-0000-000000000002", name: "Kiwi", currency: "NZD" };
    const closed = { ...account, id: "aaaaaaaa-0000-0000-0000-000000000003", name: "Old", status: "Closed" };
    const { requests } = renderFees(
      feeUser(), routes, matchPath, serveMatch(() => new Response(null, { status: 204 }), [account, other, closed]),
    );

    fireEvent.click(await screen.findByRole("button", { name: "By customer" }));
    fireEvent.change(screen.getByLabelText("Customer"), { target: { value: "Ned" } });
    fireEvent.click(screen.getByRole("button", { name: "Search" }));
    fireEvent.click(await screen.findByRole("button", { name: /Ned North/ }));

    expect(await screen.findByRole("button", { name: /Managed portfolio/ })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Kiwi/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Old/ })).not.toBeInTheDocument();
    const lookup = requestsTo(requests, "GET", "/accounts").map((request) => new URL(request.url).searchParams);
    expect(lookup.some((params) => params.get("customerId") === customerId && params.get("custodyMode") === "Custodial")).toBe(true);
  });

  it("shows a 400 at its field", async () => {
    renderFees(feeUser(), routes, matchPath, serveMatch(() =>
      problem(400, { errors: { AccountId: ["A receipt can be matched only to a Custodial account."] } }),
    ));

    fireEvent.change(await screen.findByLabelText("Deposit reference"), { target: { value: "K7M3Q9PXB" } });
    fireEvent.click(screen.getByRole("button", { name: "Find" }));
    fireEvent.click(await screen.findByRole("button", { name: /Managed portfolio/ }));
    fireEvent.click(await screen.findByRole("button", { name: "Confirm match" }));

    expect(await screen.findByText("A receipt can be matched only to a Custodial account.")).toBeInTheDocument();
  });

  it("reloads the line and says it changed on a 409", async () => {
    const { requests } = renderFees(feeUser(), routes, matchPath, serveMatch(() => problem(409, { code: "stale_row_version" })));

    fireEvent.change(await screen.findByLabelText("Deposit reference"), { target: { value: "K7M3Q9PXB" } });
    fireEvent.click(screen.getByRole("button", { name: "Find" }));
    fireEvent.click(await screen.findByRole("button", { name: /Managed portfolio/ }));
    fireEvent.click(await screen.findByRole("button", { name: "Confirm match" }));

    expect(await screen.findByText("This receipt changed. Check it and try again.")).toBeInTheDocument();
    await waitFor(() => expect(requestsTo(requests, "GET", `/statement-lines/${lineId}`).length).toBeGreaterThan(1));
  });

  it("says when no account has the reference", async () => {
    renderFees(feeUser(), routes, matchPath, serveMatch(() => new Response(null, { status: 204 }), []));

    fireEvent.change(await screen.findByLabelText("Deposit reference"), { target: { value: "H2N4R6T80" } });
    fireEvent.click(screen.getByRole("button", { name: "Find" }));

    expect(await screen.findByText("No account has this deposit reference.")).toBeInTheDocument();
  });
});

describe("Bank receipts payments out (withdrawals D49)", () => {
  const withdrawalId = "77777777-0000-0000-0000-000000000001";
  const payoutLine = {
    ...matched,
    direction: "Debit",
    reference: "K7M3Q9PXB",
    match: { accountId, customerId, transactionId, method: "Payment", at: "2026-11-20T04:07:02Z" },
    relatedLineId: null,
    withdrawalId,
    returnReason: null,
  };

  it("filters receipts by default and offers payments out and all", async () => {
    stubBrowser("/statement-lines");
    const { requests } = renderFees(feeUser(), [{ path: "/statement-lines", element: <StatementLinesListPage /> }],
      "/statement-lines", (request) => (path(request) === "/statement-lines" ? page([unmatched]) : undefined));

    await screen.findByRole("row", { name: /J SMITH/ });
    expect(new URL(requestsTo(requests, "GET", "/statement-lines")[0].url).searchParams.get("direction")).toBe("Credit");
    await chooseOption("Direction", "Payments out");
    await waitFor(() => expect(requestsTo(requests, "GET", "/statement-lines").some((request) =>
      new URL(request.url).searchParams.get("direction") === "Debit")).toBe(true));
    await chooseOption("Direction", "All");
    await waitFor(() => expect(requestsTo(requests, "GET", "/statement-lines").some((request) =>
      !new URL(request.url).searchParams.has("direction"))).toBe(true));
  });

  it("labels a payment mismatch", async () => {
    stubBrowser("/statement-lines");
    renderFees(feeUser(), [{ path: "/statement-lines", element: <StatementLinesListPage /> }], "/statement-lines", (request) =>
      path(request) === "/statement-lines" ? page([{ ...unmatched, direction: "Debit", unmatchedReason: "PaymentMismatch" }]) : undefined);

    expect(await screen.findByText("Amount differs from the payment")).toBeInTheDocument();
  });

  it("shows a payout line with the method label, a withdrawal link and no action", async () => {
    stubBrowser(`/statement-lines/${lineId}`);
    renderFees(feeUser(), [{ path: "/statement-lines/:id", element: <StatementLineDetailPage /> }], `/statement-lines/${lineId}`,
      serveLine(payoutLine));

    expect(await screen.findByText("Paid by the platform")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Withdrawal" })).toHaveAttribute("href", `/withdrawals/${withdrawalId}`);
    for (const name of ["Match", "Ignore", "Return to payer", "Reopen", "Undo match"]) {
      expect(screen.queryByRole("button", { name })).not.toBeInTheDocument();
    }
  });

  it("shows a returned payout with its method label and a link to the line it returns", async () => {
    stubBrowser(`/statement-lines/${lineId}`);
    const returnedId = "3f2c0000-0000-0000-0000-0000000000e2";
    renderFees(feeUser(), [{ path: "/statement-lines/:id", element: <StatementLineDetailPage /> }], `/statement-lines/${lineId}`,
      serveLine({
        ...payoutLine,
        direction: "Credit",
        match: { ...payoutLine.match, method: "PaymentReturn" },
        relatedLineId: returnedId,
        returnReason: "Account closed",
      }));

    expect(await screen.findByText("Payment returned")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Returned line" })).toHaveAttribute("href", `/statement-lines/${returnedId}`);
    expect(screen.getByText("Account closed")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Undo match" })).not.toBeInTheDocument();
  });
});

describe("Receipt returns, recalls and debit lines (bank-returns D50)", () => {
  const detailPath = `/statement-lines/${lineId}`;
  const detailRoutes = [{ path: "/statement-lines/:id", element: <StatementLineDetailPage /> }];
  const receiptId = "3f2c0000-0000-0000-0000-0000000000e3";
  const debit = { ...unmatched, direction: "Debit", unmatchedReason: "UnknownPayment", returnPayment: null };

  it.each([
    ["ReturnSent", "Return sent"],
    ["Returned", "Returned"],
  ])("labels the status %s", async (status, label) => {
    stubBrowser("/statement-lines");
    renderFees(feeUser(), [{ path: "/statement-lines", element: <StatementLinesListPage /> }], "/statement-lines", (request) =>
      path(request) === "/statement-lines" ? page([{ ...unmatched, status, unmatchedReason: null }]) : undefined);

    expect(await screen.findByRole("row", { name: /J SMITH/ })).toHaveTextContent(label);
  });

  it.each([
    ["UnknownPayment", "Payment out not recognised"],
    ["UnknownReturn", "Return not recognised"],
    ["RecallNotCovered", "Recalled, but the cash is gone"],
    ["ReturnRejected", "Bank refused the return"],
  ])("labels the reason %s", async (reason, label) => {
    stubBrowser("/statement-lines");
    renderFees(feeUser(), [{ path: "/statement-lines", element: <StatementLinesListPage /> }], "/statement-lines", (request) =>
      path(request) === "/statement-lines" ? page([{ ...unmatched, unmatchedReason: reason }]) : undefined);

    expect(await screen.findByRole("row", { name: /J SMITH/ })).toHaveTextContent(label);
  });

  it("offers only Ignore on an unmatched debit line, and ignores it with a note", async () => {
    stubBrowser(detailPath);
    const { requests } = renderFees(feeUser(), detailRoutes, detailPath, serveLine(debit, (request) =>
      request.method === "POST" ? new Response(null, { status: 204 }) : undefined));

    const ignore = await screen.findByRole("button", { name: "Ignore" });
    for (const name of ["Return to payer", "Reopen", "Undo match"]) {
      expect(screen.queryByRole("button", { name })).not.toBeInTheDocument();
    }
    expect(screen.queryByRole("link", { name: "Match" })).not.toBeInTheDocument();
    fireEvent.click(ignore);
    const dialog = await screen.findByRole("dialog");
    fireEvent.change(within(dialog).getByLabelText("Note"), { target: { value: "Monthly bank fee" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirm" }));

    await waitFor(() => expect(requestsTo(requests, "POST", `/statement-lines/${lineId}/ignore`)).toHaveLength(1));
    expect(await body(requestsTo(requests, "POST", `/statement-lines/${lineId}/ignore`)[0])).toEqual({
      note: "Monthly bank fee",
      rowVersion: "AAAA",
    });
    expect(screen.getByText("Payment out not recognised")).toBeInTheDocument();
  });

  it("offers Reopen on an ignored debit line", async () => {
    stubBrowser(detailPath);
    vi.spyOn(window, "confirm").mockReturnValue(true);
    const { requests } = renderFees(feeUser(), detailRoutes, detailPath, serveLine({ ...debit, status: "Ignored" }, (request) =>
      request.method === "POST" ? new Response(null, { status: 204 }) : undefined));

    fireEvent.click(await screen.findByRole("button", { name: "Reopen" }));

    await waitFor(() => expect(requestsTo(requests, "POST", `/statement-lines/${lineId}/reopen`)).toHaveLength(1));
  });

  it("shows a return waiting to be sent under the status, and offers Reopen while it has no attempt", async () => {
    stubBrowser(detailPath);
    renderFees(feeUser(), detailRoutes, detailPath, serveLine({
      ...unmatched,
      status: "ReturnRequested",
      returnPayment: { status: "Pending", attempts: 0, lastFailure: null },
    }));

    expect(await screen.findByText("Waiting to send")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reopen" })).toBeInTheDocument();
  });

  it("shows a failed send with its label, and offers no Reopen once the bank was asked", async () => {
    stubBrowser(detailPath);
    renderFees(feeUser(), detailRoutes, detailPath, serveLine({
      ...unmatched,
      status: "ReturnRequested",
      returnPayment: { status: "Pending", attempts: 2, lastFailure: "Unavailable" },
    }));

    expect(await screen.findByText("Sending failed: Bank unavailable")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reopen" })).not.toBeInTheDocument();
  });

  it.each([
    ["ReturnSent", "Return sent"],
    ["Returned", "Returned"],
  ])("offers no action on a %s line", async (status, label) => {
    stubBrowser(detailPath);
    renderFees(feeUser(), detailRoutes, detailPath, serveLine({
      ...unmatched,
      status,
      unmatchedReason: null,
      returnPayment: { status: "Accepted", attempts: 1, lastFailure: null },
    }));

    expect(await screen.findByText(label)).toBeInTheDocument();
    for (const name of ["Ignore", "Return to payer", "Reopen", "Undo match"]) {
      expect(screen.queryByRole("button", { name })).not.toBeInTheDocument();
    }
  });

  it("shows a recall with the Reversal and offers no action", async () => {
    stubBrowser(detailPath);
    renderFees(feeUser(), detailRoutes, detailPath, serveLine({
      ...matched,
      direction: "Debit",
      match: { accountId, customerId, transactionId, method: "Recall", at: "2026-11-21T02:01:08Z" },
      relatedLineId: receiptId,
      returnReason: "Payer's bank recalled the payment",
      returnPayment: null,
    }));

    expect(await screen.findByText("Recalled by the payer's bank")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Reversal" })).toHaveAttribute("href", `/transactions/${transactionId}`);
    expect(screen.getByRole("link", { name: "Returned line" })).toHaveAttribute("href", `/statement-lines/${receiptId}`);
    for (const name of ["Ignore", "Reopen", "Undo match"]) {
      expect(screen.queryByRole("button", { name })).not.toBeInTheDocument();
    }
  });

  it("shows a match with no transaction without account or transaction links", async () => {
    stubBrowser(detailPath);
    renderFees(feeUser(), detailRoutes, detailPath, serveLine({
      ...matched,
      direction: "Debit",
      match: { accountId: null, customerId: null, transactionId: null, method: "Payment", at: "2026-11-21T02:01:08Z" },
      relatedLineId: receiptId,
      returnPayment: null,
    }));

    expect(await screen.findByText("Paid by the platform")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Returned line" })).toHaveAttribute("href", `/statement-lines/${receiptId}`);
    expect(screen.queryByText("Account")).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Payout" })).not.toBeInTheDocument();
  });

  it("tells the administrator to adjust the account on a recall the cash no longer covers", async () => {
    stubBrowser(detailPath);
    const receipt = { ...matched, id: receiptId };
    renderFees(feeUser(), detailRoutes, detailPath, serveLine(
      { ...debit, unmatchedReason: "RecallNotCovered", relatedLineId: receiptId },
      (request) => (request.method === "GET" && path(request) === `/statement-lines/${receiptId}` ? json(receipt) : undefined),
    ));

    expect(await screen.findByText("Correct the account with an adjustment, then ignore this line.")).toBeInTheDocument();
    expect(await screen.findByRole("link", { name: "Managed portfolio" })).toHaveAttribute("href", `/accounts/${accountId}`);
    expect(screen.getByRole("button", { name: "Ignore" })).toBeInTheDocument();
  });
});
