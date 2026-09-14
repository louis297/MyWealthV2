import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ForbiddenPage } from "@/features/session/ForbiddenPage";

const startEndSession = vi.fn();

vi.mock("@/features/session/oidc", () => ({
  startEndSession: () => startEndSession(),
}));

describe("ForbiddenPage", () => {
  beforeEach(() => {
    startEndSession.mockReset();
  });

  it("explains the page is forbidden and signs out", () => {
    render(<ForbiddenPage />);

    expect(screen.getByRole("heading", { name: "Forbidden" })).toBeInTheDocument();
    expect(screen.getByText("You do not have access to this page.")).toBeInTheDocument();
    expect(document.querySelector('input[type="password"]')).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: "Sign out" }));

    expect(startEndSession).toHaveBeenCalledOnce();
  });
});
