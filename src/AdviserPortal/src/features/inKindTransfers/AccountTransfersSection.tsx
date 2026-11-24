import { Section } from "@/components/portal";
import { openTransferStatuses } from "@/features/inKindTransfers/inKindTransferLabels";
import { useGetInKindTransfersQuery } from "@/features/inKindTransfers/inKindTransfersApi";
import { TransfersTable } from "@/features/inKindTransfers/TransfersTable";

/** D86: the open in-kind transfers of an account, under its holdings. */
export function AccountTransfersSection({ accountId }: { accountId: string }) {
  const { data } = useGetInKindTransfersQuery({ accountId, status: openTransferStatuses });
  if (!data || data.items.length === 0) {
    return null;
  }

  return (
    <Section title="Open transfers">
      <TransfersTable items={data.items} compact />
    </Section>
  );
}
