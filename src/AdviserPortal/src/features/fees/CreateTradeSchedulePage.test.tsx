import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CreateTradeSchedulePage } from "@/features/fees/CreateTradeSchedulePage";
import { chooseOption } from "@/test/chooseOption";
import { feeUser, json, problem, renderFees, requestsTo, stubBrowser, type Handler } from "@/test/fees";

const createdId = "ffffffff-ffff-ffff-ffff-ffffffffffff";

const routes = [
  { path: "/fees/trade/new", element: <CreateTradeSchedulePage /> },
  { path: "/fees/trade/:id", element: <p>schedule detail</p> },
];

const created: Handler = (request) =>
  request.method === "POST" && new URL(request.url).pathname === "/api/fees/trade-schedules"
    ? json({ id: createdId }, 201)
    : undefined;

function renderCreate(handler: Handler = created) {
  return renderFees(feeUser(), routes, "/fees/trade/new", handler);
}

function input(label: string) {
  return screen.getByLabelText(label) as HTMLInputElement;
}

function type(label: string, value: string) {
  fireEvent.change(input(label), { target: { value } });
}

/** The field that holds `label`'s control, so an error can be checked under that field. */
function fieldOf(label: string) {
  return within(input(label).closest('[data-slot="field"]') as HTMLElement);
}

async function fillValidSchedule() {
  await chooseOption("Quote currency", "NZD");
  type("Effective from", "2026-12-01");
  type("Minimum", "10");
  type("Band 1 rate (%)", "0.5");
  fireEvent.click(screen.getByRole("button", { name: "Add band" }));
  type("Band 2 from", "10000");
  type("Band 2 rate (%)", "0.3");
}

function creates(requests: Request[]) {
  return requestsTo(requests, "POST", "/fees/trade-schedules");
}

describe("CreateTradeSchedulePage", () => {
  beforeEach(() => {
    stubBrowser("/fees/trade/new");
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-11-20T20:00:00Z"));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("posts the schedule without tenantId and opens the new schedule (F5)", async () => {
    const { requests, router } = renderCreate();
    await fillValidSchedule();
    fireEvent.click(screen.getByRole("button", { name: "Create schedule" }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/fees/trade/${createdId}`));
    const [post] = creates(requests);
    expect(post.headers.get("X-MyWealth-Request")).toBe("1");
    expect(await post.json()).toEqual({
      quoteCurrency: "NZD",
      effectiveFrom: "2026-12-01",
      minimum: 10,
      cap: null,
      bands: [
        { from: 0, ratePercent: 0.5 },
        { from: 10000, ratePercent: 0.3 },
      ],
    });
  });

  it("sends a cap when one is entered", async () => {
    const { requests, router } = renderCreate();
    await fillValidSchedule();
    type("Cap", "250");
    fireEvent.click(screen.getByRole("button", { name: "Create schedule" }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/fees/trade/${createdId}`));
    expect((await creates(requests)[0].json()).cap).toBe(250);
  });

  it("keeps the first band's From at 0, read-only and without Remove (F6)", async () => {
    renderCreate();
    await screen.findByLabelText("Band 1 from");

    expect(input("Band 1 from").value).toBe("0");
    expect(input("Band 1 from")).toHaveAttribute("readonly");
    expect(screen.queryByRole("button", { name: "Remove band 1" })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Add band" }));
    expect(screen.getByLabelText("Band 2 from")).not.toHaveAttribute("readonly");
    fireEvent.click(screen.getByRole("button", { name: "Remove band 2" }));
    expect(screen.queryByLabelText("Band 2 from")).not.toBeInTheDocument();
  });

  it("allows at most 10 bands (F6)", async () => {
    renderCreate();
    await screen.findByLabelText("Band 1 from");

    for (let count = 1; count < 10; count += 1) {
      fireEvent.click(screen.getByRole("button", { name: "Add band" }));
    }

    expect(screen.getByLabelText("Band 10 from")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Add band" })).not.toBeInTheDocument();
    expect(screen.getByText("A schedule can have at most 10 bands.")).toBeInTheDocument();
  });

  it("blocks submit while From is not ascending (F6)", async () => {
    const { requests } = renderCreate();
    await fillValidSchedule();
    type("Band 2 from", "0");
    fireEvent.click(screen.getByRole("button", { name: "Create schedule" }));

    expect(await fieldOf("Band 2 from").findByText("Each band must start above the one before it.")).toBeInTheDocument();
    expect(creates(requests)).toHaveLength(0);
  });

  it("blocks submit while the cap is below the minimum", async () => {
    const { requests } = renderCreate();
    await fillValidSchedule();
    type("Cap", "5");
    fireEvent.click(screen.getByRole("button", { name: "Create schedule" }));

    expect(await fieldOf("Cap").findByText("The cap must be at least the minimum.")).toBeInTheDocument();
    expect(creates(requests)).toHaveLength(0);
  });

  it("blocks submit while the minimum has more decimals than the currency", async () => {
    const { requests } = renderCreate();
    await fillValidSchedule();
    type("Minimum", "10.555");
    fireEvent.click(screen.getByRole("button", { name: "Create schedule" }));

    expect(
      await fieldOf("Minimum").findByText("Minimum must be at least 0 with at most 2 decimals."),
    ).toBeInTheDocument();
    expect(creates(requests)).toHaveLength(0);
  });

  it("blocks submit of an empty form", async () => {
    const { requests } = renderCreate();
    await screen.findByLabelText("Band 1 from");
    fireEvent.click(screen.getByRole("button", { name: "Create schedule" }));

    expect(await fieldOf("Effective from").findByText("Choose the first day the schedule applies.")).toBeInTheDocument();
    expect(creates(requests)).toHaveLength(0);
  });

  it("offers dates from tomorrow (UTC) and explains UTC days (F11)", async () => {
    renderCreate();

    // 20:00 UTC on 20 Nov is already 21 Nov in Auckland; tomorrow in UTC is still 21 Nov.
    expect(await screen.findByLabelText("Effective from")).toHaveAttribute("min", "2026-11-21");
    expect(input("Effective from")).toHaveAttribute("type", "date");
    expect(
      fieldOf("Effective from").getByText(
        "Dates are UTC days. A schedule from 1 Dec applies to trades booked from 1 Dec 00:00 UTC (1 Dec 13:00 in New Zealand during daylight time).",
      ),
    ).toBeInTheDocument();
  });

  it("shows a 400 errors.EffectiveFrom under the effective-from field (F12)", async () => {
    renderCreate((request) =>
      request.method === "POST"
        ? problem(400, {
            errors: {
              EffectiveFrom: ["Effective from must be after 2026-12-03."],
              "Bands[1].From": ["Each band must start above the one before it."],
            },
          })
        : undefined,
    );
    await fillValidSchedule();
    fireEvent.click(screen.getByRole("button", { name: "Create schedule" }));

    expect(await fieldOf("Effective from").findByText("Effective from must be after 2026-12-03.")).toBeInTheDocument();
    expect(fieldOf("Band 2 from").getByText("Each band must start above the one before it.")).toBeInTheDocument();
  });
});
