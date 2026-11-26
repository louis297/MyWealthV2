/** The UTC date of an instant as `yyyy-MM-dd`. */
export function utcDate(now: Date = new Date()): string {
  return now.toISOString().slice(0, 10);
}

/** A `yyyy-MM-dd` date moved by whole days, in UTC. */
export function addDays(date: string, days: number): string {
  const [year, month, day] = date.split("-").map(Number);
  return new Date(Date.UTC(year, month - 1, day + days)).toISOString().slice(0, 10);
}

/** The last final day (valuation-run R4): the UTC date minus two days. */
export function lastFinalDay(now: Date = new Date()): string {
  return addDays(utcDate(now), -2);
}
