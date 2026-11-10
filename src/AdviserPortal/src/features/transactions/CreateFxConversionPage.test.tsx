import { configureStore } from "@reduxjs/toolkit";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { sessionSlice, setCurrentUser, type CurrentUser } from "@/features/session/sessionSlice";
import { CreateFxConversionPage } from "@/features/transactions/CreateFxConversionPage";
import { api } from "@/shared/api/api";

const tenantId = "22222222-2222-2222-2222-222222222222";
const nedId = "c1111111-1111-1111-1111-111111111111";
const sourceId = "a0000000-0000-0000-0000-000000000001";
const savingsId = "a0000000-0000-0000-0000-000000000002";
const closedUsdId = "a0000000-0000-0000-0000-000000000003";
const usdId = "a0000000-0000-0000-0000-000000000004";
const createdId = "dddddddd-dddd-dddd-dddd-dddddddddddd";
const idempotencyKey = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

function account(id: string, name: string, overrides: Record<string, unknown> = {}) {
  return {
    id,
    tenantId,
    customerId: nedId,
    name,
    type: "Bank",
    currency: "NZD",
    status: "Open",
    isActive: true,
    cashBalance: 1000,
    rowVersion: "AAAA",
    ...overrides,
  };
}

const allAccounts = [
  account(sourceId, "Ned bank"),
  account(savingsId, "Ned savings"),
  account(closedUsdId, "Ned old USD", { currency: "USD", status: "Closed", isActive: false }),
  account(usdId, "Ned USD", { currency: "USD" }),
];

const ned = {
  id: nedId,
  tenantId,
  adviserId: "ffffffff-ffff-ffff-ffff-ffffffffffff",
  name: "Ned North",
  email: "ned@north.example",
  status: "Active",
  rowVersion: "AAAA",
  created: "2026-01-01T00:00:00Z",
};

function me(): CurrentUser {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    name: "Ann Adviser",
    email: "ann@north.example",
    role: "adviser",
    status: "active",
    tenantId,
    tenantCode: "north-advisory",
    adviserId: "ffffffff-ffff-ffff-ffff-ffffffffffff",
    rowVersion: "AAAA",
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

type QuoteBody = { sellAmount?: number; buyAmount?: number };

function quoteFor(body: QuoteBody, rate: number) {
  const sell = body.sellAmount ?? Math.round((body.buyAmount! / rate) * 100) / 100;
  const buy = body.buyAmount ?? Math.round(body.sellAmount! * rate * 100) / 100;
  return {
    baseCurrency: "NZD",
    quoteCurrency: "USD",
    rate,
    rateAsOfDate: "2026-11-09",
    sell: { amount: sell, currency: "NZD" },
    buy: { amount: buy, currency: "USD" },
  };
}

type Options = {
  accounts?: typeof allAccounts;
  quote?: (body: QuoteBody) => Response;
  create?: (body: Record<string, unknown>) => Response;
};

function renderForm(options: Options = {}) {
  const store = configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });
  store.dispatch(setCurrentUser(me()));
  const accounts = options.accounts ?? allAccounts;

  const fetchMock = vi.mocked(fetch);
  fetchMock.mockImplementation(async (input, init) => {
    const url = requestUrl(input);
    const method = init?.method ?? (input instanceof Request ? input.method : "GET");
    if (method === "POST" && url.endsWith("/transactions/fx/quote")) {
      const body = JSON.parse(await (input as Request).clone().text()) as QuoteBody;
      return options.quote ? options.quote(body) : json(quoteFor(body, 0.5912));
    }
    if (method === "POST" && url.endsWith("/transactions/fx")) {
      const body = JSON.parse(await (input as Request).clone().text()) as Record<string, unknown>;
      return options.create ? options.create(body) : json({ id: createdId }, 201);
    }
    if (method !== "GET") {
      return new Response("", { status: 404 });
    }
    if (url.endsWith(`/accounts/${sourceId}`)) {
      return json(accounts[0]);
    }
    if (url.endsWith(`/users/customers/${nedId}`)) {
      return json(ned);
    }
    if (url.includes("/accounts?")) {
      const customerId = new URL(url).searchParams.get("customerId");
      const items = customerId === nedId ? accounts : [];
      return json({ items, page: 1, pageSize: 100, totalCount: items.length });
    }
    return new Response("", { status: 404 });
  });

  const router = createMemoryRouter(
    [
      { path: "/accounts/:id/convert", element: <CreateFxConversionPage /> },
      { path: "/accounts/:id", element: <p>account detail</p> },
    ],
    { initialEntries: [`/accounts/${sourceId}/convert`] },
  );

  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );

  return { fetchMock, router };
}

