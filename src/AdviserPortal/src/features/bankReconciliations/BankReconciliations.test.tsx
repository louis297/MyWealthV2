import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { BankReconciliationDetailPage } from "@/features/bankReconciliations/BankReconciliationDetailPage";
import { BankReconciliationHistoryPage } from "@/features/bankReconciliations/BankReconciliationHistoryPage";
import {
  BankReconciliationOverviewPage,
  forbiddenSentence,
} from "@/features/bankReconciliations/BankReconciliationOverviewPage";
import { ReportsPage } from "@/features/reports/ReportsPage";
import { ShellLayout } from "@/layouts/ShellLayout";
import { chooseOption } from "@/test/chooseOption";
import { adviser, feeUser, json, renderFees, requestsTo, stubBrowser, type Handler } from "@/test/fees";

const usdId = "bbbbbbbb-0000-0000-0000-000000000001";
const nzdId = "bbbbbbbb-0000-0000-0000-000000000002";
const oldId = "bbbbbbbb-0000-0000-0000-000000000003";
const runId = "3f2c0000-0000-0000-0000-0000000000f1";
const lineId = "3f2c0000-0000-0000-0000-0000000000e1";
const bankReportId = "3e7b9d14-6c2a-4f58-b0e1-9a4d2c7f8e33";

const usd = {
  id: usdId,
  currency: "USD",
  bankName: "Simulator Bank",
  accountName: "Demo Firm Client Funds Trust Account",
  accountNumber: "SIM-DEMO-USD",
  isActive: true,
};

const totals = (breaks: number, breakAmount: number) => ({
  breaks: { count: breaks, amount: breakAmount },
  timing: { count: 0, amount: 0 },
  known: { count: 1, amount: 1175.2 },
});

const listItem = {
  id: runId,
  clientMoneyAccount: usd,
  trigger: "Scheduled",
  ranAt: "2026-11-22T17:00:03Z",
  ranBy: null,
  status: "Breaks",
  bankBalance: 12512.4,
  balanceAt: "2026-11-22T16:59:07Z",
  openingBalance: 0,
  statementLinesTotal: 12512.4,
  custodialCash: 10824.8,
  difference: 1687.6,
  totals: totals(2, 512.4),
  reservedCash: 300,
};

const run = {
  ...listItem,
  paymentsInFlight: { count: 1, amount: 300 },
  feed: { lastSucceededAt: "2026-11-22T16:59:07Z", lastFailedAt: null, lastFailure: null },
  items: [
    { kind: "Unexplained", class: "Break", amount: 12.4, count: 1, statementLine: null, clientMoneyAccountId: null },
    {
      kind: "Suspense",
      class: "Break",
      amount: 500,
      count: 1,
      statementLine: {
        id: lineId,
        direction: "Credit",
        valueDate: "2026-11-22",
        status: "Unmatched",
        unmatchedReason: "UnknownReference",
        note: null,
      },
      clientMoneyAccountId: null,
    },
    { kind: "Trades", class: "Known", amount: 1175.2, count: 4, statementLine: null, clientMoneyAccountId: null },
  ],
};

const overview = [
  {
    clientMoneyAccount: usd,
    bankFeed: { lastSucceededAt: "2026-11-22T16:59:07Z", lastFailedAt: "2026-11-22T17:30:00Z", lastFailure: "Unavailable" },
    latest: listItem,
  },
  {
    clientMoneyAccount: { ...usd, id: nzdId, currency: "NZD", accountNumber: "SIM-DEMO-NZD" },
    bankFeed: null,
    latest: null,
  },
  {
    clientMoneyAccount: { ...usd, id: oldId, accountNumber: "SIM-OLD-USD", isActive: false },
    bankFeed: null,
    latest: { ...listItem, id: "3f2c0000-0000-0000-0000-0000000000f2", status: "NoBankBalance", bankBalance: null, difference: null },
  },
];

const envelope = (items: unknown[]) => json({ items, page: 1, pageSize: 20, totalCount: items.length });

function path(request: Request) {
  return new URL(request.url).pathname.replace(/^\/api/, "");
}

const serve: Handler = (request) => {
  const p = path(request);
  if (request.method === "GET" && p === "/bank-reconciliations/overview") {
    return envelope(overview);
  }
  if (request.method === "GET" && p === `/bank-reconciliations/${runId}`) {
    return json(run);
  }
  if (request.method === "GET" && p === "/bank-reconciliations") {
    return envelope([listItem]);
  }
  return undefined;
};

