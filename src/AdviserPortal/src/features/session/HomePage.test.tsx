import { render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { configureStore } from "@reduxjs/toolkit";
import { HomePage } from "@/features/session/HomePage";
import { sessionSlice, type CurrentUser } from "@/features/session/sessionSlice";
import { ShellLayout } from "@/layouts/ShellLayout";
import { api } from "@/shared/api/api";

const startLogin = vi.fn();

vi.mock("@/features/session/session", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/session/session")>();
  return {
    ...actual,
    startLogin: () => startLogin(),
  };
});

vi.mock("@/features/session/SessionProbePage", () => ({
  SessionProbePage: () => <p>session probe</p>,
}));

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

function renderHome(me: CurrentUser) {
  const store = configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
    preloadedState: {
      session: {
        currentUser: me,
        tenant: null,
      },
    },
  });
  const router = createMemoryRouter(
    [
      { path: "/", element: <HomePage /> },
      { path: "/dashboard", element: <p>dashboard</p> },
      { path: "/customers", element: <p>customers workspace</p> },
      { path: "/profile", element: <p>profile</p> },
    ],
    { initialEntries: ["/"] },
  );

  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );
}

describe("HomePage", () => {
  beforeEach(() => {
    startLogin.mockReset();
    sessionStorage.clear();
  });

  it("starts authorize when there is no access token", () => {
    const store = configureStore({
      reducer: {
        session: sessionSlice.reducer,
        [api.reducerPath]: api.reducer,
      },
      middleware: (getDefault) => getDefault().concat(api.middleware),
    });
    const router = createMemoryRouter(
      [
        {
          path: "/",
          element: (
            <ShellLayout>
              <HomePage />
            </ShellLayout>
          ),
        },
      ],
      { initialEntries: ["/"] },
    );

    render(
      <Provider store={store}>
        <RouterProvider router={router} />
      </Provider>,
    );

    expect(screen.getByText("Loading session…")).toBeInTheDocument();
    expect(startLogin).not.toHaveBeenCalled();
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("does not start authorize when signed in", () => {
    const store = configureStore({
      reducer: {
        session: sessionSlice.reducer,
        [api.reducerPath]: api.reducer,
      },
      middleware: (getDefault) => getDefault().concat(api.middleware),
      preloadedState: {
        session: {
          currentUser: null,
          tenant: null,
        },
      },
    });
    const router = createMemoryRouter(
      [
        {
          path: "/",
          element: (
            <ShellLayout>
              <HomePage />
            </ShellLayout>
          ),
        },
      ],
      { initialEntries: ["/"] },
    );

    render(
      <Provider store={store}>
        <RouterProvider router={router} />
      </Provider>,
    );

    expect(startLogin).not.toHaveBeenCalled();
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("sends a signed-in TenantAdmin to /dashboard, not the customers workspace", () => {
    renderHome(user({ role: "tenantAdmin" }));

    expect(screen.getByText("dashboard")).toBeInTheDocument();
    expect(screen.queryByText("customers workspace")).not.toBeInTheDocument();
    expect(screen.queryByText("session probe")).not.toBeInTheDocument();
    expect(startLogin).not.toHaveBeenCalled();
  });

  it("sends a signed-in Adviser to /dashboard, not the customers workspace", () => {
    renderHome(user({ role: "adviser", name: "Sam Reed" }));

    expect(screen.getByText("dashboard")).toBeInTheDocument();
    expect(screen.queryByText("customers workspace")).not.toBeInTheDocument();
    expect(startLogin).not.toHaveBeenCalled();
  });

  it("sends SystemAdmin to /profile instead of a Customers workspace", () => {
    renderHome(
      user({
        role: "systemAdmin",
        name: "System Admin",
        email: "system-admin@localhost",
        tenantId: null,
        tenantCode: null,
      }),
    );

    expect(screen.getByText("profile")).toBeInTheDocument();
    expect(screen.queryByText("customers workspace")).not.toBeInTheDocument();
    expect(screen.queryByText("dashboard")).not.toBeInTheDocument();
    expect(screen.queryByText("session probe")).not.toBeInTheDocument();
  });
});
