import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MigrationBatchDetailPage } from "@/features/migrationBatches/MigrationBatchDetailPage";
import { MigrationBatchEditPage } from "@/features/migrationBatches/MigrationBatchEditPage";
import { MigrationBatchesListPage } from "@/features/migrationBatches/MigrationBatchesListPage";
import { ShellLayout } from "@/layouts/ShellLayout";
import { adviser, feeUser, json, problem, renderFees, requestsTo, stubBrowser } from "@/test/fees";

const batchId = "7b1ae0a4-0000-0000-0000-000000000001";
const smith = "aaaaaaaa-0000-0000-0000-000000000001";
const jones = "aaaaaaaa-0000-0000-0000-000000000002";
const vti = "99999999-0000-0000-0000-000000000001";
const lineId = "3f2c0000-0000-0000-0000-0000000000e1";
const me = feeUser();
const tia = { id: "11111111-1111-1111-1111-000000000002", name: "Tia Admin" };

function batch(overrides: Record<string, unknown> = {}) {
  return {
    id: batchId,
    name: "Acme Wealth transfer, November 2026",
    previousProvider: "Acme Wealth",
    asOfDate: "2026-11-20",
    status: "Draft",
    entryCount: 2,
    holdingCount: 2,
    createdBy: { id: me.id, name: me.name },
    createdAt: "2026-11-24T01:00:00Z",
    submittedBy: null,
    submittedAt: null,
    postedBy: null,
    postedAt: null,
    controlTotals: { accountCount: 2, cash: [{ currency: "USD", amount: 250000 }], quantity: [{ instrumentId: vti, quantity: 1200 }], cost: [{ currency: "USD", amount: 264000 }] },
    computedTotals: { accountCount: 2, cash: [{ currency: "USD", amount: 250000 }], quantity: [{ instrumentId: vti, quantity: 1200 }], cost: [{ currency: "USD", amount: 264000 }] },
    fundingTotals: [{ currency: "USD", amount: 0 }],
    funding: [],
    entries: [
      {
        account: { id: smith, name: "J Smith brokerage", depositReference: "K7M2Q9X4B", currency: "USD", customer: { id: "c1", name: "J Smith" } },
        externalReference: "ACME-004417",
        cash: 150000,
        holdings: [{ instrument: { id: vti, symbol: "VTI", name: "Vanguard Total Stock Market ETF", quoteCurrency: "USD" }, quantity: 800, cost: 176000 }],
        openingTransactionId: null,
        openingHoldingsTransactionId: null,
      },
      {
        account: { id: jones, name: "K Jones brokerage", depositReference: "P3H8W2N6C", currency: "USD", customer: { id: "c2", name: "K Jones" } },
        externalReference: "ACME-004592",
        cash: 100000,
        holdings: [{ instrument: { id: vti, symbol: "VTI", name: "Vanguard Total Stock Market ETF", quoteCurrency: "USD" }, quantity: 400, cost: 88000 }],
        openingTransactionId: null,
        openingHoldingsTransactionId: null,
      },
    ],
    validation: {
      isValid: false,
      issues: [{ code: "FundingMismatch", accountId: null, instrumentId: null, currency: "USD", total: null, message: "Bank receipts for this batch add up to 0.00 USD; its cash is 250,000.00 USD." }],
    },
    returned: null,
    cancelled: null,
    rowVersion: "AAAB",
    ...overrides,
  };
}

const valid = { isValid: true, issues: [] };
const funded = {
  fundingTotals: [{ currency: "USD", amount: 250000 }],
  funding: [{ statementLineId: lineId, currency: "USD", amount: 250000, valueDate: "2026-11-23", payerName: "Acme Wealth Client Trust", reference: "ACME TRANSFER NOV" }],
  validation: valid,
};

const page = (items: unknown[]) => json({ items, page: 1, pageSize: 20, totalCount: items.length });

async function body(request: Request) {
  return JSON.parse(await request.clone().text());
}

