import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { TradeScheduleDetailPage } from "@/features/fees/TradeScheduleDetailPage";
import type { TradeFeeSchedule } from "@/features/fees/feesApi";
import { adviser, feeUser, json, problem, renderFees, requestsTo, schedule, stubBrowser, type Handler } from "@/test/fees";

const id = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";
const path = `/fees/trade/${id}`;
const inEffectMessage =
  "This schedule may already apply to posted trades, so it can't be changed. Add a new schedule with a later date instead.";

const routes = [
  { path: "/fees/trade", element: <p>schedule list</p> },
  { path: "/fees/trade/:id", element: <TradeScheduleDetailPage /> },
  { path: "/fees/trade/:id/edit", element: <p>edit schedule</p> },
];

function serve(rows: TradeFeeSchedule[], extra: Handler = () => undefined): Handler {
  let served = 0;
  return async (request) => {
    const answer = await extra(request);
    if (answer) {
      return answer;
    }
    const url = new URL(request.url);
    if (request.method === "GET" && url.pathname === `/api/fees/trade-schedules/${id}`) {
      const row = rows[Math.min(served, rows.length - 1)];
      served += 1;
      return json(row);
    }
    if (request.method === "POST" && url.pathname === "/api/fees/trade-schedules/preview") {
      const body = (await request.json()) as { amounts: number[] };
      return json({
        currency: "NZD",
        items: body.amounts.map((amount) => ({ amount, bands: [], banded: 0, limitApplied: null, fee: 42 })),
      });
    }
    return undefined;
  };
}

const deleted: Handler = (request) => (request.method === "DELETE" ? new Response(null, { status: 204 }) : undefined);

describe("TradeScheduleDetailPage", () => {
  beforeEach(() => {
    stubBrowser(path);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("shows the stored row read-only", async () => {
    renderFees(feeUser(), routes, path, serve([schedule({ cap: 250 })]));

    expect(await screen.findByText("2026-12-01 UTC")).toBeInTheDocument();
    expect(screen.getByText("NZD")).toBeInTheDocument();
    expect(screen.getByText("10.00 NZD")).toBeInTheDocument();
    expect(screen.getByText("250.00 NZD")).toBeInTheDocument();
    const bands = within(screen.getByRole("table", { name: "Bands" }));
    expect(within(bands.getByText("0.3%").closest("tr") as HTMLElement).getByText("10,000.00 NZD")).toBeInTheDocument();
    expect(within(bands.getByText("0.5%").closest("tr") as HTMLElement).getByText("0.00 NZD")).toBeInTheDocument();
  });

  it("offers Edit and Delete to TenantAdmin on a pending row (F7)", async () => {
    renderFees(feeUser(), routes, path, serve([schedule({ inEffect: false })]));

    expect(await screen.findByRole("link", { name: "Edit" })).toHaveAttribute("href", `${path}/edit`);
    expect(screen.getByRole("button", { name: "Delete" })).toBeInTheDocument();
    expect(screen.queryByText(inEffectMessage)).not.toBeInTheDocument();
  });

  it("offers neither on a row in effect and says why (F7)", async () => {
    renderFees(feeUser(), routes, path, serve([schedule({ inEffect: true })]));

    expect(await screen.findByText(inEffectMessage)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Edit" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Delete" })).not.toBeInTheDocument();
  });

  it("never offers Edit or Delete to an Adviser (F7)", async () => {
    renderFees(adviser(), routes, path, serve([schedule({ inEffect: false })]));

    await screen.findByText("2026-12-01 UTC");
    expect(screen.queryByRole("link", { name: "Edit" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Delete" })).not.toBeInTheDocument();
  });

  it("deletes after confirmation and goes back to the list (F10)", async () => {
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(true);
    const { requests, router } = renderFees(feeUser(), routes, path, serve([schedule()], deleted));
    fireEvent.click(await screen.findByRole("button", { name: "Delete" }));

    await waitFor(() => expect(router.state.location.pathname).toBe("/fees/trade"));
    expect(confirm).toHaveBeenCalledWith(
      "Delete this schedule? Trades from 2026-12-01 will use the schedule before it, or have no brokerage if there is none.",
    );
    const [sent] = requestsTo(requests, "DELETE", `/fees/trade-schedules/${id}`);
    expect(sent.headers.get("X-MyWealth-Request")).toBe("1");
  });

  it("does not delete when the confirmation is cancelled (F10)", async () => {
    vi.spyOn(window, "confirm").mockReturnValue(false);
    const { requests } = renderFees(feeUser(), routes, path, serve([schedule()], deleted));
    fireEvent.click(await screen.findByRole("button", { name: "Delete" }));

    expect(requestsTo(requests, "DELETE", `/fees/trade-schedules/${id}`)).toHaveLength(0);
    expect(screen.getByRole("button", { name: "Delete" })).toBeInTheDocument();
  });

  it("on 409 fee_schedule_in_effect shows the in-effect message, reloads the row and drops Edit and Delete (F9)", async () => {
    vi.spyOn(window, "confirm").mockReturnValue(true);
    const { requests } = renderFees(
      feeUser(),
      routes,
      path,
      serve([schedule({ inEffect: false }), schedule({ inEffect: true })], (request) =>
        request.method === "DELETE" ? problem(409, { code: "fee_schedule_in_effect", detail: "In effect." }) : undefined,
      ),
    );
    fireEvent.click(await screen.findByRole("button", { name: "Delete" }));

    expect(await screen.findByText(inEffectMessage)).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole("button", { name: "Delete" })).not.toBeInTheDocument());
    expect(screen.queryByRole("link", { name: "Edit" })).not.toBeInTheDocument();
    expect(requestsTo(requests, "GET", `/fees/trade-schedules/${id}`)).toHaveLength(2);
  });

  it("branches on the problem code, not on 409 alone (F9)", async () => {
    vi.spyOn(window, "confirm").mockReturnValue(true);
    renderFees(
      feeUser(),
      routes,
      path,
      serve([schedule()], (request) =>
        request.method === "DELETE" ? problem(409, { code: "some_other_conflict", detail: "Try again later." }) : undefined,
      ),
    );
    fireEvent.click(await screen.findByRole("button", { name: "Delete" }));

    expect(await screen.findByText("Try again later.")).toBeInTheDocument();
    expect(screen.queryByText(inEffectMessage)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Delete" })).toBeInTheDocument();
  });

  it("shows Not found for an unknown or other-tenant id (F14)", async () => {
    renderFees(feeUser(), routes, path);

    expect(await screen.findByText("Not found")).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Forbidden" })).not.toBeInTheDocument();
  });

  it.each([
    ["TenantAdmin", feeUser()],
    ["Adviser", adviser()],
  ])("previews the stored row for %s (F18)", async (_label, me) => {
    const { requests } = renderFees(me, routes, path, serve([schedule({ cap: 250 })]));

    const table = await screen.findByRole("table", { name: "Fee preview" });
    expect(within(table).getAllByText("42.00 NZD")).toHaveLength(3);
    const [sent] = requestsTo(requests, "POST", "/fees/trade-schedules/preview");
    expect(await sent.json()).toEqual({
      quoteCurrency: "NZD",
      minimum: 10,
      cap: 250,
      bands: [
        { from: 0, ratePercent: 0.5 },
        { from: 10000, ratePercent: 0.3 },
      ],
      amounts: [1000, 10000, 100000],
    });
  });
});
