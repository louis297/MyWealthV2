import { configureStore } from "@reduxjs/toolkit";
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CustomerDetailPage } from "@/features/customers/CustomerDetailPage";
import { sessionSlice, setCurrentUser, type CurrentUser } from "@/features/session/sessionSlice";
import { api } from "@/shared/api/api";

const startLogin = vi.fn();

vi.mock("@/features/session/session", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/session/session")>();
  return {
    ...actual,
    startLogin: () => startLogin(),
  };
});

const customerId = "cccccccccccccccc-cccc-cccc-cccc-cccccccccccc";
const adviserId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

const customer = {
  id: customerId,
  tenantId: "22222222-2222-2222-2222-222222222222",
  adviserId,
  name: "Jordan Lee",
  email: "jordan@north.example",
  status: "active",
  rowVersion: "AAAA",
  created: "2026-09-14T00:00:00+00:00",
};

const emptyNetWorth = {
  asOf: "2026-10-12T16:00:00.000Z",
  currencies: [],
  breakdowns: { byCustomer: [], byAccountType: [], topHoldings: [] },
};

const accountId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

const account = {
  id: accountId,
  tenantId: "22222222-2222-2222-2222-222222222222",
  customerId,
  name: "Everyday spending",
  type: "Bank",
  currency: "NZD",
  status: "Open",
  isActive: true,
  cashBalance: 1250.5,
  rowVersion: "AAAA",
};

const customerNetWorth = {
  asOf: "2026-10-12T16:00:00.000Z",
  currencies: [
    {
      currency: "NZD",
      assets: { cash: 1000, holdings: 5000, total: 6000 },
      liabilities: { total: 0 },
      net: 6000,
    },
  ],
  breakdowns: { byCustomer: [], byAccountType: [], topHoldings: [] },
};

function json(body: unknown, status = 200) {
  return new Response(status === 204 ? null : JSON.stringify(body), {
    status,
    headers: status === 204 ? undefined : { "Content-Type": "application/json" },
  });
}

function user(overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    name: "Pat Admin",
    email: "pat@north.example",
    role: "tenantAdmin",
    status: "active",
    tenantId: customer.tenantId,
    tenantCode: "north-advisory",
    adviserId: null,
    rowVersion: "AAAA",
    ...overrides,
  };
}

const customerTransactions = {
  id: "8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11",
  code: "customer-transactions",
  name: "Customer Transactions",
  function: "Lists cash and security activity for one customer.",
  scope: "Customer",
  allowedTriggers: ["manual", "scheduled"],
};

const adviserBalances = {
  id: "5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22",
  code: "adviser-balances",
  name: "Adviser Balances",
  function: "Cash and holdings for an adviser's book.",
  scope: "Book",
  allowedTriggers: ["manual", "scheduled"],
};

function renderDetail(
  me: CurrentUser,
  fetchImpl: (
    url: string,
    method: string,
    body: string | undefined,
  ) => Response | Promise<Response>,
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
    const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
    const method = init?.method ?? (input instanceof Request ? input.method : "GET");
    const body =
      typeof init?.body === "string"
        ? init.body
        : input instanceof Request
          ? await input.clone().text()
          : undefined;
    return fetchImpl(url, method, body);
  });

  const router = createMemoryRouter(
    [
      { path: "/customers", element: <p>customers list</p> },
      { path: "/customers/:id", element: <CustomerDetailPage /> },
      { path: "/customers/:id/edit", element: <p>edit customer</p> },
    ],
    { initialEntries: [`/customers/${customerId}`] },
  );

  const view = render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );

  return { fetchMock, router, unmount: view.unmount };
}

