import { configureStore } from "@reduxjs/toolkit";
import { render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it } from "vitest";
import { RoleGate } from "@/features/session/RoleGate";
import { roles } from "@/features/session/roles";
import { sessionSlice, setCurrentUser, type CurrentUser } from "@/features/session/sessionSlice";
import { api } from "@/shared/api/api";

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

function renderAt(me: CurrentUser, path: string) {
  const store = configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });
  store.dispatch(setCurrentUser(me));

  const router = createMemoryRouter(
    [
      {
        path: "/forbidden",
        element: <p>forbidden</p>,
      },
      {
        path: "/customers",
        element: <RoleGate allow={[roles.tenantAdmin, roles.adviser]} />,
        children: [
          { index: true, element: <p>customers</p> },
          { path: "new", element: <p>new customer</p> },
        ],
      },
      {
        path: "/advisers",
        element: <RoleGate allow={[roles.tenantAdmin]} />,
        children: [
          { index: true, element: <p>advisers</p> },
          { path: "new", element: <p>new adviser</p> },
        ],
      },
    ],
    { initialEntries: [path] },
  );

  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );
}

describe("RoleGate", () => {
  beforeEach(() => {
    sessionStorage.clear();
  });

  it("sends an Adviser on /advisers and /advisers/new to /forbidden", () => {
    renderAt(user({ role: "adviser", name: "Sam Reed" }), "/advisers");
    expect(screen.getByText("forbidden")).toBeInTheDocument();
    expect(screen.queryByText("advisers")).not.toBeInTheDocument();
  });

  it("sends an Adviser on /advisers/new to /forbidden", () => {
    renderAt(user({ role: "adviser", name: "Sam Reed" }), "/advisers/new");
    expect(screen.getByText("forbidden")).toBeInTheDocument();
    expect(screen.queryByText("new adviser")).not.toBeInTheDocument();
  });

  it("sends SystemAdmin on /customers and /advisers to /forbidden", () => {
    renderAt(
      user({
        role: "systemAdmin",
        name: "System Admin",
        tenantId: null,
        tenantCode: null,
      }),
      "/customers",
    );
    expect(screen.getByText("forbidden")).toBeInTheDocument();
    expect(screen.queryByText("customers")).not.toBeInTheDocument();
  });

  it("allows TenantAdmin on /advisers and Adviser on /customers", () => {
    renderAt(user(), "/advisers");
    expect(screen.getByText("advisers")).toBeInTheDocument();
    expect(screen.queryByText("forbidden")).not.toBeInTheDocument();
  });
});
