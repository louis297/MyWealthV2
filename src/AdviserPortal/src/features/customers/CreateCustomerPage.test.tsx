import { configureStore } from "@reduxjs/toolkit";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CreateCustomerPage } from "@/features/customers/CreateCustomerPage";
import { sessionSlice, setCurrentUser, setTokens, type CurrentUser } from "@/features/session/sessionSlice";
import { api } from "@/shared/api/api";

const startAuthorize = vi.fn();

vi.mock("@/features/session/oidc", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/session/oidc")>();
  return {
    ...actual,
    startAuthorize: () => startAuthorize(),
  };
});

const adviserId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const customerId = "cccccccccccccccc-cccc-cccc-cccc-cccccccccccc";

const adviser = {
  id: adviserId,
  tenantId: "22222222-2222-2222-2222-222222222222",
  name: "Sam Reed",
  email: "sam@north.example",
  status: "active",
  rowVersion: "AAAA",
  created: "2026-09-14T00:00:00+00:00",
};

function user(overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    name: "Pat Admin",
    email: "pat@north.example",
    role: "tenantAdmin",
    status: "active",
    tenantId: adviser.tenantId,
    tenantCode: "north-advisory",
    adviserId: null,
    rowVersion: "AAAA",
    ...overrides,
  };
}

function renderCreate(me: CurrentUser) {
  const store = configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });
  store.dispatch(setTokens({ accessToken: "access-1", refreshToken: "refresh-1" }));
  store.dispatch(setCurrentUser(me));

  const fetchMock = vi.mocked(fetch);
  fetchMock.mockImplementation(async (input, init) => {
    const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
    const method = init?.method ?? (input instanceof Request ? input.method : "GET");
    if (url.includes("/users/advisers")) {
      return new Response(
        JSON.stringify({ items: [adviser], page: 1, pageSize: 100, totalCount: 1 }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    if (url.includes("/users/customers") && method === "POST") {
      return new Response(JSON.stringify({ id: customerId }), {
        status: 201,
        headers: { "Content-Type": "application/json" },
      });
    }
    return new Response("", { status: 404 });
  });

  const router = createMemoryRouter(
    [
      { path: "/customers/new", element: <CreateCustomerPage /> },
      { path: "/customers/:id", element: <p>customer detail</p> },
    ],
    { initialEntries: ["/customers/new"] },
  );

  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );

  return { fetchMock };
}

async function postedBody(fetchMock: ReturnType<typeof vi.mocked<typeof fetch>>) {
  const call = fetchMock.mock.calls.find(([input, init]) => {
    const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
    const method = init?.method ?? (input instanceof Request ? input.method : "GET");
    return url.includes("/users/customers") && method === "POST";
  });
  const request = call?.[0] as Request;
  return JSON.parse(await request.clone().text()) as Record<string, string>;
}

describe("CreateCustomerPage", () => {
  beforeEach(() => {
    startAuthorize.mockReset();
    sessionStorage.clear();
    vi.stubEnv("VITE_WEBAPI_BASE_URL", "https://webapi.test");
    vi.stubGlobal("fetch", vi.fn());
  });

  it("includes adviserId for TenantAdmin, then lands on the new customer", async () => {
    const { fetchMock } = renderCreate(user());

    fireEvent.change(screen.getByLabelText("Name"), { target: { value: "Jordan Lee" } });
    fireEvent.change(screen.getByLabelText("Email"), { target: { value: "jordan@north.example" } });
    fireEvent.change(screen.getByLabelText("Password"), { target: { value: "Passw0rd!" } });
    fireEvent.change(screen.getByLabelText("Confirm password"), { target: { value: "Passw0rd!" } });
    await screen.findByRole("option", { name: "Sam Reed" });
    fireEvent.change(screen.getByLabelText("Adviser"), { target: { value: adviserId } });
    fireEvent.click(screen.getByRole("button", { name: "Create customer" }));

    expect(await screen.findByText("customer detail")).toBeInTheDocument();
    expect(screen.queryByDisplayValue("Passw0rd!")).not.toBeInTheDocument();

    const body = await postedBody(fetchMock);
    expect(body).toEqual({
      name: "Jordan Lee",
      email: "jordan@north.example",
      password: "Passw0rd!",
      adviserId,
    });
  });

  it("omits adviserId for an Adviser caller", async () => {
    const { fetchMock } = renderCreate(
      user({
        role: "adviser",
        name: "Sam Reed",
        id: adviserId,
      }),
    );

    expect(screen.queryByLabelText("Adviser")).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Name"), { target: { value: "Jordan Lee" } });
    fireEvent.change(screen.getByLabelText("Email"), { target: { value: "jordan@north.example" } });
    fireEvent.change(screen.getByLabelText("Password"), { target: { value: "Passw0rd!" } });
    fireEvent.change(screen.getByLabelText("Confirm password"), { target: { value: "Passw0rd!" } });
    fireEvent.click(screen.getByRole("button", { name: "Create customer" }));

    expect(await screen.findByText("customer detail")).toBeInTheDocument();

    const body = await postedBody(fetchMock);
    expect(body).toEqual({
      name: "Jordan Lee",
      email: "jordan@north.example",
      password: "Passw0rd!",
    });
    expect(body).not.toHaveProperty("adviserId");
  });
});
