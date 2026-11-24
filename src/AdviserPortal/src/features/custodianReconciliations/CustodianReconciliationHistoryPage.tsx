import { Link, useSearchParams } from "react-router";
import { Page, PageHeading, recordLinkClass, StatusBadge } from "@/components/portal";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { custodianRunStatusLabel } from "@/features/custodianReconciliations/custodianReconciliationLabels";
import { useGetCustodianReconciliationsQuery } from "@/features/custodianReconciliations/custodianReconciliationsApi";
import { Pager } from "@/shared/components/Pager";
import { formatInstant } from "@/shared/time/instant";

/** D92: the runs of one safekeeping account, newest first. */
export function CustodianReconciliationHistoryPage() {
  const [params, setParams] = useSearchParams();
  const safekeepingAccountId = params.get("safekeepingAccountId") ?? undefined;
  const page = Number(params.get("page") ?? "1") || 1;
  const { data } = useGetCustodianReconciliationsQuery({ safekeepingAccountId, page });

  return (
    <Page>
      <PageHeading title="Custodian reconciliation history" />
      <Card className="gap-0 py-0">
        <CardContent className="px-0">
          <Table aria-label="Runs">
            <TableHeader>
              <TableRow>
                <TableHead>Ran at</TableHead>
                <TableHead>Account number</TableHead>
                <TableHead>By</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Positions as at</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {(data?.items ?? []).map((run) => (
                <TableRow key={run.id}>
                  <TableCell>
                    <Link to={`/custodian-reconciliations/${run.id}`} className={recordLinkClass}>{formatInstant(run.ranAt)}</Link>
                  </TableCell>
                  <TableCell>{run.safekeepingAccount.accountNumber}</TableCell>
                  <TableCell>{run.ranBy ? run.ranBy.name : "Daily run"}</TableCell>
                  <TableCell>
                    <StatusBadge status={custodianRunStatusLabel(run)} />
                  </TableCell>
                  <TableCell>{run.positionsAt ? formatInstant(run.positionsAt) : "—"}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
      <Pager page={page} pageSize={20} totalCount={data?.totalCount ?? 0}
        onPage={(next) => { const p = new URLSearchParams(params); p.set("page", String(next)); setParams(p); }} />
    </Page>
  );
}
