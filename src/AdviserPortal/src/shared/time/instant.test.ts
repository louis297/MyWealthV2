import { describe, expect, it } from "vitest";
import { formatInstant, toUtcIso } from "./instant";

describe("time zone pin", () => {
  it("runs the tests in Pacific/Auckland", () => {
    expect(new Date("2026-11-14T03:13:00Z").getTimezoneOffset()).toBe(-780);
  });
});

describe("formatInstant", () => {
  it("shows a UTC instant in local time", () => {
    expect(formatInstant("2026-10-10T21:00:00Z")).toBe("2026-10-11 10:00");
    expect(formatInstant("2026-10-16T09:00:00.1234567Z")).toBe("2026-10-16 22:00");
    expect(formatInstant("2026-07-01T00:05:00Z")).toBe("2026-07-01 12:05");
  });

  it("shows an instant with any offset in local time", () => {
    expect(formatInstant("2026-10-11T10:00:00+13:00")).toBe("2026-10-11 10:00");
  });

  it("returns a value it cannot read unchanged", () => {
    expect(formatInstant("not a date")).toBe("not a date");
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
