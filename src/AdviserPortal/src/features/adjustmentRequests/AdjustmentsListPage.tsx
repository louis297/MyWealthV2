import { useSearchParams } from "react-router";
import { FilterBar, Muted, Page, PageHeading, SelectField } from "@/components/portal";
import { SelectItem } from "@/components/ui/select";
import { adjustmentFilters, adjustmentsForbiddenSentence } from "@/features/adjustmentRequests/adjustmentRequestLabels";
import { useGetAdjustmentRequestsQuery } from "@/features/adjustmentRequests/adjustmentRequestsApi";
import { AdjustmentRequestsTable } from "@/features/adjustmentRequests/AdjustmentRequestsTable";
import { problemStatus } from "@/features/statementLines/statementLineLabels";
import { Pager } from "@/shared/components/Pager";

/** adjustment-review D103: the Tenant's adjustment requests, those waiting for approval by default. */
export function AdjustmentsListPage() {
  const [params, setParams] = useSearchParams();
  const filter = adjustmentFilters[params.get("status") ?? ""] ? params.get("status")! : "Requested";
  const page = Number(params.get("page") ?? "1") || 1;
  const { data, error } = useGetAdjustmentRequestsQuery({ status: adjustmentFilters[filter].status, page });

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

  if (problemStatus(error) === 403) {
    return (
      <Page>
        <PageHeading title="Adjustments" />
        <Muted>{adjustmentsForbiddenSentence}</Muted>
      </Page>
    );
  }

  return (
    <Page>
      <PageHeading title="Adjustments" />
      <FilterBar>
        <SelectField id="adjustments-status" label="Status" className="w-56" value={filter}
          onChange={(event) => update("status", event.target.value || null)}>
          {Object.entries(adjustmentFilters).map(([value, item]) => (
            <SelectItem key={value} value={value}>
              {item.label}
            </SelectItem>
          ))}
        </SelectField>
      </FilterBar>
      <AdjustmentRequestsTable items={data?.items ?? []} />
      <Pager page={page} pageSize={20} totalCount={data?.totalCount ?? 0} onPage={(next) => update("page", String(next))} />
    </Page>
  );
}