describe("Migration batches navigation (D80)", () => {
  beforeEach(() => stubBrowser("/dashboard"));

  it("shows Migration batches to a TenantAdmin and not to an Adviser", async () => {
    renderFees(me, [{ path: "/dashboard", element: <ShellLayout /> }], "/dashboard");
    expect(await screen.findByRole("link", { name: /^Migration batches/ })).toHaveAttribute("href", "/migration-batches");
  });

  it("hides Migration batches from an Adviser", async () => {
    renderFees(adviser(), [{ path: "/dashboard", element: <ShellLayout /> }], "/dashboard");
    expect(await screen.findByRole("link", { name: /^Orders/ })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /^Migration batches/ })).not.toBeInTheDocument();
  });
});

describe("MigrationBatchesListPage (D80)", () => {
  const routes = [{ path: "/migration-batches", element: <MigrationBatchesListPage /> }];

  beforeEach(() => stubBrowser("/migration-batches"));

  it("lists the Draft and Submitted batches by default, with the columns and a New batch link", async () => {
    const { requests } = renderFees(me, routes, "/migration-batches", (request) =>
      new URL(request.url).pathname === "/api/migration-batches" ? page([batch({ status: "Submitted", submittedBy: tia })]) : undefined);

    expect(await screen.findByRole("link", { name: "Acme Wealth transfer, November 2026" })).toHaveAttribute("href", `/migration-batches/${batchId}`);
    expect(new URL(requestsTo(requests, "GET", "/migration-batches")[0].url).searchParams.get("status")).toBe("Draft,Submitted");
    const table = screen.getByRole("table", { name: "Migration batches" });
    expect(within(table).getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual([
      "Name", "Previous provider", "As at", "Status", "Accounts", "Holdings", "Created", "Submitted by", "Posted",
    ]);
    const row = within(table).getAllByRole("row")[1];
    expect(within(row).getByText("Waiting to be posted")).toBeInTheDocument();
    expect(within(row).getByText("Tia Admin")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "New batch" })).toHaveAttribute("href", "/migration-batches/new");
  });

  it("shows the firm administrator sentence on a 403", async () => {
    renderFees(me, routes, "/migration-batches", (request) =>
      new URL(request.url).pathname === "/api/migration-batches" ? problem(403, { title: "Forbidden" }) : undefined);

    expect(await screen.findByText("Only a firm administrator can see migration batches.")).toBeInTheDocument();
  });
});