describe("Bank reconciliation navigation (D51)", () => {
  beforeEach(() => stubBrowser("/dashboard"));

  it("shows Bank reconciliation to a TenantAdmin", async () => {
    renderFees(feeUser(), [{ path: "/dashboard", element: <ShellLayout /> }], "/dashboard");
    expect(await screen.findByRole("link", { name: /Bank reconciliation/ })).toHaveAttribute("href", "/bank-reconciliations");
  });

  it("hides Bank reconciliation from an Adviser", async () => {
    renderFees(adviser(), [{ path: "/dashboard", element: <ShellLayout /> }], "/dashboard");
    expect(await screen.findByRole("link", { name: /Accounts/ })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Bank reconciliation/ })).not.toBeInTheDocument();
  });
});

describe("BankReconciliationOverviewPage (D51)", () => {
  const routes = [
    { path: "/bank-reconciliations", element: <BankReconciliationOverviewPage /> },
    { path: "/bank-reconciliations/:id", element: <p>Run page</p> },
  ];

  beforeEach(() => stubBrowser("/bank-reconciliations"));

  it("shows the firm administrator sentence on a 403", async () => {
    renderFees(adviser(), routes, "/bank-reconciliations", () => new Response("", { status: 403 }));
    expect(await screen.findByText(forbiddenSentence)).toBeInTheDocument();
    expect(forbiddenSentence).toBe("Only a firm administrator can see bank reconciliations.");
  });

  it("lists each account with its columns, status labels and the feed failing note", async () => {
    renderFees(feeUser(), routes, "/bank-reconciliations", serve);

    const row = await screen.findByRole("row", { name: /SIM-DEMO-USD/ });
    for (const header of ["Currency", "Bank account", "Last run", "Status", "Bank balance", "Custodial cash", "Difference", "Statement read"]) {
      expect(screen.getByRole("columnheader", { name: header })).toBeInTheDocument();
    }
    expect(row).toHaveTextContent("Simulator Bank");
    expect(row).toHaveTextContent("2 breaks");
    expect(row).toHaveTextContent("12,512.40");
    expect(row).toHaveTextContent("10,824.80");
    expect(row).toHaveTextContent("1,687.60");
    expect(row).toHaveTextContent(/Bank feed failing since/);
    expect(within(row).getByRole("link", { name: /2026|Nov/ })).toHaveAttribute("href", `/bank-reconciliations/${runId}`);
    expect(screen.getByRole("row", { name: /SIM-DEMO-NZD/ })).toHaveTextContent("Not run yet");
    const old = screen.getByRole("row", { name: /SIM-OLD-USD/ });
    expect(old).toHaveTextContent("Inactive");
    expect(old).toHaveTextContent("No bank balance yet");
  });

  it("runs now and opens the new run", async () => {
    const { requests } = renderFees(feeUser(), routes, "/bank-reconciliations", (request) => {
      if (request.method === "POST" && path(request) === "/bank-reconciliations") {
        return json({ id: runId }, 201);
      }
      return serve(request);
    });

    const row = await screen.findByRole("row", { name: /SIM-DEMO-NZD/ });
    fireEvent.click(within(row).getByRole("button", { name: "Run now" }));

    expect(await screen.findByText("Run page")).toBeInTheDocument();
    const posted = requestsTo(requests, "POST", "/bank-reconciliations");
    expect(JSON.parse(await posted[0].clone().text())).toEqual({ clientMoneyAccountId: nzdId });
  });
});

