import { useId, useState } from "react";
import { Link, useParams } from "react-router";
import { DetailGrid, DetailItem, Muted, Page, PageHeading, recordLinkClass, StatusBadge } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import {
  custodianFeedLabel,
  custodianForbiddenSentence,
  custodianRunStatusLabel,
  lineClasses,
} from "@/features/custodianReconciliations/custodianReconciliationLabels";
import {
  type CustodianReconciliationLine,
  useGetCustodianReconciliationQuery,
} from "@/features/custodianReconciliations/custodianReconciliationsApi";
import { problemStatus } from "@/features/statementLines/statementLineLabels";
import { formatInstant } from "@/shared/time/instant";

/** D92: the components of a line that are not 0, as sentences. */
function Components({ line }: { line: CustodianReconciliationLine }) {
  const movements = line.instrument
    ? `/custody-movements?status=Unmatched&instrumentId=${line.instrument.id}`
    : "/custody-movements?status=Unmatched";
  return (
    <div className="flex flex-col gap-0.5 text-xs">
      {line.notProcessed !== 0 ? <span>{`${line.notProcessed} arrived and not processed yet`}</span> : null}
      {line.unsettledTrades !== 0 ? <span>{`${line.unsettledTrades} from trades not settled yet`}</span> : null}
      {line.suspense !== 0 ? (
        <Link to={movements} className={recordLinkClass}>{`${line.suspense} in movements waiting for a decision`}</Link>
      ) : null}
      {line.unbookedFills !== 0 ? (
        <Link to="/orders?status=NeedsAttention" className={recordLinkClass}>{`${line.unbookedFills} delivered for trades not booked`}</Link>
      ) : null}
      {line.unexplained !== 0 ? <span className="text-destructive">{`${line.unexplained} not explained`}</span> : null}
    </div>
  );
}

function LineGroup({ heading, lines, collapsed }: { heading: string; lines: CustodianReconciliationLine[]; collapsed: boolean }) {
  const [open, setOpen] = useState(!collapsed);
  const headingId = useId();
  return (
    <section aria-labelledby={headingId} className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id={headingId} className="text-base font-medium">{heading}</h2>
        {collapsed ? (
          <Button type="button" variant="ghost" size="sm" aria-expanded={open} onClick={() => setOpen(!open)}>
            {`${open ? "Hide" : "Show"} ${heading} (${lines.length})`}
          </Button>
        ) : <Muted>{String(lines.length)}</Muted>}
      </div>
      {open ? (
        lines.length === 0 ? <Muted>None</Muted> : (
          <Card className="gap-0 py-0">
            <CardContent className="px-0">
              <Table aria-label={heading}>
                <TableHeader>
                  <TableRow>
                    <TableHead>Instrument</TableHead>
                    <TableHead>At the custodian</TableHead>
                    <TableHead>On the platform</TableHead>
                    <TableHead>Difference</TableHead>
                    <TableHead>Explained by</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {lines.map((line) => (
                    <TableRow key={`${line.symbol}-${line.instrument?.id ?? "none"}`}>
                      <TableCell>
                        {line.instrument ? line.instrument.symbol : (
                          <>
                            {line.symbol}
                            <span className="block text-muted-foreground text-xs">Not in your catalog</span>
                          </>
                        )}
                      </TableCell>
                      <TableCell>{line.custodianQuantity}</TableCell>
                      <TableCell>{line.platformQuantity}</TableCell>
                      <TableCell>{line.difference}</TableCell>
                      <TableCell>
                        <Components line={line} />
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        )
      ) : null}
    </section>
  );
}

/** D92: one custodian reconciliation run with its lines grouped by class. */
export function CustodianReconciliationDetailPage() {
  const { id = "" } = useParams();
  const { data: run, error, isLoading } = useGetCustodianReconciliationQuery(id);

  if (problemStatus(error) === 403) {
    return (
      <Page>
        <PageHeading title="Custodian reconciliation" />
        <Muted>{custodianForbiddenSentence}</Muted>
      </Page>
    );
  }
  if (problemStatus(error) === 404) {
    return <Muted>Not found</Muted>;
  }
  if (isLoading || !run) {
    return <Muted>Loading…</Muted>;
  }

  const account = run.safekeepingAccount;

  return (
    <Page>
      <PageHeading
        title="Custodian reconciliation"
        description={`${account.custodianName} · ${account.accountName} · ${account.accountNumber} · ${account.currency}`}
        actions={
          <Link to={`/custodian-reconciliations/history?safekeepingAccountId=${account.id}`} className={recordLinkClass}>
            History
          </Link>
        }
      />
      <DetailGrid>
        <DetailItem label="Ran at">{formatInstant(run.ranAt)}</DetailItem>
        <DetailItem label="By">{run.ranBy ? run.ranBy.name : "Daily run"}</DetailItem>
        <DetailItem label="Status">
          <StatusBadge status={custodianRunStatusLabel(run)} />
        </DetailItem>
        <DetailItem label="Positions as at">{run.positionsAt ? formatInstant(run.positionsAt) : "—"}</DetailItem>
      </DetailGrid>
      {lineClasses.map((group) => (
        <LineGroup key={group.value} heading={group.heading} collapsed={group.collapsed}
          lines={run.lines.filter((line) => line.class === group.value)} />
      ))}
      <section className="flex flex-col gap-1">
        <h2 className="text-base font-medium">Information</h2>
        <p className="text-sm">{`Transfers in flight: ${run.transfersInFlight}`}</p>
        <p className="text-sm">{`Fills not delivered: ${run.fillsNotDelivered}`}</p>
        <p className="text-sm">{`Feed: ${custodianFeedLabel(run.feed, formatInstant)}`}</p>
      </section>
    </Page>
  );
}
