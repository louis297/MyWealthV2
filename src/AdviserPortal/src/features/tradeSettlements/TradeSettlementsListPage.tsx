import { Link, useSearchParams } from "react-router";
import { FilterBar, Muted, Page, PageHeading, recordLinkClass, SelectField, StatusBadge } from "@/components/portal";
import { Card, CardContent } from "@/components/ui/card";
import { SelectItem } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { netLabel, tradeSettlementFilters, tradeSettlementStatusLabel } from "@/features/tradeSettlements/tradeSettlementLabels";
import { useGetTradeSettlementsQuery } from "@/features/tradeSettlements/tradeSettlementsApi";
import { Pager } from "@/shared/components/Pager";

/** D76: the trade settlements of the firm, the open ones by default. */
export function TradeSettlementsListPage() {
  const [params, setParams] = useSearchParams();
  const requested = params.get("status") ?? (params.get("open") === "true" ? "Open" : "");
  const filter = tradeSettlementFilters[requested] ? requested : "Open";
  const page = Number(params.get("page") ?? "1") || 1;
  const { data } = useGetTradeSettlementsQuery({
    status: tradeSettlementFilters[filter].status,
    open: tradeSettlementFilters[filter].open,
    page,
  });

  function update(key: string, value: string | null) {
    const next = new URLSearchParams(params);
    next.delete("open");
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
      <PageHeading title="Trade settlements" />
      <FilterBar>
        <SelectField id="trade-settlements-status" label="Status" className="w-64" value={filter}
          onChange={(event) => update("status", event.target.value || null)}>
          {Object.entries(tradeSettlementFilters).map(([value, item]) => (
            <SelectItem key={value} value={value}>
              {item.label}
            </SelectItem>
          ))}
        </SelectField>
      </FilterBar>
      {items.length === 0 ? <Muted>No trade settlements.</Muted> : (
        <Card className="gap-0 py-0">
          <CardContent className="px-0">
            <Table aria-label="Trade settlements">
              <TableHeader>
                <TableRow>
                  <TableHead>Settled date</TableHead>
                  <TableHead>Broker reference</TableHead>
                  <TableHead>Currency</TableHead>
                  <TableHead>Trades</TableHead>
                  <TableHead>Net</TableHead>
                  <TableHead>Status</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {items.map((item) => (
                  <TableRow key={item.id}>
                    <TableCell>
                      <Link to={`/trade-settlements/${item.id}`} className={recordLinkClass}>{item.settledDate}</Link>
                    </TableCell>
                    <TableCell>{item.brokerSettlementId}</TableCell>
                    <TableCell>{item.clientMoneyAccount.currency}</TableCell>
                    <TableCell>{item.fillCount}</TableCell>
                    <TableCell>{netLabel(item.netAmount)}</TableCell>
                    <TableCell><StatusBadge status={tradeSettlementStatusLabel(item.status)} /></TableCell>
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