describe("MigrationBatchEditPage (D81)", () => {
  const routes = [
    { path: "/migration-batches/new", element: <MigrationBatchEditPage /> },
    { path: "/migration-batches/:id", element: <p>Batch page</p> },
  ];

  beforeEach(() => stubBrowser("/migration-batches/new"));

  function serve(request: Request) {
    const url = new URL(request.url);
    if (url.pathname === "/api/instruments") {
      return page([{ id: vti, tenantId: "t", symbol: "VTI", name: "Vanguard", quoteCurrency: "USD", isActive: true }]);
    }
    if (url.pathname === "/api/accounts") {
      const reference = url.searchParams.get("depositReference");
      const accounts: Record<string, unknown> = {
        K7M2Q9X4B: { id: smith, name: "J Smith brokerage", depositReference: "K7M2Q9X4B", currency: "USD", custodyMode: "Custodial", customerId: "c1" },
        P3H8W2N6C: { id: jones, name: "K Jones brokerage", depositReference: "P3H8W2N6C", currency: "USD", custodyMode: "Custodial", customerId: "c2" },
      };
      return page(reference && accounts[reference] ? [accounts[reference]] : []);
    }
    if (url.pathname === "/api/migration-batches" && request.method === "POST") {
      return json({ id: batchId }, 201);
    }
    return undefined;
  }

  async function importCsv(text: string) {
    const input = screen.getByLabelText("Import CSV");
    fireEvent.change(input, { target: { files: [new File([text], "batch.csv", { type: "text/csv" })] } });
  }

  it("merges the rows of one account, shows the rows it cannot place, and never fills in the control totals", async () => {
    renderFees(me, routes, "/migration-batches/new", serve);
    await screen.findByLabelText("Import CSV");

    await importCsv([
      "account,externalReference,cash,symbol,quantity,cost",
      "K7M2Q9X4B,ACME-004417,150000,,,",
      "K7M2Q9X4B,ACME-004417,,VTI,800,176000",
      "ZZZZZZZZZ,ACME-9,5,,,",
      "P3H8W2N6C,ACME-004592,100000,ABC,1,1",
    ].join("\n"));

    expect(await screen.findByText("Line 4: no Custodial account has the deposit reference ZZZZZZZZZ.")).toBeInTheDocument();
    expect(screen.getByText("Line 5: no instrument has the symbol ABC.")).toBeInTheDocument();
    const entries = screen.getByRole("table", { name: "Entries" });
    const rows = within(entries).getAllByRole("row").slice(1);
    expect(rows).toHaveLength(1);
    expect(within(rows[0]).getByText("J Smith brokerage")).toBeInTheDocument();
    expect(within(rows[0]).getByText("150,000.00")).toBeInTheDocument();
    expect(within(rows[0]).getByText("800 VTI")).toBeInTheDocument();
    expect(screen.getByLabelText("Number of accounts")).toHaveValue(null);
  });

  it("saves the whole body with the typed control totals", async () => {
    const { requests, router } = renderFees(me, routes, "/migration-batches/new", serve);
    await screen.findByLabelText("Import CSV");
    fireEvent.change(screen.getByLabelText("Name"), { target: { value: "Acme transfer" } });
    fireEvent.change(screen.getByLabelText("Previous provider"), { target: { value: "Acme Wealth" } });
    fireEvent.change(screen.getByLabelText("As at"), { target: { value: "2026-11-20" } });
    fireEvent.change(screen.getByLabelText("Number of accounts"), { target: { value: "1" } });
    fireEvent.click(screen.getByRole("button", { name: "Add cash total" }));
    fireEvent.change(screen.getByLabelText("Cash currency 1"), { target: { value: "USD" } });
    fireEvent.change(screen.getByLabelText("Cash amount 1"), { target: { value: "150000" } });
    await importCsv("account,externalReference,cash,symbol,quantity,cost\nK7M2Q9X4B,ACME-004417,150000,,,");
    await screen.findByText("J Smith brokerage");

    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(requestsTo(requests, "POST", "/migration-batches")).toHaveLength(1));
    expect(await body(requestsTo(requests, "POST", "/migration-batches")[0])).toEqual({
      name: "Acme transfer",
      previousProvider: "Acme Wealth",
      asOfDate: "2026-11-20",
      controlTotals: { accountCount: 1, cash: [{ currency: "USD", amount: 150000 }], quantity: [], cost: [] },
      entries: [{ accountId: smith, externalReference: "ACME-004417", cash: 150000, holdings: [] }],
    });
    await waitFor(() => expect(router.state.location.pathname).toBe(`/migration-batches/${batchId}`));
  });
});