function urls(fetchMock: ReturnType<typeof vi.mocked<typeof fetch>>) {
  return fetchMock.mock.calls.map(([input]) => requestUrl(input));
}

function posts(fetchMock: ReturnType<typeof vi.mocked<typeof fetch>>, suffix: string) {
  return fetchMock.mock.calls
    .map(([input]) => input)
    .filter((input): input is Request => input instanceof Request && input.method === "POST" && input.url.endsWith(suffix));
}

async function chooseUsdAndType(amount: string) {
  fireEvent.click(await screen.findByRole("radio", { name: /Ned USD/ }));
  fireEvent.change(screen.getByLabelText(/Amount/), { target: { value: amount } });
  fireEvent.change(screen.getByLabelText("Booked at"), { target: { value: "2026-11-10T09:00" } });
}

describe("CreateFxConversionPage", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
    vi.spyOn(crypto, "randomUUID").mockReturnValue(idempotencyKey);
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: `/accounts/${sourceId}/convert`,
      search: "",
      href: `http://localhost:3000/accounts/${sourceId}/convert`,
      assign: vi.fn(),
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("lists only the source Customer's Accounts, with no Customer search", async () => {
    const { fetchMock } = renderForm();

    expect(await screen.findByRole("heading", { name: "Convert" })).toBeInTheDocument();
    expect(await screen.findByRole("radio", { name: /Ned USD/ })).toBeEnabled();
    expect(screen.queryByLabelText("Search customers")).not.toBeInTheDocument();
    const accountQueries = urls(fetchMock).filter((url) => url.includes("/accounts?"));
    expect(accountQueries.length).toBeGreaterThan(0);
    expect(accountQueries.every((url) => url.includes(`customerId=${nedId}`) && url.includes("pageSize=100"))).toBe(true);
    expect(urls(fetchMock).some((url) => url.includes("/users/customers?"))).toBe(false);
  });

  it("greys out same-currency, Closed and source Accounts with a reason", async () => {
    renderForm();

    const list = await screen.findByRole("radiogroup", { name: "Destination account" });
    const savings = await within(list).findByRole("radio", { name: /Ned savings/ });
    expect(savings).toBeDisabled();
    expect(savings.closest("label")).toHaveTextContent("Same currency");
    const closed = within(list).getByRole("radio", { name: /Ned old USD/ });
    expect(closed).toBeDisabled();
    expect(closed.closest("label")).toHaveTextContent("Closed");
    const source = within(list).getByRole("radio", { name: /Ned bank/ });
    expect(source).toBeDisabled();
    expect(source.closest("label")).toHaveTextContent("Source account");
    expect(within(list).getByRole("radio", { name: /Ned USD/ })).toBeEnabled();
  });

  it("says so when the Customer has no Open Account in another currency", async () => {
    renderForm({ accounts: allAccounts.slice(0, 3) });

    expect(await screen.findByText(/No open account in another currency/)).toBeInTheDocument();
  });

  it("debounces the quote and shows the rate and the computed amount", async () => {
    const { fetchMock } = renderForm();
    fireEvent.click(await screen.findByRole("radio", { name: /Ned USD/ }));
    vi.useFakeTimers();

    fireEvent.change(screen.getByLabelText(/Amount/), { target: { value: "10" } });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(100);
    });
    fireEvent.change(screen.getByLabelText(/Amount/), { target: { value: "1000" } });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(299);
    });
    expect(posts(fetchMock, "/transactions/fx/quote")).toHaveLength(0);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(1);
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(100);
    });
    vi.useRealTimers();

    expect(await screen.findByText("1 NZD = 0.5912 USD (as of 2026-11-09)")).toBeInTheDocument();
    expect(screen.getByText(/You get 591\.20 USD/)).toBeInTheDocument();
    const quotes = posts(fetchMock, "/transactions/fx/quote");
    expect(quotes).toHaveLength(1);
    const body = JSON.parse(await quotes[0].clone().text());
    expect(body).toMatchObject({ fromAccountId: sourceId, toAccountId: usdId, sellAmount: 1000 });
    expect(body).not.toHaveProperty("buyAmount");
    expect(body).not.toHaveProperty("memo");
    expect(body).not.toHaveProperty("expectedRate");
    expect(quotes[0].headers.get("Idempotency-Key")).toBeNull();
  });

  it("switches to I buy, sends buyAmount and shows the sell amount", async () => {
    const { fetchMock } = renderForm();
    fireEvent.click(await screen.findByRole("radio", { name: /Ned USD/ }));
    fireEvent.click(screen.getByRole("radio", { name: "I buy" }));
    expect(screen.getByLabelText(/Amount/).closest("div")).toHaveTextContent("USD");
    fireEvent.change(screen.getByLabelText(/Amount/), { target: { value: "500" } });

    expect(await screen.findByText(/You pay 845\.74 NZD/, {}, { timeout: 2000 })).toBeInTheDocument();
    const quotes = posts(fetchMock, "/transactions/fx/quote");
    const body = JSON.parse(await quotes[quotes.length - 1].clone().text());
    expect(body).toMatchObject({ buyAmount: 500 });
    expect(body).not.toHaveProperty("sellAmount");
  });

  it("shows a quote error and blocks submit", async () => {
    const { fetchMock } = renderForm({
      quote: () => json({ errors: { BookedAt: ["No NZD to USD rate is available on or before 2026-11-10."] } }, 400),
    });
    await chooseUsdAndType("1000");

    expect(
      await screen.findByText("No NZD to USD rate is available on or before 2026-11-10.", {}, { timeout: 2000 }),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Review conversion" })).toBeDisabled();
    expect(posts(fetchMock, "/transactions/fx")).toHaveLength(0);
  });

  it("confirms with a summary and posts the quoted expectedRate with an Idempotency-Key", async () => {
    const { fetchMock } = renderForm();
    fireEvent.click(await screen.findByRole("radio", { name: /Ned USD/ }));
    fireEvent.click(screen.getByRole("radio", { name: "I buy" }));
    fireEvent.change(screen.getByLabelText(/Amount/), { target: { value: "500" } });
    fireEvent.change(screen.getByLabelText("Booked at"), { target: { value: "2026-11-10T09:00" } });
    await screen.findByText(/You pay 845\.74 NZD/, {}, { timeout: 2000 });

    fireEvent.click(screen.getByRole("button", { name: "Review conversion" }));
    expect(
      await screen.findByText(
        "Convert 845.74 NZD from Ned bank to 500.00 USD in Ned USD at 1 NZD = 0.5912 USD (as of 2026-11-09)",
      ),
    ).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Confirm conversion" }));

    expect(await screen.findByText("account detail")).toBeInTheDocument();
    const created = posts(fetchMock, "/transactions/fx");
    expect(created).toHaveLength(1);
    expect(created[0].url).toBe("http://localhost:3000/api/transactions/fx");
    expect(created[0].headers.get("Idempotency-Key")).toBe(idempotencyKey);
    expect(JSON.parse(await created[0].clone().text())).toEqual({
      fromAccountId: sourceId,
      toAccountId: usdId,
      buyAmount: 500,
      expectedRate: 0.5912,
      bookedAt: "2026-11-10T09:00",
      memo: null,
      reference: null,
    });
  });

  it("re-quotes on a 409 for expectedRate and asks to confirm again", async () => {
    let rate = 0.5912;
    let attempts = 0;
    const { fetchMock } = renderForm({
      quote: (body) => json(quoteFor(body, rate)),
      create: () => {
        attempts += 1;
        if (attempts === 1) {
          rate = 0.6;
          return json({ errors: { ExpectedRate: ["The rate changed since the quote."] } }, 409);
        }
        return json({ id: createdId }, 201);
      },
    });
    await chooseUsdAndType("1000");
    await screen.findByText(/You get 591\.20 USD/, {}, { timeout: 2000 });
    fireEvent.click(screen.getByRole("button", { name: "Review conversion" }));
    fireEvent.click(await screen.findByRole("button", { name: "Confirm conversion" }));

    expect(await screen.findByText(/The rate changed/)).toBeInTheDocument();
    expect(await screen.findByText("1 NZD = 0.6 USD (as of 2026-11-09)", {}, { timeout: 2000 })).toBeInTheDocument();
    expect(screen.queryByText("account detail")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Confirm conversion" })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Review conversion" }));
    expect(
      await screen.findByText("Convert 1000.00 NZD from Ned bank to 600.00 USD in Ned USD at 1 NZD = 0.6 USD (as of 2026-11-09)"),
    ).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Confirm conversion" }));

    expect(await screen.findByText("account detail")).toBeInTheDocument();
    const created = posts(fetchMock, "/transactions/fx");
    expect(created).toHaveLength(2);
    expect(JSON.parse(await created[1].clone().text())).toMatchObject({ expectedRate: 0.6 });
  });
});
