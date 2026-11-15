import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { CreateTradeSchedulePage } from "@/features/fees/CreateTradeSchedulePage";
import { chooseOption } from "@/test/chooseOption";
import { feeUser, json, problem, renderFees, requestsTo, stubBrowser, type Handler } from "@/test/fees";

const routes = [{ path: "/fees/trade/new", element: <CreateTradeSchedulePage /> }];

type PreviewBody = { minimum: number; amounts: number[] };

/** One response item; the values are not R8 arithmetic on purpose, so the page must show them as sent. */
function item(amount: number, fee: number, limitApplied: string | null = null) {
  return {
    amount,
    bands: [
      { from: 0, ratePercent: 0.5, slice: Math.min(amount, 10000), fee: 1.2345 },
      ...(amount > 10000 ? [{ from: 10000, ratePercent: 0.3, slice: amount - 10000, fee: 6.789 }] : []),
    ],
    banded: 8.0235,
    limitApplied,
    fee,
  };
}

function previewResponse(body: PreviewBody) {
  return json({
    currency: "NZD",
    items: body.amounts.map((amount, index) => item(amount, body.minimum + index + 0.11, index === 0 ? "minimum" : null)),
  });
}

const answerPreviews: Handler = async (request) => {
  if (request.method === "POST" && new URL(request.url).pathname === "/api/fees/trade-schedules/preview") {
    return previewResponse((await request.json()) as PreviewBody);
  }
  return undefined;
};

function type(label: string, value: string) {
  fireEvent.change(screen.getByLabelText(label), { target: { value } });
}

function fieldOf(label: string) {
  return within(screen.getByLabelText(label).closest('[data-slot="field"]') as HTMLElement);
}

async function fillSchedule() {
  await chooseOption("Quote currency", "NZD");
  type("Minimum", "10");
  type("Band 1 rate (%)", "0.5");
  fireEvent.click(screen.getByRole("button", { name: "Add band" }));
  type("Band 2 from", "10000");
  type("Band 2 rate (%)", "0.3");
}

function previews(requests: Request[]) {
  return requestsTo(requests, "POST", "/fees/trade-schedules/preview");
}

const settle = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

