import { configureStore } from "@reduxjs/toolkit";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { sessionSlice, setCurrentUser, type CurrentUser } from "@/features/session/sessionSlice";
import { CreateInternalTransferPage } from "@/features/transactions/CreateInternalTransferPage";
import { api } from "@/shared/api/api";

const tenantId = "22222222-2222-2222-2222-222222222222";
const nedId = "c1111111-1111-1111-1111-111111111111";
const noraId = "c2222222-2222-2222-2222-222222222222";
const sourceId = "a0000000-0000-0000-0000-000000000001";
const savingsId = "a0000000-0000-0000-0000-000000000002";
const closedId = "a0000000-0000-0000-0000-000000000003";
const audId = "a0000000-0000-0000-0000-000000000004";
const noraBankId = "a0000000-0000-0000-0000-000000000005";
const createdId = "dddddddd-dddd-dddd-dddd-dddddddddddd";
const idempotencyKey = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

function account(id: string, customerId: string, name: string, overrides: Record<string, unknown> = {}) {
  return {
    id,
    tenantId,
    customerId,
    name,
    type: "Bank",
    currency: "NZD",
    status: "Open",
    isActive: true,
    cashBalance: 1000,
    rowVersion: "AAAA",
    ...overrides,
  };
}

const nedAccounts = [
  account(sourceId, nedId, "Ned bank"),
  account(savingsId, nedId, "Ned savings"),
  account(closedId, nedId, "Ned old", { status: "Closed", isActive: false }),
  account(audId, nedId, "Ned AUD", { currency: "AUD" }),
];
const noraAccounts = [account(noraBankId, noraId, "Nora bank")];

function customer(id: string, name: string) {
  return {
    id,
    tenantId,
    adviserId: "ffffffff-ffff-ffff-ffff-ffffffffffff",
    name,
    email: `${name.split(" ")[0].toLowerCase()}@north.example`,
    status: "Active",
    rowVersion: "AAAA",
    created: "2026-01-01T00:00:00Z",
  };
}

const ned = customer(nedId, "Ned North");
const nora = customer(noraId, "Nora North");

function me(): CurrentUser {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    name: "Ann Adviser",
    email: "ann@north.example",
    role: "adviser",
    status: "active",
    tenantId,
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

function requestUrl(input: RequestInfo | URL) {
  if (typeof input === "string") {
    return input;
  }
  if (input instanceof Request) {
    return input.url;
  }
  return String(input);
}

function renderForm() {
  const store = configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });
  store.dispatch(setCurrentUser(me()));

  const fetchMock = vi.mocked(fetch);
  fetchMock.mockImplementation(async (input, init) => {
    const url = requestUrl(input);
    const method = init?.method ?? (input instanceof Request ? input.method : "GET");
    if (method === "POST" && url.endsWith("/transactions/transfer")) {
      return json({ id: createdId }, 201);
    }
    if (method !== "GET") {
      return new Response("", { status: 404 });
    }
    if (url.endsWith(`/accounts/${sourceId}`)) {
      return json(nedAccounts[0]);
    }
    if (url.endsWith(`/users/customers/${nedId}`)) {
      return json(ned);
    }
    if (url.includes("/users/customers?")) {
      const search = new URL(url).searchParams.get("search") ?? "";
      const items = [ned, nora].filter((item) => item.name.toLowerCase().includes(search.toLowerCase()));
      return json({ items, page: 1, pageSize: 20, totalCount: items.length });
    }
    if (url.includes("/accounts?")) {
      const customerId = new URL(url).searchParams.get("customerId");
      const items = customerId === nedId ? nedAccounts : customerId === noraId ? noraAccounts : [];
      return json({ items, page: 1, pageSize: 100, totalCount: items.length });
    }
    return new Response("", { status: 404 });
  });

  const router = createMemoryRouter(
    [
      { path: "/accounts/:id/transfer", element: <CreateInternalTransferPage /> },
      { path: "/accounts/:id", element: <p>account detail</p> },
    ],
    { initialEntries: [`/accounts/${sourceId}/transfer`] },
  );

  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );

  return { fetchMock, router };
}

function urls(fetchMock: ReturnType<typeof vi.mocked<typeof fetch>>) {
  return fetchMock.mock.calls.map(([input]) => requestUrl(input));
}

async function fillAndReview(amount = "500") {
  fireEvent.change(screen.getByLabelText("Amount"), { target: { value: amount } });
  fireEvent.change(screen.getByLabelText("Booked at"), { target: { value: "2026-11-10T09:00" } });
  fireEvent.click(screen.getByRole("button", { name: "Review transfer" }));
}

