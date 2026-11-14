import { describe, expect, it } from "vitest";
import { toUtcIso } from "./instant";

describe("time zone pin", () => {
  it("runs the tests in Pacific/Auckland", () => {
    expect(new Date("2026-11-14T03:13:00Z").getTimezoneOffset()).toBe(-780);
  });
});

describe("toUtcIso", () => {
  it("converts a datetime-local value from local time to a UTC ISO string", () => {
    expect(toUtcIso("2026-11-14T16:13")).toBe("2026-11-14T03:13:00.000Z");
    expect(toUtcIso("2026-07-01T09:00:30")).toBe("2026-06-30T21:00:30.000Z");
  });

  it("rejects a value that is not a datetime-local value", () => {
    expect(() => toUtcIso("2026-11-14")).toThrow();
    expect(() => toUtcIso("2026-11-14T03:13:00Z")).toThrow();
    expect(() => toUtcIso("")).toThrow();
  });
});
