import { configureStore } from "@reduxjs/toolkit";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ReportsPage } from "@/features/reports/ReportsPage";
import { sessionSlice, setCurrentUser, type CurrentUser } from "@/features/session/sessionSlice";
import { api } from "@/shared/api/api";

const customerTransactions = {
  id: "8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11",
  code: "customer-transactions",
  name: "Customer Transactions",
  function: "Lists cash and security activity for one customer.",
  scope: "Customer",
  allowedTriggers: ["manual", "scheduled"],
};

const adviserBalances = {
  id: "5a9d3c70-2e84-4b1f-8c36-7f1e0a9d5b22",
  code: "adviser-balances",
  name: "Adviser Balances",
  function: "Cash and holdings for an adviser's book.",
  scope: "Book",
  allowedTriggers: ["manual", "scheduled"],
};

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

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

function renderReports(
  me: CurrentUser,
  fetchImpl: (url: string, method: string) => Response | Promise<Response>,
) {
  const store = configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });
  store.dispatch(setCurrentUser(me));

  const fetchMock = vi.mocked(fetch);
  fetchMock.mockImplementation(async (input, init) => {
    const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
    const method = init?.method ?? (input instanceof Request ? input.method : "GET");
    return fetchImpl(url, method);
  });

  const router = createMemoryRouter([{ path: "/reports", element: <ReportsPage /> }], {
    initialEntries: ["/reports"],
  });

  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );

  return { fetchMock };
}

