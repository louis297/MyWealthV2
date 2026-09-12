import { beforeEach, describe, expect, it } from "vitest";
import {
  clearSession,
  hydrateSession,
  sessionSlice,
  setTokens,
} from "@/features/session/sessionSlice";

const storageKey = {
  access: "adviser-portal.accessToken",
  refresh: "adviser-portal.refreshToken",
};

describe("session slice", () => {
  beforeEach(() => {
    sessionStorage.clear();
  });

  it("persists tokens to sessionStorage and hydrates them", () => {
    const withTokens = sessionSlice.reducer(
      sessionSlice.getInitialState(),
      setTokens({ accessToken: "access-1", refreshToken: "refresh-1" }),
    );

    expect(withTokens.accessToken).toBe("access-1");
    expect(withTokens.refreshToken).toBe("refresh-1");
    expect(sessionStorage.getItem(storageKey.access)).toBe("access-1");
    expect(sessionStorage.getItem(storageKey.refresh)).toBe("refresh-1");

    const hydrated = sessionSlice.reducer(sessionSlice.getInitialState(), hydrateSession());
    expect(hydrated.accessToken).toBe("access-1");
    expect(hydrated.refreshToken).toBe("refresh-1");
  });

  it("clears tokens from redux and sessionStorage", () => {
    sessionSlice.reducer(
      sessionSlice.getInitialState(),
      setTokens({ accessToken: "access-1", refreshToken: "refresh-1" }),
    );

    const cleared = sessionSlice.reducer(sessionSlice.getInitialState(), clearSession());
    expect(cleared.accessToken).toBeNull();
    expect(cleared.refreshToken).toBeNull();
    expect(sessionStorage.getItem(storageKey.access)).toBeNull();
    expect(sessionStorage.getItem(storageKey.refresh)).toBeNull();
  });
});
