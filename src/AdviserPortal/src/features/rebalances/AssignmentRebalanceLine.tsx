import { Link } from "react-router";
import { recordLinkClass } from "@/components/portal";
import type { AssignmentRebalance } from "@/features/portfolios/portfoliosApi";
import { rebalanceStatusLabel } from "@/features/rebalances/rebalanceLabels";

/** P7, P20: the newest rebalance of an assignment, linked to its page, with the failure message when it has one. */
export function AssignmentRebalanceLine({ rebalance, as = "p" }: { rebalance: AssignmentRebalance | null; as?: "p" | "span" }) {
  const Tag = as;
  if (!rebalance) {
    return <Tag>No rebalance yet.</Tag>;
  }
  return (
    <Tag>
      <Link to={`/rebalances/${rebalance.id}`} className={recordLinkClass}>{rebalanceStatusLabel(rebalance.status)}</Link>
      {rebalance.failure ? ` ${rebalance.failure.message}` : ""}
    </Tag>
  );
}