describe("ReportsPage", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
  });

  it("lists book-scoped reports and does not request customer scope", async () => {
    const { fetchMock } = renderReports(user(), (url, method) => {
      if (url.includes("/api/reports") && method === "GET") {
        const scope = new URL(url).searchParams.get("scope");
        return json(scope === "Book" ? [adviserBalances] : [customerTransactions]);
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByRole("heading", { name: "Reports" })).toBeInTheDocument();
    expect(screen.getByText("These run for an adviser's book.")).toBeInTheDocument();
    const report = await screen.findByLabelText("Report");
    expect(within(report).getByRole("option", { name: "Adviser Balances" })).toBeInTheDocument();
    expect(within(report).queryByRole("option", { name: "Customer Transactions" })).not.toBeInTheDocument();
    expect(screen.queryByText(adviserBalances.function)).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Include closed accounts")).not.toBeInTheDocument();
    fireEvent.change(report, { target: { value: adviserBalances.id } });
    expect(screen.getByText(adviserBalances.function)).toBeInTheDocument();
    expect(screen.getByLabelText("Include closed accounts")).toBeInTheDocument();
    expect(screen.queryByLabelText("From")).not.toBeInTheDocument();

    const request = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.url.includes("/api/reports"));
    expect(request).toBeDefined();
    const url = new URL(request!.url);
    expect(url.pathname).toBe("/api/reports");
    expect(url.searchParams.get("scope")).toBe("Book");
    expect(request!.headers.get("Authorization")).toBeNull();
    expect(request!.credentials).toBe("include");
    const scopes = fetchMock.mock.calls.map(([input]) => {
      const raw = input instanceof Request ? input.url : String(input);
      return new URL(raw, "http://localhost:3000").searchParams.get("scope");
    });
    expect(scopes).not.toContain("Customer");
  });

  it("shows an empty book report list", async () => {
    renderReports(user({ role: "adviser", name: "Sam Reed", adviserId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" }), (url) => {
      if (url.includes("/api/reports")) {
        return json([]);
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByText("No reports available.")).toBeInTheDocument();
    expect(screen.queryByText("No reports available for this customer.")).not.toBeInTheDocument();
  });

  it("shows a loading line while the book report list is in flight", async () => {
    let release: (response: Response) => void = () => {};
    const pending = new Promise<Response>((resolve) => {
      release = resolve;
    });
    renderReports(user(), (url) => {
      if (url.includes("/api/reports")) {
        return pending;
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByText("Loading reports…")).toBeInTheDocument();
    release(json([]));
    expect(await screen.findByText("No reports available.")).toBeInTheDocument();
  });

  it("retries a failed book report list", async () => {
    let reportGets = 0;
    renderReports(user(), (url) => {
      if (url.includes("/api/reports")) {
        reportGets += 1;
        if (reportGets === 1) {
          return new Response("", { status: 500 });
        }
        return json([adviserBalances]);
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByText("Reports could not be loaded.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(within(await screen.findByLabelText("Report")).getByRole("option", { name: "Adviser Balances" })).toBeInTheDocument();
    expect(reportGets).toBeGreaterThan(1);
  });

  it("disables generate when the selected report has no parameter form", async () => {
    const futureReport = {
      id: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
      code: "future-report",
      name: "Future Report",
      function: "Not in this version.",
      scope: "Book",
      allowedTriggers: ["manual"],
    };
    renderReports(user(), (url, method) => {
      if (url.includes("/api/reports") && method === "GET") {
        return json([futureReport]);
      }
      return new Response("", { status: 404 });
    });

    fireEvent.change(await screen.findByLabelText("Report"), { target: { value: futureReport.id } });
    expect(screen.getByText("parameters not available in this version")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Generate now" })).toBeDisabled();
  });

  it("queues adviser balances without an adviser id", async () => {
    const { fetchMock } = renderReports(
      user({
        role: "adviser",
        name: "Sam Reed",
        adviserId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      }),
      (url, method) => {
        if (url.includes("/api/reports") && method === "GET") {
          return json([adviserBalances]);
        }
        if (url.includes("/reports/request") && method === "POST") {
          return json({ id: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee" }, 202);
        }
        return new Response("", { status: 404 });
      },
    );

    const report = await screen.findByLabelText("Report");
    expect(screen.queryByLabelText("Adviser")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Include closed accounts")).not.toBeInTheDocument();
    fireEvent.change(report, { target: { value: adviserBalances.id } });
    expect(screen.queryByLabelText("Adviser")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Include closed accounts")).not.toBeChecked();
    fireEvent.click(screen.getByRole("button", { name: "Generate now" }));

    expect(await screen.findByText("Report queued")).toBeInTheDocument();
    const request = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.url.includes("/reports/request"));
    expect(request).toBeDefined();
    expect(request!.method).toBe("POST");
    expect(request!.headers.get("Authorization")).toBeNull();
    expect(request!.headers.get("X-MyWealth-Request")).toBe("1");
    expect(request!.credentials).toBe("include");
    const body = JSON.parse(await request!.clone().text()) as {
      reportId: string;
      parameters: Record<string, unknown>;
    };
    expect(body).toEqual({
      reportId: adviserBalances.id,
      parameters: { includeClosedAccounts: false },
    });
    expect(body).not.toHaveProperty("tenantId");
    expect(body.parameters).not.toHaveProperty("adviserId");
    expect(body.parameters).not.toHaveProperty("asOf");
    const urls = fetchMock.mock.calls.map(([input]) =>
      input instanceof Request ? input.url : String(input),
    );
    expect(urls.some((url) => url.includes("/users/advisers"))).toBe(false);
  });

  it("requires a tenant admin to pick an adviser before queueing balances", async () => {
    const samId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    const { fetchMock } = renderReports(user(), (url, method) => {
      if (url.includes("/api/reports") && method === "GET") {
        return json([adviserBalances]);
      }
      if (url.includes("/users/advisers") && method === "GET") {
        return json({
          items: [
            {
              id: samId,
              tenantId: "22222222-2222-2222-2222-222222222222",
              name: "Sam Reed",
              email: "sam@north.example",
              status: "active",
              rowVersion: "AAAA",
              created: "2026-09-14T00:00:00+00:00",
            },
          ],
          page: 1,
          pageSize: 100,
          totalCount: 1,
        });
      }
      if (url.includes("/reports/request") && method === "POST") {
        return json({ id: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee" }, 202);
      }
      return new Response("", { status: 404 });
    });

    fireEvent.change(await screen.findByLabelText("Report"), { target: { value: adviserBalances.id } });
    const adviser = screen.getByLabelText("Adviser");
    expect(adviser).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Generate now" })).toBeDisabled();
    expect(await screen.findByRole("option", { name: "Sam Reed" })).toBeInTheDocument();

    fireEvent.change(adviser, { target: { value: samId } });
    fireEvent.click(screen.getByRole("button", { name: "Generate now" }));

    expect(await screen.findByText("Report queued")).toBeInTheDocument();
    const request = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.url.includes("/reports/request"));
    const body = JSON.parse(await request!.clone().text()) as {
      parameters: Record<string, unknown>;
    };
    expect(body).toEqual({
      reportId: adviserBalances.id,
      parameters: {
        adviserId: samId,
        includeClosedAccounts: false,
      },
    });
    expect(body.parameters).not.toHaveProperty("asOf");
    expect(body).not.toHaveProperty("tenantId");

    const advisersCall = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.url.includes("/users/advisers"));
    expect(advisersCall).toBeDefined();
    const advisersUrl = new URL(advisersCall!.url);
    expect(advisersUrl.pathname).toBe("/api/users/advisers");
    expect(advisersUrl.searchParams.get("enabledOnly")).toBe("true");
    expect(advisersUrl.searchParams.get("pageSize")).toBe("100");
    expect(advisersUrl.searchParams.has("tenantId")).toBe(false);
  });

  it("creates an adviser balances schedule without an adviser id", async () => {
    const { fetchMock } = renderReports(
      user({
        role: "adviser",
        name: "Sam Reed",
        adviserId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      }),
      (url, method) => {
        if (url.includes("/api/reports") && method === "GET" && !url.includes("/schedules")) {
          return json([adviserBalances]);
        }
        if (url.includes("/reports/schedules") && method === "GET") {
          return json([]);
        }
        if (url.includes("/reports/schedules") && method === "POST") {
          return json({ id: "ssssssss-ssss-ssss-ssss-ssssssssssss" }, 201);
        }
        return new Response("", { status: 404 });
      },
    );

    fireEvent.click(await screen.findByRole("button", { name: "Schedules" }));
    expect(screen.queryByLabelText("Adviser")).not.toBeInTheDocument();
    expect(document.querySelector('input[name="cron"]')).toBeNull();
    fireEvent.change(screen.getByLabelText("Time"), { target: { value: "09:00" } });
    fireEvent.click(screen.getByRole("button", { name: "Create schedule" }));

    expect(await screen.findByText("Schedule saved")).toBeInTheDocument();
    const request = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.method === "POST");
    const body = JSON.parse(await request!.clone().text()) as { parameters: Record<string, unknown> };
    expect(body).toEqual({
      reportId: adviserBalances.id,
      parameters: { includeClosedAccounts: false },
      cron: "0 9 * * *",
      isActive: true,
    });
    expect(body.parameters).not.toHaveProperty("adviserId");
    expect(body.parameters).not.toHaveProperty("asOf");
    expect(body).not.toHaveProperty("tenantId");
  });

  it("requires a tenant admin to pick an adviser before creating a balances schedule", async () => {
    const samId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    const { fetchMock } = renderReports(user(), (url, method) => {
      if (url.includes("/api/reports") && method === "GET" && !url.includes("/schedules")) {
        return json([adviserBalances]);
      }
      if (url.includes("/users/advisers") && method === "GET") {
        return json({
          items: [
            {
              id: samId,
              tenantId: "22222222-2222-2222-2222-222222222222",
              name: "Sam Reed",
              email: "sam@north.example",
              status: "active",
              rowVersion: "AAAA",
              created: "2026-09-14T00:00:00+00:00",
            },
          ],
          page: 1,
          pageSize: 100,
          totalCount: 1,
        });
      }
      if (url.includes("/reports/schedules") && method === "GET") {
        return json([]);
      }
      if (url.includes("/reports/schedules") && method === "POST") {
        return json({ id: "ssssssss-ssss-ssss-ssss-ssssssssssss" }, 201);
      }
      return new Response("", { status: 404 });
    });

    fireEvent.click(await screen.findByRole("button", { name: "Schedules" }));
    expect(screen.getByRole("button", { name: "Create schedule" })).toBeDisabled();
    expect(await screen.findByRole("option", { name: "Sam Reed" })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Adviser"), { target: { value: samId } });
    fireEvent.change(screen.getByLabelText("Time"), { target: { value: "09:00" } });
    fireEvent.click(screen.getByRole("button", { name: "Create schedule" }));

    expect(await screen.findByText("Schedule saved")).toBeInTheDocument();
    const request = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.method === "POST" && input.url.includes("/schedules"));
    expect(JSON.parse(await request!.clone().text())).toEqual({
      reportId: adviserBalances.id,
      parameters: { adviserId: samId, includeClosedAccounts: false },
      cron: "0 9 * * *",
      isActive: true,
    });
  });

  it("loads book execution history", async () => {
    const { fetchMock } = renderReports(user({ role: "adviser", name: "Sam Reed" }), (url, method) => {
      if (url.includes("/api/reports") && method === "GET" && !url.includes("/executions") && !url.includes("/schedules")) {
        return json([adviserBalances]);
      }
      if (url.includes("/reports/executions") && method === "GET") {
        return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
      }
      return new Response("", { status: 404 });
    });

    expect(screen.queryByRole("button", { name: "History" })).not.toBeInTheDocument();
    expect(await screen.findByRole("heading", { name: "History" })).toBeInTheDocument();
    expect(await screen.findByText("No runs yet.")).toBeInTheDocument();
    const request = fetchMock.mock.calls
      .map(([input]) => input)
      .find((input): input is Request => input instanceof Request && input.url.includes("/reports/executions"));
    expect(request).toBeDefined();
    const url = new URL(request!.url);
    expect(url.pathname).toBe("/api/reports/executions");
    expect(url.searchParams.get("scope")).toBe("Book");
    expect(url.searchParams.get("page")).toBe("1");
    expect(url.searchParams.get("pageSize")).toBe("20");
    expect(url.searchParams.has("reportId")).toBe(false);
    expect(request!.headers.get("Authorization")).toBeNull();
    expect(request!.credentials).toBe("include");
  });

  it("lists every book run and adds reportId when history is filtered", async () => {
    const futureBook = {
      id: "99999999-9999-9999-9999-999999999999",
      code: "future-book",
      name: "Future Book",
      function: "Later.",
      scope: "Book",
      allowedTriggers: ["manual"],
    };
    const balancesRun = {
      id: "e1e1e1e1-e1e1-e1e1-e1e1-e1e1e1e1e1e1",
      reportId: adviserBalances.id,
      status: "Succeeded",
      triggerKind: "Manual",
      created: "2026-10-16T08:30:00+00:00",
      parameters: { includeClosedAccounts: false },
    };
    const futureRun = {
      id: "e2e2e2e2-e2e2-e2e2-e2e2-e2e2e2e2e2e2",
      reportId: futureBook.id,
      status: "Failed",
      triggerKind: "Scheduled",
      created: "2026-10-15T08:30:00+00:00",
      parameters: {},
    };
    const { fetchMock } = renderReports(user(), (url, method) => {
      if (url.includes("/reports/executions") && method === "GET") {
        const parsed = new URL(url);
        const reportId = parsed.searchParams.get("reportId");
        const items = reportId === adviserBalances.id ? [balancesRun] : [balancesRun, futureRun];
        return json({ items, page: 1, pageSize: 20, totalCount: reportId ? 1 : 21 });
      }
      if (url.includes("/api/reports") && method === "GET") {
        return json([adviserBalances, futureBook]);
      }
      return new Response("", { status: 404 });
    });

    expect(await screen.findByText("Failed")).toBeInTheDocument();
    expect(screen.getAllByText("Future Book").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Adviser Balances").length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: "Next" })).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: "Download" })).toHaveLength(1);

    fireEvent.change(screen.getByLabelText("History report"), { target: { value: adviserBalances.id } });
    await waitFor(() => {
      expect(screen.queryByText("Failed")).not.toBeInTheDocument();
    });
    const filtered = fetchMock.mock.calls
      .map(([input]) => input)
      .filter((input): input is Request => input instanceof Request && input.url.includes("/reports/executions"))
      .map((input) => new URL(input.url));
    expect(filtered.some((url) => url.searchParams.get("reportId") === adviserBalances.id && url.searchParams.get("page") === "1")).toBe(true);
  });
});
