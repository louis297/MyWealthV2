import { configureStore } from "@reduxjs/toolkit";
import { fireEvent, render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { EditAdviserPage } from "@/features/advisers/EditAdviserPage";
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
const adviser = {
  id: adviserId,
  tenantId: "22222222-2222-2222-2222-222222222222",
  name: "Sam Reed",
  email: "sam@north.example",
  status: "active",
  rowVersion: "AAAA",
  created: "2026-09-14T00:00:00+00:00",
};

function user(): CurrentUser {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    name: "Pat Admin",
    email: "pat@north.example",
    role: "tenantAdmin",
    status: "active",
    tenantId: adviser.tenantId,
    tenantCode: "north-advisory",
    adviserId: null,
    rowVersion: "AAAA",
  };
}

describe("EditAdviserPage", () => {
  beforeEach(() => {
    startAuthorize.mockReset();
    sessionStorage.clear();
    vi.stubEnv("VITE_WEBAPI_BASE_URL", "https://webapi.test");
    vi.stubGlobal("fetch", vi.fn());
  });

  it("renames an adviser and does not send email or tenantId", async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockImplementation(async (input, init) => {
      const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
      const method = init?.method ?? (input instanceof Request ? input.method : "GET");
      if (method === "GET") {
        return new Response(JSON.stringify(adviser), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        });
      }
      return new Response(null, { status: 204 });
    });

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
        { path: "/advisers/:id", element: <p>adviser detail</p> },
        { path: "/advisers/:id/edit", element: <EditAdviserPage /> },
      ],
      { initialEntries: [`/advisers/${adviserId}/edit`] },
    );

    render(
      <Provider store={store}>
        <RouterProvider router={router} />
      </Provider>,
    );

    fireEvent.change(await screen.findByLabelText("Name"), { target: { value: "Samantha Reed" } });
    expect(screen.queryByLabelText("Email")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByText("adviser detail")).toBeInTheDocument();
    const put = fetchMock.mock.calls.find(([input, init]) => {
      const method = init?.method ?? (input instanceof Request ? input.method : "GET");
      return method === "PUT";
    })?.[0] as Request;
    expect(JSON.parse(await put.clone().text())).toEqual({
      name: "Samantha Reed",
      rowVersion: "AAAA",
    });
  });
});