describe("Fee preview on the schedule form", () => {
  beforeEach(() => {
    stubBrowser("/fees/trade/new");
  });

  it("sends nothing and says why while the form is incomplete (F19)", async () => {
    const { requests } = renderFees(feeUser(), routes, "/fees/trade/new", answerPreviews);
    await chooseOption("Quote currency", "NZD");
    type("Minimum", "10");
    await settle(400);

    expect(screen.getByText("Complete the bands, minimum and cap to see a preview.")).toBeInTheDocument();
    expect(previews(requests)).toHaveLength(0);
  });

  it("sends one preview of the current values with the prefilled amounts (F16)", async () => {
    const { requests } = renderFees(feeUser(), routes, "/fees/trade/new", answerPreviews);
    await fillSchedule();
    type("Effective from", "2026-12-01");

    await screen.findByRole("table", { name: "Fee preview" });
    await settle(400);
    const sent = previews(requests);
    expect(sent).toHaveLength(1);
    expect(sent[0].headers.get("X-MyWealth-Request")).toBe("1");
    expect(await sent[0].json()).toEqual({
      quoteCurrency: "NZD",
      minimum: 10,
      cap: null,
      bands: [
        { from: 0, ratePercent: 0.5 },
        { from: 10000, ratePercent: 0.3 },
      ],
      amounts: [1000, 10000, 100000],
    });
  });

  it("waits 300 ms after the last change before sending again (F16)", async () => {
    const { requests } = renderFees(feeUser(), routes, "/fees/trade/new", answerPreviews);
    await fillSchedule();
    await waitFor(() => expect(previews(requests)).toHaveLength(1));

    type("Minimum", "1");
    type("Minimum", "15");
    type("Cap", "500");
    await settle(150);
    expect(previews(requests)).toHaveLength(1);

    await waitFor(() => expect(previews(requests)).toHaveLength(2));
    await settle(400);
    expect(previews(requests)).toHaveLength(2);
    expect(await previews(requests)[1].json()).toMatchObject({ minimum: 15, cap: 500 });
  });

  it("does not let a late response from an older request overwrite a newer one (F16)", async () => {
    let releaseFirst: () => void = () => {};
    const firstHeld = new Promise<void>((resolve) => {
      releaseFirst = resolve;
    });
    const { requests } = renderFees(feeUser(), routes, "/fees/trade/new", async (request) => {
      if (request.method !== "POST" || !request.url.endsWith("/preview")) {
        return undefined;
      }
      const body = (await request.json()) as PreviewBody;
      if (body.minimum === 10) {
        await firstHeld;
      }
      return previewResponse(body);
    });
    await fillSchedule();
    await waitFor(() => expect(previews(requests)).toHaveLength(1));

    type("Minimum", "20");
    const table = await screen.findByRole("table", { name: "Fee preview" });
    expect(within(table).getByText("20.11 NZD")).toBeInTheDocument();

    releaseFirst();
    await settle(50);
    expect(within(screen.getByRole("table", { name: "Fee preview" })).getByText("20.11 NZD")).toBeInTheDocument();
    expect(screen.queryByText("10.11 NZD")).not.toBeInTheDocument();
  });

  it("shows each amount's fee, the limit applied and the band breakdown from the response (F17)", async () => {
    renderFees(feeUser(), routes, "/fees/trade/new", answerPreviews);
    await fillSchedule();

    const table = await screen.findByRole("table", { name: "Fee preview" });
    const first = within(within(table).getByText("1,000.00 NZD").closest("tr") as HTMLElement);
    expect(first.getByText("10.11 NZD")).toBeInTheDocument();
    expect(first.getByText("minimum applied")).toBeInTheDocument();
    const third = within(within(table).getByText("100,000.00 NZD").closest("tr") as HTMLElement);
    expect(third.getByText("12.11 NZD")).toBeInTheDocument();
    expect(third.queryByText(/applied/)).not.toBeInTheDocument();

    expect(screen.queryByRole("table", { name: "Bands for 100,000.00 NZD" })).not.toBeInTheDocument();
    fireEvent.click(third.getByRole("button", { name: "Show bands for 100,000.00 NZD" }));
    const bands = within(screen.getByRole("table", { name: "Bands for 100,000.00 NZD" }));
    const second = within(bands.getByText("10,000.00 NZD").closest("tr") as HTMLElement);
    expect(second.getByText("0.3%")).toBeInTheDocument();
    expect(second.getByText("90,000.00 NZD")).toBeInTheDocument();
    expect(second.getByText("6.789 NZD")).toBeInTheDocument();
    expect(bands.getByText("1.2345 NZD")).toBeInTheDocument();
    expect(screen.getByText("Banded 8.0235 NZD")).toBeInTheDocument();
  });

  it("previews up to 5 amounts the user changes, adds or removes (F16)", async () => {
    const { requests } = renderFees(feeUser(), routes, "/fees/trade/new", answerPreviews);
    await fillSchedule();
    await waitFor(() => expect(previews(requests)).toHaveLength(1));

    expect((screen.getByLabelText("Trade amount 1") as HTMLInputElement).value).toBe("1000");
    type("Trade amount 2", "2500");
    fireEvent.click(screen.getByRole("button", { name: "Remove amount 1" }));
    fireEvent.click(screen.getByRole("button", { name: "Add amount" }));
    type("Trade amount 3", "50");
    await waitFor(() => expect(previews(requests)).toHaveLength(2));
    expect((await previews(requests)[1].json()).amounts).toEqual([2500, 100000, 50]);

    fireEvent.click(screen.getByRole("button", { name: "Add amount" }));
    fireEvent.click(screen.getByRole("button", { name: "Add amount" }));
    expect(screen.getByLabelText("Trade amount 5")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Add amount" })).not.toBeInTheDocument();
    expect(screen.getByText("You can preview up to 5 amounts.")).toBeInTheDocument();
  });

  it("checks each amount before sending", async () => {
    const { requests } = renderFees(feeUser(), routes, "/fees/trade/new", answerPreviews);
    await fillSchedule();
    await waitFor(() => expect(previews(requests)).toHaveLength(1));

    type("Trade amount 2", "0");
    await settle(400);

    expect(fieldOf("Trade amount 2").getByText("Each amount must be above 0 with at most 4 decimals.")).toBeInTheDocument();
    expect(previews(requests)).toHaveLength(1);
  });

  it("shows a preview 400's errors under the form fields (F19)", async () => {
    renderFees(feeUser(), routes, "/fees/trade/new", (request) =>
      request.method === "POST" && request.url.endsWith("/preview")
        ? problem(400, {
            errors: {
              "Bands[1].RatePercent": ["The rate must be between 0 and 100 with at most 4 decimals."],
              "Amounts[2]": ["Each amount must be above 0 with at most 4 decimals."],
            },
          })
        : undefined,
    );
    await fillSchedule();

    expect(
      await fieldOf("Band 2 rate (%)").findByText("The rate must be between 0 and 100 with at most 4 decimals."),
    ).toBeInTheDocument();
    expect(fieldOf("Trade amount 3").getByText("Each amount must be above 0 with at most 4 decimals.")).toBeInTheDocument();
  });
});
