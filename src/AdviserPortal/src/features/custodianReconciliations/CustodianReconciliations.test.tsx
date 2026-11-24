import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { CustodianReconciliationDetailPage } from "@/features/custodianReconciliations/CustodianReconciliationDetailPage";
import { CustodianReconciliationHistoryPage } from "@/features/custodianReconciliations/CustodianReconciliationHistoryPage";
import { CustodianReconciliationOverviewPage } from "@/features/custodianReconciliations/CustodianReconciliationOverviewPage";
import { feeUser, json, renderFees, requestsTo, stubBrowser } from "@/test/fees";

const runId = "66666666-0000-0000-0000-000000000001";
const safekeepingId = "55555555-0000-0000-0000-000000000001";

const safekeeping = {
  id: safekeepingId,
  currency: "USD",
  custodianName: "Simulator Custody",
  accountName: "Demo Advisory Nominees",
  accountNumber: "SIM-DEMO-CUSTODY-USD",
  isActive: true,
};

const listItem = {
  id: runId,
  safekeepingAccount: safekeeping,
  trigger: "Manual",
  ranAt: "2026-11-24T02:00:00Z",
  ranBy: { id: "11111111-1111-1111-1111-111111111111", name: "Pat Admin" },
  status: "Breaks",
  positionsAt: "2026-11-24T01:59:00Z",
  breakCount: 2,
  timingCount: 1,
  agreedCount: 1,
  transfersInFlight: 1,
  fillsNotDelivered: 1,
};

const line = (overrides: Record<string, unknown>) => ({
  instrument: { id: "dddddddd-0000-0000-0000-000000000001", symbol: "VTI", name: "Vanguard Total Stock Market ETF" },
  symbol: "VTI",
  custodianQuantity: 0,
  platformQuantity: 0,
  difference: 0,
  notProcessed: 0,
  unsettledTrades: 0,
  suspense: 0,
  unbookedFills: 0,
  unexplained: 0,
  class: "Agreed",
  ...overrides,
});

const run = {
  ...listItem,
  feed: { lastSucceededAt: "2026-11-24T01:59:00Z", lastFailedAt: null, lastFailure: null },
  lines: [
    line({ custodianQuantity: 0, platformQuantity: 1200, difference: -1200, unexplained: -1200, class: "Break" }),
    line({ instrument: null, symbol: "ZZZ", custodianQuantity: 5, difference: 5, suspense: 5, class: "Break" }),
    line({ instrument: { id: "dddddddd-0000-0000-0000-000000000002", symbol: "BETA.SIM", name: "Beta Simulated" }, symbol: "BETA.SIM",
      custodianQuantity: 50, platformQuantity: 60, difference: -10, unsettledTrades: -10, class: "Timing" }),
    line({ instrument: { id: "dddddddd-0000-0000-0000-000000000003", symbol: "ACME.SIM", name: "Acme Simulated" }, symbol: "ACME.SIM",
      custodianQuantity: 100, platformQuantity: 100, class: "Agreed" }),
  ],
};

const page = (items: unknown[]) => json({ items, page: 1, pageSize: 20, totalCount: items.length });

describe("CustodianReconciliationOverviewPage (in-kind-transfers D91)", () => {
  const routes = [
    { path: "/custodian-reconciliations", element: <CustodianReconciliationOverviewPage /> },
    { path: "/custodian-reconciliations/:id", element: <p>Run page</p> },
  ];

  beforeEach(() => stubBrowser("/custodian-reconciliations"));

  it("shows each safekeeping account with its last run, and Run now opens the new run", async () => {
    const { requests, router } = renderFees(feeUser(), routes, "/custodian-reconciliations", (request) => {
      if (request.method === "POST") {
        return json({ id: runId }, 201);
      }
      return new URL(request.url).pathname === "/api/custodian-reconciliations/overview"
        ? page([
            { safekeepingAccount: safekeeping, custodianFeed: { ...run.feed, positionsAt: "2026-11-24T01:59:00Z" }, latest: listItem },
            { safekeepingAccount: { ...safekeeping, id: "55555555-0000-0000-0000-000000000002", currency: "NZD", accountNumber: "SIM-DEMO-CUSTODY-NZD" },
              custodianFeed: null, latest: null },
          ])
        : undefined;
    });

    expect(await screen.findByText("2 breaks")).toBeInTheDocument();
    expect(screen.getByText("No positions yet")).toBeInTheDocument();
    expect(screen.getAllByText(/^Positions as at /)).toHaveLength(1);
    fireEvent.click(screen.getAllByRole("button", { name: "Run now" })[0]);

    await waitFor(() => expect(router.state.location.pathname).toBe(`/custodian-reconciliations/${runId}`));
    expect(JSON.parse(await requestsTo(requests, "POST", "/custodian-reconciliations")[0].text())).toEqual({ safekeepingAccountId: safekeepingId });
  });

  it("tells a firm with no safekeeping account that holdings are not checked", async () => {
    renderFees(feeUser(), routes, "/custodian-reconciliations", (request) =>
      new URL(request.url).pathname === "/api/custodian-reconciliations/overview" ? page([]) : undefined);

    expect(await screen.findByText("Your firm has no safekeeping account. Holdings are not checked against a custodian.")).toBeInTheDocument();
  });
});

describe("CustodianReconciliationDetailPage (in-kind-transfers D92)", () => {
  beforeEach(() => stubBrowser(`/custodian-reconciliations/${runId}`));

  it("groups the lines and explains the components that are not 0", async () => {
    renderFees(feeUser(), [{ path: "/custodian-reconciliations/:id", element: <CustodianReconciliationDetailPage /> }],
      `/custodian-reconciliations/${runId}`, (request) =>
        new URL(request.url).pathname === `/api/custodian-reconciliations/${runId}` ? json(run) : undefined);

    const breaks = await screen.findByRole("region", { name: "Breaks" });
    expect(within(breaks).getByText("-1200 not explained")).toBeInTheDocument();
    expect(within(breaks).getByText("Not in your catalog")).toBeInTheDocument();
    expect(within(breaks).getByRole("link", { name: "5 in movements waiting for a decision" })).toHaveAttribute("href", "/custody-movements?status=Unmatched");
    const timing = screen.getByRole("region", { name: "Timing" });
    expect(within(timing).getByText("-10 from trades not settled yet")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "History" })).toHaveAttribute("href", `/custodian-reconciliations/history?safekeepingAccountId=${safekeepingId}`);
    expect(screen.getByText("Transfers in flight: 1")).toBeInTheDocument();
    expect(screen.queryByText("ACME.SIM")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: /Agreed/ }));
    expect(await screen.findByText("ACME.SIM")).toBeInTheDocument();
  });
});

describe("CustodianReconciliationHistoryPage (in-kind-transfers D92)", () => {
  beforeEach(() => stubBrowser("/custodian-reconciliations/history"));

  it("lists the runs of one safekeeping account", async () => {
    const { requests } = renderFees(feeUser(), [{ path: "/custodian-reconciliations/history", element: <CustodianReconciliationHistoryPage /> }],
      `/custodian-reconciliations/history?safekeepingAccountId=${safekeepingId}`, (request) =>
        new URL(request.url).pathname === "/api/custodian-reconciliations" ? page([listItem]) : undefined);

    expect(await screen.findByText("2 breaks")).toBeInTheDocument();
    expect(new URL(requestsTo(requests, "GET", "/custodian-reconciliations")[0].url).searchParams.get("safekeepingAccountId")).toBe(safekeepingId);
  });
});
