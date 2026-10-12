import { configureStore } from "@reduxjs/toolkit";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CreateAccountPage } from "@/features/accounts/CreateAccountPage";
import { sessionSlice, setCurrentUser, type CurrentUser } from "@/features/session/sessionSlice";
import { api } from "@/shared/api/api";

const customerId = "cccccccccccccccc-cccc-cccc-cccc-cccccccccccc";
const accountId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

const currencies = [
  { code: "NZD", name: "New Zealand dollar", decimalPlaces: 2, isActive: true },
  { code: "USD", name: "US dollar", decimalPlaces: 2, isActive: true },
];

function user(overrides: Partial<CurrentUser> = {}): CurrentUser {
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
    ...overrides,
  };
}

function renderCreate(me: CurrentUser, postResponse?: Response) {
  const store = configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });
  store.dispatch(setCurrentUser(me));

  const fetchMock = vi.mocked(fetch);
  fetchMock.mockImplementation(async (input) => {
    const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
    const method = input instanceof Request ? input.method : "GET";
    if (url.includes("/currencies") && method === "GET") {
      return new Response(JSON.stringify(currencies), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      });
    }
    if (url.includes("/accounts") && method === "POST") {
      return (
        postResponse ??
        new Response(JSON.stringify({ id: accountId }), {
          status: 201,
          headers: { "Content-Type": "application/json" },
        })
      );
    }
    return new Response("", { status: 404 });
  });

  const router = createMemoryRouter(
    [
      { path: "/customers/:id/accounts/new", element: <CreateAccountPage /> },
      { path: "/accounts/:id", element: <p>account detail</p> },
    ],
    { initialEntries: [`/customers/${customerId}/accounts/new`] },
  );

  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );

  return { fetchMock, router };
}

async function postCall(fetchMock: ReturnType<typeof vi.mocked<typeof fetch>>) {
  const call = fetchMock.mock.calls.find(([input]) => {
    const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
    const method = input instanceof Request ? input.method : "GET";
    return url.includes("/accounts") && method === "POST";
  });
  return call?.[0] as Request;
}

async function fillAndSubmit() {
  await screen.findByRole("option", { name: "NZD" });
  const type = screen.getByLabelText("Type");
  expect(within(type).getAllByRole("option").map((option) => option.textContent)).toEqual([
    "Bank",
    "Cash",
    "Brokerage",
    "Other",
  ]);
  fireEvent.change(screen.getByLabelText("Name"), { target: { value: "Everyday spending" } });
  fireEvent.change(type, { target: { value: "Bank" } });
  fireEvent.change(screen.getByLabelText("Currency"), { target: { value: "NZD" } });
  fireEvent.click(screen.getByRole("button", { name: "Create account" }));
}

describe("CreateAccountPage", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: `/customers/${customerId}/accounts/new`,
      search: "",
      href: `http://localhost:3000/customers/${customerId}/accounts/new`,
      assign: vi.fn(),
    });
  });

  it("posts the account without tenantId and opens the new account", async () => {
    const { fetchMock, router } = renderCreate(user());

    await fillAndSubmit();

    expect(await screen.findByText("account detail")).toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/accounts/${accountId}`);

    const request = await postCall(fetchMock);
    const url = new URL(request.url);
    expect(url.origin + url.pathname).toBe("http://localhost:3000/api/accounts");
    expect(url.search).toBe("");
    expect(request.method).toBe("POST");
    expect(request.headers.get("Authorization")).toBeNull();
    expect(request.headers.get("X-MyWealth-Request")).toBe("1");
    expect(request.credentials).toBe("include");
    expect(JSON.parse(await request.clone().text())).toEqual({
      customerId,
      name: "Everyday spending",
      type: "Bank",
      currency: "NZD",
    });

    const currenciesCall = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.url.includes("/currencies"));
    expect(currenciesCall).toBeDefined();
    const currenciesUrl = new URL(currenciesCall!.url);
    expect(currenciesUrl.pathname).toBe("/api/currencies");
    expect(currenciesUrl.searchParams.get("enabledOnly")).toBe("true");
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("omits tenantId for an Adviser", async () => {
    const { fetchMock } = renderCreate(user({ role: "adviser", name: "Sam Reed" }));

    await fillAndSubmit();

    expect(await screen.findByText("account detail")).toBeInTheDocument();
    const request = await postCall(fetchMock);
    const body = JSON.parse(await request.clone().text()) as Record<string, string>;
    expect(body).toEqual({
      customerId,
      name: "Everyday spending",
      type: "Bank",
      currency: "NZD",
    });
    expect(body).not.toHaveProperty("tenantId");
  });

  it("shows a validation error and stays on the form", async () => {
    renderCreate(
      user(),
      new Response(
        JSON.stringify({ errors: { Currency: ["Currency must be an enabled catalog code."] } }),
        { status: 400, headers: { "Content-Type": "application/json" } },
      ),
    );

    await fillAndSubmit();

    expect(await screen.findByText("Currency must be an enabled catalog code.")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "New account" })).toBeInTheDocument();
    expect(screen.queryByText("account detail")).not.toBeInTheDocument();
  });

  it("shows a disabled-customer detail and stays on the form", async () => {
    renderCreate(
      user(),
      new Response(
        JSON.stringify({
          detail: "Cannot create an account while the customer is disabled. Enable the customer first.",
        }),
        { status: 400, headers: { "Content-Type": "application/json" } },
      ),
    );

    await fillAndSubmit();

    expect(
      await screen.findByText(
        "Cannot create an account while the customer is disabled. Enable the customer first.",
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "New account" })).toBeInTheDocument();
    expect(screen.queryByText("account detail")).not.toBeInTheDocument();
  });
});
