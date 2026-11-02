import { configureStore } from "@reduxjs/toolkit";
import { render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { PortfoliosListPage } from "@/features/portfolios/PortfoliosListPage";
import { sessionSlice, setCurrentUser, type CurrentUser } from "@/features/session/sessionSlice";
import { api } from "@/shared/api/api";

const portfolioId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

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
  lines: [],
};

function user(overrides: Partial<CurrentUser> = {}): CurrentUser {
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
    ...overrides,
  };
}

function renderList(items: unknown[], totalCount = items.length) {
  const store = configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });
  store.dispatch(setCurrentUser(user()));

  const fetchMock = vi.mocked(fetch);
  fetchMock.mockImplementation(async () =>
    new Response(JSON.stringify({ items, page: 1, pageSize: 20, totalCount }), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    }),
  );

  const router = createMemoryRouter(
    [
      { path: "/portfolios", element: <PortfoliosListPage /> },
      { path: "/portfolios/new", element: <p>new portfolio</p> },
      { path: "/portfolios/:id", element: <p>portfolio detail</p> },
    ],
    { initialEntries: ["/portfolios"] },
  );

  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );

  return { fetchMock };
}

describe("PortfoliosListPage", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: "/portfolios",
      search: "",
      href: "http://localhost:3000/portfolios",
      assign: vi.fn(),
    });
  });

  it("shows the empty copy and a create link", async () => {
    renderList([]);

    expect(await screen.findByText("No portfolios yet. Create one to get started.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Create" })).toHaveAttribute("href", "/portfolios/new");
  });

  it("lists portfolios from GET /portfolios without tenantId", async () => {
    const { fetchMock } = renderList([portfolio]);

    expect(await screen.findByRole("link", { name: "Balanced Growth" })).toHaveAttribute(
      "href",
      `/portfolios/${portfolioId}`,
    );
    expect(screen.getByText("NZD")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
    expect(screen.queryByText("No portfolios yet. Create one to get started.")).not.toBeInTheDocument();

    const request = fetchMock.mock.calls[0][0] as Request;
    const url = new URL(request.url);
    expect(url.origin + url.pathname).toBe("http://localhost:3000/api/portfolios");
    expect(url.searchParams.get("page")).toBe("1");
    expect(url.searchParams.get("pageSize")).toBe("20");
    expect(url.searchParams.has("tenantId")).toBe(false);
    expect(request.method).toBe("GET");
    expect(request.headers.get("Authorization")).toBeNull();
    expect(request.credentials).toBe("include");
  });
});
