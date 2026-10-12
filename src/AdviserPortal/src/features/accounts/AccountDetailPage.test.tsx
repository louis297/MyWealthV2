import { configureStore } from "@reduxjs/toolkit";
import { render, screen, within } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AccountDetailPage } from "@/features/accounts/AccountDetailPage";
import { sessionSlice, setCurrentUser, type CurrentUser } from "@/features/session/sessionSlice";
import { api } from "@/shared/api/api";

const accountId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
const customerId = "cccccccccccccccc-cccc-cccc-cccc-cccccccccccc";

const account = {
  id: accountId,
  tenantId: "22222222-2222-2222-2222-222222222222",
  customerId,
  name: "Everyday spending",
  type: "Brokerage",
  currency: "NZD",
  status: "Open",
  isActive: true,
  cashBalance: 1250.5,
  rowVersion: "AAAA",
};

const holding = {
  id: "hhhhhhhh-hhhh-hhhh-hhhh-hhhhhhhhhhhh",
  accountId,
  instrumentId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
  symbol: "DEMO",
  name: "Demo Fund",
  quantity: 100,
  cost: 5000,
  costCurrency: "NZD",
};

function json(body: unknown) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}

function user(): CurrentUser {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    name: "Pat Admin",
    email: "pat@north.example",
    role: "tenantAdmin",
    status: "active",
    tenantId: account.tenantId,
    tenantCode: "north-advisory",
    adviserId: null,
    rowVersion: "AAAA",
  };
}

function renderDetail(
  fetchImpl: (url: string, method: string) => Response,
) {
  const store = configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });
  store.dispatch(setCurrentUser(user()));

  const fetchMock = vi.mocked(fetch);
  fetchMock.mockImplementation(async (input, init) => {
    const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
    const method = init?.method ?? (input instanceof Request ? input.method : "GET");
    return fetchImpl(url, method);
  });

  const router = createMemoryRouter(
    [
      { path: "/accounts", element: <p>accounts list</p> },
      { path: "/accounts/:id", element: <AccountDetailPage /> },
    ],
    { initialEntries: [`/accounts/${accountId}`] },
  );

  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );

  return { fetchMock, router };
}

describe("AccountDetailPage", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: `/accounts/${accountId}`,
      search: "",
      href: `http://localhost:3000/accounts/${accountId}`,
      assign: vi.fn(),
    });
  });

  it("shows cash balance and holdings for the account", async () => {
    const { fetchMock } = renderDetail((url, method) => {
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [holding] });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json(account);
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByRole("heading", { name: "Everyday spending" })).toBeInTheDocument();
    expect(screen.getByText("1250.5")).toBeInTheDocument();
    expect(screen.getByText("Brokerage")).toBeInTheDocument();
    expect(screen.queryByText("Forbidden")).not.toBeInTheDocument();
    expect(document.querySelector('input[type="password"]')).toBeNull();

    const holdings = screen.getByRole("table", { name: "Holdings" });
    expect(within(holdings).getByText("DEMO")).toBeInTheDocument();
    expect(within(holdings).getByText("Demo Fund")).toBeInTheDocument();
    expect(within(holdings).getByText("100")).toBeInTheDocument();
    expect(within(holdings).getByText("5000")).toBeInTheDocument();
    expect(within(holdings).getByText("NZD")).toBeInTheDocument();

    const requests = fetchMock.mock.calls.map(([input]) => input).filter((input): input is Request => input instanceof Request);
    const accountRequest = requests.find((request) => request.url.endsWith(`/accounts/${accountId}`));
    const holdingsRequest = requests.find((request) => request.url.endsWith(`/accounts/${accountId}/holdings`));
    expect(accountRequest?.url).toBe(`http://localhost:3000/api/accounts/${accountId}`);
    expect(holdingsRequest?.url).toBe(`http://localhost:3000/api/accounts/${accountId}/holdings`);
    for (const request of [accountRequest, holdingsRequest]) {
      expect(request?.method).toBe("GET");
      expect(request?.headers.get("Authorization")).toBeNull();
      expect(request?.credentials).toBe("include");
      expect(new URL(request?.url ?? "").searchParams.has("tenantId")).toBe(false);
    }
  });

  it("shows no holdings when the projection is empty", async () => {
    renderDetail((url, method) => {
      if (url.endsWith(`/accounts/${accountId}/holdings`) && method === "GET") {
        return json({ items: [] });
      }
      if (url.endsWith(`/accounts/${accountId}`) && method === "GET") {
        return json(account);
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByText("1250.5")).toBeInTheDocument();
    expect(screen.getByText("No holdings.")).toBeInTheDocument();
  });

  it("stays on the URL and shows Not found for a missing account", async () => {
    const { router } = renderDetail(() => new Response("", { status: 404 }));

    expect(await screen.findByText("Not found")).toBeInTheDocument();
    expect(screen.queryByText("accounts list")).not.toBeInTheDocument();
    expect(screen.queryByText("Forbidden")).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/accounts/${accountId}`);
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });
});
