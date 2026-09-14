import { configureStore } from "@reduxjs/toolkit";
import { fireEvent, render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CreateAdviserPage } from "@/features/advisers/CreateAdviserPage";
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

function user(): CurrentUser {
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
  };
}

describe("CreateAdviserPage", () => {
  beforeEach(() => {
    startAuthorize.mockReset();
    sessionStorage.clear();
    vi.stubEnv("VITE_WEBAPI_BASE_URL", "https://webapi.test");
    vi.stubGlobal("fetch", vi.fn());
  });

  it("POSTs name, email, and password without tenantId, then lands on detail", async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ id: adviserId }), {
        status: 201,
        headers: { "Content-Type": "application/json" },
      }),
    );

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
      [
        { path: "/advisers/new", element: <CreateAdviserPage /> },
        { path: "/advisers/:id", element: <p>adviser detail</p> },
      ],
      { initialEntries: ["/advisers/new"] },
    );

    render(
      <Provider store={store}>
        <RouterProvider router={router} />
      </Provider>,
    );

    fireEvent.change(screen.getByLabelText("Name"), { target: { value: "Sam Reed" } });
    fireEvent.change(screen.getByLabelText("Email"), { target: { value: "sam@north.example" } });
    fireEvent.change(screen.getByLabelText("Password"), { target: { value: "Passw0rd!" } });
    fireEvent.change(screen.getByLabelText("Confirm password"), { target: { value: "Passw0rd!" } });
    fireEvent.click(screen.getByRole("button", { name: "Create adviser" }));

    expect(await screen.findByText("adviser detail")).toBeInTheDocument();
    const request = fetchMock.mock.calls[0][0] as Request;
    expect(request.url).toBe("https://webapi.test/users/advisers");
    expect(JSON.parse(await request.clone().text())).toEqual({
      name: "Sam Reed",
      email: "sam@north.example",
      password: "Passw0rd!",
    });
  });
});
