import { screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { FeesTradePage } from "@/features/fees/FeesTradePage";
import type { TradeFeeSchedule } from "@/features/fees/feesApi";
import { adviser, feeUser, json, renderFees, requestsTo, schedule, stubBrowser } from "@/test/fees";

const routes = [
  { path: "/fees/trade", element: <FeesTradePage /> },
  { path: "/fees/trade/new", element: <p>new schedule</p> },
  { path: "/fees/trade/:id", element: <p>schedule detail</p> },
];

const rows: TradeFeeSchedule[] = [
  schedule({ id: "nzd-earlier", quoteCurrency: "NZD", effectiveFrom: "2026-01-01", inEffect: true }),
  schedule({ id: "nzd-today", quoteCurrency: "NZD", effectiveFrom: "2026-11-01", inEffect: true, cap: 100 }),
  schedule({ id: "nzd-scheduled", quoteCurrency: "NZD", effectiveFrom: "2026-12-01", inEffect: false }),
  schedule({ id: "nzd-locked", quoteCurrency: "NZD", effectiveFrom: "2027-01-01", inEffect: true }),
  // Tomorrow in UTC but today in Auckland: the state uses the UTC date.
  schedule({ id: "usd-tomorrow", quoteCurrency: "USD", effectiveFrom: "2026-11-21", inEffect: false }),
  schedule({
    id: "jpy",
    quoteCurrency: "JPY",
    effectiveFrom: "2025-06-01",
    inEffect: true,
    minimum: 500,
    bands: [
      { from: 0, ratePercent: 0.5 },
      { from: 1000000, ratePercent: 0.3 },
    ],
  }),
];

function listHandler(items: TradeFeeSchedule[], pageSize = items.length) {
  return (request: Request) => {
    const url = new URL(request.url);
    if (url.pathname !== "/api/fees/trade-schedules" || request.method !== "GET") {
      return undefined;
    }
    const page = Number(url.searchParams.get("page") ?? "1");
    const slice = items.slice((page - 1) * pageSize, page * pageSize);
    return json({ items: slice, page, pageSize, totalCount: items.length });
  };
}

function rowFor(table: HTMLElement, effectiveFrom: string) {
  const link = within(table).getByRole("link", { name: `${effectiveFrom} UTC` });
  return link.closest("tr") as HTMLElement;
}

describe("FeesTradePage", () => {
  beforeEach(() => {
    stubBrowser("/fees/trade");
    // 20:00 UTC on 20 Nov 2026 is 09:00 on 21 Nov in Auckland.
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-11-20T20:00:00Z"));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("groups schedules by quote currency in code order, newest first (F2)", async () => {
    renderFees(feeUser(), routes, "/fees/trade", listHandler(rows));

    const tables = await screen.findAllByRole("table");
    expect(tables.map((table) => table.getAttribute("aria-label"))).toEqual([
      "JPY trade fee schedules",
      "NZD trade fee schedules",
      "USD trade fee schedules",
    ]);
    const nzd = screen.getByRole("table", { name: "NZD trade fee schedules" });
    expect(within(nzd).getAllByRole("link").map((link) => link.textContent)).toEqual([
      "2027-01-01 UTC",
      "2026-12-01 UTC",
      "2026-11-01 UTC",
      "2026-01-01 UTC",
    ]);
    expect(within(nzd).getByRole("link", { name: "2026-12-01 UTC" })).toHaveAttribute(
      "href",
      "/fees/trade/nzd-scheduled",
    );
  });

  it("shows rates, minimum and cap in the currency's decimals (F2)", async () => {
    renderFees(feeUser(), routes, "/fees/trade", listHandler(rows));

    const nzd = await screen.findByRole("table", { name: "NZD trade fee schedules" });
    const capped = within(rowFor(nzd, "2026-11-01"));
    expect(capped.getByText("0.5% to 10,000.00 · 0.3% above")).toBeInTheDocument();
    expect(capped.getByText("10.00 NZD")).toBeInTheDocument();
    expect(capped.getByText("100.00 NZD")).toBeInTheDocument();
    expect(within(rowFor(nzd, "2026-01-01")).getByText("None")).toBeInTheDocument();

    const jpy = within(rowFor(screen.getByRole("table", { name: "JPY trade fee schedules" }), "2025-06-01"));
    expect(jpy.getByText("0.5% to 1,000,000 · 0.3% above")).toBeInTheDocument();
    expect(jpy.getByText("500 JPY")).toBeInTheDocument();
  });

  it("shows the state of each row from today's UTC date and inEffect (F3)", async () => {
    renderFees(feeUser(), routes, "/fees/trade", listHandler(rows));

    const nzd = await screen.findByRole("table", { name: "NZD trade fee schedules" });
    expect(within(rowFor(nzd, "2027-01-01")).getByText("Scheduled, locked")).toBeInTheDocument();
    expect(within(rowFor(nzd, "2026-12-01")).getByText("Scheduled")).toBeInTheDocument();
    expect(within(rowFor(nzd, "2026-11-01")).getByText("Applies today")).toBeInTheDocument();
    expect(within(rowFor(nzd, "2026-01-01")).getByText("Earlier")).toBeInTheDocument();
    const usd = screen.getByRole("table", { name: "USD trade fee schedules" });
    expect(within(rowFor(usd, "2026-11-21")).getByText("Scheduled")).toBeInTheDocument();
    const jpy = screen.getByRole("table", { name: "JPY trade fee schedules" });
    expect(within(rowFor(jpy, "2025-06-01")).getByText("Applies today")).toBeInTheDocument();
  });

  it("pages through the list 100 rows at a time until every row is loaded", async () => {
    const { requests } = renderFees(feeUser(), routes, "/fees/trade", (request) => {
      const url = new URL(request.url);
      if (url.pathname !== "/api/fees/trade-schedules") {
        return undefined;
      }
      const page = Number(url.searchParams.get("page"));
      return json({ items: [rows[page - 1]], page, pageSize: 100, totalCount: 2 });
    });

    const nzd = await screen.findByRole("table", { name: "NZD trade fee schedules" });
    await waitFor(() => expect(within(nzd).getAllByRole("link")).toHaveLength(2));
    const listCalls = requestsTo(requests, "GET", "/fees/trade-schedules").map((request) => new URL(request.url).searchParams);
    expect(listCalls.map((params) => [params.get("page"), params.get("pageSize")])).toEqual([
      ["1", "100"],
      ["2", "100"],
    ]);
  });

  it("shows the empty message when there are no schedules (F2)", async () => {
    renderFees(feeUser(), routes, "/fees/trade", listHandler([]));

    expect(
      await screen.findByText("No trade fee schedules yet. Trades have no brokerage until a schedule starts."),
    ).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("offers New schedule to TenantAdmin (F4)", async () => {
    renderFees(feeUser(), routes, "/fees/trade", listHandler(rows));

    expect(await screen.findByRole("link", { name: "New schedule" })).toHaveAttribute("href", "/fees/trade/new");
    expect(screen.getByRole("tab", { name: "Trade fees" })).toBeInTheDocument();
  });

  it("does not offer New schedule to an Adviser (F4)", async () => {
    renderFees(adviser(), routes, "/fees/trade", listHandler(rows));

    await screen.findByRole("table", { name: "NZD trade fee schedules" });
    expect(screen.queryByRole("link", { name: "New schedule" })).not.toBeInTheDocument();
  });
});
