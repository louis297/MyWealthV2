import { render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { configureStore } from "@reduxjs/toolkit";
import { HomePage } from "@/features/session/HomePage";
import { sessionSlice, type CurrentUser } from "@/features/session/sessionSlice";
import { ShellLayout } from "@/layouts/ShellLayout";
import { api } from "@/shared/api/api";

const startAuthorize = vi.fn();

vi.mock("@/features/session/oidc", () => ({
  startAuthorize: () => startAuthorize(),
}));

vi.mock("@/features/session/SessionProbePage", () => ({
  SessionProbePage: () => <p>session probe</p>,
}));

describe("HomePage", () => {
  beforeEach(() => {
    startAuthorize.mockReset();
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

    expect(startAuthorize).toHaveBeenCalledOnce();
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
          accessToken: "access",
          refreshToken: "refresh",
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

    expect(startAuthorize).not.toHaveBeenCalled();
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("sends a signed-in TenantAdmin to /customers, not the session probe", () => {
    const me: CurrentUser = {
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
    const store = configureStore({
      reducer: {
        session: sessionSlice.reducer,
        [api.reducerPath]: api.reducer,
      },
      middleware: (getDefault) => getDefault().concat(api.middleware),
      preloadedState: {
        session: {
          accessToken: "access",
          refreshToken: "refresh",
          currentUser: me,
          tenant: null,
        },
      },
    });
    const router = createMemoryRouter(
      [
        { path: "/", element: <HomePage /> },
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

    expect(screen.getByText("customers workspace")).toBeInTheDocument();
    expect(screen.queryByText("session probe")).not.toBeInTheDocument();
    expect(startAuthorize).not.toHaveBeenCalled();
  });

  it("sends SystemAdmin to /profile instead of a Customers workspace", () => {
    const me: CurrentUser = {
      id: "11111111-1111-1111-1111-111111111111",
      name: "System Admin",
      email: "system-admin@localhost",
      role: "systemAdmin",
      status: "active",
      tenantId: null,
      tenantCode: null,
      adviserId: null,
      rowVersion: "AAAA",
    };
    const store = configureStore({
      reducer: {
        session: sessionSlice.reducer,
        [api.reducerPath]: api.reducer,
      },
      middleware: (getDefault) => getDefault().concat(api.middleware),
      preloadedState: {
        session: {
          accessToken: "access",
          refreshToken: "refresh",
          currentUser: me,
          tenant: null,
        },
      },
    });
    const router = createMemoryRouter(
      [
        { path: "/", element: <HomePage /> },
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

    expect(screen.getByText("profile")).toBeInTheDocument();
    expect(screen.queryByText("customers workspace")).not.toBeInTheDocument();
    expect(screen.queryByText("session probe")).not.toBeInTheDocument();
  });
});
