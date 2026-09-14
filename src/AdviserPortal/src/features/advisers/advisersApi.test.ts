import { configureStore } from "@reduxjs/toolkit";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { advisersApi } from "@/features/advisers/advisersApi";
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

const envelope = {
  items: [
    {
      id: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      tenantId: "22222222-2222-2222-2222-222222222222",
      name: "Sam Reed",
      email: "sam@north.example",
      status: "active",
      rowVersion: "AAAA",
      created: "2026-09-14T00:00:00+00:00",
    },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 1,
};

function createStore() {
  return configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });
}

describe("advisers API", () => {
  beforeEach(() => {
    startAuthorize.mockReset();
    sessionStorage.clear();
    vi.stubEnv("VITE_WEBAPI_BASE_URL", "https://webapi.test");
    vi.stubGlobal("fetch", vi.fn());
  });

  it("GETs /users/advisers with pageSize 20", async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify(envelope), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
    );

    const store = createStore();
    store.dispatch(setTokens({ accessToken: "access-1", refreshToken: "refresh-1" }));

    const result = await store.dispatch(
      advisersApi.endpoints.getAdvisers.initiate({ page: 1, enabledOnly: true }),
    );

    expect(result.data).toEqual(envelope);
    const request = fetchMock.mock.calls[0][0] as Request;
    const url = new URL(request.url);
    expect(url.origin + url.pathname).toBe("https://webapi.test/users/advisers");
    expect(url.searchParams.get("page")).toBe("1");
    expect(url.searchParams.get("pageSize")).toBe("20");
    expect(url.searchParams.get("enabledOnly")).toBe("true");
    expect(request.headers.get("Authorization")).toBe("Bearer access-1");
  });

  it("creates, gets, updates, disables, and enables an adviser", async () => {
    const fetchMock = vi.mocked(fetch);
    const item = envelope.items[0];
    fetchMock.mockImplementation(async (input, init) => {
      const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
      const method = init?.method ?? (input instanceof Request ? input.method : "GET");
      if (url.endsWith("/users/advisers") && method === "POST") {
        return new Response(JSON.stringify({ id: item.id }), {
          status: 201,
          headers: { "Content-Type": "application/json" },
        });
      }
      if (url.endsWith(`/users/advisers/${item.id}`) && method === "GET") {
        return new Response(JSON.stringify(item), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        });
      }
      if (method === "PUT" || method === "POST") {
        return new Response(null, { status: 204 });
      }
      return new Response("", { status: 404 });
    });

    const store = createStore();
    store.dispatch(setTokens({ accessToken: "access-1", refreshToken: "refresh-1" }));

    const created = await store.dispatch(
      advisersApi.endpoints.createAdviser.initiate({
        name: "Sam Reed",
        email: "sam@north.example",
        password: "Passw0rd!",
      }),
    );
    expect(created.data).toEqual({ id: item.id });

    const got = await store.dispatch(advisersApi.endpoints.getAdviser.initiate(item.id));
    expect(got.data).toEqual(item);

    await store.dispatch(
      advisersApi.endpoints.updateAdviser.initiate({
        id: item.id,
        name: "Samantha Reed",
        rowVersion: "AAAA",
      }),
    );
    await store.dispatch(
      advisersApi.endpoints.disableAdviser.initiate({ id: item.id, rowVersion: "AAAA" }),
    );
    await store.dispatch(
      advisersApi.endpoints.enableAdviser.initiate({ id: item.id, rowVersion: "BBBB" }),
    );

    async function bodyOf(method: string, suffix: string) {
      const call = fetchMock.mock.calls.find(([input, init]) => {
        const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
        const usedMethod = init?.method ?? (input instanceof Request ? input.method : "GET");
        return usedMethod === method && url.endsWith(suffix);
      });
      const request = call?.[0] as Request;
      return JSON.parse(await request.clone().text()) as unknown;
    }

    expect(await bodyOf("POST", "/users/advisers")).toEqual({
      name: "Sam Reed",
      email: "sam@north.example",
      password: "Passw0rd!",
    });
    expect(await bodyOf("PUT", `/users/advisers/${item.id}`)).toEqual({
      name: "Samantha Reed",
      rowVersion: "AAAA",
    });
    expect(await bodyOf("POST", `/users/advisers/${item.id}/disable`)).toEqual({
      rowVersion: "AAAA",
    });
    expect(await bodyOf("POST", `/users/advisers/${item.id}/enable`)).toEqual({
      rowVersion: "BBBB",
    });
  });
});
