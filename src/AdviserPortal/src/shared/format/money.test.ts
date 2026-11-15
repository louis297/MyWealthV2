import { describe, expect, it } from "vitest";
import { formatAmount, formatMoney } from "@/shared/format/money";

describe("formatMoney", () => {
  it("groups thousands and pads to the currency's decimal places", () => {
    expect(formatMoney(1250, "NZD", 2)).toBe("1,250.00 NZD");
    expect(formatMoney(-10, "NZD", 2)).toBe("-10.00 NZD");
    expect(formatMoney(1000000, "JPY", 0)).toBe("1,000,000 JPY");
  });

  it("keeps the decimals of an exact value beyond the currency's places", () => {
    expect(formatAmount(6.1728, 2)).toBe("6.1728");
    expect(formatAmount(0.000123, 2)).toBe("0.000123");
  });

  it("shows the value as sent when the decimal places are unknown", () => {
    expect(formatAmount(1250.5)).toBe("1,250.5");
    expect(formatAmount(10)).toBe("10");
  });
});
