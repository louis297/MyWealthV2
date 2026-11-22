import { Section } from "@/components/portal";
import { orderFilters } from "@/features/orders/orderLabels";
import { useGetOrdersQuery } from "@/features/orders/ordersApi";
import { OrdersTable } from "@/features/orders/OrdersTable";

/** D69: the open orders of an account, under its holdings. */
export function AccountOrdersSection({ accountId }: { accountId: string }) {
  const { data } = useGetOrdersQuery({ accountId, status: orderFilters.Open.status });
  const waiting = useGetOrdersQuery({ accountId, pendingFill: true });
  if (!data || data.items.length === 0) {
    return null;
  }

  return (
    <Section title="Open orders">
      <OrdersTable items={data.items} attention={new Set((waiting.data?.items ?? []).map((item) => item.id))} compact />
    </Section>
  );
}