describe("CreateInternalTransferPage", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
    vi.spyOn(crypto, "randomUUID").mockReturnValue(idempotencyKey);
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: `/accounts/${sourceId}/transfer`,
      search: "",
      href: `http://localhost:3000/accounts/${sourceId}/transfer`,
      assign: vi.fn(),
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("defaults the destination Customer to the source Account's Customer", async () => {
    const { fetchMock } = renderForm();

    expect(await screen.findByRole("heading", { name: "Transfer" })).toBeInTheDocument();
    expect(await screen.findByText("Destination customer: Ned North")).toBeInTheDocument();
    expect(await screen.findByRole("radio", { name: /Ned savings/ })).toBeEnabled();
    expect(urls(fetchMock).some((url) => url.includes(`/accounts?`) && url.includes(`customerId=${nedId}`) && url.includes("pageSize=100"))).toBe(true);
  });

  it("greys out Closed, different-currency and source Accounts with a reason", async () => {
    renderForm();

    const list = await screen.findByRole("radiogroup", { name: "Destination account" });
    const closed = await within(list).findByRole("radio", { name: /Ned old/ });
    expect(closed).toBeDisabled();
    expect(within(list).getByText(/Ned old/).closest("label")).toHaveTextContent("Closed");
    const aud = within(list).getByRole("radio", { name: /Ned AUD/ });
    expect(aud).toBeDisabled();
    expect(aud.closest("label")).toHaveTextContent("Different currency");
    const source = within(list).getByRole("radio", { name: /Ned bank/ });
    expect(source).toBeDisabled();
    expect(source.closest("label")).toHaveTextContent("Source account");
    expect(within(list).getByRole("radio", { name: /Ned savings/ })).toBeEnabled();
  });

  it("debounces the Customer search", async () => {
    const { fetchMock } = renderForm();
    await screen.findByText("Destination customer: Ned North");
    vi.useFakeTimers();

    fireEvent.change(screen.getByLabelText("Search customers"), { target: { value: "Nor" } });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(100);
    });
    fireEvent.change(screen.getByLabelText("Search customers"), { target: { value: "Nora" } });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(299);
    });
    expect(urls(fetchMock).some((url) => url.includes("search=Nor"))).toBe(false);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(1);
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(100);
    });
    expect(screen.getByRole("button", { name: "Nora North" })).toBeInTheDocument();
    const searches = urls(fetchMock).filter((url) => url.includes("/users/customers?"));
    expect(searches).toHaveLength(1);
    expect(searches[0]).toContain("search=Nora");
    expect(searches[0]).toContain("enabledOnly=true");
  });

  it("posts a transfer to another Customer after a confirm summary with a warning", async () => {
    const { fetchMock } = renderForm();
    await screen.findByText("Destination customer: Ned North");

    fireEvent.change(screen.getByLabelText("Search customers"), { target: { value: "Nora" } });
    fireEvent.click(await screen.findByRole("button", { name: "Nora North" }, { timeout: 2000 }));
    expect(await screen.findByText("Destination customer: Nora North")).toBeInTheDocument();
    fireEvent.click(await screen.findByRole("radio", { name: /Nora bank/ }));
    await fillAndReview();

    expect(
      await screen.findByText("Transfer 500.00 NZD from Ned bank (Ned North) to Nora bank (Nora North)"),
    ).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent("different customers");
    fireEvent.click(screen.getByRole("button", { name: "Confirm transfer" }));

    expect(await screen.findByText("account detail")).toBeInTheDocument();
    const post = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.method === "POST");
    expect(post).toBeDefined();
    expect(post!.url).toBe("http://localhost:3000/api/transactions/transfer");
    expect(post!.headers.get("Idempotency-Key")).toBe(idempotencyKey);
    expect(JSON.parse(await post!.clone().text())).toEqual({
      fromAccountId: sourceId,
      toAccountId: noraBankId,
      amount: 500,
      bookedAt: "2026-11-10T09:00",
      memo: null,
      reference: null,
    });
  });

  it("shows no warning when both Accounts belong to the same Customer", async () => {
    renderForm();
    fireEvent.click(await screen.findByRole("radio", { name: /Ned savings/ }));
    await fillAndReview("25");

    expect(
      await screen.findByText("Transfer 25.00 NZD from Ned bank (Ned North) to Ned savings (Ned North)"),
    ).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it.each(["0", "-5", "abc"])("rejects a non-positive amount %s without posting", async (amount) => {
    const { fetchMock } = renderForm();
    fireEvent.click(await screen.findByRole("radio", { name: /Ned savings/ }));
    await fillAndReview(amount);

    expect(await screen.findByText("Amount must be greater than 0.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Confirm transfer" })).not.toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([input]) => input instanceof Request && input.method === "POST")).toBe(false);
  });
});
