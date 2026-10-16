export type CronPreset = "daily" | "weekly" | "monthly";

export function toCron(preset: CronPreset, time: string): string {
  const [hourText, minuteText] = time.split(":");
  const minute = Number(minuteText);
  const hour = Number(hourText);
  if (preset === "weekly") {
    return `${minute} ${hour} * * 1`;
  }
  if (preset === "monthly") {
    return `${minute} ${hour} 1 * *`;
  }
  return `${minute} ${hour} * * *`;
}

function clock(minute: string, hour: string): string {
  return `${hour.padStart(2, "0")}:${minute.padStart(2, "0")}`;
}

export function humaniseCron(cron: string): string {
  const daily = /^(\d+) (\d+) \* \* \*$/.exec(cron);
  if (daily) {
    return `every day at ${clock(daily[1], daily[2])}`;
  }
  const weekly = /^(\d+) (\d+) \* \* 1$/.exec(cron);
  if (weekly) {
    return `every Monday at ${clock(weekly[1], weekly[2])}`;
  }
  const monthly = /^(\d+) (\d+) 1 \* \*$/.exec(cron);
  if (monthly) {
    return `1st of the month at ${clock(monthly[1], monthly[2])}`;
  }
  return cron;
}
