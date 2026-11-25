import { Section } from "@/components/portal";
import { useGetAdjustmentRequestsQuery } from "@/features/adjustmentRequests/adjustmentRequestsApi";
import { AdjustmentRequestsTable } from "@/features/adjustmentRequests/AdjustmentRequestsTable";

/** adjustment-review D105: the open adjustment requests of a Custodial account, above its activity. TenantAdmin only. */
export function AccountAdjustmentRequestsSection({ accountId }: { accountId: string }) {
  const { data } = useGetAdjustmentRequestsQuery({ accountId, status: "Requested" });
  if (!data || data.items.length === 0) {
    return null;
  }

  return (
    <Section title="Adjustments waiting for approval">
      <AdjustmentRequestsTable items={data.items} compact />
    </Section>
  );
}
