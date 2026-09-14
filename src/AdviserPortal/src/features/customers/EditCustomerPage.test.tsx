import { configureStore } from "@reduxjs/toolkit";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { EditCustomerPage } from "@/features/customers/EditCustomerPage";
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

const customerId = "cccccccccccccccc-cccc-cccc-cccc-cccccccccccc";
const adviserId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const otherAdviserId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

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

const advisers = [
  {
    id: adviserId,
    tenantId: customer.tenantId,
    name: "Sam Reed",
    email: "sam@north.example",
    status: "active",
    rowVersion: "AAAA",
    created: "2026-09-14T00:00:00+00:00",
  },
  {
    id: otherAdviserId,
    tenantId: customer.tenantId,
    name: "Alex Kim",
    email: "alex@north.example",
    status: "active",
    rowVersion: "AAAA",
    created: "2026-09-14T00:00:00+00:00",
  },
];

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

function renderEdit(
  me: CurrentUser,
  fetchImpl: (url: string, method: string, body: string | undefined) => Response,
) {
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
      { path: "/customers/:id", element: <p>customer detail</p> },
      { path: "/customers/:id/edit", element: <EditCustomerPage /> },
    ],
    { initialEntries: [`/customers/${customerId}/edit`] },
  );

  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );

  return { fetchMock };
}

describe("EditCustomerPage", () => {
  beforeEach(() => {
    startAuthorize.mockReset();
    sessionStorage.clear();
    vi.stubEnv("VITE_WEBAPI_BASE_URL", "https://webapi.test");
    vi.stubGlobal("fetch", vi.fn());
  });

  it("lets TenantAdmin rename and reassign adviserId", async () => {
    const puts: string[] = [];
    renderEdit(user(), (url, method, body) => {
      if (url.includes("/users/advisers")) {
        return new Response(
          JSON.stringify({ items: advisers, page: 1, pageSize: 100, totalCount: 2 }),
          { status: 200, headers: { "Content-Type": "application/json" } },
        );
      }
      if (url.endsWith(`/users/customers/${customerId}`) && method === "GET") {
        return new Response(JSON.stringify(customer), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        });
      }
      if (url.endsWith(`/users/customers/${customerId}`) && method === "PUT") {
        puts.push(body ?? "");
        return new Response(null, { status: 204 });
      }
      return new Response("", { status: 404 });
    });

    fireEvent.change(await screen.findByLabelText("Name"), { target: { value: "Jordan Updated" } });
    await screen.findByRole("option", { name: "Alex Kim" });
    fireEvent.change(screen.getByLabelText("Adviser"), { target: { value: otherAdviserId } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByText("customer detail")).toBeInTheDocument();
    expect(puts).toEqual([
      JSON.stringify({
        name: "Jordan Updated",
        adviserId: otherAdviserId,
        rowVersion: "AAAA",
      }),
    ]);
  });

  it("omits the adviser control for an Adviser caller", async () => {
    const puts: string[] = [];
    renderEdit(
      user({ role: "adviser", name: "Sam Reed", id: adviserId }),
      (url, method, body) => {
        if (url.includes("/users/advisers")) {
          return new Response("", { status: 403 });
        }
        if (url.endsWith(`/users/customers/${customerId}`) && method === "GET") {
          return new Response(JSON.stringify(customer), {
            status: 200,
            headers: { "Content-Type": "application/json" },
          });
        }
        if (method === "PUT") {
          puts.push(body ?? "");
          return new Response(null, { status: 204 });
        }
        return new Response("", { status: 404 });
      },
    );

    expect(await screen.findByLabelText("Name")).toBeInTheDocument();
    expect(screen.queryByLabelText("Adviser")).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Name"), { target: { value: "Jordan Updated" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => {
      expect(puts).toEqual([JSON.stringify({ name: "Jordan Updated", rowVersion: "AAAA" })]);
    });
    expect(JSON.parse(puts[0] ?? "{}")).not.toHaveProperty("adviserId");
  });

  it("does not replay a stale rowVersion after 409", async () => {
    const puts: string[] = [];
    renderEdit(user(), (url, method, body) => {
      if (url.includes("/users/advisers")) {
        return new Response(
          JSON.stringify({ items: advisers, page: 1, pageSize: 100, totalCount: 2 }),
          { status: 200, headers: { "Content-Type": "application/json" } },
        );
      }
      if (url.endsWith(`/users/customers/${customerId}`) && method === "GET") {
        return new Response(JSON.stringify(customer), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        });
      }
      if (method === "PUT") {
        puts.push(body ?? "");
        return new Response("", { status: 409 });
      }
      return new Response("", { status: 404 });
    });

    fireEvent.change(await screen.findByLabelText("Name"), { target: { value: "Jordan Updated" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByText(/reload/i)).toBeInTheDocument();
    expect(puts).toEqual([
      JSON.stringify({
        name: "Jordan Updated",
        adviserId,
        rowVersion: "AAAA",
      }),
    ]);
  });
});
