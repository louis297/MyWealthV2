import { configureStore } from "@reduxjs/toolkit";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { profileApi } from "@/features/profile/profileApi";
import { sessionSlice } from "@/features/session/sessionSlice";
import { api } from "@/shared/api/api";

const startLogin = vi.fn();

vi.mock("@/features/session/session", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/session/session")>();
  return {
    ...actual,
    startLogin: () => startLogin(),
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
    startLogin.mockReset();
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
  });

  it("PUTs /users/me with name and rowVersion", async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));

    const store = createStore();

    await store.dispatch(
      profileApi.endpoints.updateMe.initiate({ name: "Pat Updated", rowVersion: "AAAA" }),
    );

    const request = fetchMock.mock.calls[0][0] as Request;
    expect(request.url).toBe("http://localhost:3000/api/users/me");
    expect(request.method).toBe("PUT");
    expect(request.headers.get("Authorization")).toBeNull();
    expect(request.credentials).toBe("include");
    expect(JSON.parse(await request.text())).toEqual({
      name: "Pat Updated",
      rowVersion: "AAAA",
    });
  });

  it("PUTs /users/me/password with currentPassword and newPassword", async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));

    const store = createStore();

    await store.dispatch(
      profileApi.endpoints.updateMePassword.initiate({
        currentPassword: "old-pass-1",
        newPassword: "new-pass-1",
      }),
    );

    const request = fetchMock.mock.calls[0][0] as Request;
    expect(request.url).toBe("http://localhost:3000/api/users/me/password");
    expect(request.method).toBe("PUT");
    expect(JSON.parse(await request.text())).toEqual({
      currentPassword: "old-pass-1",
      newPassword: "new-pass-1",
    });
  });
});
