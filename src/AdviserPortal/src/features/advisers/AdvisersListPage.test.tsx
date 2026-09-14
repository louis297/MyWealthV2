import { configureStore } from "@reduxjs/toolkit";
import { render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AdvisersListPage } from "@/features/advisers/AdvisersListPage";
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

const adviser = {
  id: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  tenantId: "22222222-2222-2222-2222-222222222222",
  name: "Sam Reed",
  email: "sam@north.example",
  status: "active",
  rowVersion: "AAAA",
  created: "2026-09-14T00:00:00+00:00",
};

function user(): CurrentUser {
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
  };
}

describe("AdvisersListPage", () => {
  beforeEach(() => {
    startAuthorize.mockReset();
    sessionStorage.clear();
    vi.stubEnv("VITE_WEBAPI_BASE_URL", "https://webapi.test");
    vi.stubGlobal("fetch", vi.fn());
  });

  it("lists advisers without an assigned-customer count", async () => {
    const store = configureStore({
      reducer: {
        session: sessionSlice.reducer,
        [api.reducerPath]: api.reducer,
      },
      middleware: (getDefault) => getDefault().concat(api.middleware),
    });
    store.dispatch(setTokens({ accessToken: "access-1", refreshToken: "refresh-1" }));
    store.dispatch(setCurrentUser(user()));
    vi.mocked(fetch).mockResolvedValue(
      new Response(
        JSON.stringify({ items: [adviser], page: 1, pageSize: 20, totalCount: 1 }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      ),
    );

    const router = createMemoryRouter(
      [
        { path: "/advisers", element: <AdvisersListPage /> },
        { path: "/advisers/new", element: <p>new adviser</p> },
      ],
      { initialEntries: ["/advisers"] },
    );

    render(
      <Provider store={store}>
        <RouterProvider router={router} />
      </Provider>,
    );

    expect(await screen.findByText("Sam Reed")).toBeInTheDocument();
    expect(screen.getByText("sam@north.example")).toBeInTheDocument();
    expect(screen.getByText("2026-09-14")).toBeInTheDocument();
    expect(screen.queryByText(/assigned/i)).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "New adviser" })).toHaveAttribute("href", "/advisers/new");
    expect(screen.getByRole("link", { name: "Sam Reed" })).toHaveAttribute("href", `/advisers/${adviser.id}`);
  });
});
