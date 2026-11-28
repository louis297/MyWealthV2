/** The tax years a FIF report can be asked for (fif-report R27). */
export function reportableTaxYears(now: Date = new Date()): number[] {
  void now;
  return [];
}

/** "{Y} (1 April {Y − 1} to 31 March {Y})". */
export function taxYearLabel(taxYear: number): string {
  return String(taxYear);
}
