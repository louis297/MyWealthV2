import { utcDate } from "@/shared/time/days";

/** The first tax year the tax year read answers (tax-year-events R4). */
const firstTaxYear = 2009;

/**
 * The tax years a FIF report can be asked for today (fif-report R28, C50, §9): tax year Y from 2 April Y, worked out from
 * the UTC date, latest first, down to 2009. The server checks the year again (R7).
 */
export function reportableTaxYears(now: Date = new Date()): number[] {
  const today = utcDate(now);
  const year = Number(today.slice(0, 4));
  const latest = today >= `${year}-04-02` ? year : year - 1;
  const years: number[] = [];
  for (let taxYear = latest; taxYear >= firstTaxYear; taxYear -= 1) {
    years.push(taxYear);
  }
  return years;
}

/** "{Y} (1 April {Y − 1} to 31 March {Y})". */
export function taxYearLabel(taxYear: number): string {
  return `${taxYear} (1 April ${taxYear - 1} to 31 March ${taxYear})`;
}
