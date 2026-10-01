import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { SIGN_OUT_IN_PROGRESS_KEY, startLogin } from "@/features/session/session";

const bffOrigin = "http://127.0.0.1:5290";

describe("startLogin host", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.unstubAllEnvs();
    vi.stubGlobal("location", {
      origin: "http://localhost:5173",
      pathname: "/customers",
      search: "?page=2",
      assign: vi.fn(),
    });
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    vi.unstubAllGlobals();
  });

  it("sends a Vite document to the BFF origin before /bff/login", () => {
    vi.stubEnv("VITE_BFF_HTTP", bffOrigin);

    startLogin();

    expect(window.location.assign).toHaveBeenCalledOnce();
    expect(window.location.assign).toHaveBeenCalledWith(`${bffOrigin}/customers?page=2`);
    expect(window.location.assign).not.toHaveBeenCalledWith(
      expect.stringContaining("/bff/login"),
    );
  });

  it("assigns /bff/login when the document is already the BFF", () => {
    vi.stubEnv("VITE_BFF_HTTP", `${bffOrigin}/`);
    vi.stubGlobal("location", {
      origin: bffOrigin,
      pathname: "/customers",
      search: "",
      assign: vi.fn(),
    });

    startLogin();

    expect(window.location.assign).toHaveBeenCalledWith("/bff/login?returnUrl=%2Fcustomers");
  });

  it("assigns /bff/login when the BFF origin is not configured", () => {
    vi.stubEnv("VITE_BFF_HTTP", "");

    startLogin("/profile");

    expect(window.location.assign).toHaveBeenCalledWith("/bff/login?returnUrl=%2Fprofile");
  });

  it("does not leave the Vite host while sign-out is in progress", () => {
    vi.stubEnv("VITE_BFF_HTTP", bffOrigin);
    sessionStorage.setItem(SIGN_OUT_IN_PROGRESS_KEY, "1");

    startLogin();

    expect(window.location.assign).not.toHaveBeenCalled();
  });
});
