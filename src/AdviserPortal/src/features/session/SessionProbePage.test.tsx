import { render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { describe, expect, it, vi } from "vitest";
import { configureStore } from "@reduxjs/toolkit";
import { sessionSlice } from "@/features/session/sessionSlice";
import { SessionProbePage } from "@/features/session/SessionProbePage";
import { api } from "@/shared/api/api";

vi.mock("@/shared/api/api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/shared/api/api")>();
  return {
    ...actual,
    useGetMeQuery: () => ({
      data: {
        id: "11111111-1111-1111-1111-111111111111",
        name: "System Admin",
        email: "system-admin@localhost",
        role: "systemAdmin",
        status: "active",
        tenantId: null,
        tenantCode: null,
        adviserId: null,
        rowVersion: "AAAA",
      },
      error: undefined,
      isLoading: false,
    }),
  };
});

describe("SessionProbePage", () => {
  it("renders display name, role, tenant, and the /users/me JSON", () => {
    const store = configureStore({
      reducer: {
        session: sessionSlice.reducer,
        [api.reducerPath]: api.reducer,
      },
      middleware: (getDefault) => getDefault().concat(api.middleware),
    });

    render(
      <Provider store={store}>
        <SessionProbePage />
      </Provider>,
    );

    expect(screen.getAllByText(/System Admin/).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/systemAdmin/).length).toBeGreaterThan(0);
    expect(screen.getByText(/no tenant/i)).toBeInTheDocument();
    expect(screen.getByText(/system-admin@localhost/)).toBeInTheDocument();
  });
});
