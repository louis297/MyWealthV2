import { configureStore } from "@reduxjs/toolkit";
import { fireEvent, render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { PortfolioDetailPage } from "@/features/portfolios/PortfolioDetailPage";
import { sessionSlice, setCurrentUser, type CurrentUser } from "@/features/session/sessionSlice";
import { api } from "@/shared/api/api";

const portfolioId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
const accountId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const vti = "cccccccc-cccc-cccc-cccc-cccccccccccc";
const fph = "dddddddd-dddd-dddd-dddd-dddddddddddd";

const portfolio = {
  id: portfolioId,
  tenantId: "22222222-2222-2222-2222-222222222222",
  name: "Balanced Growth",
  baseCurrency: "NZD",
  isActive: true,
  driftAbsolute: 1,
  driftRelative: 5,
  minimumTrade: 100,
  cashResidualPercent: 1,
  rowVersion: "AAAA",
  lines: [
    { instrumentId: vti, targetWeight: 60, sortOrder: 0 },
    { instrumentId: fph, targetWeight: 40, sortOrder: 1 },
  ],
};

const instruments = {
  items: [
    { id: vti, tenantId: portfolio.tenantId, symbol: "VTI", name: "Vanguard", quoteCurrency: "USD", isActive: true, rowVersion: "AAAA" },
    { id: fph, tenantId: portfolio.tenantId, symbol: "FPH", name: "Fisher", quoteCurrency: "NZD", isActive: true, rowVersion: "AAAA" },
  ],
  page: 1,
  pageSize: 100,
  totalCount: 2,
};

const brokerageAccount = {
  id: accountId,
  tenantId: portfolio.tenantId,
  customerId: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
  name: "Growth account",
  type: "Brokerage",
  currency: "NZD",
  cashBalance: 1000,
  status: "Open",
  isActive: true,
  rowVersion: "BBBB",
};

function user(): CurrentUser {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    name: "Pat Admin",
    email: "pat@north.example",
    role: "tenantAdmin",
    status: "active",
    tenantId: portfolio.tenantId,
    tenantCode: "north-advisory",
    adviserId: null,
    rowVersion: "AAAA",
  };
}

