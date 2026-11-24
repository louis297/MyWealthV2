import { useSearchParams } from "react-router";
import { FilterBar, Page, PageHeading, SelectField } from "@/components/portal";
import { SelectItem } from "@/components/ui/select";
import { transferFilters } from "@/features/inKindTransfers/inKindTransferLabels";
import { useGetInKindTransfersQuery } from "@/features/inKindTransfers/inKindTransfersApi";
import { TransfersTable } from "@/features/inKindTransfers/TransfersTable";
import { Pager } from "@/shared/components/Pager";

/** D88: the in-kind transfers of the caller's Customers, the open ones by default. */
export function TransfersListPage() {
  const [params, setParams] = useSearchParams();
  const filter = transferFilters[params.get("status") ?? ""] ? params.get("status")! : "Open";
  const page = Number(params.get("page") ?? "1") || 1;
  const { data } = useGetInKindTransfersQuery({ status: transferFilters[filter].status, page });

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
      <PageHeading title="Transfers" />
      <FilterBar>
        <SelectField id="transfers-status" label="Status" className="w-56" value={filter}
          onChange={(event) => update("status", event.target.value || null)}>
          {Object.entries(transferFilters).map(([value, item]) => (
            <SelectItem key={value} value={value}>
              {item.label}
            </SelectItem>
          ))}
        </SelectField>
      </FilterBar>
      <TransfersTable items={data?.items ?? []} />
      <Pager page={page} pageSize={20} totalCount={data?.totalCount ?? 0} onPage={(next) => update("page", String(next))} />
    </Page>
  );
}
