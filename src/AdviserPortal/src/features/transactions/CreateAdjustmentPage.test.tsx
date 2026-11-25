import { fireEvent, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CreateAdjustmentPage } from "@/features/transactions/CreateAdjustmentPage";
import { chooseOption, openSelect } from "@/test/chooseOption";
import { feeUser, json, renderFees, requestsTo, stubBrowser, tenantId, type Handler } from "@/test/fees";

const accountId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
const heldId = "dddddddd-dddd-dddd-dddd-dddddddddddd";
const otherId = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";
const createdId = "99999999-9999-9999-9999-999999999999";
const idempotencyKey = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const path = `/accounts/${accountId}/adjust`;

const account = {
  id: accountId,
  tenantId,
  customerId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
  name: "Managed",
  type: "Brokerage",
  custodyMode: "Custodial",
  currency: "USD",
  status: "Open",
  isActive: true,
  cashBalance: 100,
  rowVersion: "AAAA",
};

const holding = {
  id: "hhhhhhhh-hhhh-hhhh-hhhh-hhhhhhhhhhhh",
  accountId,
  instrumentId: heldId,
  symbol: "AAPL",
  name: "Apple",
  quantity: 10,
  cost: 251,
  costCurrency: "USD",
};

function instrument(id: string, symbol: string, name: string) {
  return { id, tenantId, symbol, name, quoteCurrency: "USD", isActive: true, rowVersion: "AAAA" };
}

const routes = [
  { path: "/accounts/:id/adjust", element: <CreateAdjustmentPage /> },
  { path: "/accounts/:id", element: <p>account page</p> },
  { path: "/adjustments/:id", element: <p>adjustment page</p> },
];

function serve(onCreate: (body: Record<string, unknown>) => Response = () => json({ id: createdId }, 201)): Handler {
  return async (request) => {
    const url = new URL(request.url);
    if (request.method === "POST" && url.pathname === "/api/adjustment-requests") {
      return onCreate(JSON.parse(await request.clone().text()) as Record<string, unknown>);
    }
    if (request.method !== "GET") {
      return undefined;
    }
    if (url.pathname === `/api/accounts/${accountId}`) {
      return json(account);
    }
    if (url.pathname === `/api/accounts/${accountId}/holdings`) {
      return json({ items: [holding] });
    }
    if (url.pathname === "/api/instruments") {
      return json({
        items: [instrument(heldId, "AAPL", "Apple"), instrument(otherId, "VTI", "Vanguard")],
        page: 1,
        pageSize: 100,
        totalCount: 2,
      });
    }
    return undefined;
  };
}

async function fillCashAndCost() {
  await chooseOption("Reason", "Entry error");
  fireEvent.change(screen.getByLabelText("Memo"), { target: { value: "Buy was filled at 25.40, not 25.10" } });
  fireEvent.change(screen.getByLabelText("Cash change (USD)"), { target: { value: "-0.60" } });
  await chooseOption("Instrument", "AAPL Apple");
  fireEvent.change(screen.getByLabelText("Quantity change"), { target: { value: "0" } });
  fireEvent.change(await screen.findByLabelText("Cost change (USD)"), { target: { value: "0.60" } });
}

