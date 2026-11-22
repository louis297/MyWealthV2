import type { RateBand } from "@/features/interest/interestApi";
import { formatAmount } from "@/shared/format/money";

/** D58: the kind label of a withholding tax. */
export function taxKindLabel(kind: string) {
  switch (kind) {
    case "Rwt":
      return "RWT";
    case "Nrwt":
      return "NRWT";
    default:
      return kind;
  }
}

/** D58: why a withholding rate applies, in words. */
export function basisWords(basis: string) {
  switch (basis) {
    case "Elected":
      return "chosen rate";
    case "Default":
      return "no rate chosen";
    case "NonDeclaration":
      return "no IRD number held";
    case "NonResident":
      return "overseas resident";
    default:
      return basis;
  }
}

export function residencyLabel(residency: string) {
  return residency === "Overseas" ? "Overseas" : "New Zealand";
}

/** D59: "3 % up to 100,000.00, then 2 %". */
export function rateBandsText(bands: RateBand[], decimalPlaces?: number) {
  return bands
    .map((band, index) => {
      const next = bands[index + 1];
      const rate = `${band.ratePercent} %`;
      if (index === 0) {
        return next ? `${rate} up to ${formatAmount(next.from, decimalPlaces)}` : rate;
      }
      return next ? `then ${rate} up to ${formatAmount(next.from, decimalPlaces)}` : `then ${rate}`;
    })
    .join(", ");
}

/** D60: the label of a run's trigger. */
export function triggerLabel(trigger: string) {
  switch (trigger) {
    case "Scheduled":
      return "Daily run";
    case "Manual":
      return "Run by the platform team";
    case "AccountClosure":
      return "Account closure";
    default:
      return trigger;
  }
}

export const inEffectRateMessage =
  "This rate has already been used and cannot be changed. Add a new one from a later date.";

export const ratesHelp =
  "Rates are per year. Interest is worked out on each day's closing balance and paid after the end of each month.";

export const runsForbiddenSentence = "Only a firm administrator can see interest runs.";

export const taxProfileConflictSentence = "Someone else changed these details.";

export const irdNotStoredSentence = "We do not store the IRD number here.";
