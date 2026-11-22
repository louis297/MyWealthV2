import { useState } from "react";
import { Link } from "react-router";
import { Muted, Page, PageHeading, recordLinkClass } from "@/components/portal";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { type InterestRun, useGetInterestRunsQuery } from "@/features/interest/interestApi";
import { runsForbiddenSentence, triggerLabel } from "@/features/interest/interestText";
import { problemStatus } from "@/features/statementLines/statementLineLabels";
import { Pager } from "@/shared/components/Pager";
import { formatMoney } from "@/shared/format/money";

/** D60: "96.12 NZD, 410.55 USD". */
export function useTotalsText() {
  const currencies = useGetCurrenciesQuery();
  const places = (code: string) => currencies.data?.find((item) => item.code === code)?.decimalPlaces ?? 2;
  return (run: InterestRun, field: "grossAmount" | "taxAmount") =>
    run.totals.map((total) => formatMoney(total[field], total.currency, places(total.currency))).join(", ");
}

export function FailedCount({ count }: { count: number }) {
  return <span className={count > 0 ? "text-destructive font-medium" : undefined}>{count}</span>;
}

/** D60: a TenantAdmin's list of the firm's interest runs. There is no start button: the platform team starts runs. */
export function InterestRunsPage() {
  const [page, setPage] = useState(1);
  const { data, error } = useGetInterestRunsQuery({ page });
  const totals = useTotalsText();

  return (
    <Page>
      <PageHeading title="Interest runs" />
      {problemStatus(error) === 403 ? (
        <Muted>{runsForbiddenSentence}</Muted>
      ) : (
        <>
          <Card className="gap-0 py-0">
            <CardContent className="px-0">
              <Table aria-label="Interest runs">
                <TableHeader>
                  <TableRow>
                    <TableHead>As of</TableHead>
                    <TableHead>Trigger</TableHead>
                    <TableHead>Accounts</TableHead>
                    <TableHead>Failed</TableHead>
                    <TableHead>Interest paid</TableHead>
                    <TableHead>Tax withheld</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {(data?.items ?? []).map((run) => (
                    <TableRow key={run.id}>
                      <TableCell>
                        <Link to={`/interest/runs/${run.id}`} className={recordLinkClass}>{run.asOfDate}</Link>
                      </TableCell>
                      <TableCell>{triggerLabel(run.trigger)}</TableCell>
                      <TableCell>{run.accountsProcessed}</TableCell>
                      <TableCell><FailedCount count={run.accountsFailed} /></TableCell>
                      <TableCell>{totals(run, "grossAmount")}</TableCell>
                      <TableCell>{totals(run, "taxAmount")}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
          <Pager page={page} pageSize={20} totalCount={data?.totalCount ?? 0} onPage={setPage} />
        </>
      )}
    </Page>
  );
}
