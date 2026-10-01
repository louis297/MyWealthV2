import { beforeEach, describe, expect, it } from "vitest";
import { clearSession, sessionSlice, setCurrentUser } from "@/features/session/sessionSlice";

describe("session slice", () => {
  beforeEach(() => {
    sessionStorage.clear();
  });

  it("does not store access or refresh tokens", () => {
    const state = sessionSlice.reducer(
      sessionSlice.getInitialState(),
      setCurrentUser({
        id: "1",
        name: "Pat",
        email: "pat@firm.example",
        role: "tenantAdmin",
        status: "active",
        tenantId: null,
        tenantCode: "firm",
        adviserId: null,
        rowVersion: "AAAA",
      }),
    );

    expect(state.currentUser?.name).toBe("Pat");
    expect(sessionStorage.getItem("adviser-portal.accessToken")).toBeNull();
    expect(sessionStorage.getItem("adviser-portal.refreshToken")).toBeNull();
    expect("accessToken" in state).toBe(false);
    expect("refreshToken" in state).toBe(false);

    const cleared = sessionSlice.reducer(state, clearSession());
    expect(cleared.currentUser).toBeNull();
  });
});
