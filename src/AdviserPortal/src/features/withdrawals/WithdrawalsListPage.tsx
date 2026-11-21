import { useSearchParams } from "react-router";
import { FilterBar, Page, PageHeading, SelectField } from "@/components/portal";
import { SelectItem } from "@/components/ui/select";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { withdrawalFilters } from "@/features/withdrawals/withdrawalLabels";
import { WithdrawalsTable } from "@/features/withdrawals/WithdrawalsTable";
import { useGetWithdrawalsQuery } from "@/features/withdrawals/withdrawalsApi";
import { Pager } from "@/shared/components/Pager";

/** D46: the withdrawals of the caller's Customers, waiting for approval by default. */
export function WithdrawalsListPage() {
  const [params, setParams] = useSearchParams();
  const filter = withdrawalFilters[params.get("status") ?? ""] ? params.get("status")! : "Requested";
  const page = Number(params.get("page") ?? "1") || 1;
  const { data } = useGetWithdrawalsQuery({ status: withdrawalFilters[filter].status, page });
  const currencies = useGetCurrenciesQuery();
  const places = (code: string) => currencies.data?.find((item) => item.code === code)?.decimalPlaces;

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
      <PageHeading title="Withdrawals" />
      <FilterBar>
        <SelectField id="withdrawals-status" label="Status" className="w-56" value={filter}
          onChange={(event) => update("status", event.target.value || null)}>
          {Object.entries(withdrawalFilters).map(([value, item]) => (
            <SelectItem key={value} value={value}>
              {item.label}
            </SelectItem>
          ))}
        </SelectField>
      </FilterBar>
      <WithdrawalsTable items={data?.items ?? []} places={places} />
      <Pager page={page} pageSize={20} totalCount={data?.totalCount ?? 0} onPage={(next) => update("page", String(next))} />
    </Page>
  );
}
