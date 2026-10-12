import { configureStore } from "@reduxjs/toolkit";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { sessionSlice, setCurrentUser, type CurrentUser } from "@/features/session/sessionSlice";
import { CreateTransactionPage } from "@/features/transactions/CreateTransactionPage";
import { api } from "@/shared/api/api";

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
      return json({ items: [instrument], page: 1, pageSize: 100, totalCount: 1 });
    }
    if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
      return json({ items: [holding] });
    }
    if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
      return json({ ...account, ...accountOverride });
    }
    if (url.endsWith("/transactions") && method === "POST") {
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
          errors: { "": [problem] },
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

  it("posts a Buy without a client price and returns to the account", async () => {
    const { fetchMock, router } = renderForm();

    expect(await screen.findByRole("heading", { name: "New transaction" })).toBeInTheDocument();
    await screen.findByRole("option", { name: "DEMO Demo Fund" });
    fireEvent.change(screen.getByLabelText("Type"), { target: { value: "Buy" } });
    fireEvent.change(screen.getByLabelText("Instrument"), { target: { value: instrumentId } });
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "50" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-11T10:00:00+13:00" },
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
      bookedAt: "2026-10-11T10:00:00+13:00",
      memo: null,
      reference: null,
      security: [{ instrumentId, quantity: 50 }],
    });
    expect(body).not.toHaveProperty("tenantId");
    expect(body).not.toHaveProperty("amount");
    expect(body).not.toHaveProperty("cash");
    expect(body).not.toHaveProperty("currency");
    expect(body).not.toHaveProperty("price");
    expect(body).not.toHaveProperty("cost");
    expect(body).not.toHaveProperty("ratio");
  });

  it("stays on the form when the server has no price", async () => {
    const { router } = renderForm(user(), 400);

    await screen.findByRole("option", { name: "DEMO Demo Fund" });
    fireEvent.change(screen.getByLabelText("Instrument"), { target: { value: instrumentId } });
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "50" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-11T10:00:00+13:00" },
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
    await screen.findByRole("option", { name: "DEMO Demo Fund" });
    fireEvent.change(screen.getByLabelText("Type"), { target: { value: "Sell" } });
    fireEvent.change(screen.getByLabelText("Instrument"), { target: { value: instrumentId } });
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "200" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-11T10:00:00+13:00" },
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

    await screen.findByRole("option", { name: "DEMO Demo Fund" });
    fireEvent.change(screen.getByLabelText("Instrument"), { target: { value: instrumentId } });
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "50" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-11T10:00:00+13:00" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(await screen.findByText("Cost cannot be set on a buy or sell.")).toBeInTheDocument();
    expect(screen.queryByText("account detail")).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/accounts/${accountId}/transactions/new`);
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("posts StockIn with client cost and no cash", async () => {
    const { fetchMock } = renderForm();

    await screen.findByRole("option", { name: "DEMO Demo Fund" });
    fireEvent.change(screen.getByLabelText("Type"), { target: { value: "StockIn" } });
    fireEvent.change(screen.getByLabelText("Instrument"), { target: { value: instrumentId } });
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "20" } });
    fireEvent.change(screen.getByLabelText("Cost"), { target: { value: "300" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-11T10:00:00+13:00" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(await screen.findByText("account detail")).toBeInTheDocument();
    const body = await postedBody(fetchMock);
    expect(body).toEqual({
      accountId,
      type: "StockIn",
      bookedAt: "2026-10-11T10:00:00+13:00",
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

    await screen.findByRole("option", { name: "DEMO Demo Fund" });
    fireEvent.change(screen.getByLabelText("Type"), { target: { value: "StockOut" } });
    expect(screen.queryByLabelText("Cost")).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Instrument"), { target: { value: instrumentId } });
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "20" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-11T10:00:00+13:00" },
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

      await screen.findByRole("option", { name: "DEMO Demo Fund" });
      fireEvent.change(screen.getByLabelText("Type"), { target: { value: type } });
      expect(screen.getByLabelText("New total quantity")).toBeInTheDocument();
      expect(screen.queryByLabelText("Cost")).not.toBeInTheDocument();
      expect(screen.queryByLabelText("Ratio")).not.toBeInTheDocument();
      fireEvent.change(screen.getByLabelText("Instrument"), { target: { value: instrumentId } });
      fireEvent.change(screen.getByLabelText("New total quantity"), { target: { value: "200" } });
      fireEvent.change(screen.getByLabelText("Booked at"), {
        target: { value: "2026-10-12T10:00:00+13:00" },
      });
      fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

      expect(await screen.findByText("account detail")).toBeInTheDocument();
      const body = await postedBody(fetchMock);
      expect(body).toMatchObject({
        type,
        bookedAt: "2026-10-12T10:00:00+13:00",
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

    await screen.findByRole("option", { name: "DEMO Demo Fund" });
    fireEvent.change(screen.getByLabelText("Instrument"), { target: { value: instrumentId } });
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "50" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-11T10:00:00+13:00" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(await screen.findByText("account detail")).toBeInTheDocument();
    const body = await postedBody(fetchMock);
    expect(body).not.toHaveProperty("tenantId");
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it.each(["Bank", "Cash"])("does not offer security types on a %s account", async (type) => {
    renderForm(user(), 201, "Price is not available.", { type });

    await waitFor(() => {
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

    await screen.findByRole("option", { name: "DEMO Demo Fund" });
    fireEvent.change(screen.getByLabelText("Instrument"), { target: { value: instrumentId } });
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "50" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-11T10:00:00+13:00" },
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

    await screen.findByRole("option", { name: "DEMO Demo Fund" });
    fireEvent.change(screen.getByLabelText("Instrument"), { target: { value: instrumentId } });
    fireEvent.change(screen.getByLabelText("Quantity"), { target: { value: "50" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-11T10:00:00+13:00" },
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

    expect(await screen.findByRole("option", { name: "TransferIn" })).toBeInTheDocument();
    expect(screen.queryByLabelText("Instrument")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Quantity")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Cost")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Currency")).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Amount"), { target: { value: "250.50" } });
    fireEvent.change(screen.getByLabelText("Memo"), { target: { value: "Salary" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-12T09:00:00+13:00" },
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
      bookedAt: "2026-10-12T09:00:00+13:00",
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

    expect(await screen.findByRole("option", { name: "TransferOut" })).toBeInTheDocument();
    expect(screen.queryByLabelText("Instrument")).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Amount"), { target: { value: "-40.25" } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-12T09:30:00+13:00" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Post transaction" }));

    expect(await screen.findByText("account detail")).toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/accounts/${accountId}`);
    const body = await postedBody(fetchMock);
    expect(body).toEqual({
      accountId,
      type: "TransferOut",
      amount: -40.25,
      bookedAt: "2026-10-12T09:30:00+13:00",
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

    await screen.findByRole("option", { name: type });
    fireEvent.change(screen.getByLabelText("Amount"), { target: { value: amount } });
    fireEvent.change(screen.getByLabelText("Booked at"), {
      target: { value: "2026-10-12T09:00:00+13:00" },
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
});
