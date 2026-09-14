import { configureStore } from "@reduxjs/toolkit";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ProfilePage } from "@/features/profile/ProfilePage";
import { sessionSlice, setCurrentUser, setTokens, type CurrentUser } from "@/features/session/sessionSlice";
import { ACCESS_TOKEN_KEY, REFRESH_TOKEN_KEY } from "@/features/session/oidcStorage";
import { api } from "@/shared/api/api";

const startAuthorize = vi.fn();

vi.mock("@/features/session/oidc", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/session/oidc")>();
  return {
    ...actual,
    startAuthorize: () => startAuthorize(),
  };
});

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

function renderProfile(me: CurrentUser) {
  const store = configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });
  store.dispatch(setTokens({ accessToken: "access-1", refreshToken: "refresh-1" }));
  store.dispatch(setCurrentUser(me));

  const router = createMemoryRouter(
    [{ path: "/profile", element: <ProfilePage /> }],
    { initialEntries: ["/profile"] },
  );

  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );

  return store;
}

describe("ProfilePage", () => {
  beforeEach(() => {
    startAuthorize.mockReset();
    sessionStorage.clear();
    vi.stubEnv("VITE_WEBAPI_BASE_URL", "https://webapi.test");
    vi.stubGlobal("fetch", vi.fn());
  });

  it("shows read-only identity and PUTs a trimmed name with rowVersion", async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));

    renderProfile(user());

    expect(screen.getByText("pat@north.example")).toBeInTheDocument();
    expect(screen.getByText("tenantAdmin")).toBeInTheDocument();
    expect(screen.getByText("active")).toBeInTheDocument();
    expect(screen.getByText("north-advisory")).toBeInTheDocument();
    expect(screen.getByText("11111111-1111-1111-1111-111111111111")).toBeInTheDocument();
    expect(screen.queryByRole("textbox", { name: /email/i })).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Name"), { target: { value: "  Pat Updated  " } });
    fireEvent.click(screen.getByRole("button", { name: "Save name" }));

    await waitFor(async () => {
      expect(fetchMock).toHaveBeenCalled();
      const request = fetchMock.mock.calls[0][0] as Request;
      expect(request.url).toBe("https://webapi.test/users/me");
      expect(request.method).toBe("PUT");
      expect(JSON.parse(await request.clone().text())).toEqual({
        name: "Pat Updated",
        rowVersion: "AAAA",
      });
    });
  });

  it("clears tokens and re-authorizes after a successful password change", async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));

    const store = renderProfile(user());

    fireEvent.change(screen.getByLabelText("Current password"), {
      target: { value: "old-pass-1" },
    });
    fireEvent.change(screen.getByLabelText("New password"), {
      target: { value: "new-pass-12" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Change password" }));

    await waitFor(() => {
      expect(store.getState().session.accessToken).toBeNull();
      expect(sessionStorage.getItem(ACCESS_TOKEN_KEY)).toBeNull();
      expect(sessionStorage.getItem(REFRESH_TOKEN_KEY)).toBeNull();
      expect(startAuthorize).toHaveBeenCalledOnce();
    });

    const request = fetchMock.mock.calls[0][0] as Request;
    expect(request.url).toBe("https://webapi.test/users/me/password");
    expect(JSON.parse(await request.clone().text())).toEqual({
      currentPassword: "old-pass-1",
      newPassword: "new-pass-12",
    });
  });
});
