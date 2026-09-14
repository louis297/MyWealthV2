import { configureStore } from "@reduxjs/toolkit";
import { fireEvent, render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AdviserDetailPage } from "@/features/advisers/AdviserDetailPage";
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

const adviser = {
  id: adviserId,
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

describe("AdviserDetailPage", () => {
  beforeEach(() => {
    startAuthorize.mockReset();
    sessionStorage.clear();
    vi.stubEnv("VITE_WEBAPI_BASE_URL", "https://webapi.test");
    vi.stubGlobal("fetch", vi.fn());
    vi.spyOn(window, "confirm").mockReturnValue(true);
  });

  it("shows the assigned-customer disable sentence and a customers filter link, not code=disabled", async () => {
    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
      const method = init?.method ?? (input instanceof Request ? input.method : "GET");
      if (url.endsWith(`/users/advisers/${adviserId}`) && method === "GET") {
        return new Response(JSON.stringify(adviser), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        });
      }
      if (url.endsWith("/disable")) {
        return new Response(
          JSON.stringify({
            title: "One or more validation errors occurred.",
            status: 400,
            errors: {
              "": ["Reassign or disable assigned customers before disabling this adviser."],
            },
          }),
          { status: 400, headers: { "Content-Type": "application/json" } },
        );
      }
      return new Response("", { status: 404 });
    });

    const store = configureStore({
      reducer: {
        session: sessionSlice.reducer,
        [api.reducerPath]: api.reducer,
      },
      middleware: (getDefault) => getDefault().concat(api.middleware),
    });
    store.dispatch(setTokens({ accessToken: "access-1", refreshToken: "refresh-1" }));
    store.dispatch(setCurrentUser(user()));

    const router = createMemoryRouter(
      [{ path: "/advisers/:id", element: <AdviserDetailPage /> }],
      { initialEntries: [`/advisers/${adviserId}`] },
    );

    render(
      <Provider store={store}>
        <RouterProvider router={router} />
      </Provider>,
    );

    expect(await screen.findByText("Sam Reed")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Disable" }));

    expect(
      await screen.findByText(
        "Reassign or disable assigned customers before disabling this adviser.",
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /customers/i })).toHaveAttribute(
      "href",
      `/customers?adviserId=${adviserId}`,
    );
    expect(screen.queryByText(/code=disabled/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/user is disabled/i)).not.toBeInTheDocument();
  });
});
