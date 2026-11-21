import { Link, useNavigate } from "react-router";
import { Muted, Page, PageHeading, recordLinkClass, StatusBadge } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { runStatusLabel } from "@/features/bankReconciliations/bankReconciliationLabels";
import {
  useGetBankReconciliationOverviewQuery,
  useRunBankReconciliationMutation,
  type ReconciliationOverviewEntry,
} from "@/features/bankReconciliations/bankReconciliationsApi";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { problemStatus } from "@/features/statementLines/statementLineLabels";
import { formatAmount } from "@/shared/format/money";
import { formatInstant } from "@/shared/time/instant";

export const forbiddenSentence = "Only a firm administrator can see bank reconciliations.";

/** D51: the feed is failing when its last failure is after its last success. */
function feedFailingSince(entry: ReconciliationOverviewEntry) {
  const feed = entry.bankFeed;
  if (!feed?.lastFailedAt) {
    return null;
  }
  return !feed.lastSucceededAt || feed.lastFailedAt > feed.lastSucceededAt ? feed.lastFailedAt : null;
}

export function BankReconciliationOverviewPage() {
  const { data, error } = useGetBankReconciliationOverviewQuery();
  const [run, { isLoading: running }] = useRunBankReconciliationMutation();
  const navigate = useNavigate();
  const currencies = useGetCurrenciesQuery();
  const places = (code: string) => currencies.data?.find((item) => item.code === code)?.decimalPlaces;
  const amount = (value: number | null | undefined, code: string) =>
    value === null || value === undefined ? "" : formatAmount(value, places(code));

  async function runNow(clientMoneyAccountId: string) {
    const created = await run({ clientMoneyAccountId }).unwrap();
    navigate(`/bank-reconciliations/${created.id}`);
  }

  return (
    <Page>
      <PageHeading title="Bank reconciliation" />
      {problemStatus(error) === 403 ? (
        <Muted>{forbiddenSentence}</Muted>
      ) : (
        <Card className="gap-0 py-0">
          <CardContent className="px-0">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Currency</TableHead>
                  <TableHead>Bank account</TableHead>
                  <TableHead>Last run</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Bank balance</TableHead>
                  <TableHead>Custodial cash</TableHead>
                  <TableHead>Difference</TableHead>
                  <TableHead>Statement read</TableHead>
                  <TableHead />
                </TableRow>
              </TableHeader>
              <TableBody>
                {(data?.items ?? []).map((entry) => {
                  const account = entry.clientMoneyAccount;
                  const latest = entry.latest;
                  const failing = feedFailingSince(entry);
                  return (
                    <TableRow key={account.id}>
                      <TableCell>{account.currency}</TableCell>
                      <TableCell>
                        {account.bankName} {account.accountNumber}
                        {account.isActive ? null : <span className="ml-2 text-muted-foreground">Inactive</span>}
                      </TableCell>
                      <TableCell>
                        {latest ? (
                          <Link to={`/bank-reconciliations/${latest.id}`} className={recordLinkClass}>
                            {formatInstant(latest.ranAt)}
                          </Link>
                        ) : (
                          ""
                        )}
                      </TableCell>
                      <TableCell>
                        <StatusBadge status={runStatusLabel(latest)} />
                      </TableCell>
                      <TableCell>{amount(latest?.bankBalance, account.currency)}</TableCell>
                      <TableCell>{amount(latest?.custodialCash, account.currency)}</TableCell>
                      <TableCell>{amount(latest?.difference, account.currency)}</TableCell>
                      <TableCell>
                        {latest?.balanceAt ? formatInstant(latest.balanceAt) : ""}
                        {failing ? (
                          <span className="block text-destructive">Bank feed failing since {formatInstant(failing)}</span>
                        ) : null}
                      </TableCell>
                      <TableCell>
                        <Button type="button" variant="outline" size="sm" disabled={running} onClick={() => void runNow(account.id)}>
                          Run now
                        </Button>
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}
    </Page>
  );
}
