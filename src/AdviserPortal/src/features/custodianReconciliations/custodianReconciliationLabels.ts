import { custodianFailureLabel } from "@/features/inKindTransfers/inKindTransferLabels";

/** D91: the status of a run: "Agreed", "<n> breaks" or "No positions yet"; no run yet is empty. */
export function custodianRunStatusLabel(run: { status: string; breakCount: number } | null | undefined) {
  if (!run) {
    return "";
  }
  switch (run.status) {
    case "Breaks":
      return `${run.breakCount} ${run.breakCount === 1 ? "break" : "breaks"}`;
    case "NoPositions":
      return "No positions yet";
    default:
      return run.status;
  }
}

/** D91: the feed state of a safekeeping account, as BO23 shows a feed: never read, last success, or failing since. */
export function custodianFeedLabel(feed: { lastSucceededAt: string | null; lastFailedAt: string | null; lastFailure: string | null } | null,
  format: (instant: string) => string) {
  if (!feed || (!feed.lastSucceededAt && !feed.lastFailedAt)) {
    return "Never read";
  }
  if (feed.lastFailedAt && (!feed.lastSucceededAt || feed.lastFailedAt > feed.lastSucceededAt)) {
    return `Failing since ${format(feed.lastFailedAt)}: ${custodianFailureLabel(feed.lastFailure)}`;
  }
  return `Read ${format(feed.lastSucceededAt!)}`;
}

/** D92: the line groups, the last collapsed. */
export const lineClasses = [
  { value: "Break", heading: "Breaks", collapsed: false },
  { value: "Timing", heading: "Timing", collapsed: false },
  { value: "Agreed", heading: "Agreed", collapsed: true },
] as const;

export const noSafekeepingSentence = "Your firm has no safekeeping account. Holdings are not checked against a custodian.";
export const custodianForbiddenSentence = "Only a firm administrator can see custodian reconciliations.";
