import { describe, expect, it } from "vitest";
import { formatInstant } from "./instant";

describe("time zone pin", () => {
  it("runs the tests in Pacific/Auckland", () => {
    expect(new Date("2026-11-14T03:13:00Z").getTimezoneOffset()).toBe(-780);
  });
});

describe("formatInstant", () => {
  it("shows a UTC instant in local time", () => {
    expect(formatInstant("2026-11-09T20:00:00Z")).toBe("2026-11-10 09:00");
    expect(formatInstant("2026-07-01T00:05:00.5Z")).toBe("2026-07-01 12:05");
  });

  it("returns a value it cannot read unchanged", () => {
    expect(formatInstant("not a date")).toBe("not a date");
  });
});
