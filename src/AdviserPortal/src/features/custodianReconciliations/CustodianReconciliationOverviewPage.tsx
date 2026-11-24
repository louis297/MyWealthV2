import { Link, useNavigate } from "react-router";
import { Muted, Page, PageHeading, recordLinkClass, StatusBadge } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import {
  custodianFeedLabel,
  custodianForbiddenSentence,
  custodianRunStatusLabel,
  noSafekeepingSentence,
} from "@/features/custodianReconciliations/custodianReconciliationLabels";
import {
  useGetCustodianReconciliationOverviewQuery,
  useRunCustodianReconciliationMutation,
} from "@/features/custodianReconciliations/custodianReconciliationsApi";
import { problemStatus } from "@/features/statementLines/statementLineLabels";
import { formatInstant } from "@/shared/time/instant";

/** D91: each safekeeping account of the firm with its latest custodian reconciliation, and Run now. */
export function CustodianReconciliationOverviewPage() {
  const { data, error } = useGetCustodianReconciliationOverviewQuery();
  const [run, { isLoading: running }] = useRunCustodianReconciliationMutation();
  const navigate = useNavigate();

  async function runNow(safekeepingAccountId: string) {
    const created = await run({ safekeepingAccountId }).unwrap();
    void navigate(`/custodian-reconciliations/${created.id}`);
  }

  return (
    <Page>
      <PageHeading title="Custodian reconciliation" />
      {problemStatus(error) === 403 ? <Muted>{custodianForbiddenSentence}</Muted> : null}
      {data && data.items.length === 0 ? <Muted>{noSafekeepingSentence}</Muted> : null}
      {data && data.items.length > 0 ? (
        <Card className="gap-0 py-0">
          <CardContent className="px-0">
            <Table aria-label="Safekeeping accounts">
              <TableHeader>
                <TableRow>
                  <TableHead>Currency</TableHead>
                  <TableHead>Custodian</TableHead>
                  <TableHead>Account number</TableHead>
                  <TableHead>Last run</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Feed</TableHead>
                  <TableHead />
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.items.map((entry) => {
                  const account = entry.safekeepingAccount;
                  const latest = entry.latest;
                  return (
                    <TableRow key={account.id}>
                      <TableCell>{account.currency}</TableCell>
                      <TableCell>{account.custodianName}</TableCell>
                      <TableCell>
                        {account.accountNumber}
                        {account.isActive ? null : <span className="ml-2 text-muted-foreground">Inactive</span>}
                      </TableCell>
                      <TableCell>
                        {latest ? (
                          <Link to={`/custodian-reconciliations/${latest.id}`} className={recordLinkClass}>
                            {formatInstant(latest.ranAt)}
                          </Link>
                        ) : null}
                        {latest?.positionsAt ? (
                          <span className="block text-muted-foreground text-xs">{`Positions as at ${formatInstant(latest.positionsAt)}`}</span>
                        ) : null}
                      </TableCell>
                      <TableCell>
                        {latest ? <StatusBadge status={custodianRunStatusLabel(latest)} /> : <Muted>No positions yet</Muted>}
                      </TableCell>
                      <TableCell>{custodianFeedLabel(entry.custodianFeed, formatInstant)}</TableCell>
                      <TableCell>
                        {account.isActive ? (
                          <Button type="button" variant="outline" size="sm" disabled={running} onClick={() => void runNow(account.id)}>
                            Run now
                          </Button>
                        ) : null}
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      ) : null}
    </Page>
  );
}
