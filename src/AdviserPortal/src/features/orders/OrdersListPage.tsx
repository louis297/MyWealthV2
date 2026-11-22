import { useSearchParams } from "react-router";
import { FilterBar, Page, PageHeading, SelectField } from "@/components/portal";
import { SelectItem } from "@/components/ui/select";
import { orderFilters } from "@/features/orders/orderLabels";
import { useGetOrdersQuery } from "@/features/orders/ordersApi";
import { OrdersTable } from "@/features/orders/OrdersTable";
import { Pager } from "@/shared/components/Pager";

/** D71: the orders of the caller's Customers, the open ones by default. */
export function OrdersListPage() {
  const [params, setParams] = useSearchParams();
  const filter = orderFilters[params.get("status") ?? ""] ? params.get("status")! : "Open";
  const page = Number(params.get("page") ?? "1") || 1;
  const { data } = useGetOrdersQuery({ status: orderFilters[filter].status, pendingFill: orderFilters[filter].pendingFill, page });
  // The list item carries no fills, so the orders with a fill not booked yet are read by their own filter.
  const waiting = useGetOrdersQuery({ pendingFill: true });
  const attention = new Set((waiting.data?.items ?? []).map((item) => item.id));

  function update(key: string, value: string | null) {
    const next = new URLSearchParams(params);
    if (value === null) {
      next.delete(key);
    } else {
      next.set(key, value);
    }
    if (key !== "page") {
      next.delete("page");
    }
    setParams(next);
  }

  return (
    <Page>
      <PageHeading title="Orders" />
      <FilterBar>
        <SelectField id="orders-status" label="Status" className="w-56" value={filter}
          onChange={(event) => update("status", event.target.value || null)}>
          {Object.entries(orderFilters).map(([value, item]) => (
            <SelectItem key={value} value={value}>
              {item.label}
            </SelectItem>
          ))}
        </SelectField>
      </FilterBar>
      <OrdersTable items={data?.items ?? []} attention={attention} />
      <Pager page={page} pageSize={20} totalCount={data?.totalCount ?? 0} onPage={(next) => update("page", String(next))} />
    </Page>
  );
}
