import { Link } from "react-router";
import { useAppSelector } from "@/app/hooks";
import { Muted, Page, PageHeading, recordLinkClass, Section, StatusBadge } from "@/components/portal";
import { buttonVariants } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { bandsText, custodialOnlyLine } from "@/features/fees/feeText";
import { useGetAllTradeSchedulesQuery, type TradeFeeSchedule } from "@/features/fees/feesApi";
import { scheduleStates, todayUtc } from "@/features/fees/scheduleState";
import { canManageFees } from "@/features/session/roles";
import { formatMoney } from "@/shared/format/money";
import { cn } from "cn";

function byCurrency(items: TradeFeeSchedule[]) {
  const groups = new Map<string, TradeFeeSchedule[]>();
  for (const item of items) {
    groups.set(item.quoteCurrency, [...(groups.get(item.quoteCurrency) ?? []), item]);
  }
  return [...groups.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([currency, rows]) => ({
      currency,
      rows: [...rows].sort((a, b) => b.effectiveFrom.localeCompare(a.effectiveFrom)),
    }));
}

export function FeesTradePage() {
  const role = useAppSelector((state) => state.session.currentUser?.role);
  const { data } = useGetAllTradeSchedulesQuery();
  const currencies = useGetCurrenciesQuery();
  const places = new Map((currencies.data ?? []).map((item) => [item.code, item.decimalPlaces]));
  const items = data ?? [];
  const states = scheduleStates(items, todayUtc());

  return (
    <Page>
      <PageHeading
        title="Fees"
        description={custodialOnlyLine}
        actions={
          canManageFees(role) ? (
            <Link to="/fees/trade/new" className={cn(buttonVariants({ size: "sm" }))}>
              New schedule
            </Link>
          ) : null
        }
      />
      <Tabs value="trade">
        <TabsList>
          <TabsTrigger value="trade">Trade fees</TabsTrigger>
        </TabsList>
        <TabsContent value="trade" className="flex flex-col gap-6">
          {data && items.length === 0 ? (
            <Muted>No trade fee schedules yet. Trades have no brokerage until a schedule starts.</Muted>
          ) : null}
          {byCurrency(items).map(({ currency, rows }) => (
            <Section key={currency} title={currency}>
              <Card className="gap-0 py-0">
                <CardContent className="px-0">
                  <Table aria-label={`${currency} trade fee schedules`}>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Effective from</TableHead>
                        <TableHead>Rates</TableHead>
                        <TableHead>Minimum</TableHead>
                        <TableHead>Cap</TableHead>
                        <TableHead>State</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {rows.map((row) => (
                        <TableRow key={row.id}>
                          <TableCell>
                            <Link to={`/fees/trade/${row.id}`} className={recordLinkClass}>
                              {`${row.effectiveFrom} UTC`}
                            </Link>
                          </TableCell>
                          <TableCell>{bandsText(row.bands, places.get(currency))}</TableCell>
                          <TableCell>{formatMoney(row.minimum, currency, places.get(currency))}</TableCell>
                          <TableCell>
                            {row.cap === null ? "None" : formatMoney(row.cap, currency, places.get(currency))}
                          </TableCell>
                          <TableCell>
                            <StatusBadge status={states.get(row.id) ?? ""} />
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </CardContent>
              </Card>
            </Section>
          ))}
        </TabsContent>
      </Tabs>
    </Page>
  );
}
