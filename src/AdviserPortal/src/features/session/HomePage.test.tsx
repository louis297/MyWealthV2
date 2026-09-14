import { render } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { configureStore } from "@reduxjs/toolkit";
import { HomePage } from "@/features/session/HomePage";
import { sessionSlice } from "@/features/session/sessionSlice";
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
});
