import { describe, expect, it } from "vitest";
import { scheduleStates, todayUtc } from "@/features/fees/scheduleState";
import { schedule } from "@/test/fees";

const today = "2026-11-20";

describe("scheduleStates (adviser-portal-fees State)", () => {
  it("labels the latest row on or before today as applying, older rows as earlier", () => {
    const states = scheduleStates(
      [
        schedule({ id: "old", effectiveFrom: "2026-01-01", inEffect: true }),
        schedule({ id: "current", effectiveFrom: "2026-11-01", inEffect: true }),
        schedule({ id: "older", effectiveFrom: "2025-06-01", inEffect: true }),
      ],
      today,
    );

    expect(states.get("current")).toBe("Applies today");
    expect(states.get("old")).toBe("Earlier");
    expect(states.get("older")).toBe("Earlier");
  });

  it("counts a row starting today as applying today", () => {
    const states = scheduleStates(
      [
        schedule({ id: "yesterday", effectiveFrom: "2026-11-19", inEffect: true }),
        schedule({ id: "today", effectiveFrom: today, inEffect: true }),
      ],
      today,
    );

    expect(states.get("today")).toBe("Applies today");
    expect(states.get("yesterday")).toBe("Earlier");
  });

  it("labels a future row as scheduled, or scheduled and locked when inEffect", () => {
    const states = scheduleStates(
      [
        schedule({ id: "pending", effectiveFrom: "2026-11-21", inEffect: false }),
        schedule({ id: "locked", effectiveFrom: "2026-12-01", inEffect: true }),
      ],
      today,
    );

    expect(states.get("pending")).toBe("Scheduled");
    expect(states.get("locked")).toBe("Scheduled, locked");
  });

  it("works out each quote currency on its own", () => {
    const states = scheduleStates(
      [
        schedule({ id: "nzd-old", quoteCurrency: "NZD", effectiveFrom: "2026-01-01", inEffect: true }),
        schedule({ id: "nzd-new", quoteCurrency: "NZD", effectiveFrom: "2026-10-01", inEffect: true }),
        schedule({ id: "usd", quoteCurrency: "USD", effectiveFrom: "2025-01-01", inEffect: true }),
      ],
      today,
    );

    expect(states.get("nzd-new")).toBe("Applies today");
    expect(states.get("nzd-old")).toBe("Earlier");
    expect(states.get("usd")).toBe("Applies today");
  });
});

describe("todayUtc", () => {
  it("returns the UTC date, not the browser's local date", () => {
    // 20:00 UTC on 20 Nov is 09:00 on 21 Nov in Auckland (TZ pinned in vite.config.ts).
    expect(todayUtc(new Date("2026-11-20T20:00:00Z"))).toBe("2026-11-20");
  });
});
