import { describe, expect, it } from "vitest";
import { humaniseCron, toCron } from "@/features/reports/cron";

describe("cron presets", () => {
  it("maps daily, weekly, and monthly times to five-field expressions", () => {
    expect(toCron("daily", "09:00")).toBe("0 9 * * *");
    expect(toCron("weekly", "09:00")).toBe("0 9 * * 1");
    expect(toCron("monthly", "09:30")).toBe("30 9 1 * *");
  });

  it("humanises the three presets and leaves other expressions unchanged", () => {
    expect(humaniseCron("0 9 * * *")).toBe("every day at 09:00");
    expect(humaniseCron("0 9 * * 1")).toBe("every Monday at 09:00");
    expect(humaniseCron("30 9 1 * *")).toBe("1st of the month at 09:30");
    expect(humaniseCron("15 8 * * 5")).toBe("15 8 * * 5");
  });
});
