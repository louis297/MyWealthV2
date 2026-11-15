/**
 * Problem code of a failed RTK Query call (api-design §4.2). Pages branch on this, not on the status alone,
 * once a route can return more than one 409.
 */
export function problemCode(error: unknown): string | undefined {
  if (typeof error !== "object" || error === null || !("data" in error)) {
    return undefined;
  }
  const data = (error as { data?: unknown }).data;
  if (typeof data !== "object" || data === null || !("code" in data)) {
    return undefined;
  }
  const code = (data as { code?: unknown }).code;
  return typeof code === "string" ? code : undefined;
}

/** api-design §4.2: the request's rowVersion is not the stored one. The page reloads the row. */
export const staleRowVersion = "stale_row_version";

/** trade-fees §8.2: the schedule row may already apply to a posted trade. The page offers no change. */
export const feeScheduleInEffect = "fee_schedule_in_effect";
