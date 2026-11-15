import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { EditTradeSchedulePage } from "@/features/fees/EditTradeSchedulePage";
import type { TradeFeeSchedule } from "@/features/fees/feesApi";
import { feeUser, json, problem, renderFees, requestsTo, schedule, stubBrowser, type Handler } from "@/test/fees";

const id = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";
const path = `/fees/trade/${id}/edit`;
const inEffectMessage =
  "This schedule may already apply to posted trades, so it can't be changed. Add a new schedule with a later date instead.";
const staleMessage = "Someone else changed this schedule. Reload to see the latest version.";

const routes = [
  { path: "/fees/trade/:id", element: <p>schedule detail</p> },
  { path: "/fees/trade/:id/edit", element: <EditTradeSchedulePage /> },
];

/** Serves `rows` in turn on each GET of the item (the last one repeats) and `put` on PUT. */
function serve(rows: TradeFeeSchedule[], put: Handler = () => new Response(null, { status: 204 })): Handler {
  let served = 0;
  return (request) => {
    const url = new URL(request.url);
    if (url.pathname !== `/api/fees/trade-schedules/${id}`) {
      return undefined;
    }
    if (request.method === "GET") {
      const row = rows[Math.min(served, rows.length - 1)];
      served += 1;
      return json(row);
    }
    return request.method === "PUT" ? put(request) : undefined;
  };
}

function input(label: string) {
  return screen.getByLabelText(label) as HTMLInputElement;
}

function fieldOf(label: string) {
  return within(input(label).closest('[data-slot="field"]') as HTMLElement);
}

function puts(requests: Request[]) {
  return requestsTo(requests, "PUT", `/fees/trade-schedules/${id}`);
}

describe("EditTradeSchedulePage", () => {
  beforeEach(() => {
    stubBrowser(path);
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-11-20T20:00:00Z"));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("loads the row into the form with the quote currency read-only", async () => {
    renderFees(feeUser(), routes, path, serve([schedule({ cap: 250 })]));

    expect(await screen.findByLabelText("Minimum")).toHaveValue("10");
    expect(input("Quote currency")).toHaveValue("NZD");
    expect(input("Quote currency")).toHaveAttribute("readonly");
    expect(input("Effective from")).toHaveValue("2026-12-01");
    expect(input("Cap")).toHaveValue("250");
    expect(input("Band 2 from")).toHaveValue("10000");
    expect(input("Band 2 rate (%)")).toHaveValue("0.3");
  });

  it("puts the form with rowVersion and without quoteCurrency, then opens the detail (F8)", async () => {
    const { requests, router } = renderFees(feeUser(), routes, path, serve([schedule({ rowVersion: "v1" })]));
    fireEvent.change(await screen.findByLabelText("Minimum"), { target: { value: "12.5" } });
    fireEvent.change(input("Cap"), { target: { value: "300" } });
    fireEvent.click(screen.getByRole("button", { name: "Save schedule" }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/fees/trade/${id}`));
    const [sent] = puts(requests);
    expect(sent.headers.get("X-MyWealth-Request")).toBe("1");
    expect(await sent.json()).toEqual({
      effectiveFrom: "2026-12-01",
      minimum: 12.5,
      cap: 300,
      bands: [
        { from: 0, ratePercent: 0.5 },
        { from: 10000, ratePercent: 0.3 },
      ],
      rowVersion: "v1",
    });
  });

  it("on 409 stale_row_version asks for a reload and does not resend the old version (F8)", async () => {
    const { requests } = renderFees(
      feeUser(),
      routes,
      path,
      serve([schedule({ rowVersion: "v1" }), schedule({ rowVersion: "v2", minimum: 20 })], async (request) => {
        const body = (await request.json()) as { rowVersion: string };
        return body.rowVersion === "v1"
          ? problem(409, { code: "stale_row_version", detail: "Stale." })
          : new Response(null, { status: 204 });
      }),
    );
    fireEvent.change(await screen.findByLabelText("Minimum"), { target: { value: "15" } });
    fireEvent.click(screen.getByRole("button", { name: "Save schedule" }));

    expect(await screen.findByText(staleMessage)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save schedule" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Save schedule" }));
    expect(puts(requests)).toHaveLength(1);

    fireEvent.click(screen.getByRole("button", { name: "Reload" }));
    await waitFor(() => expect(input("Minimum")).toHaveValue("20"));
    expect(screen.queryByText(staleMessage)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Save schedule" }));
    await waitFor(() => expect(puts(requests)).toHaveLength(2));
    expect((await puts(requests)[1].json()).rowVersion).toBe("v2");
  });

  it("on 409 fee_schedule_in_effect shows the in-effect message, reloads the row and drops the form (F9)", async () => {
    const { requests } = renderFees(
      feeUser(),
      routes,
      path,
      serve([schedule({ inEffect: false }), schedule({ inEffect: true })], () =>
        problem(409, { code: "fee_schedule_in_effect", detail: "In effect." }),
      ),
    );
    fireEvent.click(await screen.findByRole("button", { name: "Save schedule" }));

    expect(await screen.findByText(inEffectMessage)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Save schedule" })).not.toBeInTheDocument();
    await waitFor(() => expect(requestsTo(requests, "GET", `/fees/trade-schedules/${id}`)).toHaveLength(2));
  });

  it("shows the in-effect message instead of the form for a row in effect", async () => {
    renderFees(feeUser(), routes, path, serve([schedule({ inEffect: true })]));

    expect(await screen.findByText(inEffectMessage)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Save schedule" })).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Minimum")).not.toBeInTheDocument();
  });

  it("shows a 400 errors.EffectiveFrom under the effective-from field (F12)", async () => {
    renderFees(
      feeUser(),
      routes,
      path,
      serve([schedule()], () => problem(400, { errors: { EffectiveFrom: ["A schedule already starts on 2026-12-01."] } })),
    );
    fireEvent.click(await screen.findByRole("button", { name: "Save schedule" }));

    expect(await fieldOf("Effective from").findByText("A schedule already starts on 2026-12-01.")).toBeInTheDocument();
  });

  it("shows Not found for an unknown or other-tenant id (F14)", async () => {
    renderFees(feeUser(), routes, path);

    expect(await screen.findByText("Not found")).toBeInTheDocument();
  });
});
