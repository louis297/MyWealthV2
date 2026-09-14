import { configureStore } from "@reduxjs/toolkit";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { profileApi } from "@/features/profile/profileApi";
import { sessionSlice, setTokens } from "@/features/session/sessionSlice";
import { api } from "@/shared/api/api";

const startAuthorize = vi.fn();

vi.mock("@/features/session/oidc", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/session/oidc")>();
  return {
    ...actual,
    startAuthorize: () => startAuthorize(),
  };
});

function createStore() {
  return configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });
}

describe("profile API", () => {
  beforeEach(() => {
    startAuthorize.mockReset();
    sessionStorage.clear();
    vi.stubEnv("VITE_WEBAPI_BASE_URL", "https://webapi.test");
    vi.stubGlobal("fetch", vi.fn());
  });

  it("PUTs /users/me with name and rowVersion", async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));

    const store = createStore();
    store.dispatch(setTokens({ accessToken: "access-1", refreshToken: "refresh-1" }));

    await store.dispatch(
      profileApi.endpoints.updateMe.initiate({ name: "Pat Updated", rowVersion: "AAAA" }),
    );

    const request = fetchMock.mock.calls[0][0] as Request;
    expect(request.url).toBe("https://webapi.test/users/me");
    expect(request.method).toBe("PUT");
    expect(request.headers.get("Authorization")).toBe("Bearer access-1");
    expect(JSON.parse(await request.text())).toEqual({
      name: "Pat Updated",
      rowVersion: "AAAA",
    });
  });

  it("PUTs /users/me/password with currentPassword and newPassword", async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));

    const store = createStore();
    store.dispatch(setTokens({ accessToken: "access-1", refreshToken: "refresh-1" }));

    await store.dispatch(
      profileApi.endpoints.updateMePassword.initiate({
        currentPassword: "old-pass-1",
        newPassword: "new-pass-1",
      }),
    );

    const request = fetchMock.mock.calls[0][0] as Request;
    expect(request.url).toBe("https://webapi.test/users/me/password");
    expect(request.method).toBe("PUT");
    expect(JSON.parse(await request.text())).toEqual({
      currentPassword: "old-pass-1",
      newPassword: "new-pass-1",
    });
  });
});
