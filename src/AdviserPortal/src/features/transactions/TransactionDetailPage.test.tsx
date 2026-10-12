import { configureStore } from "@reduxjs/toolkit";
import { fireEvent, render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { sessionSlice, setCurrentUser, type CurrentUser } from "@/features/session/sessionSlice";
import { TransactionDetailPage } from "@/features/transactions/TransactionDetailPage";
import { api } from "@/shared/api/api";

const originalId = "dddddddd-dddd-dddd-dddd-dddddddddddd";
const reverseId = "99999999-9999-9999-9999-999999999999";
const idempotencyKey = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

const original = {
  id: originalId,
  tenantId: "22222222-2222-2222-2222-222222222222",
  accountId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  customerId: "cccccccccccccccc-cccc-cccc-cccc-cccccccccccc",
  type: "Buy",
  cash: { amount: -1250, currency: "NZD" },
  bookedAt: "2026-10-11T10:00:00+13:00",
  memo: "Parcel",
  reference: null,
  originalTransactionId: null,
  rowVersion: "AAAA",
  security: [
    {
      instrumentId: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
      quantity: 50,
      cost: 750,
      costCurrency: "USD",
    },
  ],
};

function user(): CurrentUser {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    name: "Sam Reed",
    email: "sam@north.example",
    role: "adviser",
    status: "active",
    tenantId: original.tenantId,
    tenantCode: "north-advisory",
    adviserId: "ffffffff-ffff-ffff-ffff-ffffffffffff",
    rowVersion: "AAAA",
  };
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

describe("TransactionDetailPage", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
    vi.spyOn(crypto, "randomUUID").mockReturnValue(idempotencyKey);
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: `/transactions/${originalId}`,
      search: "",
      href: `http://localhost:3000/transactions/${originalId}`,
      assign: vi.fn(),
    });
  });

  it("reverses a Buy once and keeps the original row", async () => {
    const store = configureStore({
      reducer: {
        session: sessionSlice.reducer,
        [api.reducerPath]: api.reducer,
      },
      middleware: (getDefault) => getDefault().concat(api.middleware),
    });
    store.dispatch(setCurrentUser(user()));

    const fetchMock = vi.mocked(fetch);
    fetchMock.mockImplementation(async (input, init) => {
      const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
      const method = init?.method ?? (input instanceof Request ? input.method : "GET");
      if (url.endsWith(`/transactions/${originalId}/reverse`) && method === "POST") {
        return json({ id: reverseId }, 201);
      }
      if (url.endsWith(`/transactions/${originalId}`) && method === "GET") {
        return json(original);
      }
      return new Response("", { status: 404 });
    });

    const router = createMemoryRouter(
      [{ path: "/transactions/:id", element: <TransactionDetailPage /> }],
      { initialEntries: [`/transactions/${originalId}`] },
    );

    render(
      <Provider store={store}>
        <RouterProvider router={router} />
      </Provider>,
    );

    expect(await screen.findByText(originalId)).toBeInTheDocument();
    expect(screen.getByText("Buy")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reverse" }));
    expect(await screen.findByText(reverseId)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reverse" }));
    expect(await screen.findAllByText(reverseId)).toHaveLength(1);
    expect(screen.getByText(originalId)).toBeInTheDocument();
    expect(screen.getByText("Buy")).toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/transactions/${originalId}`);
    expect(document.querySelector('input[type="password"]')).toBeNull();

    const posts = fetchMock.mock.calls
      .map(([input]) => input)
      .filter((input): input is Request => input instanceof Request && input.method === "POST");
    expect(posts).toHaveLength(2);
    for (const post of posts) {
      expect(post.url).toBe(`http://localhost:3000/api/transactions/${originalId}/reverse`);
      expect(post.headers.get("Idempotency-Key")).toBe(idempotencyKey);
      expect(post.headers.get("X-MyWealth-Request")).toBe("1");
      expect(post.headers.get("Authorization")).toBeNull();
      expect(post.credentials).toBe("include");
      const text = await post.clone().text();
      expect(text === "" || text === "{}").toBe(true);
      expect(text).not.toContain("tenantId");
    }
  });

  it("stays on the original when it has already been reversed", async () => {
    const store = configureStore({
      reducer: {
        session: sessionSlice.reducer,
        [api.reducerPath]: api.reducer,
      },
      middleware: (getDefault) => getDefault().concat(api.middleware),
    });
    store.dispatch(setCurrentUser(user()));

    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
      const method = init?.method ?? (input instanceof Request ? input.method : "GET");
      if (url.endsWith(`/transactions/${originalId}/reverse`) && method === "POST") {
        return json({
          type: "https://tools.ietf.org/html/rfc9110#section-15.5.1",
          title: "One or more validation errors occurred.",
          status: 400,
          errors: { "": ["This transaction has already been reversed."] },
        }, 400);
      }
      if (url.endsWith(`/transactions/${originalId}`) && method === "GET") {
        return json(original);
      }
      return new Response("", { status: 404 });
    });

    const router = createMemoryRouter(
      [{ path: "/transactions/:id", element: <TransactionDetailPage /> }],
      { initialEntries: [`/transactions/${originalId}`] },
    );

    render(
      <Provider store={store}>
        <RouterProvider router={router} />
      </Provider>,
    );

    expect(await screen.findByText(originalId)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reverse" }));

    expect(await screen.findByText("This transaction has already been reversed.")).toBeInTheDocument();
    expect(screen.getByText(originalId)).toBeInTheDocument();
    expect(screen.getByText("Buy")).toBeInTheDocument();
    expect(screen.queryByText(reverseId)).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe(`/transactions/${originalId}`);
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("shows a closed account when reverse is rejected", async () => {
    const store = configureStore({
      reducer: {
        session: sessionSlice.reducer,
        [api.reducerPath]: api.reducer,
      },
      middleware: (getDefault) => getDefault().concat(api.middleware),
    });
    store.dispatch(setCurrentUser(user()));
    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const method = init?.method ?? (input instanceof Request ? input.method : "GET");
      if (method === "POST") {
        return json({
          status: 400,
          errors: { "": ["Cannot reverse a transaction while the account is closed. Reopen the account first."] },
        }, 400);
      }
      if (method === "GET") {
        return json(original);
      }
      return new Response("", { status: 404 });
    });
    render(
      <Provider store={store}>
        <RouterProvider router={createMemoryRouter(
          [{ path: "/transactions/:id", element: <TransactionDetailPage /> }],
          { initialEntries: [`/transactions/${originalId}`] },
        )} />
      </Provider>,
    );
    expect(await screen.findByRole("button", { name: "Reverse" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reverse" }));
    expect(
      await screen.findByText("Cannot reverse a transaction while the account is closed. Reopen the account first."),
    ).toBeInTheDocument();
    expect(screen.getByText(originalId)).toBeInTheDocument();
  });

  it("shows a disabled customer when reverse is rejected", async () => {
    const store = configureStore({
      reducer: {
        session: sessionSlice.reducer,
        [api.reducerPath]: api.reducer,
      },
      middleware: (getDefault) => getDefault().concat(api.middleware),
    });
    store.dispatch(setCurrentUser(user()));
    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const method = init?.method ?? (input instanceof Request ? input.method : "GET");
      if (method === "POST") {
        return json({
          status: 400,
          title: "User is disabled",
          detail: "Cannot reverse a transaction while the customer is disabled. Enable the customer first.",
          code: "disabled",
          target: "user",
          targetId: original.customerId,
        }, 400);
      }
      if (method === "GET") {
        return json(original);
      }
      return new Response("", { status: 404 });
    });
    render(
      <Provider store={store}>
        <RouterProvider router={createMemoryRouter(
          [{ path: "/transactions/:id", element: <TransactionDetailPage /> }],
          { initialEntries: [`/transactions/${originalId}`] },
        )} />
      </Provider>,
    );
    expect(await screen.findByRole("button", { name: "Reverse" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reverse" }));
    expect(
      await screen.findByText("Cannot reverse a transaction while the customer is disabled. Enable the customer first."),
    ).toBeInTheDocument();
    expect(screen.getByText(originalId)).toBeInTheDocument();
    expect(screen.queryByText(reverseId)).not.toBeInTheDocument();
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });
});
