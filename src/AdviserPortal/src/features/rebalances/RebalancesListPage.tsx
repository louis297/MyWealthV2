import { Link, useSearchParams } from "react-router";
import { FilterBar, Muted, Page, PageHeading, recordLinkClass, SelectField, StatusBadge } from "@/components/portal";
import { Card, CardContent } from "@/components/ui/card";
import { SelectItem } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useGetAccountQuery } from "@/features/accounts/accountsApi";
import { CustomerName } from "@/features/nominatedBankAccounts/BankAccountChecksPage";
import { rebalanceFilters, rebalanceStatusText, rebalanceTriggerLabel } from "@/features/rebalances/rebalanceLabels";
import { useGetRebalancesQuery } from "@/features/rebalances/rebalancesApi";
import { Pager } from "@/shared/components/Pager";
import { formatInstant } from "@/shared/time/instant";

function AccountName({ id }: { id: string }) {
  const { data } = useGetAccountQuery(id);
  return <>{data?.name ?? ""}</>;
}

/** P19: the rebalances of the accounts the caller can see, the open ones by default. */
export function RebalancesListPage() {
  const [params, setParams] = useSearchParams();
  const requested = params.get("status") ?? "";
  const filter = rebalanceFilters[requested] ? requested : "Open";
  const page = Number(params.get("page") ?? "1") || 1;
  const { data } = useGetRebalancesQuery({ status: rebalanceFilters[filter].status, open: rebalanceFilters[filter].open, page });

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

  const items = data?.items ?? [];
  return (
    <Page>
      <PageHeading title="Rebalances" />
      <FilterBar>
        <SelectField id="rebalances-status" label="Status" className="w-64" value={filter}
          onChange={(event) => update("status", event.target.value || null)}>
          {Object.entries(rebalanceFilters).map(([value, item]) => (
            <SelectItem key={value} value={value}>
              {item.label}
            </SelectItem>
          ))}
        </SelectField>
      </FilterBar>
      {items.length === 0 ? <Muted>No rebalances.</Muted> : (
        <Card className="gap-0 py-0">
          <CardContent className="px-0">
            <Table aria-label="Rebalances">
              <TableHeader>
                <TableRow>
                  <TableHead>Proposed</TableHead>
                  <TableHead>Customer</TableHead>
                  <TableHead>Account</TableHead>
                  <TableHead>Portfolio</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Started by</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {items.map((item) => (
                  <TableRow key={item.id}>
                    <TableCell>
                      <Link to={`/rebalances/${item.id}`} className={recordLinkClass}>{formatInstant(item.proposedAt)}</Link>
                    </TableCell>
                    <TableCell><CustomerName id={item.customerId} /></TableCell>
                    <TableCell><AccountName id={item.accountId} /></TableCell>
                    <TableCell>{item.portfolio.name}</TableCell>
                    <TableCell><StatusBadge status={rebalanceStatusText(item)} /></TableCell>
                    <TableCell>{rebalanceTriggerLabel(item.trigger)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}
      <Pager page={page} pageSize={20} totalCount={data?.totalCount ?? 0} onPage={(next) => update("page", String(next))} />
    </Page>
  );
}