function renderDetail(options?: { assigned?: boolean; putStatus?: number; putBody?: unknown; inactive?: boolean; missing?: boolean }) {
  const store = configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });
  store.dispatch(setCurrentUser(user()));

  const fetchMock = vi.mocked(fetch);
  let disabled = false;
  fetchMock.mockImplementation(async (input) => {
    const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
    const method = input instanceof Request ? input.method : "GET";
    const path = new URL(url, "http://localhost:3000").pathname;
    if (path === `/api/portfolios/${portfolioId}` && method === "GET") {
      if (options?.missing) {
        return new Response("", { status: 404 });
      }
      return new Response(JSON.stringify({ ...portfolio, isActive: options?.inactive ? false : !disabled }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      });
    }
    if (path === `/api/portfolios/${portfolioId}/rebalance` && method === "POST") {
      return new Response(
        JSON.stringify({ jobId: "job-1", accounts: [{ accountId, handle: "handle-1" }] }),
        { status: 202, headers: { "Content-Type": "application/json" } },
      );
    }
    if (path === `/api/portfolios/${portfolioId}/disable` && method === "POST") {
      disabled = true;
      return new Response("", { status: 204 });
    }
    if (path === "/api/instruments" && method === "GET") {
      return new Response(JSON.stringify(instruments), { status: 200, headers: { "Content-Type": "application/json" } });
    }
    if (path === "/api/accounts" && method === "GET") {
      const type = new URL(url, "http://localhost:3000").searchParams.get("type");
      const items = options?.assigned && type === "Brokerage" ? [brokerageAccount] : [];
      return new Response(JSON.stringify({ items, page: 1, pageSize: 100, totalCount: items.length }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      });
    }
    if (path === `/api/accounts/${accountId}/portfolio` && method === "GET") {
      return new Response(
        JSON.stringify({
          accountId,
          portfolioId,
          status: "Active",
          lastRebalanceAt: null,
          lastRebalanceStatus: "Success",
          lastRebalanceMessage: null,
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    if (path === `/api/portfolios/${portfolioId}` && method === "PUT") {
      return new Response(options?.putBody ? JSON.stringify(options.putBody) : "", {
        status: options?.putStatus ?? 204,
        headers: options?.putBody ? { "Content-Type": "application/json" } : undefined,
      });
    }
    return new Response("", { status: 404 });
  });

  const router = createMemoryRouter([{ path: "/portfolios/:id", element: <PortfolioDetailPage /> }], {
    initialEntries: [`/portfolios/${portfolioId}`],
  });
  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );
  return { fetchMock };
}

function putRequest(fetchMock: ReturnType<typeof vi.mocked<typeof fetch>>) {
  const call = fetchMock.mock.calls.find(([input]) => input instanceof Request && input.method === "PUT");
  return call?.[0] as Request | undefined;
}

describe("PortfolioDetailPage edit", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
    vi.stubGlobal("confirm", vi.fn(() => true));
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: `/portfolios/${portfolioId}`,
      search: "",
      href: `http://localhost:3000/portfolios/${portfolioId}`,
      assign: vi.fn(),
    });
  });

  it("saves unchanged weights without a confirm and without base currency", async () => {
    const { fetchMock } = renderDetail();

    expect(await screen.findByRole("heading", { name: "Balanced Growth" })).toBeInTheDocument();
    expect(screen.getByText("NZD")).toBeInTheDocument();
    expect(screen.queryByLabelText("Base currency")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    const request = await vi.waitFor(() => {
      const found = putRequest(fetchMock);
      expect(found).toBeDefined();
      return found!;
    });
    expect(window.confirm).not.toHaveBeenCalled();
    expect(new URL(request.url).pathname).toBe(`/api/portfolios/${portfolioId}`);
    expect(request.headers.get("X-MyWealth-Request")).toBe("1");
    expect(request.headers.get("Authorization")).toBeNull();
    expect(JSON.parse(await request.clone().text())).toEqual({
      rowVersion: "AAAA",
      name: "Balanced Growth",
      lines: [
        { instrumentId: vti, targetWeight: 60 },
        { instrumentId: fph, targetWeight: 40 },
      ],
    });
    expect(screen.queryByText("Rebalance queued.")).not.toBeInTheDocument();
  });

  it("confirms a weight change when an account is assigned and shows the queued rebalance", async () => {
    const { fetchMock } = renderDetail({
      assigned: true,
      putStatus: 202,
      putBody: {
        jobId: "job-1",
        accounts: [{ accountId, handle: "handle-1" }],
      },
    });

    expect(await screen.findByRole("heading", { name: "Balanced Growth" })).toBeInTheDocument();
    const weights = await screen.findAllByLabelText("Weight");
    fireEvent.change(weights[0], { target: { value: "70" } });
    fireEvent.change(weights[1], { target: { value: "30" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(window.confirm).toHaveBeenCalledWith(
      "This will rebalance every account currently assigned to this portfolio. Continue?",
    );
    const request = await vi.waitFor(() => {
      const found = putRequest(fetchMock);
      expect(found).toBeDefined();
      return found!;
    });
    expect(JSON.parse(await request.clone().text())).toMatchObject({
      lines: [
        { instrumentId: vti, targetWeight: 70 },
        { instrumentId: fph, targetWeight: 30 },
      ],
    });
    expect(await screen.findByText("Rebalance queued.")).toBeInTheDocument();
  });

  it("does not save when the weight-change confirm is cancelled", async () => {
    vi.stubGlobal("confirm", vi.fn(() => false));
    const { fetchMock } = renderDetail({ assigned: true });

    expect(await screen.findByRole("heading", { name: "Balanced Growth" })).toBeInTheDocument();
    const weights = await screen.findAllByLabelText("Weight");
    fireEvent.change(weights[0], { target: { value: "70" } });
    fireEvent.change(weights[1], { target: { value: "30" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(window.confirm).toHaveBeenCalled();
    expect(putRequest(fetchMock)).toBeUndefined();
  });

  it("disables the portfolio with the row version only", async () => {
    const { fetchMock } = renderDetail();

    fireEvent.click(await screen.findByRole("button", { name: "Disable" }));

    const request = await vi.waitFor(() => {
      const found = fetchMock.mock.calls
        .map(([input]) => input)
        .find((input): input is Request => input instanceof Request && input.method === "POST");
      expect(found).toBeDefined();
      return found!;
    });
    expect(new URL(request.url).pathname).toBe(`/api/portfolios/${portfolioId}/disable`);
    expect(request.headers.get("X-MyWealth-Request")).toBe("1");
    expect(request.headers.get("Authorization")).toBeNull();
    expect(JSON.parse(await request.clone().text())).toEqual({ rowVersion: "AAAA" });
    expect(await screen.findByText("Inactive")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Disable" })).not.toBeInTheDocument();
  });

  it("does not offer Disable on an inactive portfolio", async () => {
    renderDetail({ inactive: true });

    expect(await screen.findByRole("heading", { name: "Balanced Growth" })).toBeInTheDocument();
    expect(screen.getByText("Inactive")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Disable" })).not.toBeInTheDocument();
  });

  it("says when no accounts are assigned", async () => {
    renderDetail();

    expect(await screen.findByText("No accounts are assigned to this portfolio.")).toBeInTheDocument();
  });

  it("lists an assigned account and queues a bulk rebalance", async () => {
    const { fetchMock } = renderDetail({ assigned: true });

    expect(await screen.findByRole("link", { name: "Growth account" })).toHaveAttribute("href", `/accounts/${accountId}`);
    expect(screen.getByText("Success")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Rebalance accounts" }));

    const request = await vi.waitFor(() => {
      const found = fetchMock.mock.calls
        .map(([input]) => input)
        .find((input): input is Request => input instanceof Request && input.method === "POST" && input.url.includes("/rebalance"));
      expect(found).toBeDefined();
      return found!;
    });
    expect(new URL(request.url).pathname).toBe(`/api/portfolios/${portfolioId}/rebalance`);
    expect(request.headers.get("X-MyWealth-Request")).toBe("1");
    expect(await screen.findByText("Rebalance queued.")).toBeInTheDocument();
  });

  it("shows Not found for an unknown portfolio", async () => {
    renderDetail({ missing: true });

    expect(await screen.findByText("Not found")).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Forbidden" })).not.toBeInTheDocument();
  });
});