describe("BankReconciliationDetailPage (D51)", () => {
  const routes = [{ path: "/bank-reconciliations/:id", element: <BankReconciliationDetailPage /> }];

  beforeEach(() => stubBrowser(`/bank-reconciliations/${runId}`));

  it("shows the header, the sides, the groups with their totals, the labels and the information", async () => {
    renderFees(feeUser(), routes, `/bank-reconciliations/${runId}`, serve);

    expect(await screen.findByText("Daily run")).toBeInTheDocument();
    expect(screen.getByText(/SIM-DEMO-USD/)).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Breaks" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Timing" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Known differences" })).toBeInTheDocument();
    expect(screen.getByText("Not explained")).toBeInTheDocument();
    expect(screen.getByText("This should not happen. Report it to the platform team.")).toBeInTheDocument();
    expect(screen.getByText("In suspense")).toBeInTheDocument();
    expect(screen.getByText("Reference not found")).toBeInTheDocument();
    expect(screen.getByText("Trades not settled through the trust account")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /2026-11-22/ })).toHaveAttribute("href", `/statement-lines/${lineId}`);
    expect(screen.getByText("Payments sent to the bank appear on both sides when the statement shows them.")).toBeInTheDocument();
    expect(screen.getByText(/Reserved cash/)).toBeInTheDocument();
    expect(screen.getByText(/Payments in flight/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "History" })).toHaveAttribute(
      "href",
      `/bank-reconciliations/history?clientMoneyAccountId=${usdId}`,
    );
    expect(screen.queryByRole("button", { name: /PDF/ })).not.toBeInTheDocument();
  });

  it("names the person of a manual run", async () => {
    renderFees(feeUser(), routes, `/bank-reconciliations/${runId}`, (request) =>
      path(request) === `/bank-reconciliations/${runId}`
        ? json({ ...run, trigger: "Manual", ranBy: { id: "11111111-1111-1111-1111-111111111111", name: "Pat Admin" } })
        : undefined,
    );
    expect(await screen.findByText(/Pat Admin/)).toBeInTheDocument();
  });
});

describe("BankReconciliationHistoryPage (D51)", () => {
  const routes = [{ path: "/bank-reconciliations/history", element: <BankReconciliationHistoryPage /> }];

  beforeEach(() => stubBrowser("/bank-reconciliations/history"));

  it("lists the runs of the account and filters by status", async () => {
    const { requests } = renderFees(feeUser(), routes, `/bank-reconciliations/history?clientMoneyAccountId=${usdId}`, serve);

    const row = await screen.findByRole("row", { name: /2 breaks/ });
    expect(within(row).getByRole("link")).toHaveAttribute("href", `/bank-reconciliations/${runId}`);
    await chooseOption("Status", "Breaks");
    await waitFor(() => {
      const listed = requestsTo(requests, "GET", "/bank-reconciliations");
      const last = new URL(listed[listed.length - 1].url).searchParams;
      expect(last.get("clientMoneyAccountId")).toBe(usdId);
      expect(last.get("status")).toBe("Breaks");
    });
  });
});

describe("Firm reports on the Reports tab (D52)", () => {
  const routes = [{ path: "/reports", element: <ReportsPage /> }];
  const bankReport = {
    id: bankReportId,
    code: "bank-reconciliation",
    name: "Bank reconciliation",
    function: "A stored bank reconciliation of one client money account.",
    scope: "Tenant",
    allowedTriggers: ["manual", "scheduled"],
  };
  const reports: Handler = (request) => {
    const p = path(request);
    const scope = new URL(request.url).searchParams.get("scope");
    if (request.method === "GET" && p === "/reports") {
      return json(scope === "Tenant" ? [bankReport] : []);
    }
    if (request.method === "GET" && (p === "/reports/schedules" || p === "/reports/executions")) {
      return p === "/reports/schedules" ? json([]) : json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    }
    if (request.method === "POST" && p === "/reports/request") {
      return json({ id: "99999999-0000-0000-0000-000000000001" }, 202);
    }
    return serve(request);
  };

  beforeEach(() => stubBrowser("/reports"));

  it("lists Tenant-scoped reports under Firm reports and generates one for a client money account", async () => {
    const { requests } = renderFees(feeUser(), routes, "/reports", reports);

    const section = (await screen.findByRole("heading", { name: "Firm reports" })).closest("section") as HTMLElement;
    await chooseOption("Firm report", "Bank reconciliation");
    const form = within((await within(section).findByRole("button", { name: "Generate now" })).closest("form") as HTMLElement);
    await chooseOption("Client money account", "USD · SIM-DEMO-USD", form);
    fireEvent.click(form.getByRole("button", { name: "Generate now" }));

    expect(await screen.findByText("Report queued")).toBeInTheDocument();
    const posted = requestsTo(requests, "POST", "/reports/request");
    expect(JSON.parse(await posted[0].clone().text())).toEqual({
      reportId: bankReportId,
      parameters: { clientMoneyAccountId: usdId },
    });
  });

  it("does not show Firm reports to an Adviser", async () => {
    const { requests } = renderFees(adviser(), routes, "/reports", reports);

    expect(await screen.findByRole("heading", { name: "Reports" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Firm reports" })).not.toBeInTheDocument();
    expect(requests.some((request) => new URL(request.url).searchParams.get("scope") === "Tenant")).toBe(false);
  });
});