describe("CreateAdjustmentPage (adviser-portal-dashboard D29, adjustment-review D102)", () => {
  beforeEach(() => {
    stubBrowser(path);
    vi.spyOn(crypto, "randomUUID").mockReturnValue(idempotencyKey);
  });

  it("asks for a reason, a memo the customer sees, and the changes, with no booked-at input", async () => {
    renderFees(feeUser(), routes, path, serve());

    expect(await screen.findByRole("heading", { name: "Adjust" })).toBeInTheDocument();
    expect(await screen.findByLabelText("Reason")).toBeInTheDocument();
    expect(screen.getByLabelText("Memo")).toBeInTheDocument();
    expect(screen.getByText("The customer sees this memo")).toBeInTheDocument();
    expect(await screen.findByLabelText("Cash change (USD)")).toBeInTheDocument();
    expect(screen.queryByLabelText(/Booked at/)).not.toBeInTheDocument();
  });

  it("offers the held instruments and the enabled ones, each once", async () => {
    renderFees(feeUser(), routes, path, serve());

    await openSelect("Instrument");
    const listbox = screen.getByRole("listbox");
    expect(within(listbox).getAllByRole("option", { name: "AAPL Apple" })).toHaveLength(1);
    expect(within(listbox).getByRole("option", { name: "VTI Vanguard" })).toBeInTheDocument();
  });

  it("reviews the changes as entered, says a second person approves, requests with one key and opens the request", async () => {
    const { requests, router } = renderFees(feeUser(), routes, path, serve());
    await fillCashAndCost();

    fireEvent.click(screen.getByRole("button", { name: "Review adjustment" }));

    expect(await screen.findByText("Cash −0.60 USD; AAPL quantity 0, cost +0.60 USD")).toBeInTheDocument();
    expect(screen.getByText("Another firm administrator must approve this adjustment before it is posted.")).toBeInTheDocument();
    expect(requestsTo(requests, "POST", "/adjustment-requests")).toHaveLength(0);

    fireEvent.click(screen.getByRole("button", { name: "Confirm adjustment" }));

    expect(await screen.findByText("adjustment page")).toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/adjustments/${createdId}`);
    const [post] = requestsTo(requests, "POST", "/adjustment-requests");
    expect(post.headers.get("Idempotency-Key")).toBe(idempotencyKey);
    expect(JSON.parse(await post.clone().text())).toEqual({
      accountId,
      reason: "EntryError",
      memo: "Buy was filled at 25.40, not 25.10",
      reference: null,
      amount: -0.6,
      security: { instrumentId: heldId, quantityChange: 0, costChange: 0.6 },
    });
  });

  it("posts a cash-only adjustment without a security part", async () => {
    const { requests } = renderFees(feeUser(), routes, path, serve());
    await chooseOption("Reason", "Reconciliation");
    fireEvent.change(screen.getByLabelText("Memo"), { target: { value: "Bank statement differs" } });
    fireEvent.change(await screen.findByLabelText("Cash change (USD)"), { target: { value: "12.5" } });

    fireEvent.click(screen.getByRole("button", { name: "Review adjustment" }));
    expect(await screen.findByText("Cash +12.50 USD")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Confirm adjustment" }));

    expect(await screen.findByText("adjustment page")).toBeInTheDocument();
    const [post] = requestsTo(requests, "POST", "/adjustment-requests");
    expect(JSON.parse(await post.clone().text())).toEqual({
      accountId,
      reason: "ReconciliationBreak",
      memo: "Bank statement differs",
      reference: null,
      amount: 12.5,
    });
  });

  it("shows a 400 at its field, a 400 without a field as the sentence, and keeps the key on retry", async () => {
    let calls = 0;
    const { requests } = renderFees(feeUser(), routes, path, serve(() => {
      calls += 1;
      return calls === 1
        ? json({
            status: 400,
            errors: {
              Memo: ["Memo is required and must be 1 to 200 characters."],
              "Security.CostChange": ["Cost change can have at most 4 decimal places."],
            },
          }, 400)
        : json({ status: 400, errors: { "": ["Cash balance cannot become negative."] } }, 400);
    }));
    await fillCashAndCost();

    fireEvent.click(screen.getByRole("button", { name: "Review adjustment" }));
    fireEvent.click(await screen.findByRole("button", { name: "Confirm adjustment" }));

    const memoError = await screen.findByText("Memo is required and must be 1 to 200 characters.");
    expect(screen.getByLabelText("Memo")).toHaveAttribute("aria-describedby", memoError.id);
    const costError = screen.getByText("Cost change can have at most 4 decimal places.");
    expect(screen.getByLabelText("Cost change (USD)")).toHaveAttribute("aria-describedby", costError.id);

    fireEvent.click(screen.getByRole("button", { name: "Review adjustment" }));
    fireEvent.click(await screen.findByRole("button", { name: "Confirm adjustment" }));

    expect(await screen.findByText("Cash balance cannot become negative.")).toBeInTheDocument();
    const posts = requestsTo(requests, "POST", "/adjustment-requests");
    expect(posts).toHaveLength(2);
    expect(posts.map((post) => post.headers.get("Idempotency-Key"))).toEqual([idempotencyKey, idempotencyKey]);
  });
});
