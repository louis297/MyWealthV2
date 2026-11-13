/**
 * Problem code of a failed RTK Query call (api-design §4.2). Pages branch on this, not on the status alone,
 * once a route can return more than one 409.
 */
export function problemCode(_error: unknown): string | undefined {
  return undefined;
}
