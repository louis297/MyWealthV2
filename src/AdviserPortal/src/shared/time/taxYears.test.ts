import { describe, expect, it } from "vitest";
import { reportableTaxYears, taxYearLabel } from "@/shared/time/taxYears";

describe("reportableTaxYears (fif-report R27)", () => {
  it("offers tax year Y from 2 April Y, in UTC, latest first, down to 2009", () => {
    expect(reportableTaxYears(new Date("2026-04-01T23:59:59Z"))[0]).toBe(2025);
    expect(reportableTaxYears(new Date("2026-04-02T00:00:00Z"))[0]).toBe(2026);
    expect(reportableTaxYears(new Date("2026-11-28T03:12:45Z"))).toHaveLength(18);
    expect(reportableTaxYears(new Date("2026-11-28T03:12:45Z")).at(-1)).toBe(2009);
  });

  it("offers nothing before 2 April 2009", () => {
    expect(reportableTaxYears(new Date("2009-04-01T12:00:00Z"))).toEqual([]);
    expect(reportableTaxYears(new Date("2009-04-02T00:00:00Z"))).toEqual([2009]);
  });

  it("labels a tax year with its dates", () => {
    expect(taxYearLabel(2026)).toBe("2026 (1 April 2025 to 31 March 2026)");
  });
});
