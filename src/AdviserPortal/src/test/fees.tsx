import { configureStore } from "@reduxjs/toolkit";
import { render } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider, type RouteObject } from "react-router";
import { vi } from "vitest";
import type { TradeFeeSchedule } from "@/features/fees/feesApi";
import { sessionSlice, setCurrentUser, type CurrentUser } from "@/features/session/sessionSlice";
import { api } from "@/shared/api/api";

export const tenantId = "22222222-2222-2222-2222-222222222222";

export const currencies = [
  { code: "JPY", name: "Japanese yen", decimalPlaces: 0, isActive: true },
  { code: "NZD", name: "New Zealand dollar", decimalPlaces: 2, isActive: true },
  { code: "USD", name: "US dollar", decimalPlaces: 2, isActive: true },
];

export function feeUser(overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    name: "Pat Admin",
    email: "pat@north.example",
    role: "tenantAdmin",
    status: "active",
    tenantId,
    tenantCode: "north-advisory",
    adviserId: null,
    rowVersion: "AAAA",
    ...overrides,
  };
}

export const adviser = () => feeUser({ role: "adviser", name: "Sam Reed" });

export function schedule(overrides: Partial<TradeFeeSchedule> = {}): TradeFeeSchedule {
  return {
    id: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
    quoteCurrency: "NZD",
    effectiveFrom: "2026-12-01",
    minimum: 10,
    cap: null,
    bands: [
      { from: 0, ratePercent: 0.5 },
      { from: 10000, ratePercent: 0.3 },
    ],
    inEffect: false,
    rowVersion: "AAAA",
    ...overrides,
  };
}

export function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

export function problem(status: number, body: Record<string, unknown>) {
  return new Response(JSON.stringify({ status, ...body }), {
    status,
    headers: { "Content-Type": "application/problem+json" },
  });
}

export type Handler = (request: Request) => Response | Promise<Response> | undefined;

/**
 * Renders `routes` at `path` with a fresh store. `handler` answers each fetch; anything it leaves
 * unanswered gets `GET /currencies` or a 404. Every request is recorded.
 */
export function renderFees(me: CurrentUser, routes: RouteObject[], path: string, handler: Handler = () => undefined) {
  const store = configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });
  store.dispatch(setCurrentUser(me));

  const requests: Request[] = [];
  vi.mocked(fetch).mockImplementation(async (input, init) => {
    const request = input instanceof Request ? input : new Request(String(input), init);
    requests.push(request.clone());
    const answer = await handler(request);
    if (answer) {
      return answer;
    }
    if (request.url.includes("/currencies") && request.method === "GET") {
      return json(currencies);
    }
    return new Response("", { status: 404 });
  });

  const router = createMemoryRouter(routes, { initialEntries: [path] });
  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );

  return { router, requests };
}

export function stubBrowser(pathname: string) {
  sessionStorage.clear();
  vi.stubGlobal("fetch", vi.fn());
  vi.stubGlobal("location", {
    origin: "http://localhost:3000",
    pathname,
    search: "",
    href: `http://localhost:3000${pathname}`,
    assign: vi.fn(),
  });
}

export function requestsTo(requests: Request[], method: string, path: string) {
  return requests.filter((request) => request.method === method && new URL(request.url).pathname === `/api${path}`);
}
