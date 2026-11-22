import { Link } from "react-router";
import { recordLinkClass, Section } from "@/components/portal";
import { kindLabel, ratioText } from "@/features/corporateActions/corporateActionText";
import type { TransactionCorporateAction } from "@/features/transactions/transactionsApi";

/** D68: the event behind an entitlement posting, its quantities and a link to the corporate action. */
export function CorporateActionPostingBlock({ detail }: { detail: TransactionCorporateAction }) {
  return (
    <Section title={`${kindLabel(detail.kind)} ${ratioText(detail)}`}>
      <p className="text-sm">{`Ex-date ${detail.exDate}`}</p>
      <p className="text-sm">{`${detail.quantityBefore} → ${detail.quantityAfter} shares`}</p>
      <Link to={`/corporate-actions/${detail.id}`} className={recordLinkClass}>Corporate action</Link>
    </Section>
  );
}
