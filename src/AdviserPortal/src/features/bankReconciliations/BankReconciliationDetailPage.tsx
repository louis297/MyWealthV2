import { Link, useParams } from "react-router";
import { DetailGrid, DetailItem, Muted, Page, PageHeading, recordLinkClass, Section, StatusBadge } from "@/components/portal";
import { Card, CardContent } from "@/components/ui/card";
import {
  inFlightSentence,
  itemClasses,
  kindLabel,
  runStatusLabel,
  unexplainedSentence,
} from "@/features/bankReconciliations/bankReconciliationLabels";
import { useGetBankReconciliationQuery } from "@/features/bankReconciliations/bankReconciliationsApi";
import { forbiddenSentence } from "@/features/bankReconciliations/BankReconciliationOverviewPage";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { problemStatus, reasonLabel, statusLabel } from "@/features/statementLines/statementLineLabels";
import { formatAmount } from "@/shared/format/money";
import { formatInstant } from "@/shared/time/instant";

export function BankReconciliationDetailPage() {
  const { id = "" } = useParams();
  const { data: run, error, isLoading } = useGetBankReconciliationQuery(id);
  const currencies = useGetCurrenciesQuery();

  if (problemStatus(error) === 403) {
    return (
      <Page>
        <PageHeading title="Bank reconciliation" />
        <Muted>{forbiddenSentence}</Muted>
      </Page>
    );
  }
  if (problemStatus(error) === 404) {
    return <Muted>Not found</Muted>;
  }
  if (isLoading || !run) {
    return <Muted>Loading…</Muted>;
  }

  const account = run.clientMoneyAccount;
  const places = currencies.data?.find((item) => item.code === account.currency)?.decimalPlaces;
  const amount = (value: number | null) => (value === null ? "" : `${formatAmount(value, places)} ${account.currency}`);

  return (
    <Page>
      <PageHeading
        title="Bank reconciliation"
        description={`${account.bankName} · ${account.accountName} · ${account.accountNumber} · ${account.currency}`}
        actions={
          <Link to={`/bank-reconciliations/history?clientMoneyAccountId=${account.id}`} className={recordLinkClass}>
            History
          </Link>
        }
      />
      <DetailGrid>
        <DetailItem label="Ran at">{formatInstant(run.ranAt)}</DetailItem>
        <DetailItem label="By">{run.ranBy ? run.ranBy.name : "Daily run"}</DetailItem>
        <DetailItem label="Status">
          <StatusBadge status={runStatusLabel(run)} />
        </DetailItem>
        {run.status === "NoBankBalance" ? null : (
          <>
            <DetailItem label="Bank balance">{amount(run.bankBalance)}</DetailItem>
            <DetailItem label="Statement read">{run.balanceAt ? formatInstant(run.balanceAt) : ""}</DetailItem>
            <DetailItem label="Opening balance">{amount(run.openingBalance)}</DetailItem>
          </>
        )}
        <DetailItem label="Custodial cash">{amount(run.custodialCash)}</DetailItem>
        {run.status === "NoBankBalance" ? null : <DetailItem label="Difference">{amount(run.difference)}</DetailItem>}
      </DetailGrid>
      {itemClasses.map((group) => {
        const items = run.items.filter((item) => item.class === group.value);
        const total = run.totals[group.total];
        return (
          <Section key={group.value} title={group.heading} actions={<Muted>{`${total.count} · ${amount(total.amount)}`}</Muted>}>
            {items.length === 0 ? <Muted>None</Muted> : null}
            {items.map((item, index) => (
              <Card key={`${group.value}-${index}`} className="py-2">
                <CardContent className="flex flex-col gap-1 text-sm">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <span className="font-medium">{kindLabel(item.kind)}</span>
                    <span>{amount(item.amount)}</span>
                  </div>
                  {item.statementLine ? (
                    <div className="flex flex-wrap gap-2 text-muted-foreground">
                      <span>{item.statementLine.direction}</span>
                      <Link to={`/statement-lines/${item.statementLine.id}`} className={recordLinkClass}>
                        {item.statementLine.valueDate}
                      </Link>
                      <span>{statusLabel(item.statementLine.status)}</span>
                      {item.statementLine.unmatchedReason ? <span>{reasonLabel(item.statementLine.unmatchedReason)}</span> : null}
                      {item.statementLine.note ? <span>{item.statementLine.note}</span> : null}
                    </div>
                  ) : item.count > 1 ? (
                    <span className="text-muted-foreground">{item.count} transactions</span>
                  ) : null}
                  {item.kind === "Unexplained" ? <span className="text-destructive">{unexplainedSentence}</span> : null}
                  {item.kind === "TradesNeedingAction" ? (
                    // trade-settlement D78.
                    <Link to="/trade-settlements?open=true" className={recordLinkClass}>Open trade settlements</Link>
                  ) : null}
                  {item.kind === "DividendsNotPosted" || item.kind === "DividendDifference" ? (
                    // dividends D101.
                    <Link to="/corporate-actions?kind=Dividend" className={recordLinkClass}>Dividends</Link>
                  ) : null}
                </CardContent>
              </Card>
            ))}
          </Section>
        );
      })}
      <Section title="Information">
        <p className="text-sm">Reserved cash: {amount(run.reservedCash)}</p>
        <p className="text-sm">
          Payments in flight: {run.paymentsInFlight.count} · {amount(run.paymentsInFlight.amount)}
        </p>
        <Muted>{inFlightSentence}</Muted>
        {run.openSettlements ? (
          <p className="text-sm">
            {`Open trade settlements: ${run.openSettlements.count}, net ${amount(run.openSettlements.amount)}`}
          </p>
        ) : null}
      </Section>
    </Page>
  );
}
