import { render, screen } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CallbackPage } from "@/features/session/CallbackPage";

const completeCallback = vi.fn();

vi.mock("@/features/session/oidc", () => ({
  completeCallback: () => completeCallback(),
}));

describe("CallbackPage", () => {
  beforeEach(() => {
    completeCallback.mockReset();
  });

  it("navigates to the in-app path returned by the callback", async () => {
    completeCallback.mockResolvedValue("/customers/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    const router = createMemoryRouter(
      [
        { path: "/callback", element: <CallbackPage /> },
        { path: "/", element: <p>home</p> },
        { path: "/customers/:id", element: <p>customer detail</p> },
      ],
      { initialEntries: ["/callback"] },
    );

    render(<RouterProvider router={router} />);

    expect(await screen.findByText("customer detail")).toBeInTheDocument();
  });
});
