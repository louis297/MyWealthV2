import { render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { configureStore } from "@reduxjs/toolkit";
import { HomePage } from "@/features/session/HomePage";
import { sessionSlice } from "@/features/session/sessionSlice";
import { ShellLayout } from "@/layouts/ShellLayout";

const startAuthorize = vi.fn();

vi.mock("@/features/session/oidc", () => ({
  startAuthorize: () => startAuthorize(),
}));

describe("HomePage", () => {
  beforeEach(() => {
    startAuthorize.mockReset();
    sessionStorage.clear();
  });

  it("starts authorize when there is no access token", () => {
    const store = configureStore({ reducer: { session: sessionSlice.reducer } });
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
      reducer: { session: sessionSlice.reducer },
      preloadedState: {
        session: { accessToken: "access", refreshToken: "refresh", currentUser: null },
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
    expect(screen.getByText(/signed in/i)).toBeInTheDocument();
  });
});
