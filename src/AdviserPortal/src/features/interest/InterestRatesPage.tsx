import { useState } from "react";
import { Link } from "react-router";
import { useAppSelector } from "@/app/hooks";
import { Messages, Muted, Page, PageHeading, Section, StatusBadge } from "@/components/portal";
import { Button, buttonVariants } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import {
  type InterestRateSchedule,
  useDeleteInterestRateScheduleMutation,
  useGetAllInterestRateSchedulesQuery,
} from "@/features/interest/interestApi";
import { inEffectRateMessage, rateBandsText, ratesHelp } from "@/features/interest/interestText";
import { canManageInterestRates, canReadInterestRuns } from "@/features/session/roles";
import { problemMessages } from "@/features/statementLines/statementLineLabels";
import { cn } from "cn";

function byCurrency(items: InterestRateSchedule[]) {
  const groups = new Map<string, InterestRateSchedule[]>();
  for (const item of items) {
    groups.set(item.currency, [...(groups.get(item.currency) ?? []), item]);
  }
  return [...groups.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([currency, rows]) => ({ currency, rows: [...rows].sort((a, b) => b.effectiveFrom.localeCompare(a.effectiveFrom)) }));
}

function problemCode(error: unknown) {
  return typeof error === "object" && error !== null && "data" in error
    ? (error as { data?: { code?: string } }).data?.code
    : undefined;
}

/** D59: the firm's interest rates per currency. A TenantAdmin adds, edits and deletes a pending row. */
export function InterestRatesPage() {
  const role = useAppSelector((state) => state.session.currentUser?.role);
  const { data, refetch } = useGetAllInterestRateSchedulesQuery();
  const currencies = useGetCurrenciesQuery();
  const [remove] = useDeleteInterestRateScheduleMutation();
  const [messages, setMessages] = useState<string[]>([]);
  const places = new Map((currencies.data ?? []).map((item) => [item.code, item.decimalPlaces]));
  const manage = canManageInterestRates(role);
  const items = data ?? [];

  async function onDelete(row: InterestRateSchedule) {
    try {
      await remove(row.id).unwrap();
      setMessages([]);
    } catch (error: unknown) {
      if (problemCode(error) === "interest_schedule_in_effect") {
        setMessages([inEffectRateMessage]);
        void refetch();
        return;
      }
      const result = problemMessages(error);
      setMessages([...result.messages, ...Object.values(result.fields).flat()]);
    }
  }

  return (
    <Page>
      <PageHeading
        title="Interest rates"
        description={ratesHelp}
        actions={
          <div className="flex gap-2">
            {canReadInterestRuns(role) ? (
              <Link to="/interest/runs" className={cn(buttonVariants({ size: "sm", variant: "outline" }))}>
                Interest runs
              </Link>
            ) : null}
            {manage ? (
              <Link to="/interest/rates/new" className={cn(buttonVariants({ size: "sm" }))}>
                New rate
              </Link>
            ) : null}
          </div>
        }
      />
      <Messages messages={messages} />
      {data && items.length === 0 ? <Muted>No interest rates yet. Custodial cash earns nothing until a rate starts.</Muted> : null}
      {byCurrency(items).map(({ currency, rows }) => (
        <Section key={currency} title={currency}>
          <Card className="gap-0 py-0">
            <CardContent className="px-0">
              <Table aria-label={`${currency} interest rates`}>
                <TableHeader>
                  <TableRow>
                    <TableHead>Effective from</TableHead>
                    <TableHead>Rates</TableHead>
                    <TableHead>State</TableHead>
                    {manage ? <TableHead /> : null}
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {rows.map((row) => (
                    <TableRow key={row.id}>
                      <TableCell>{`${row.effectiveFrom} UTC`}</TableCell>
                      <TableCell>{rateBandsText(row.bands, places.get(currency))}</TableCell>
                      <TableCell>
                        <StatusBadge status={row.inEffect ? "In effect" : `Starts ${row.effectiveFrom}`} />
                      </TableCell>
                      {manage ? (
                        <TableCell>
                          {row.inEffect ? null : (
                            <div className="flex gap-2">
                              <Link to={`/interest/rates/${row.id}/edit`} className={cn(buttonVariants({ size: "sm", variant: "outline" }))}>
                                Edit
                              </Link>
                              <Button size="sm" variant="outline" onClick={() => void onDelete(row)}>
                                Delete
                              </Button>
                            </div>
                          )}
                        </TableCell>
                      ) : null}
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        </Section>
      ))}
    </Page>
  );
}
