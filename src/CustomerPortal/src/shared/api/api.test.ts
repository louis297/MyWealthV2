import { configureStore } from "@reduxjs/toolkit";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { sessionSlice } from "@/features/session/sessionSlice";
import { api } from "@/shared/api/api";

const probeApi = api.injectEndpoints({
  endpoints: (builder) => ({
    postProbe: builder.query<unknown, void>({
      query: () => ({ url: "/probe", method: "POST", body: {} }),
    }),
  }),
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

function json(body: unknown) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}

describe("BFF API client", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: "/",
      search: "",
      href: "http://localhost:3000/",
      assign: vi.fn(),
    });
  });

  it("does not send X-MyWealth-Request on a GET query", async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockResolvedValueOnce(json({}));

    await createStore().dispatch(api.endpoints.getMe.initiate());

    const request = fetchMock.mock.calls[0][0] as Request;
    expect(request.method).toBe("GET");
    expect(request.headers.get("X-MyWealth-Request")).toBeNull();
  });

  it("sends X-MyWealth-Request on a POST query", async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockResolvedValueOnce(json({}));

    await createStore().dispatch(probeApi.endpoints.postProbe.initiate());

    const request = fetchMock.mock.calls[0][0] as Request;
    expect(request.method).toBe("POST");
    expect(request.headers.get("X-MyWealth-Request")).toBe("1");
  });
});
