import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { CustodyMovementsPage } from "@/features/custodyMovements/CustodyMovementsPage";
import { adviser, feeUser, json, problem, renderFees, requestsTo, stubBrowser } from "@/test/fees";

const movementId = "88888888-0000-0000-0000-000000000001";
const transferId = "99999999-0000-0000-0000-000000000001";

function movement(overrides: Record<string, unknown> = {}) {
  return {
    id: movementId,
    safekeepingAccountId: "55555555-0000-0000-0000-000000000001",
    direction: "In",
    kind: "Transfer",
    symbol: "VTI",
    currency: "USD",
    instrument: { id: "dddddddd-0000-0000-0000-000000000001", symbol: "VTI", name: "Vanguard Total Stock Market ETF" },
    quantity: 800,
    settlementDate: "2026-11-24",
    instructionId: null,
    tradeReference: null,
    corporateActionReference: null,
    counterpartyName: "Acme Wealth",
    counterpartyAccount: null,
    reference: "Bulk delivery",
    receivedAt: "2026-11-24T02:00:00Z",
    status: "Unmatched",
    unmatchedReason: "NoInstruction",
    method: null,
    accountId: null,
    transactionId: null,
    inKindTransferId: null,
    orderId: null,
    fillId: null,
    corporateActionId: null,
    lastAction: null,
    rowVersion: "AAAA",
    ...overrides,
  };
}

const page = (items: unknown[]) => json({ items, page: 1, pageSize: 20, totalCount: items.length });

describe("CustodyMovementsPage (in-kind-transfers D90)", () => {
  const routes = [{ path: "/custody-movements", element: <CustodyMovementsPage /> }];

  beforeEach(() => stubBrowser("/custody-movements"));

  function serve(items: unknown[], action?: () => Response) {
    return (request: Request) => {
      if (request.method === "POST") {
        return action?.() ?? new Response(null, { status: 204 });
      }
      return new URL(request.url).pathname === "/api/custody-movements" ? page(items) : undefined;
    };
  }

  it("lists the movements in suspense by default with their reason, and a symbol when there is no instrument", async () => {
    const { requests } = renderFees(feeUser(), routes, "/custody-movements", serve([
      movement(),
      movement({ id: "88888888-0000-0000-0000-000000000002", kind: null, instrument: null, symbol: "ZZZ", unmatchedReason: "UnsupportedKind" }),
    ]));

    const table = await screen.findByRole("table", { name: "Custody movements" });
    expect(within(table).getByText("No transfer was requested")).toBeInTheDocument();
    expect(within(table).getByText("Unknown")).toBeInTheDocument();
    expect(within(table).getByText("ZZZ USD")).toBeInTheDocument();
    expect(within(table).getByText("Kind not supported")).toBeInTheDocument();
    expect(new URL(requestsTo(requests, "GET", "/custody-movements")[0].url).searchParams.get("status")).toBe("Unmatched");
  });

  it("ignores a movement with a required note, showing the hint of its reason", async () => {
    const { requests } = renderFees(feeUser(), routes, "/custody-movements", serve([movement()]));

    fireEvent.click(await screen.findByRole("button", { name: "Ignore" }));
    expect(screen.getByText(/Finds whose they are/)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Note"), { target: { value: "Migration delivery" } });
    fireEvent.click(screen.getByRole("button", { name: "Confirm" }));

    await waitFor(() => expect(requestsTo(requests, "POST", `/custody-movements/${movementId}/ignore`)).toHaveLength(1));
    expect(JSON.parse(await requestsTo(requests, "POST", `/custody-movements/${movementId}/ignore`)[0].text()))
      .toEqual({ note: "Migration delivery", rowVersion: "AAAA" });
  });

  it("offers Reopen on an ignored movement, and links a matched one to its transfer", async () => {
    const { requests } = renderFees(feeUser(), routes, "/custody-movements?status=All", serve([
      movement({ status: "Ignored" }),
      movement({ id: "88888888-0000-0000-0000-000000000003", status: "Matched", unmatchedReason: null, method: "Transfer", inKindTransferId: transferId }),
    ]));

    expect(await screen.findByRole("link", { name: "Transfer" })).toHaveAttribute("href", `/transfers/${transferId}`);
    fireEvent.click(screen.getByRole("button", { name: "Reopen" }));
    await waitFor(() => expect(requestsTo(requests, "POST", `/custody-movements/${movementId}/reopen`)).toHaveLength(1));
  });

  it("tells an Adviser the page is for a firm administrator", async () => {
    renderFees(adviser(), routes, "/custody-movements", (request) =>
      new URL(request.url).pathname === "/api/custody-movements" ? problem(403, {}) : undefined);

    expect(await screen.findByText("Only a firm administrator can see custody movements.")).toBeInTheDocument();
  });
});
