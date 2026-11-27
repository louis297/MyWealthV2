import { configureStore } from "@reduxjs/toolkit";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { sessionSlice, setCurrentUser, type CurrentUser } from "@/features/session/sessionSlice";
import { CreateTransactionPage } from "@/features/transactions/CreateTransactionPage";
import { api } from "@/shared/api/api";
import { chooseOption, openSelect, pickOption } from "@/test/chooseOption";

const accountId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
const instrumentId = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";
const createdId = "dddddddd-dddd-dddd-dddd-dddddddddddd";
const idempotencyKey = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

const account = {
  id: accountId,
  tenantId: "22222222-2222-2222-2222-222222222222",
  customerId: "cccccccccccccccc-cccc-cccc-cccc-cccccccccccc",
  name: "Demo brokerage",
  type: "Brokerage",
  custodyMode: "External",
  currency: "NZD",
  status: "Open",
  isActive: true,
  cashBalance: 1250.5,
  rowVersion: "AAAA",
};

const instrument = {
  id: instrumentId,
  tenantId: account.tenantId,
  symbol: "DEMO",
  name: "Demo Fund",
  quoteCurrency: "USD",
  isActive: true,
  rowVersion: "AAAA",
};

const localInstrumentId = "eeeeeeee-eeee-eeee-eeee-111111111111";
const localInstrument = {
  ...instrument,
  id: localInstrumentId,
  symbol: "LOCAL",
  name: "Local Fund",
  quoteCurrency: "NZD",
};

function user(overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    name: "Sam Reed",
    email: "sam@north.example",
    role: "adviser",
    status: "active",
    tenantId: account.tenantId,
    tenantCode: "north-advisory",
    adviserId: "ffffffff-ffff-ffff-ffff-ffffffffffff",
    rowVersion: "AAAA",
    ...overrides,
  };
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function requestUrl(input: RequestInfo | URL) {
  if (typeof input === "string") {
    return input;
  }
  if (input instanceof Request) {
    return input.url;
  }
  return String(input);
}

const holding = {
  id: "hhhhhhhh-hhhh-hhhh-hhhh-hhhhhhhhhhhh",
  accountId,
  instrumentId,
  symbol: "DEMO",
  name: "Demo Fund",
  quantity: 100,
  cost: 5000,
  costCurrency: "NZD",
};

function renderForm(
  me: CurrentUser = user(),
  postStatus = 201,
  problem = "Price is not available.",
  accountOverride: Partial<typeof account> = {},
  problemShape: "errors" | "disabled" = "errors",
  search = "",
  accountStatus = 200,
  assigned = false,
  fieldErrors: Record<string, string[]> | null = null,
) {
  const store = configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });
  store.dispatch(setCurrentUser(me));

  const fetchMock = vi.mocked(fetch);
  fetchMock.mockImplementation(async (input, init) => {
    const url = requestUrl(input);
    const method = init?.method ?? (input instanceof Request ? input.method : "GET");
    if (url.includes("/instruments") && method === "GET") {
      return json({ items: [instrument, localInstrument], page: 1, pageSize: 100, totalCount: 2 });
    }
    if (url.endsWith(`/accounts/${accountId}/portfolio`) && method === "GET") {
      return assigned
        ? json({
            accountId,
            portfolioId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            status: "Active",
            rebalance: null,
          })
        : new Response("", { status: 404 });
    }
    if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
      return json({ items: [holding] });
    }
    if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
      if (accountStatus === 404) {
        return new Response("", { status: 404 });
      }
      return json({ ...account, ...accountOverride });
    }
    if ((url.endsWith("/transactions") || url.endsWith("/transactions/dividend")) && method === "POST") {
      if (postStatus === 404) {
        return new Response("", { status: 404 });
      }
      if (postStatus === 400 && problemShape === "disabled") {
        return json({
          type: "https://tools.ietf.org/html/rfc9110#section-15.5.1",
          title: "User is disabled",
          status: 400,
          detail: problem,
          code: "disabled",
          target: "user",
          targetId: account.customerId,
        }, 400);
      }
      if (postStatus === 400) {
        return json({
          type: "https://tools.ietf.org/html/rfc9110#section-15.5.1",
          title: "One or more validation errors occurred.",
          status: 400,
          errors: fieldErrors ?? { "": [problem] },
        }, 400);
      }
      return json({ id: createdId }, 201);
    }
    return new Response("", { status: 404 });
  });

  const router = createMemoryRouter(
    [
      { path: "/accounts/:id/transactions/new", element: <CreateTransactionPage /> },
      { path: "/accounts/:id", element: <p>account detail</p> },
    ],
    { initialEntries: [`/accounts/${accountId}/transactions/new${search}`] },
  );

  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );

  return { fetchMock, router };
}

/** D22: the executed price and the FX rate an External Buy / Sell of the USD instrument needs. */
function fillCallerNumbers() {
  fireEvent.change(screen.getByLabelText("Price per unit (USD)"), { target: { value: "25" } });
  fireEvent.change(screen.getByLabelText("FX rate (1 USD = ? NZD)"), { target: { value: "1.6" } });
}