describe("CustomerDetailPage", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  beforeEach(() => {
    startLogin.mockReset();
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
    vi.spyOn(window, "confirm").mockReturnValue(true);
  });

  it("lists the customer's accounts and links to a new account", async () => {
    const { fetchMock } = renderDetail(user(), (url, method) => {
      if (url.includes("/api/accounts") && method === "GET") {
        return json({ items: [account], page: 1, pageSize: 20, totalCount: 1 });
      }
      if (url.endsWith(`/users/customers/${customerId}/net-worth`) && method === "GET") {
        return json(emptyNetWorth);
      }
      if (url.endsWith(`/users/customers/${customerId}`) && method === "GET") {
        return json(customer);
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByRole("heading", { name: "Accounts" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Everyday spending" })).toHaveAttribute(
      "href",
      `/accounts/${accountId}`,
    );
    expect(screen.getByText("1250.5")).toBeInTheDocument();
    expect(screen.getByText("Bank")).toBeInTheDocument();
    expect(screen.getByText("NZD")).toBeInTheDocument();
    expect(screen.getByText("Open")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "New account" })).toHaveAttribute(
      "href",
      `/customers/${customerId}/accounts/new`,
    );
    expect(screen.getByText("Jordan Lee")).toBeInTheDocument();

    const request = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.url.includes("/api/accounts"));
    expect(request).toBeDefined();
    const url = new URL(request!.url);
    expect(url.origin + url.pathname).toBe("http://localhost:3000/api/accounts");
    expect(url.searchParams.get("customerId")).toBe(customerId);
    expect(url.searchParams.get("page")).toBe("1");
    expect(url.searchParams.get("pageSize")).toBe("20");
    expect(url.searchParams.has("tenantId")).toBe(false);
    expect(request!.method).toBe("GET");
    expect(request!.headers.get("Authorization")).toBeNull();
    expect(request!.credentials).toBe("include");
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("stays on the URL and shows Not found for a missing customer", async () => {
    const { router } = renderDetail(user(), () => new Response("", { status: 404 }));

    expect(await screen.findByText("Not found")).toBeInTheDocument();
    expect(screen.queryByText("customers list")).not.toBeInTheDocument();
    expect(screen.queryByText("Forbidden")).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/customers/${customerId}`);
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("shows the customer and accounts without dashboard breakdowns", async () => {
    const { fetchMock } = renderDetail(user(), (url, method) => {
      if (url.includes("/api/accounts") && method === "GET") {
        return json({ items: [account], page: 1, pageSize: 20, totalCount: 1 });
      }
      if (url.endsWith(`/users/customers/${customerId}`) && method === "GET") {
        return json(customer);
      }
      if (url.includes("net-worth")) {
        return json({
          ...customerNetWorth,
          breakdowns: {
            byCustomer: [
              {
                customerId,
                name: "Amy Able",
                currencies: customerNetWorth.currencies,
              },
            ],
            byAccountType: [
              {
                type: "Brokerage",
                currencies: customerNetWorth.currencies,
              },
            ],
            topHoldings: [
              {
                instrumentId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
                symbol: "DEMO",
                quantity: 100,
                marketValue: 5000,
                currency: "NZD",
              },
            ],
          },
        });
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByRole("heading", { name: "Jordan Lee" })).toBeInTheDocument();
    expect(screen.getByText("jordan@north.example")).toBeInTheDocument();
    expect(await screen.findByRole("heading", { name: "Accounts" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "New account" })).toHaveAttribute(
      "href",
      `/customers/${customerId}/accounts/new`,
    );
    await expect(
      screen.findByRole("heading", { name: "By customer" }, { timeout: 300 }),
    ).rejects.toThrow();
    expect(screen.queryByRole("heading", { name: "Net worth" })).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "By account type" })).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Top holdings" })).not.toBeInTheDocument();
    expect(screen.queryByText("Amy Able")).not.toBeInTheDocument();
    expect(screen.queryByText("Brokerage")).not.toBeInTheDocument();
    expect(screen.queryByText("DEMO")).not.toBeInTheDocument();
    expect(screen.queryByText("Not found")).not.toBeInTheDocument();
    expect(document.querySelector('input[type="password"]')).toBeNull();

    const urls = fetchMock.mock.calls.map(([input]) => {
      if (typeof input === "string") {
        return input;
      }
      if (input instanceof Request) {
        return input.url;
      }
      return String(input);
    });
    expect(urls.some((url) => url.includes("net-worth"))).toBe(false);
  });

  it("is read-only and disables with the current rowVersion", async () => {
    const posted: string[] = [];
    renderDetail(user(), (url, method, body) => {
      if (url.endsWith(`/users/customers/${customerId}/net-worth`) && method === "GET") {
        return json(emptyNetWorth);
      }
      if (url.endsWith(`/users/customers/${customerId}`) && method === "GET") {
        return new Response(JSON.stringify(customer), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        });
      }
      if (url.endsWith("/disable") && method === "POST") {
        posted.push(body ?? "");
        return new Response(null, { status: 204 });
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByText("Jordan Lee")).toBeInTheDocument();
    expect(screen.getByText(customerId)).toBeInTheDocument();
    expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Edit" })).toHaveAttribute(
      "href",
      `/customers/${customerId}/edit`,
    );

    fireEvent.click(screen.getByRole("button", { name: "Disable" }));

    await waitFor(() => {
      expect(posted).toEqual([JSON.stringify({ rowVersion: "AAAA" })]);
    });
  });

  it("asks the user to reload on 409 and does not replay the stale rowVersion", async () => {
    const posted: string[] = [];
    let gets = 0;
    renderDetail(user(), (url, method, body) => {
      if (url.endsWith(`/users/customers/${customerId}/net-worth`) && method === "GET") {
        return json(emptyNetWorth);
      }
      if (url.endsWith(`/users/customers/${customerId}`) && method === "GET") {
        gets += 1;
        return new Response(
          JSON.stringify({ ...customer, rowVersion: gets === 1 ? "AAAA" : "BBBB" }),
          { status: 200, headers: { "Content-Type": "application/json" } },
        );
      }
      if (url.endsWith("/disable") && method === "POST") {
        posted.push(body ?? "");
        return new Response("", { status: 409 });
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByText("Jordan Lee")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Disable" }));

    expect(await screen.findByText(/reload/i)).toBeInTheDocument();
    await waitFor(() => {
      expect(gets).toBeGreaterThan(1);
    });
    expect(posted).toEqual([JSON.stringify({ rowVersion: "AAAA" })]);
  });

  it("lists customer-scoped reports and does not request book scope", async () => {
    const { fetchMock } = renderDetail(user(), (url, method) => {
      if (url.includes("/api/reports") && method === "GET") {
        const scope = new URL(url).searchParams.get("scope");
        return json(scope === "Customer" ? [customerTransactions] : [adviserBalances]);
      }
      if (url.includes("/api/accounts") && method === "GET") {
        return json({ items: [account], page: 1, pageSize: 20, totalCount: 1 });
      }
      if (url.endsWith(`/users/customers/${customerId}`) && method === "GET") {
        return json(customer);
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByRole("heading", { name: "Reports" })).toBeInTheDocument();
    expect(screen.getByText("These run for this customer.")).toBeInTheDocument();
    const report = await screen.findByLabelText("Report");
    expect(screen.getByRole("option", { name: "Customer Transactions" })).toBeInTheDocument();
    expect(screen.queryByRole("option", { name: "Adviser Balances" })).not.toBeInTheDocument();
    expect(screen.queryByText(customerTransactions.function)).not.toBeInTheDocument();
    expect(screen.queryByLabelText("From")).not.toBeInTheDocument();
    fireEvent.change(report, { target: { value: customerTransactions.id } });
    expect(screen.getByText(customerTransactions.function)).toBeInTheDocument();
    expect(screen.getByLabelText("From")).toBeInTheDocument();
    expect(screen.queryByLabelText("Adviser")).not.toBeInTheDocument();

    const request = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.url.includes("/api/reports"));
    expect(request).toBeDefined();
    const url = new URL(request!.url);
    expect(url.pathname).toBe("/api/reports");
    expect(url.searchParams.get("scope")).toBe("Customer");
    expect(request!.headers.get("Authorization")).toBeNull();
    expect(request!.credentials).toBe("include");
    const scopes = fetchMock.mock.calls.map(([input]) => {
      const raw = input instanceof Request ? input.url : String(input);
      return new URL(raw, "http://localhost:3000").searchParams.get("scope");
    });
    expect(scopes).not.toContain("Book");
  });

  it("shows an empty customer report list", async () => {
    renderDetail(user(), (url, method) => {
      if (url.includes("/api/reports") && method === "GET") {
        return json([]);
      }
      if (url.endsWith(`/users/customers/${customerId}`) && method === "GET") {
        return json(customer);
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByText("No reports available for this customer.")).toBeInTheDocument();
    expect(screen.queryByText("No reports available.")).not.toBeInTheDocument();
  });

  it("shows a loading line while the customer report list is in flight", async () => {
    let release: (response: Response) => void = () => {};
    const pending = new Promise<Response>((resolve) => {
      release = resolve;
    });
    renderDetail(user(), (url, method) => {
      if (url.includes("/api/reports") && method === "GET") {
        return pending;
      }
      if (url.endsWith(`/users/customers/${customerId}`) && method === "GET") {
        return json(customer);
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByText("Loading reports…")).toBeInTheDocument();
    release(json([]));
    expect(await screen.findByText("No reports available for this customer.")).toBeInTheDocument();
  });

  it("retries a failed customer report list", async () => {
    let reportGets = 0;
    renderDetail(user(), (url, method) => {
      if (url.includes("/api/reports") && method === "GET") {
        reportGets += 1;
        if (reportGets === 1) {
          return new Response("", { status: 500 });
        }
        return json([customerTransactions]);
      }
      if (url.endsWith(`/users/customers/${customerId}`) && method === "GET") {
        return json(customer);
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByText("Reports could not be loaded.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(await screen.findByRole("option", { name: "Customer Transactions" })).toBeInTheDocument();
    expect(reportGets).toBeGreaterThan(1);
  });

  it("disables generate when the selected customer report has no parameter form", async () => {
    const futureReport = {
      id: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
      code: "future-report",
      name: "Future Report",
      function: "Not in this version.",
      scope: "Customer",
      allowedTriggers: ["manual"],
    };
    renderDetail(user(), (url, method) => {
      if (url.includes("/api/reports") && method === "GET") {
        return json([futureReport]);
      }
      if (url.endsWith(`/users/customers/${customerId}`) && method === "GET") {
        return json(customer);
      }
      return new Response("", { status: 404 });
    });

    fireEvent.change(await screen.findByLabelText("Report"), { target: { value: futureReport.id } });
    expect(screen.getByText("parameters not available in this version")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Generate now" })).toBeDisabled();
  });

  async function openGenerate(fetchImpl: Parameters<typeof renderDetail>[1]) {
    const rendered = renderDetail(user(), (url, method, body) => {
      if (url.endsWith(`/users/customers/${customerId}`) && method === "GET") {
        return json(customer);
      }
      if (url.includes("/api/accounts") && method === "GET") {
        return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      }
      if (url.includes("/api/reports") && method === "GET" && !url.includes("/executions") && !url.includes("/schedules")) {
        return json([customerTransactions]);
      }
      return fetchImpl(url, method, body);
    });
    fireEvent.change(await screen.findByLabelText("Report"), { target: { value: customerTransactions.id } });
    return rendered;
  }

  it("queues a customer transactions report for this customer", async () => {
    let release: (response: Response) => void = () => {};
    const pending = new Promise<Response>((resolve) => {
      release = resolve;
    });
    const { fetchMock } = await openGenerate((url, method) => {
      if (url.includes("/reports/request") && method === "POST") {
        return pending;
      }
      return new Response("", { status: 404 });
    });

    fireEvent.change(screen.getByLabelText("From"), { target: { value: "2026-01-01" } });
    fireEvent.change(screen.getByLabelText("To"), { target: { value: "2026-03-31" } });
    fireEvent.click(screen.getByRole("button", { name: "Generate now" }));
    expect(screen.getByRole("button", { name: "Generate now" })).toBeDisabled();

    release(json({ id: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee" }, 202));

    expect(await screen.findByText("Report queued")).toBeInTheDocument();
    expect(screen.queryByLabelText("From")).not.toBeInTheDocument();

    const request = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.url.includes("/reports/request"));
    expect(request).toBeDefined();
    expect(request!.method).toBe("POST");
    expect(request!.headers.get("Authorization")).toBeNull();
    expect(request!.headers.get("X-MyWealth-Request")).toBe("1");
    expect(request!.credentials).toBe("include");
    expect(JSON.parse(await request!.clone().text())).toEqual({
      reportId: customerTransactions.id,
      parameters: {
        customerId,
        from: "2026-01-01",
        to: "2026-03-31",
        includeClosedAccounts: false,
      },
    });
  });

  it("sends includeClosedAccounts when the box is checked", async () => {
    const { fetchMock } = await openGenerate((url, method) => {
      if (url.includes("/reports/request") && method === "POST") {
        return json({ id: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee" }, 202);
      }
      return new Response("", { status: 404 });
    });

    fireEvent.change(screen.getByLabelText("From"), { target: { value: "2026-01-01" } });
    fireEvent.change(screen.getByLabelText("To"), { target: { value: "2026-03-31" } });
    fireEvent.click(screen.getByLabelText("Include closed accounts"));
    fireEvent.click(screen.getByRole("button", { name: "Generate now" }));

    expect(await screen.findByText("Report queued")).toBeInTheDocument();
    const request = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.url.includes("/reports/request"));
    const body = JSON.parse(await request!.clone().text()) as {
      parameters: { includeClosedAccounts: boolean };
    };
    expect(body.parameters.includeClosedAccounts).toBe(true);
    expect(body).not.toHaveProperty("tenantId");
    expect(body.parameters).not.toHaveProperty("asOf");
  });

  it("shows a parameter error and does not queue the report", async () => {
    await openGenerate((url, method) => {
      if (url.includes("/reports/request") && method === "POST") {
        return json({ errors: { From: ["From is required."] } }, 400);
      }
      return new Response("", { status: 404 });
    });

    fireEvent.change(screen.getByLabelText("From"), { target: { value: "2026-01-01" } });
    fireEvent.change(screen.getByLabelText("To"), { target: { value: "2026-03-31" } });
    fireEvent.click(screen.getByRole("button", { name: "Generate now" }));

    expect(await screen.findByText("From is required.")).toBeInTheDocument();
    expect(screen.queryByText("Report queued")).not.toBeInTheDocument();
    expect(screen.getByLabelText("From")).toBeInTheDocument();
  });

  it("shows that a missing report is not available", async () => {
    await openGenerate((url, method) => {
      if (url.includes("/reports/request") && method === "POST") {
        return new Response("", { status: 404 });
      }
      return new Response("", { status: 404 });
    });

    fireEvent.change(screen.getByLabelText("From"), { target: { value: "2026-01-01" } });
    fireEvent.change(screen.getByLabelText("To"), { target: { value: "2026-03-31" } });
    fireEvent.click(screen.getByRole("button", { name: "Generate now" }));

    expect(await screen.findByText("Report is not available.")).toBeInTheDocument();
    expect(screen.queryByText("Report queued")).not.toBeInTheDocument();
  });

  it("does not submit a customer report when to is before from", async () => {
    const { fetchMock } = await openGenerate(() => new Response("", { status: 404 }));

    fireEvent.change(screen.getByLabelText("From"), { target: { value: "2026-03-31" } });
    fireEvent.change(screen.getByLabelText("To"), { target: { value: "2026-01-01" } });
    fireEvent.click(screen.getByRole("button", { name: "Generate now" }));

    expect(await screen.findByText("To must be on or after from.")).toBeInTheDocument();
    const posted = fetchMock.mock.calls.some(([input, init]) => {
      const url = input instanceof Request ? input.url : String(input);
      const method = init?.method ?? (input instanceof Request ? input.method : "GET");
      return url.includes("/reports/request") && method === "POST";
    });
    expect(posted).toBe(false);
  });

  it("creates a daily customer schedule from presets", async () => {
    const { fetchMock } = renderDetail(user(), (url, method) => {
      if (url.endsWith(`/users/customers/${customerId}`) && method === "GET") {
        return json(customer);
      }
      if (url.includes("/api/reports") && method === "GET" && !url.includes("/schedules")) {
        return json([customerTransactions]);
      }
      if (url.includes("/reports/schedules") && method === "GET") {
        return json([]);
      }
      if (url.includes("/reports/schedules") && method === "POST") {
        return json({ id: "ssssssss-ssss-ssss-ssss-ssssssssssss" }, 201);
      }
      return new Response("", { status: 404 });
    });

    fireEvent.click(await screen.findByRole("button", { name: "Schedules" }));
    expect(await screen.findByText("No schedules.")).toBeInTheDocument();
    expect(screen.getByText("Times are sent as chosen. The server interprets the schedule in UTC.")).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Daily" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Weekly" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Monthly" })).toBeInTheDocument();
    expect(document.querySelector('input[name="cron"]')).toBeNull();
    expect(document.querySelector("textarea")).toBeNull();

    fireEvent.change(screen.getByLabelText("From"), { target: { value: "2026-01-01" } });
    fireEvent.change(screen.getByLabelText("To"), { target: { value: "2026-03-31" } });
    fireEvent.change(screen.getByLabelText("Time"), { target: { value: "09:00" } });
    expect(screen.getByLabelText("Active")).toBeChecked();
    fireEvent.click(screen.getByRole("button", { name: "Create schedule" }));

    expect(await screen.findByText("Schedule saved")).toBeInTheDocument();
    const request = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.method === "POST" && input.url.includes("/reports/schedules"));
    expect(request).toBeDefined();
    expect(new URL(request!.url).pathname).toBe("/api/reports/schedules");
    expect(request!.headers.get("Authorization")).toBeNull();
    expect(request!.headers.get("X-MyWealth-Request")).toBe("1");
    expect(request!.credentials).toBe("include");
    expect(JSON.parse(await request!.clone().text())).toEqual({
      reportId: customerTransactions.id,
      parameters: {
        customerId,
        from: "2026-01-01",
        to: "2026-03-31",
        includeClosedAccounts: false,
      },
      cron: "0 9 * * *",
      isActive: true,
    });
  });

  it("lists this customer's schedules and hides another customer's", async () => {
    renderDetail(user(), (url, method) => {
      if (url.endsWith(`/users/customers/${customerId}`) && method === "GET") {
        return json(customer);
      }
      if (url.includes("/api/reports") && method === "GET" && !url.includes("/schedules")) {
        return json([customerTransactions]);
      }
      if (url.includes("/reports/schedules") && method === "GET") {
        const scope = new URL(url).searchParams.get("scope");
        if (scope !== "Customer") {
          return json([]);
        }
        return json([
          {
            id: "ssssssss-ssss-ssss-ssss-ssssssssssss",
            reportId: customerTransactions.id,
            parameters: { customerId, from: "2026-01-01", to: "2026-03-31" },
            cron: "0 9 * * *",
            isActive: true,
            rowVersion: "AAAA",
          },
          {
            id: "tttttttt-tttt-tttt-tttt-tttttttttttt",
            reportId: customerTransactions.id,
            parameters: {
              customerId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
              from: "2026-04-01",
              to: "2026-05-01",
            },
            cron: "0 9 * * 1",
            isActive: true,
            rowVersion: "BBBB",
          },
        ]);
      }
      return new Response("", { status: 404 });
    });

    fireEvent.click(await screen.findByRole("button", { name: "Schedules" }));
    expect(await screen.findByText("every day at 09:00")).toBeInTheDocument();
    expect(screen.queryByText("every Monday at 09:00")).not.toBeInTheDocument();
    expect(screen.queryByText("2026-04-01")).not.toBeInTheDocument();
  });

  const customerSchedule = {
    id: "ssssssss-ssss-ssss-ssss-ssssssssssss",
    reportId: customerTransactions.id,
    parameters: {
      customerId,
      from: "2026-01-01",
      to: "2026-03-31",
      includeClosedAccounts: false,
    },
    cron: "0 9 * * *",
    isActive: true,
    rowVersion: "AAAA",
  };

  it("sends rowVersion when a schedule is edited and shows a stale row", async () => {
    const puts: string[] = [];
    renderDetail(user(), (url, method, body) => {
      if (url.endsWith(`/users/customers/${customerId}`) && method === "GET") {
        return json(customer);
      }
      if (url.includes("/api/reports") && method === "GET" && !url.includes("/schedules")) {
        return json([customerTransactions]);
      }
      if (url.includes("/reports/schedules") && method === "GET") {
        return json([customerSchedule]);
      }
      if (url.includes("/reports/schedules/") && method === "PUT") {
        puts.push(body ?? "");
        return new Response("", { status: 409 });
      }
      return new Response("", { status: 404 });
    });

    fireEvent.click(await screen.findByRole("button", { name: "Schedules" }));
    fireEvent.click(await screen.findByRole("button", { name: "Edit" }));
    fireEvent.change(screen.getByLabelText("Time"), { target: { value: "10:00" } });
    fireEvent.click(screen.getByRole("button", { name: "Save schedule" }));

    expect(await screen.findByText("This row changed. Reload and try again.")).toBeInTheDocument();
    expect(puts).toHaveLength(1);
    expect(JSON.parse(puts[0])).toEqual({
      reportId: customerTransactions.id,
      parameters: customerSchedule.parameters,
      cron: "0 10 * * *",
      isActive: true,
      rowVersion: "AAAA",
    });
  });

  it("disables a schedule and keeps the inactive row", async () => {
    let scheduleGets = 0;
    const posted: Array<{ url: string; body: string | undefined }> = [];
    renderDetail(user(), (url, method, body) => {
      if (url.endsWith(`/users/customers/${customerId}`) && method === "GET") {
        return json(customer);
      }
      if (url.includes("/api/reports") && method === "GET" && !url.includes("/schedules")) {
        return json([customerTransactions]);
      }
      if (url.includes("/reports/schedules") && method === "GET") {
        scheduleGets += 1;
        return json([{ ...customerSchedule, isActive: scheduleGets === 1 }]);
      }
      if (url.includes("/disable") && method === "POST") {
        posted.push({ url, body });
        return new Response(null, { status: 204 });
      }
      return new Response("", { status: 404 });
    });

    fireEvent.click(await screen.findByRole("button", { name: "Schedules" }));
    fireEvent.click(await screen.findByRole("button", { name: "Disable schedule" }));

    expect(await screen.findByText("Inactive")).toBeInTheDocument();
    expect(screen.getByText("every day at 09:00")).toBeInTheDocument();
    expect(posted).toHaveLength(1);
    expect(new URL(posted[0].url).pathname).toBe(
      `/api/reports/schedules/${customerSchedule.id}/disable`,
    );
    expect(posted[0].body ?? "").toBe("");
  });

  const succeededExecution = {
    id: "e1e1e1e1-e1e1-e1e1-e1e1-e1e1e1e1e1e1",
    reportId: customerTransactions.id,
    status: "Succeeded",
    triggerKind: "Manual",
    created: "2026-10-16T08:30:00+00:00",
    parameters: { customerId, from: "2026-01-01", to: "2026-03-31" },
    asOf: "2026-10-16T09:00:00+00:00",
  };

  const queuedExecution = {
    id: "e2e2e2e2-e2e2-e2e2-e2e2-e2e2e2e2e2e2",
    reportId: customerTransactions.id,
    status: "Queued",
    triggerKind: "Scheduled",
    created: "2026-10-15T08:30:00+00:00",
    parameters: { customerId, from: "2026-02-01", to: "2026-02-28" },
  };

  function historyPage(items: unknown[], totalCount = items.length) {
    return json({ items, page: 1, pageSize: 20, totalCount });
  }

  it("lists this customer's runs and offers download only when succeeded", async () => {
    const { fetchMock } = renderDetail(user(), (url, method) => {
      if (url.endsWith(`/users/customers/${customerId}`) && method === "GET") {
        return json(customer);
      }
      if (url.includes("/api/reports") && method === "GET" && !url.includes("/schedules") && !url.includes("/executions")) {
        return json([customerTransactions]);
      }
      if (url.includes("/reports/executions") && method === "GET" && !url.includes("/download")) {
        const parsed = new URL(url);
        expect(parsed.searchParams.get("scope")).toBe("Customer");
        expect(parsed.searchParams.get("pageSize")).toBe("20");
        if (parsed.searchParams.get("page") === "2") {
          return json({ items: [], page: 2, pageSize: 20, totalCount: 21 });
        }
        return historyPage(
          [
            succeededExecution,
            queuedExecution,
            {
              ...succeededExecution,
              id: "e3e3e3e3-e3e3-e3e3-e3e3-e3e3e3e3e3e3",
              parameters: {
                customerId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
                from: "2026-04-01",
                to: "2026-05-01",
              },
            },
          ],
          21,
        );
      }
      return new Response("", { status: 404 });
    });

    fireEvent.click(await screen.findByRole("button", { name: "History" }));
    expect(await screen.findByText("Succeeded")).toBeInTheDocument();
    expect(screen.getByText("Manual")).toBeInTheDocument();
    expect(screen.getByText("Scheduled")).toBeInTheDocument();
    expect(screen.getByText("Queued")).toBeInTheDocument();
    expect(screen.getByText("2026-01-01 to 2026-03-31")).toBeInTheDocument();
    expect(screen.getByText("2026-10-16T09:00:00+00:00")).toBeInTheDocument();
    expect(screen.queryByText("2026-04-01 to 2026-05-01")).not.toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: "Download" })).toHaveLength(1);

    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    await waitFor(() => {
      const pages = fetchMock.mock.calls
        .map(([input]) => (input instanceof Request ? input.url : String(input)))
        .filter((url) => url.includes("/reports/executions"));
      expect(pages.some((url) => new URL(url).searchParams.get("page") === "2")).toBe(true);
    });
  });

  it("downloads a succeeded pdf and reports when the file is not ready", async () => {
    const createObjectURL = vi.fn((_blob: Blob) => "blob:report");
    Object.defineProperty(URL, "createObjectURL", { configurable: true, value: createObjectURL });
    let downloads = 0;
    renderDetail(user(), (url, method) => {
      if (url.endsWith(`/users/customers/${customerId}`) && method === "GET") {
        return json(customer);
      }
      if (url.includes("/api/reports") && method === "GET" && !url.includes("/executions")) {
        return json([customerTransactions]);
      }
      if (url.includes("/download") && method === "GET") {
        downloads += 1;
        if (downloads === 1) {
          return new Response(new Blob(["%PDF"]), {
            status: 200,
            headers: { "Content-Type": "application/pdf" },
          });
        }
        return new Response("", { status: 409 });
      }
      if (url.includes("/reports/executions") && method === "GET") {
        return historyPage([succeededExecution]);
      }
      return new Response("", { status: 404 });
    });

    fireEvent.click(await screen.findByRole("button", { name: "History" }));
    fireEvent.click(await screen.findByRole("button", { name: "Download" }));
    await waitFor(() => {
      expect(createObjectURL).toHaveBeenCalled();
    });
    const blob = createObjectURL.mock.calls[0]?.[0];
    expect(blob?.type).toBe("application/pdf");

    fireEvent.click(screen.getByRole("button", { name: "Download" }));
    expect(await screen.findByText("Download not available.")).toBeInTheDocument();
  });

  it("shows an empty run list", async () => {
    renderDetail(user(), (url, method) => {
      if (url.endsWith(`/users/customers/${customerId}`) && method === "GET") {
        return json(customer);
      }
      if (url.includes("/api/reports") && method === "GET" && !url.includes("/executions")) {
        return json([customerTransactions]);
      }
      if (url.includes("/reports/executions") && method === "GET") {
        return historyPage([]);
      }
      return new Response("", { status: 404 });
    });

    fireEvent.click(await screen.findByRole("button", { name: "History" }));
    expect(await screen.findByText("No runs yet.")).toBeInTheDocument();
  });

  it("polls a queued customer report until it succeeds", async () => {
    const executionId = "e1e1e1e1-e1e1-e1e1-e1e1-e1e1e1e1e1e1";
    let polls = 0;
    const { fetchMock } = await openGenerate((url, method) => {
      if (url.includes("/reports/request") && method === "POST") {
        return json({ id: executionId }, 202);
      }
      if (url.endsWith(`/reports/executions/${executionId}`) && method === "GET") {
        polls += 1;
        return json({
          ...succeededExecution,
          id: executionId,
          status: polls < 3 ? "Queued" : "Succeeded",
          asOf: undefined,
        });
      }
      if (url.includes("/reports/executions") && method === "GET") {
        return historyPage([
          {
            ...queuedExecution,
            id: executionId,
            triggerKind: "Manual",
          },
        ]);
      }
      return new Response("", { status: 404 });
    });

    vi.useFakeTimers();
    fireEvent.change(screen.getByLabelText("From"), { target: { value: "2026-01-01" } });
    fireEvent.change(screen.getByLabelText("To"), { target: { value: "2026-03-31" } });
    fireEvent.click(screen.getByRole("button", { name: "Generate now" }));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(500);
    });

    expect(screen.getByText("Report queued")).toBeInTheDocument();
    expect(screen.getByText("Queued")).toBeInTheDocument();
    expect(polls).toBe(0);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(2000);
    });
    expect(polls).toBe(1);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(2000);
    });
    expect(polls).toBe(2);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(2000);
    });
    expect(polls).toBe(3);
    expect(screen.getByRole("button", { name: "Download" })).toBeInTheDocument();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(10000);
    });
    expect(polls).toBe(3);
    const byId = fetchMock.mock.calls.filter(([input]) => {
      const url = input instanceof Request ? input.url : String(input);
      return url.includes(`/reports/executions/${executionId}`);
    });
    expect(byId).toHaveLength(3);
  });

  it("stops polling a report when the customer page unmounts", async () => {
    const executionId = "e1e1e1e1-e1e1-e1e1-e1e1-e1e1e1e1e1e1";
    let polls = 0;
    const { unmount } = await openGenerate((url, method) => {
      if (url.includes("/reports/request") && method === "POST") {
        return json({ id: executionId }, 202);
      }
      if (url.endsWith(`/reports/executions/${executionId}`) && method === "GET") {
        polls += 1;
        return json({ ...queuedExecution, id: executionId });
      }
      if (url.includes("/reports/executions") && method === "GET") {
        return historyPage([{ ...queuedExecution, id: executionId, triggerKind: "Manual" }]);
      }
      return new Response("", { status: 404 });
    });

    vi.useFakeTimers();
    fireEvent.change(screen.getByLabelText("From"), { target: { value: "2026-01-01" } });
    fireEvent.change(screen.getByLabelText("To"), { target: { value: "2026-03-31" } });
    fireEvent.click(screen.getByRole("button", { name: "Generate now" }));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(screen.getByText("Report queued")).toBeInTheDocument();

    unmount();
    await vi.advanceTimersByTimeAsync(10000);
    expect(polls).toBe(0);
  });
});
