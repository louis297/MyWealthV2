/** fixes/utc-instants.md: instants go to the API in UTC. */

const datetimeLocal = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,3})?)?$/;

/** Converts an `<input type="datetime-local">` value (browser local time) to a UTC ISO string. */
export function toUtcIso(value: string): string {
  if (!datetimeLocal.test(value)) {
    throw new Error(`Not a local date and time: "${value}".`);
  }
  // A date-time string with no offset is parsed as local time.
  return new Date(value).toISOString();
}

/** Shows an instant from the API in browser local time as `yyyy-MM-dd HH:mm`. */
export function formatInstant(iso: string): string {
  return iso;
}