describe("CreateTransactionPage", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
    vi.spyOn(crypto, "randomUUID").mockReturnValue(idempotencyKey);
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: `/accounts/${accountId}/transactions/new`,
      search: "",
      href: `http://localhost:3000/accounts/${accountId}/transactions/new`,
      assign: vi.fn(),
    });
  });

  it("posts an External Buy with the executed price, the fee and the FX rate and returns to the account (D22)", async () => {
    const { fetchMock, router } = renderForm();

    expect(await screen.findByRole("heading", { name: "New transaction" })).toBeInTheDocument();
    await chooseOption("Type", "Buy");
    await chooseOption("Instrument", "DEMO Demo Fund");
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "50" } });
    fireEvent.change(screen.getByLabelText("Price per unit (USD)"), { target: { value: "180.00" } });
    fireEvent.change(screen.getByLabelText("Fee (USD)"), { target: { value: "9.95" } });
    fireEvent.change(screen.getByLabelText("FX rate (1 USD = ? NZD)"), { target: { value: "1.69147" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-11T10:00" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(await screen.findByText("account detail")).toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/accounts/${accountId}`);
    expect(document.querySelector('input[type="password"]')).toBeNull();

    const instrumentRequest = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.url.includes("/instruments"));
    const instrumentUrl = new URL(instrumentRequest?.url ?? "");
    expect(instrumentUrl.origin + instrumentUrl.pathname).toBe("http://localhost:3000/api/instruments");
    expect(instrumentUrl.searchParams.get("enabledOnly")).toBe("true");
    expect(instrumentUrl.searchParams.get("page")).toBe("1");
    expect(instrumentUrl.searchParams.get("pageSize")).toBe("100");
    expect(instrumentUrl.searchParams.has("tenantId")).toBe(false);
    expect(instrumentRequest?.headers.get("Authorization")).toBeNull();
    expect(instrumentRequest?.credentials).toBe("include");

    const post = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.method === "POST");
    expect(post?.url).toBe("http://localhost:3000/api/transactions");
    expect(post?.headers.get("Authorization")).toBeNull();
    expect(post?.headers.get("X-MyWealth-Request")).toBe("1");
    expect(post?.headers.get("Idempotency-Key")).toBe(idempotencyKey);
    expect(post?.credentials).toBe("include");
    const body = JSON.parse(await post!.clone().text()) as Record<string, unknown>;
    expect(body).toEqual({
      accountId,
      type: "Buy",
      bookedAt: "2026-10-10T21:00:00.000Z",
      memo: null,
      reference: null,
      security: [{ instrumentId, quantity: 50, price: 180 }],
      fee: 9.95,
      fxRate: 1.69147,
    });
    expect(body).not.toHaveProperty("tenantId");
    expect(body).not.toHaveProperty("amount");
    expect(body).not.toHaveProperty("cash");
    expect(body).not.toHaveProperty("currency");
    expect(body).not.toHaveProperty("cost");
    expect(body).not.toHaveProperty("ratio");
  });

  it("shows Price and Fee only once an instrument is picked on an External account (D22)", async () => {
    renderForm();

    await chooseOption("Type", "Sell");
    expect(screen.queryByLabelText(/Price per unit/)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/^Fee/)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/FX rate/)).not.toBeInTheDocument();

    await chooseOption("Instrument", "DEMO Demo Fund");

    expect(screen.getByLabelText("Price per unit (USD)")).toBeRequired();
    expect(screen.getByLabelText("Fee (USD)")).not.toBeRequired();
    expect(screen.getByLabelText("FX rate (1 USD = ? NZD)")).toBeRequired();
    expect(screen.queryByText("Brokerage is added by the server from the trade fee schedule.")).not.toBeInTheDocument();
  });

  it("asks for no FX rate and sends none when the instrument is in the account currency (D22)", async () => {
    const { fetchMock } = renderForm();

    await chooseOption("Type", "Sell");
    await chooseOption("Instrument", "LOCAL Local Fund");
    expect(screen.getByLabelText("Price per unit (NZD)")).toBeInTheDocument();
    expect(screen.getByLabelText("Fee (NZD)")).toBeInTheDocument();
    expect(screen.queryByLabelText(/FX rate/)).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "40" } });
    fireEvent.change(screen.getByLabelText("Price per unit (NZD)"), { target: { value: "2.50" } });
    fireEvent.change(screen.getByLabelText("Booked at"), { target: { value: "2026-11-18T11:30" } });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(await screen.findByText("account detail")).toBeInTheDocument();
    const body = await postedBody(fetchMock);
    expect(body.security).toEqual([{ instrumentId: localInstrumentId, quantity: 40, price: 2.5 }]);
    expect(body).not.toHaveProperty("fee");
    expect(body).not.toHaveProperty("fxRate");
  });

  it("shows a 400 on Security[0].Price, Fee or FxRate at its input (D22)", async () => {
    const { router } = renderForm(user(), 400, "", {}, "errors", "", 200, false, {
      "Security[0].Price": ["Price can have at most 8 decimal places."],
      Fee: ["Fee can have at most 2 decimal places."],
      FxRate: ["FxRate must be greater than 0."],
    });

    await chooseOption("Type", "Buy");
    await chooseOption("Instrument", "DEMO Demo Fund");
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "1" } });
    fireEvent.change(screen.getByLabelText("Price per unit (USD)"), { target: { value: "1.123456789" } });
    fireEvent.change(screen.getByLabelText("Fee (USD)"), { target: { value: "1.005" } });
    fireEvent.change(screen.getByLabelText("FX rate (1 USD = ? NZD)"), { target: { value: "0.1" } });
    fireEvent.change(screen.getByLabelText("Booked at"), { target: { value: "2026-11-18T11:30" } });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(await screen.findByText("Price can have at most 8 decimal places.")).toHaveAttribute("id", "transaction-price-error");
    expect(screen.getByLabelText("Price per unit (USD)")).toHaveAttribute("aria-describedby", "transaction-price-error");
    expect(screen.getByText("Fee can have at most 2 decimal places.")).toHaveAttribute("id", "transaction-fee-error");
    expect(screen.getByLabelText("Fee (USD)")).toHaveAttribute("aria-describedby", "transaction-fee-error");
    expect(screen.getByText("FxRate must be greater than 0.")).toHaveAttribute("id", "transaction-fx-rate-error");
    expect(screen.getByLabelText("FX rate (1 USD = ? NZD)")).toHaveAttribute("aria-describedby", "transaction-fx-rate-error");
    expect(router.state.location.pathname).toBe(`/accounts/${accountId}/transactions/new`);
  });

  it("stays on the form when the server has no price", async () => {
    const { router } = renderForm(user(), 400);

    await chooseOption("Instrument", "DEMO Demo Fund");
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "50" } });
    fireEvent.change(screen.getByLabelText("Price per unit (USD)"), { target: { value: "25" } });
    fireEvent.change(screen.getByLabelText("FX rate (1 USD = ? NZD)"), { target: { value: "1.6" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-11T10:00" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(await screen.findByText("Price is not available.")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "New transaction" })).toBeInTheDocument();
    expect(screen.queryByText("account detail")).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/accounts/${accountId}/transactions/new`);
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("keeps the original cash and quantity when a Sell is rejected", async () => {
    const { router } = renderForm(user(), 400, "Quantity cannot exceed the holding quantity.");

    expect(await screen.findByText("1250.5")).toBeInTheDocument();
    expect(screen.getByText("100")).toBeInTheDocument();
    await chooseOption("Type", "Sell");
    await chooseOption("Instrument", "DEMO Demo Fund");
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "200" } });
    fireEvent.change(screen.getByLabelText("Price per unit (USD)"), { target: { value: "25" } });
    fireEvent.change(screen.getByLabelText("FX rate (1 USD = ? NZD)"), { target: { value: "1.6" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-11T10:00" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(await screen.findByText("Quantity cannot exceed the holding quantity.")).toBeInTheDocument();
    expect(screen.getByText("1250.5")).toBeInTheDocument();
    expect(screen.getByText("100")).toBeInTheDocument();
    expect(screen.queryByText("account detail")).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/accounts/${accountId}/transactions/new`);
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  async function postedBody(fetchMock: ReturnType<typeof vi.mocked<typeof fetch>>) {
    const post = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.method === "POST");
    return JSON.parse(await post!.clone().text()) as Record<string, unknown>;
  }

  it("stays on the form when Buy is rejected for a cost field", async () => {
    const { router } = renderForm(user(), 400, "Cost cannot be set on a buy or sell.");

    await chooseOption("Instrument", "DEMO Demo Fund");
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "50" } });
    fillCallerNumbers();
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-11T10:00" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(await screen.findByText("Cost cannot be set on a buy or sell.")).toBeInTheDocument();
    expect(screen.queryByText("account detail")).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/accounts/${accountId}/transactions/new`);
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("shows no brokerage note on a StockIn", async () => {
    renderForm();

    await chooseOption("Type", "StockIn");

    expect(await screen.findByLabelText("Cost")).toBeInTheDocument();
    expect(screen.queryByText("Brokerage is added by the server from the trade fee schedule.")).not.toBeInTheDocument();
  });

  it("posts StockIn with client cost and no cash", async () => {
    const { fetchMock } = renderForm();

    await chooseOption("Type", "StockIn");
    await chooseOption("Instrument", "DEMO Demo Fund");
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "20" } });
    fireEvent.change(screen.getByLabelText("Cost"), { target: { value: "300" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-11T10:00" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(await screen.findByText("account detail")).toBeInTheDocument();
    const body = await postedBody(fetchMock);
    expect(body).toEqual({
      accountId,
      type: "StockIn",
      bookedAt: "2026-10-10T21:00:00.000Z",
      memo: null,
      reference: null,
      security: [{ instrumentId, quantity: 20, cost: 300 }],
    });
    expect(body).not.toHaveProperty("cash");
    expect(body).not.toHaveProperty("amount");
    expect(body).not.toHaveProperty("price");
    expect(body).not.toHaveProperty("tenantId");
  });

  it("posts StockOut with quantity only", async () => {
    const { fetchMock } = renderForm();

    await chooseOption("Type", "StockOut");
    expect(screen.queryByLabelText("Cost")).not.toBeInTheDocument();
    await chooseOption("Instrument", "DEMO Demo Fund");
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "20" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-11T10:00" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(await screen.findByText("account detail")).toBeInTheDocument();
    const body = await postedBody(fetchMock);
    expect(body).toMatchObject({
      type: "StockOut",
      security: [{ instrumentId, quantity: 20 }],
    });
    const leg = (body.security as Record<string, unknown>[])[0];
    expect(leg).not.toHaveProperty("cost");
    expect(body).not.toHaveProperty("cash");
    expect(body).not.toHaveProperty("ratio");
    expect(body).not.toHaveProperty("tenantId");
  });

  it.each(["Split", "Bonus", "Scrip"])(
    "posts %s as a new total quantity with cost unchanged",
    async (type) => {
      const { fetchMock } = renderForm();

      await chooseOption("Type", type);
      expect(screen.getByLabelText("New total quantity")).toBeInTheDocument();
      expect(screen.queryByLabelText("Cost")).not.toBeInTheDocument();
      expect(screen.queryByLabelText("Ratio")).not.toBeInTheDocument();
      await chooseOption("Instrument", "DEMO Demo Fund");
      fireEvent.change(screen.getByLabelText("New total quantity"), { target: { value: "200" } });
      fireEvent.change(screen.getByLabelText("Booked at"), {
        target: { value: "2026-10-12T10:00" },
      });
      fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

      expect(await screen.findByText("account detail")).toBeInTheDocument();
      const body = await postedBody(fetchMock);
      expect(body).toMatchObject({
        type,
        bookedAt: "2026-10-11T21:00:00.000Z",
        security: [{ instrumentId, quantity: 200 }],
      });
      const leg = (body.security as Record<string, unknown>[])[0];
      expect(leg).not.toHaveProperty("cost");
      expect(leg).not.toHaveProperty("ratio");
      expect(body).not.toHaveProperty("cash");
      expect(body).not.toHaveProperty("amount");
      expect(body).not.toHaveProperty("ratio");
      expect(body).not.toHaveProperty("tenantId");
      expect(document.querySelector('input[type="password"]')).toBeNull();
    },
  );

  it("omits tenantId for a TenantAdmin", async () => {
    const { fetchMock } = renderForm(user({ role: "tenantAdmin", name: "Pat Admin", adviserId: null }));

    await chooseOption("Instrument", "DEMO Demo Fund");
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "50" } });
    fillCallerNumbers();
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-11T10:00" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(await screen.findByText("account detail")).toBeInTheDocument();
    const body = await postedBody(fetchMock);
    expect(body).not.toHaveProperty("tenantId");
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it.each(["Bank", "Cash"])("does not offer security types on a %s account", async (type) => {
    renderForm(user(), 201, "Price is not available.", { type });

    await openSelect("Type");
    await waitFor(() => {
      expect(screen.getByRole("option", { name: "TransferIn" })).toBeInTheDocument();
      expect(screen.queryByRole("option", { name: "Buy" })).not.toBeInTheDocument();
    });
    for (const securityType of ["Sell", "StockIn", "StockOut", "Split", "Bonus", "Scrip"]) {
      expect(screen.queryByRole("option", { name: securityType })).not.toBeInTheDocument();
    }
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("shows a closed-account rejection on the form", async () => {
    const { router } = renderForm(
      user(),
      400,
      "Cannot post a transaction while the account is closed. Reopen the account first.",
      { status: "Closed" },
    );

    await chooseOption("Instrument", "DEMO Demo Fund");
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "50" } });
    fillCallerNumbers();
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-11T10:00" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(
      await screen.findByText("Cannot post a transaction while the account is closed. Reopen the account first."),
    ).toBeInTheDocument();
    expect(screen.queryByText("account detail")).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/accounts/${accountId}/transactions/new`);
  });

  it("shows a disabled customer on create", async () => {
    const { router } = renderForm(
      user(),
      400,
      "Cannot create a transaction while the customer is disabled. Enable the customer first.",
      {},
      "disabled",
    );

    await chooseOption("Instrument", "DEMO Demo Fund");
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "50" } });
    fillCallerNumbers();
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-11T10:00" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(
      await screen.findByText("Cannot create a transaction while the customer is disabled. Enable the customer first."),
    ).toBeInTheDocument();
    expect(screen.queryByText("account detail")).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/accounts/${accountId}/transactions/new`);
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it.each([
    ["an Adviser", user()],
    ["a TenantAdmin", user({ role: "tenantAdmin", name: "Pat Admin", adviserId: null })],
  ])("posts a TransferIn for %s and returns to the account", async (_label, me) => {
    const { fetchMock, router } = renderForm(me, 201, "Price is not available.", {}, "errors", "?type=TransferIn");

    await openSelect("Type");
    expect(await screen.findByRole("option", { name: "TransferIn" })).toBeInTheDocument();
    fireEvent.click(screen.getByLabelText("Type"));
    expect(screen.queryByLabelText("Instrument")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Quantity")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Cost")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Currency")).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Amount"), { target: { value: "250.50" } });
    fireEvent.change(screen.getByLabelText("Memo"), { target: { value: "Salary" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-12T09:00" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(await screen.findByText("account detail")).toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/accounts/${accountId}`);

    const post = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.method === "POST");
    expect(post?.url).toBe("http://localhost:3000/api/transactions");
    expect(post?.headers.get("Authorization")).toBeNull();
    expect(post?.headers.get("X-MyWealth-Request")).toBe("1");
    expect(post?.headers.get("Idempotency-Key")).toBe(idempotencyKey);
    expect(post?.credentials).toBe("include");
    const body = JSON.parse(await post!.clone().text()) as Record<string, unknown>;
    expect(body).toEqual({
      accountId,
      type: "TransferIn",
      amount: 250.5,
      bookedAt: "2026-10-11T20:00:00.000Z",
      memo: "Salary",
    });
    expect(body).not.toHaveProperty("security");
    expect(body).not.toHaveProperty("instrumentId");
    expect(body).not.toHaveProperty("price");
    expect(body).not.toHaveProperty("cost");
    expect(body).not.toHaveProperty("currency");
    expect(body).not.toHaveProperty("cash");
    expect(body).not.toHaveProperty("tenantId");
    expect(body).not.toHaveProperty("reference");
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("posts a TransferOut with a negative amount and no security leg", async () => {
    const { fetchMock, router } = renderForm(
      user(),
      201,
      "Price is not available.",
      {},
      "errors",
      "?type=TransferOut",
    );

    await openSelect("Type");
    expect(await screen.findByRole("option", { name: "TransferOut" })).toBeInTheDocument();
    fireEvent.click(screen.getByLabelText("Type"));
    expect(screen.queryByLabelText("Instrument")).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Amount"), { target: { value: "-40.25" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-12T09:30" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(await screen.findByText("account detail")).toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/accounts/${accountId}`);
    const body = await postedBody(fetchMock);
    expect(body).toEqual({
      accountId,
      type: "TransferOut",
      amount: -40.25,
      bookedAt: "2026-10-11T20:30:00.000Z",
      memo: null,
    });
    expect(body).not.toHaveProperty("security");
    expect(body).not.toHaveProperty("instrumentId");
    expect(body).not.toHaveProperty("price");
    expect(body).not.toHaveProperty("cost");
    expect(body).not.toHaveProperty("currency");
    expect(body).not.toHaveProperty("tenantId");
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it.each([
    ["TransferIn", "0"],
    ["TransferIn", "-10"],
    ["TransferOut", "0"],
    ["TransferOut", "40"],
  ])("rejects a %s amount of %s before posting", async (type, amount) => {
    const { fetchMock, router } = renderForm(
      user(),
      201,
      "Price is not available.",
      {},
      "errors",
      `?type=${type}`,
    );

    await openSelect("Type");
    expect(await screen.findByRole("option", { name: type })).toBeInTheDocument();
    fireEvent.click(screen.getByLabelText("Type"));
    fireEvent.change(screen.getByLabelText("Amount"), { target: { value: amount } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-12T09:00" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(await screen.findByText("Amount sign is not valid for this transaction type.")).toBeInTheDocument();
    expect(screen.queryByText("account detail")).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/accounts/${accountId}/transactions/new`);
    const post = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.method === "POST");
    expect(post).toBeUndefined();
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("stays on the form when a TransferOut would make cash negative", async () => {
    const { fetchMock, router } = renderForm(
      user(),
      400,
      "Cash balance cannot become negative.",
      {},
      "errors",
      "?type=TransferOut",
    );

    expect(await screen.findByText("1250.5")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Amount"), { target: { value: "-2000" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-12T09:30" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(await screen.findByText("Cash balance cannot become negative.")).toBeInTheDocument();
    expect(screen.getByText("1250.5")).toBeInTheDocument();
    expect(screen.queryByText("account detail")).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/accounts/${accountId}/transactions/new`);
    const body = await postedBody(fetchMock);
    expect(body).toMatchObject({ type: "TransferOut", amount: -2000 });
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("shows Not found when the transfer account is not visible", async () => {
    const { fetchMock, router } = renderForm(
      user(),
      201,
      "Price is not available.",
      {},
      "errors",
      "?type=TransferIn",
      404,
    );

    expect(await screen.findByText("Not found")).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "New transaction" })).not.toBeInTheDocument();
    expect(screen.queryByText("Forbidden")).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/accounts/${accountId}/transactions/new`);
    const post = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.method === "POST");
    expect(post).toBeUndefined();
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("shows Not found when posting a transfer returns 404", async () => {
    const { router } = renderForm(
      user(),
      404,
      "Price is not available.",
      {},
      "errors",
      "?type=TransferOut",
    );

    await screen.findByLabelText("Amount");
    fireEvent.change(screen.getByLabelText("Amount"), { target: { value: "-10" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-12T09:30" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(await screen.findByText("Not found")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "New transaction" })).toBeInTheDocument();
    expect(screen.queryByText("account detail")).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/accounts/${accountId}/transactions/new`);
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it.each(["", "?type=Buy", "?type=TransferOut", "?type=CloseOut", "?type=TransferIn"])(
    "offers no type on a Custodial account and points to Place order (orders D69) %s",
    async (query) => {
      renderForm(user(), 201, "Price is not available.", { custodyMode: "Custodial" }, "errors", query);

      expect(await screen.findByText("Trades on a Custodial account go through the broker.")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Place an order" })).toHaveAttribute("href", `/accounts/${accountId}/order`);
      expect(screen.queryByLabelText("Type")).not.toBeInTheDocument();
      expect(screen.queryByLabelText("Amount")).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Post transaction" })).not.toBeInTheDocument();
    },
  );

  it("offers only cash transfers on an open Bank account", async () => {
    renderForm(user(), 201, "Price is not available.", { type: "Bank" }, "errors", "?type=TransferIn");

    await openSelect("Type");
    expect(await screen.findByRole("option", { name: "TransferIn" })).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByRole("option", { name: "Buy" })).not.toBeInTheDocument();
    });
    expect(screen.getByRole("option", { name: "TransferOut" })).toBeInTheDocument();
    expect(screen.queryByLabelText("Instrument")).not.toBeInTheDocument();
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("shows a closed-account rejection on a transfer", async () => {
    const { router } = renderForm(
      user(),
      400,
      "Cannot post a transaction while the account is closed. Reopen the account first.",
      { status: "Closed" },
      "errors",
      "?type=TransferOut",
    );

    fireEvent.change(await screen.findByLabelText("Amount"), { target: { value: "-10" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-12T09:30" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(
      await screen.findByText("Cannot post a transaction while the account is closed. Reopen the account first."),
    ).toBeInTheDocument();
    expect(screen.queryByText("account detail")).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/accounts/${accountId}/transactions/new`);
  });

  it("shows a disabled customer on a transfer", async () => {
    const { router } = renderForm(
      user(),
      400,
      "Cannot create a transaction while the customer is disabled. Enable the customer first.",
      {},
      "disabled",
      "?type=TransferIn",
    );

    fireEvent.change(await screen.findByLabelText("Amount"), { target: { value: "10" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-12T09:00" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(
      await screen.findByText("Cannot create a transaction while the customer is disabled. Enable the customer first."),
    ).toBeInTheDocument();
    expect(screen.queryByText("account detail")).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/accounts/${accountId}/transactions/new`);
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("blocks a security trade while a portfolio is assigned", async () => {
    const { fetchMock } = renderForm(
      user(),
      201,
      "Price is not available.",
      {},
      "errors",
      "?type=Buy",
      200,
      true,
    );

    expect(
      await screen.findByText("Manual security trades are blocked while a portfolio is assigned."),
    ).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Post transaction" })).not.toBeInTheDocument();
    expect(
      fetchMock.mock.calls.some(([input]) => input instanceof Request && input.method === "POST"),
    ).toBe(false);
  });

  it("still posts a cash transfer while a portfolio is assigned", async () => {
    const { fetchMock } = renderForm(
      user(),
      201,
      "Price is not available.",
      {},
      "errors",
      "?type=TransferIn",
      200,
      true,
    );

    fireEvent.change(await screen.findByLabelText("Amount"), { target: { value: "25" } });
    fireEvent.change(screen.getByLabelText("Booked at"), { target: { value: "2026-10-12T09:00" } });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(await screen.findByText("account detail")).toBeInTheDocument();
    const post = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.method === "POST");
    expect(JSON.parse(await post!.clone().text())).toMatchObject({ type: "TransferIn", amount: 25 });
  });

  async function postRequest(fetchMock: ReturnType<typeof vi.mocked<typeof fetch>>) {
    await waitFor(() => {
      expect(fetchMock.mock.calls.some(([input]) => input instanceof Request && input.method === "POST")).toBe(true);
    });
    return fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.method === "POST")!;
  }

  function taxRow(index: number) {
    return within(screen.getByRole("group", { name: `Tax ${index}` }));
  }

  async function addTax(index: number, kind: string, rate: string, amount: string, currency: string) {
    fireEvent.click(screen.getByRole("button", { name: "Add tax" }));
    const row = taxRow(index);
    await chooseOption("Kind", kind, row);
    fireEvent.change(row.getByLabelText("Rate %"), { target: { value: rate } });
    fireEvent.change(row.getByLabelText(`Amount (${currency})`), { target: { value: amount } });
  }

  describe("a dividend on an External account (external-income D111)", () => {
    it("posts a New Zealand company's dividend with its credit and RWT to /transactions/dividend", async () => {
      const { fetchMock, router } = renderForm(user(), 201, "", { type: "Bank" }, "errors", "?type=Dividend");

      await chooseOption("Type", "Dividend");
      await chooseOption("Instrument", "LOCAL Local Fund");
      fireEvent.change(screen.getByLabelText("Shares"), { target: { value: "1000" } });
      fireEvent.change(screen.getByLabelText("Gross amount (NZD)"), { target: { value: "2000.00" } });
      expect(screen.queryByLabelText("Imputation credit (NZD)")).not.toBeInTheDocument();
      await chooseOption("Company", "New Zealand");
      fireEvent.change(screen.getByLabelText("Imputation credit (NZD)"), { target: { value: "200.00" } });
      await addTax(1, "RWT", "33", "526.00", "NZD");
      expect(screen.getByText("Net 1474.00 NZD")).toBeInTheDocument();
      expect(screen.queryByLabelText(/FX rate/)).not.toBeInTheDocument();
      fireEvent.change(screen.getByLabelText("Booked at"), { target: { value: "2026-11-20T13:00" } });
      fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

      expect(await screen.findByText("account detail")).toBeInTheDocument();
      expect(router.state.location.pathname).toBe(`/accounts/${accountId}`);
      const post = await postRequest(fetchMock);
      expect(post.url).toBe("http://localhost:3000/api/transactions/dividend");
      expect(post.headers.get("Idempotency-Key")).toBe(idempotencyKey);
      expect(JSON.parse(await post.clone().text())).toEqual({
        accountId,
        instrumentId: localInstrumentId,
        quantity: 1000,
        grossAmount: 2000,
        taxSource: "NewZealand",
        imputationCredit: 200,
        taxes: [{ kind: "Rwt", ratePercent: 33, amount: 526 }],
        bookedAt: "2026-11-20T00:00:00.000Z",
        memo: null,
        reference: null,
      });
    });

    it("asks for the FX rate in another currency, offers Foreign tax and RWT for a foreign company, and sends the optional fields", async () => {
      const { fetchMock } = renderForm(user(), 201, "", {}, "errors", "?type=Dividend");

      await chooseOption("Type", "Dividend");
      await chooseOption("Instrument", "DEMO Demo Fund");
      fireEvent.change(screen.getByLabelText("Shares"), { target: { value: "10" } });
      fireEvent.change(screen.getByLabelText("Gross amount (USD)"), { target: { value: "2.60" } });
      fireEvent.change(screen.getByLabelText("Amount per share (USD)"), { target: { value: "0.26" } });
      await chooseOption("Company", "Foreign");
      expect(screen.queryByLabelText("Imputation credit (USD)")).not.toBeInTheDocument();
      fireEvent.click(screen.getByRole("button", { name: "Add tax" }));
      fireEvent.click(taxRow(1).getByLabelText("Kind"));
      expect(await screen.findByRole("option", { name: "Foreign tax" })).toBeInTheDocument();
      expect(screen.getByRole("option", { name: "RWT" })).toBeInTheDocument();
      expect(screen.queryByRole("option", { name: "NRWT" })).not.toBeInTheDocument();
      await pickOption("Foreign tax");
      fireEvent.change(taxRow(1).getByLabelText("Rate %"), { target: { value: "15" } });
      fireEvent.change(taxRow(1).getByLabelText("Amount (USD)"), { target: { value: "0.39" } });
      expect(screen.getByText("Net 2.21 USD")).toBeInTheDocument();
      fireEvent.change(screen.getByLabelText("FX rate (1 USD = ? NZD)"), { target: { value: "1.69147" } });
      fireEvent.change(screen.getByLabelText("Ex-date"), { target: { value: "2026-11-09" } });
      fireEvent.change(screen.getByLabelText("Record date"), { target: { value: "2026-11-10" } });
      fireEvent.change(screen.getByLabelText("Payment date"), { target: { value: "2026-11-13" } });
      fireEvent.change(screen.getByLabelText("Memo"), { target: { value: "Quarterly dividend" } });
      fireEvent.change(screen.getByLabelText("Reference"), { target: { value: "HATCH-2026-11" } });
      fireEvent.change(screen.getByLabelText("Booked at"), { target: { value: "2026-11-20T13:00" } });
      fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

      const post = await postRequest(fetchMock);
      expect(JSON.parse(await post.clone().text())).toEqual({
        accountId,
        instrumentId,
        quantity: 10,
        grossAmount: 2.6,
        amountPerShare: 0.26,
        taxSource: "Foreign",
        taxes: [{ kind: "ForeignWithholding", ratePercent: 15, amount: 0.39 }],
        fxRate: 1.69147,
        exDate: "2026-11-09",
        recordDate: "2026-11-10",
        paymentDate: "2026-11-13",
        bookedAt: "2026-11-20T00:00:00.000Z",
        memo: "Quarterly dividend",
        reference: "HATCH-2026-11",
      });
    });

    it("preselects no company, offers RWT and NRWT for a New Zealand company, and adds at most three taxes", async () => {
      renderForm(user(), 201, "", {}, "errors", "?type=Dividend");

      await chooseOption("Type", "Dividend");
      await chooseOption("Instrument", "LOCAL Local Fund");
      expect(screen.getByLabelText("Company")).toHaveTextContent(/select/i);
      await chooseOption("Company", "New Zealand");
      fireEvent.click(screen.getByRole("button", { name: "Add tax" }));
      fireEvent.click(taxRow(1).getByLabelText("Kind"));
      expect(await screen.findByRole("option", { name: "NRWT" })).toBeInTheDocument();
      expect(screen.queryByRole("option", { name: "Foreign tax" })).not.toBeInTheDocument();
      fireEvent.keyDown(document.activeElement ?? document.body, { key: "Escape" });
      fireEvent.click(screen.getByRole("button", { name: "Add tax" }));
      fireEvent.click(screen.getByRole("button", { name: "Add tax" }));
      expect(screen.getAllByRole("group", { name: /^Tax \d$/ })).toHaveLength(3);
      expect(screen.queryByRole("button", { name: "Add tax" })).not.toBeInTheDocument();
    });

    it("shows a 400 at its input, the taxes' error under the list and stays on the form", async () => {
      const { router } = renderForm(user(), 400, "", {}, "errors", "?type=Dividend", 200, false, {
        GrossAmount: ["GrossAmount can have at most 2 decimal places."],
        Taxes: ["Tax withheld cannot be more than the gross amount."],
        "Taxes[0].Amount": ["Amount can have at most 2 decimal places."],
      });

      await chooseOption("Type", "Dividend");
      await chooseOption("Instrument", "LOCAL Local Fund");
      fireEvent.change(screen.getByLabelText("Shares"), { target: { value: "10" } });
      fireEvent.change(screen.getByLabelText("Gross amount (NZD)"), { target: { value: "10.005" } });
      await chooseOption("Company", "New Zealand");
      await addTax(1, "RWT", "33", "3.301", "NZD");
      fireEvent.change(screen.getByLabelText("Booked at"), { target: { value: "2026-11-20T13:00" } });
      fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

      expect(await screen.findByText("GrossAmount can have at most 2 decimal places.")).toBeInTheDocument();
      expect(screen.getByLabelText("Gross amount (NZD)")).toHaveAttribute("aria-describedby", "dividend-gross-error");
      expect(screen.getByText("Tax withheld cannot be more than the gross amount.")).toHaveAttribute("id", "dividend-taxes-error");
      expect(taxRow(1).getByText("Amount can have at most 2 decimal places.")).toBeInTheDocument();
      expect(router.state.location.pathname).toBe(`/accounts/${accountId}/transactions/new`);
    });
  });

  describe("tax on a hand Interest (external-income D112)", () => {
    it("offers Interest, labels the amount the gross once a tax is added, shows the net and posts the taxes", async () => {
      const { fetchMock } = renderForm(user(), 201, "", { type: "Bank" }, "errors", "?type=Interest");

      await chooseOption("Type", "Interest");
      fireEvent.change(screen.getByLabelText("Amount (NZD)"), { target: { value: "12.34" } });
      await addTax(1, "RWT", "33", "4.07", "NZD");
      // The only "Amount" left is the tax row's: the interest's own input is now the gross.
      expect(screen.getAllByLabelText("Amount (NZD)")).toEqual([taxRow(1).getByLabelText("Amount (NZD)")]);
      expect(screen.getByLabelText("Gross interest (NZD)")).toHaveValue("12.34");
      expect(screen.getByText("Net 8.27 NZD")).toBeInTheDocument();
      fireEvent.change(screen.getByLabelText("Reference"), { target: { value: "STMT-11" } });
      fireEvent.change(screen.getByLabelText("Booked at"), { target: { value: "2026-11-26T13:00" } });
      fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

      const post = await postRequest(fetchMock);
      expect(post.url).toBe("http://localhost:3000/api/transactions");
      expect(JSON.parse(await post.clone().text())).toEqual({
        accountId,
        type: "Interest",
        amount: 12.34,
        bookedAt: "2026-11-26T00:00:00.000Z",
        memo: null,
        reference: "STMT-11",
        taxes: [{ kind: "Rwt", ratePercent: 33, amount: 4.07 }],
      });
    });

    it("offers RWT, NRWT and Foreign tax, and posts an Interest without a tax of either sign and without taxes", async () => {
      const { fetchMock } = renderForm(user(), 201, "", { type: "Bank" }, "errors", "?type=Interest");

      await chooseOption("Type", "Interest");
      fireEvent.click(screen.getByRole("button", { name: "Add tax" }));
      fireEvent.click(taxRow(1).getByLabelText("Kind"));
      expect(await screen.findByRole("option", { name: "Foreign tax" })).toBeInTheDocument();
      expect(screen.getByRole("option", { name: "NRWT" })).toBeInTheDocument();
      expect(screen.getByRole("option", { name: "RWT" })).toBeInTheDocument();
      fireEvent.keyDown(document.activeElement ?? document.body, { key: "Escape" });
      fireEvent.click(taxRow(1).getByRole("button", { name: "Remove" }));
      fireEvent.change(screen.getByLabelText("Amount (NZD)"), { target: { value: "-1.25" } });
      fireEvent.change(screen.getByLabelText("Booked at"), { target: { value: "2026-11-26T13:00" } });
      fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

      const body = JSON.parse(await (await postRequest(fetchMock)).clone().text()) as Record<string, unknown>;
      expect(body).toMatchObject({ type: "Interest", amount: -1.25 });
      expect(body).not.toHaveProperty("taxes");
    });

    it("refuses a gross interest that is not above 0 once a tax is added, and posts nothing", async () => {
      const { fetchMock } = renderForm(user(), 201, "", { type: "Bank" }, "errors", "?type=Interest");

      await chooseOption("Type", "Interest");
      fireEvent.change(screen.getByLabelText("Amount (NZD)"), { target: { value: "-1" } });
      await addTax(1, "RWT", "33", "1", "NZD");
      fireEvent.change(screen.getByLabelText("Booked at"), { target: { value: "2026-11-26T13:00" } });
      fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

      expect(await screen.findByText("Gross interest must be above 0.")).toBeInTheDocument();
      expect(fetchMock.mock.calls.some(([input]) => input instanceof Request && input.method === "POST")).toBe(false);
    });
  });
});
