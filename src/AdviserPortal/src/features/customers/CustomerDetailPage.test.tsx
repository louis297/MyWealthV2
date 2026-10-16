import { configureStore } from "@reduxjs/toolkit";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
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

  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );

  return { fetchMock, router };
}

describe("CustomerDetailPage", () => {
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
    expect(screen.getByText("Customer Transactions")).toBeInTheDocument();
    expect(screen.getByText(customerTransactions.function)).toBeInTheDocument();
    expect(screen.queryByText("Adviser Balances")).not.toBeInTheDocument();

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
    expect(await screen.findByText("Customer Transactions")).toBeInTheDocument();
    expect(reportGets).toBeGreaterThan(1);
  });
});
