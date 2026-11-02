import { configureStore } from "@reduxjs/toolkit";
import { fireEvent, render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CreatePortfolioPage } from "@/features/portfolios/CreatePortfolioPage";
import { sessionSlice, setCurrentUser, type CurrentUser } from "@/features/session/sessionSlice";
import { api } from "@/shared/api/api";
import { chooseOption } from "@/test/chooseOption";

const portfolioId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
const vti = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const fph = "dddddddd-dddd-dddd-dddd-dddddddddddd";

const currencies = [
  { code: "NZD", name: "New Zealand dollar", decimalPlaces: 2, isActive: true },
];

const instruments = {
  items: [
    { id: vti, tenantId: "22222222-2222-2222-2222-222222222222", symbol: "VTI", name: "Vanguard", quoteCurrency: "USD", isActive: true, rowVersion: "AAAA" },
    { id: fph, tenantId: "22222222-2222-2222-2222-222222222222", symbol: "FPH", name: "Fisher", quoteCurrency: "NZD", isActive: true, rowVersion: "AAAA" },
  ],
  page: 1,
  pageSize: 100,
  totalCount: 2,
};

function user(): CurrentUser {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    name: "Pat Admin",
    email: "pat@north.example",
    role: "tenantAdmin",
    status: "active",
    tenantId: "22222222-2222-2222-2222-222222222222",
    tenantCode: "north-advisory",
    adviserId: null,
    rowVersion: "AAAA",
  };
}

function renderCreate() {
  const store = configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });
  store.dispatch(setCurrentUser(user()));

  const fetchMock = vi.mocked(fetch);
  fetchMock.mockImplementation(async (input) => {
    const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
    const method = input instanceof Request ? input.method : "GET";
    if (url.includes("/currencies") && method === "GET") {
      return new Response(JSON.stringify(currencies), { status: 200, headers: { "Content-Type": "application/json" } });
    }
    if (url.includes("/instruments") && method === "GET") {
      return new Response(JSON.stringify(instruments), { status: 200, headers: { "Content-Type": "application/json" } });
    }
    if (url.includes("/portfolios") && method === "POST") {
      return new Response(JSON.stringify({ id: portfolioId }), {
        status: 201,
        headers: { "Content-Type": "application/json" },
      });
    }
    return new Response("", { status: 404 });
  });

  const router = createMemoryRouter(
    [
      { path: "/portfolios/new", element: <CreatePortfolioPage /> },
      { path: "/portfolios/:id", element: <p>portfolio detail</p> },
    ],
    { initialEntries: ["/portfolios/new"] },
  );

  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );

  return { fetchMock, router };
}

function postRequest(fetchMock: ReturnType<typeof vi.mocked<typeof fetch>>) {
  const call = fetchMock.mock.calls.find(([input]) => input instanceof Request && input.method === "POST");
  return call?.[0] as Request | undefined;
}

async function nameAndCurrency() {
  fireEvent.change(screen.getByLabelText("Name"), { target: { value: "Balanced Growth" } });
  await chooseOption("Base currency", "NZD");
}

