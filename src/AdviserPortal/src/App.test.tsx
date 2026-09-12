import { render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { describe, expect, it } from "vitest";
import { store } from "@/app/store";
import { ShellLayout } from "@/layouts/ShellLayout";

describe("Adviser Portal shell", () => {
  it("renders without a password field", () => {
    const router = createMemoryRouter(
      [
        {
          path: "/",
          element: (
            <ShellLayout>
              <p>Adviser Portal</p>
            </ShellLayout>
          ),
        },
      ],
      { initialEntries: ["/"] },
    );

    render(
      <Provider store={store}>
        <RouterProvider router={router} />
      </Provider>,
    );

    expect(screen.getAllByText("Adviser Portal").length).toBeGreaterThan(0);
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });
});
