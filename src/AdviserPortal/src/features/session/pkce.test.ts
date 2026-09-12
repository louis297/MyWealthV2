import { describe, expect, it } from "vitest";
import { challengeS256 } from "@/features/session/pkce";

describe("PKCE S256", () => {
  it("matches the RFC 7636 code challenge", async () => {
    const verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
    await expect(challengeS256(verifier)).resolves.toBe(
      "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
    );
  });
});