describe("CreatePortfolioPage", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: "/portfolios/new",
      search: "",
      href: "http://localhost:3000/portfolios/new",
      assign: vi.fn(),
    });
  });

  it("rejects weights that do not total 100.00", async () => {
    const { fetchMock } = renderCreate();
    await nameAndCurrency();
    await chooseOption("Instrument", "VTI");
    fireEvent.change(screen.getByLabelText("Weight"), { target: { value: "60" } });
    fireEvent.click(screen.getByRole("button", { name: "Create portfolio" }));

    expect(await screen.findByText("Weights must total 100.00.")).toBeInTheDocument();
    expect(postRequest(fetchMock)).toBeUndefined();
  });

  it("rejects a weight that is not greater than 0 and no more than 100", async () => {
    const { fetchMock } = renderCreate();
    await nameAndCurrency();
    await chooseOption("Instrument", "VTI");
    fireEvent.change(screen.getByLabelText("Weight"), { target: { value: "0" } });
    fireEvent.click(screen.getByRole("button", { name: "Create portfolio" }));

    expect(await screen.findByText("Each weight must be greater than 0 and no more than 100.")).toBeInTheDocument();
    expect(postRequest(fetchMock)).toBeUndefined();
  });

  it("rejects the same instrument on two lines", async () => {
    const { fetchMock } = renderCreate();
    await nameAndCurrency();
    await chooseOption("Instrument", "VTI");
    fireEvent.change(screen.getByLabelText("Weight"), { target: { value: "50" } });
    fireEvent.click(screen.getByRole("button", { name: "Add line" }));
    const instruments = screen.getAllByLabelText("Instrument");
    const weights = screen.getAllByLabelText("Weight");
    fireEvent.click(instruments[1]);
    const option = await screen.findByRole("option", { name: "VTI" });
    fireEvent.pointerDown(option, { pointerType: "mouse", button: 0 });
    fireEvent.click(option);
    fireEvent.change(weights[1], { target: { value: "50" } });
    fireEvent.click(screen.getByRole("button", { name: "Create portfolio" }));

    expect(await screen.findByText("Choose an active instrument.")).toBeInTheDocument();
    expect(postRequest(fetchMock)).toBeUndefined();
  });

  it("rejects a missing instrument", async () => {
    const { fetchMock } = renderCreate();
    await nameAndCurrency();
    fireEvent.change(screen.getByLabelText("Weight"), { target: { value: "100" } });
    fireEvent.click(screen.getByRole("button", { name: "Create portfolio" }));

    expect(await screen.findByText("Choose an active instrument.")).toBeInTheDocument();
    expect(postRequest(fetchMock)).toBeUndefined();
  });

  it("posts name, base currency, and lines, and omits blank settings and tenantId", async () => {
    const { fetchMock, router } = renderCreate();
    await nameAndCurrency();
    await chooseOption("Instrument", "VTI");
    fireEvent.change(screen.getByLabelText("Weight"), { target: { value: "60" } });
    fireEvent.click(screen.getByRole("button", { name: "Add line" }));
    const instruments = screen.getAllByLabelText("Instrument");
    const weights = screen.getAllByLabelText("Weight");
    fireEvent.click(instruments[1]);
    const option = await screen.findByRole("option", { name: "FPH" });
    fireEvent.pointerDown(option, { pointerType: "mouse", button: 0 });
    fireEvent.click(option);
    fireEvent.change(weights[1], { target: { value: "40" } });
    fireEvent.click(screen.getByRole("button", { name: "Create portfolio" }));

    expect(await screen.findByText("portfolio detail")).toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/portfolios/${portfolioId}`);

    const request = postRequest(fetchMock)!;
    const url = new URL(request.url);
    expect(url.origin + url.pathname).toBe("http://localhost:3000/api/portfolios");
    expect(request.headers.get("Authorization")).toBeNull();
    expect(request.headers.get("X-MyWealth-Request")).toBe("1");
    expect(request.credentials).toBe("include");
    expect(JSON.parse(await request.clone().text())).toEqual({
      name: "Balanced Growth",
      baseCurrency: "NZD",
      lines: [
        { instrumentId: vti, targetWeight: 60 },
        { instrumentId: fph, targetWeight: 40 },
      ],
    });
  });

  it("sends a setting only when the user fills it", async () => {
    const { fetchMock } = renderCreate();
    await nameAndCurrency();
    await chooseOption("Instrument", "VTI");
    fireEvent.change(screen.getByLabelText("Weight"), { target: { value: "100" } });
    fireEvent.change(screen.getByLabelText("Drift absolute"), { target: { value: "2" } });
    fireEvent.click(screen.getByRole("button", { name: "Create portfolio" }));

    expect(await screen.findByText("portfolio detail")).toBeInTheDocument();
    expect(JSON.parse(await postRequest(fetchMock)!.clone().text())).toEqual({
      name: "Balanced Growth",
      baseCurrency: "NZD",
      lines: [{ instrumentId: vti, targetWeight: 100 }],
      driftAbsolute: 2,
    });
  });
});