describe("MigrationBatchDetailPage (D82, D83)", () => {
  const routes = [{ path: "/migration-batches/:id", element: <MigrationBatchDetailPage /> }];
  const detailPath = `/migration-batches/${batchId}`;

  beforeEach(() => stubBrowser(detailPath));
  afterEach(() => vi.unstubAllGlobals());

  function serve(item: () => unknown, extra?: (request: Request) => Response | undefined) {
    return (request: Request) => {
      const answer = extra?.(request);
      if (answer) {
        return answer;
      }
      if (request.method === "POST") {
        return new Response(null, { status: 204 });
      }
      return new URL(request.url).pathname === `/api/migration-batches/${batchId}` ? json(item()) : undefined;
    };
  }

  it("shows the totals with the bank column and a mark where they differ, the issues and the entries", async () => {
    renderFees(me, routes, detailPath, serve(() => batch()));

    const totals = await screen.findByRole("table", { name: "Totals" });
    expect(within(totals).getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual([
      "", "Control", "From the entries", "From the bank", "",
    ]);
    const cash = within(totals).getByRole("row", { name: /Cash USD/ });
    expect(within(cash).getByText("Differs")).toBeInTheDocument();
    expect(screen.getByText("Bank receipts for this batch add up to 0.00 USD; its cash is 250,000.00 USD.")).toBeInTheDocument();
    const entries = screen.getByRole("table", { name: "Entries" });
    expect(within(entries).getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual([
      "Account", "Customer", "Deposit reference", "Previous reference", "Cash", "Holdings",
    ]);
    expect(within(entries).getByRole("link", { name: "J Smith brokerage" })).toHaveAttribute("href", `/accounts/${smith}`);
    expect(screen.getByText("No bank receipt yet.")).toBeInTheDocument();
  });

  it("disables Submit while the batch has issues, and offers Edit and Cancel on a Draft", async () => {
    renderFees(me, routes, detailPath, serve(() => batch()));

    expect(await screen.findByRole("button", { name: "Submit" })).toBeDisabled();
    expect(screen.getByText("Fix 1 issues first.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Edit" })).toHaveAttribute("href", `/migration-batches/${batchId}/edit`);
    expect(screen.getByRole("button", { name: "Cancel batch" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Post" })).not.toBeInTheDocument();
  });

  it("submits a valid Draft with the row version", async () => {
    const { requests } = renderFees(me, routes, detailPath, serve(() => batch(funded)));

    fireEvent.click(await screen.findByRole("button", { name: "Submit" }));

    await waitFor(() => expect(requestsTo(requests, "POST", `/migration-batches/${batchId}/submit`)).toHaveLength(1));
    expect(await body(requestsTo(requests, "POST", `/migration-batches/${batchId}/submit`)[0])).toEqual({ rowVersion: "AAAB" });
  });

  it("disables Post for the person who submitted the batch", async () => {
    renderFees(me, routes, detailPath, serve(() => batch({ ...funded, status: "Submitted", submittedBy: { id: me.id, name: me.name } })));

    expect(await screen.findByRole("button", { name: "Post" })).toBeDisabled();
    expect(screen.getByText("Someone else must post this batch.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Return" })).toBeInTheDocument();
  });

  it("asks before it posts, with the accounts, the cash and the holdings", async () => {
    const confirm = vi.fn(() => true);
    vi.stubGlobal("confirm", confirm);
    const { requests } = renderFees(me, routes, detailPath, serve(() => batch({ ...funded, status: "Submitted", submittedBy: tia })));

    fireEvent.click(await screen.findByRole("button", { name: "Post" }));

    expect(confirm).toHaveBeenCalledWith("Post 2 accounts: 250,000.00 USD and 2 holdings. This cannot be undone.");
    await waitFor(() => expect(requestsTo(requests, "POST", `/migration-batches/${batchId}/post`)).toHaveLength(1));
  });

  it("returns a submitted batch with a note", async () => {
    const { requests } = renderFees(me, routes, detailPath, serve(() => batch({ ...funded, status: "Submitted", submittedBy: tia })));

    fireEvent.click(await screen.findByRole("button", { name: "Return" }));
    fireEvent.change(await screen.findByLabelText("Note"), { target: { value: "Wrong cost for VTI" } });
    fireEvent.click(screen.getByRole("button", { name: "Confirm" }));

    await waitFor(() => expect(requestsTo(requests, "POST", `/migration-batches/${batchId}/return`)).toHaveLength(1));
    expect(await body(requestsTo(requests, "POST", `/migration-batches/${batchId}/return`)[0])).toEqual({ rowVersion: "AAAB", note: "Wrong cost for VTI" });
  });

  it("reloads the batch and shows the answer when the server finds it not valid", async () => {
    let reads = 0;
    renderFees(me, routes, detailPath, serve(() => { reads += 1; return batch(funded); }, (request) =>
      request.method === "POST" ? problem(400, { code: "migration_batch_not_valid", title: "Migration batch not valid", detail: "The migration batch has 1 issues." }) : undefined));

    fireEvent.click(await screen.findByRole("button", { name: "Submit" }));

    expect(await screen.findByText("The migration batch has 1 issues.")).toBeInTheDocument();
    await waitFor(() => expect(reads).toBeGreaterThan(1));
  });

  it("lists the bank receipts of the batch, and removes one with a note", async () => {
    const { requests } = renderFees(me, routes, detailPath, serve(() => batch(funded), (request) =>
      new URL(request.url).pathname === `/api/statement-lines/${lineId}` && request.method === "GET"
        ? json({ id: lineId, rowVersion: "LINE", status: "Matched" })
        : undefined));

    const receipts = await screen.findByRole("table", { name: "Bank receipts for this batch" });
    expect(within(receipts).getByText("ACME TRANSFER NOV")).toBeInTheDocument();
    fireEvent.click(within(receipts).getByRole("button", { name: "Remove" }));
    fireEvent.change(await screen.findByLabelText("Note"), { target: { value: "Wrong batch" } });
    fireEvent.click(screen.getByRole("button", { name: "Confirm" }));

    await waitFor(() => expect(requestsTo(requests, "POST", `/statement-lines/${lineId}/unmatch`)).toHaveLength(1));
    expect(await body(requestsTo(requests, "POST", `/statement-lines/${lineId}/unmatch`)[0])).toEqual({ rowVersion: "LINE", note: "Wrong batch" });
  });

  it("adds an unmatched receipt to a Draft", async () => {
    const receipt = { id: lineId, currency: "USD", direction: "Credit", amount: 250000, valueDate: "2026-11-23", payerName: "Acme Wealth Client Trust", reference: "ACME TRANSFER NOV", status: "Unmatched", rowVersion: "LINE" };
    const { requests } = renderFees(me, routes, detailPath, serve(() => batch(), (request) =>
      new URL(request.url).pathname === "/api/statement-lines" ? page([receipt]) : undefined));

    fireEvent.click(await screen.findByRole("button", { name: "Add receipt" }));
    const list = await screen.findByRole("table", { name: "Unmatched receipts" });
    fireEvent.click(within(list).getByRole("button", { name: "Add" }));

    await waitFor(() => expect(requestsTo(requests, "POST", `/statement-lines/${lineId}/match`)).toHaveLength(1));
    expect(await body(requestsTo(requests, "POST", `/statement-lines/${lineId}/match`)[0])).toEqual({ migrationBatchId: batchId, rowVersion: "LINE", note: null });
    const listed = new URL(requestsTo(requests, "GET", "/statement-lines")[0].url).searchParams;
    expect([listed.get("status"), listed.get("direction")]).toEqual(["Unmatched", "Credit"]);
  });

  it("links the posted entries to their transactions and offers no action", async () => {
    const posted = batch({
      ...funded,
      status: "Posted",
      validation: null,
      submittedBy: tia,
      postedBy: { id: me.id, name: me.name },
      entries: batch().entries.map((entry, index) => ({ ...entry, openingTransactionId: `t-${index}`, openingHoldingsTransactionId: `h-${index}` })),
    });
    renderFees(me, routes, detailPath, serve(() => posted));

    const entries = await screen.findByRole("table", { name: "Entries" });
    expect(within(entries).getAllByRole("link", { name: "Opening balance" })[0]).toHaveAttribute("href", "/transactions/t-0");
    expect(within(entries).getAllByRole("link", { name: "Holdings transferred in" })[0]).toHaveAttribute("href", "/transactions/h-0");
    expect(screen.queryByRole("button", { name: "Submit" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Post" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Remove" })).not.toBeInTheDocument();
  });
});
