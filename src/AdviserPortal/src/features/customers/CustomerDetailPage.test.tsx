import { configureStore } from "@reduxjs/toolkit";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CustomerDetailPage } from "@/features/customers/CustomerDetailPage";
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

function renderDetail(
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
    startAuthorize.mockReset();
    sessionStorage.clear();
    vi.stubEnv("VITE_WEBAPI_BASE_URL", "https://webapi.test");
    vi.stubGlobal("fetch", vi.fn());
    vi.spyOn(window, "confirm").mockReturnValue(true);
  });

  it("stays on the URL and shows Not found for a missing customer", async () => {
    const { router } = renderDetail(user(), () => new Response("", { status: 404 }));

    expect(await screen.findByText("Not found")).toBeInTheDocument();
    expect(screen.queryByText("customers list")).not.toBeInTheDocument();
    expect(screen.queryByText("Forbidden")).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/customers/${customerId}`);
  });

  it("is read-only and disables with the current rowVersion", async () => {
    const posted: string[] = [];
    renderDetail(user(), (url, method, body) => {
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
});
