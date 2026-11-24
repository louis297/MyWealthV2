import { useState } from "react";
import { Link, useSearchParams } from "react-router";
import { FilterBar, Page, PageHeading, recordLinkClass, SelectField } from "@/components/portal";
import { buttonVariants } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { SelectItem } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useAppSelector } from "@/app/hooks";
import { useGetCorporateActionsQuery } from "@/features/corporateActions/corporateActionsApi";
import { dividendStatusLabel, kindLabel, originLabel, ratioText, statusLabel } from "@/features/corporateActions/corporateActionText";
import { canManageCorporateActions } from "@/features/session/roles";
import { Pager } from "@/shared/components/Pager";
import { cn } from "cn";

const statuses = ["Announced", "Effective", "Applied", "Cancelled"];
const kinds = ["Split", "Consolidation", "Bonus", "Dividend"];

/** D64: the firm's corporate actions, latest ex-date first, with a status filter. */
export function CorporateActionsPage() {
  const role = useAppSelector((state) => state.session.currentUser?.role);
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState("All");
  // dividends D101: the bank reconciliation links here with ?kind=Dividend.
  const [searchParams] = useSearchParams();
  const [kind, setKind] = useState(() => {
    const initial = searchParams.get("kind");
    return initial && kinds.includes(initial) ? initial : "All";
  });
  const { data } = useGetCorporateActionsQuery({
    page,
    status: status === "All" ? undefined : status,
    kind: kind === "All" ? undefined : kind,
  });

  return (
    <Page>
      <PageHeading
        title="Corporate actions"
        actions={canManageCorporateActions(role) ? (
          <Link to="/corporate-actions/new" className={cn(buttonVariants({ size: "sm" }))}>New corporate action</Link>
        ) : undefined}
      />
      <FilterBar>
        <SelectField id="corporate-action-status" label="Status" value={status}
          onChange={(event) => { setStatus(event.target.value); setPage(1); }}>
          <SelectItem value="All">All</SelectItem>
          {statuses.map((item) => <SelectItem key={item} value={item}>{statusLabel(item)}</SelectItem>)}
        </SelectField>
        {/* dividends D94: a kind filter. */}
        <SelectField id="corporate-action-kind" label="Kind" value={kind}
          onChange={(event) => { setKind(event.target.value); setPage(1); }}>
          <SelectItem value="All">All</SelectItem>
          {kinds.map((item) => <SelectItem key={item} value={item}>{kindLabel(item)}</SelectItem>)}
        </SelectField>
      </FilterBar>
      <Card className="gap-0 py-0">
        <CardContent className="px-0">
          <Table aria-label="Corporate actions">
            <TableHeader>
              <TableRow>
                <TableHead>Instrument</TableHead>
                <TableHead>Kind</TableHead>
                <TableHead>Ratio</TableHead>
                <TableHead>Ex-date</TableHead>
                <TableHead>Source</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Accounts</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {(data?.items ?? []).map((item) => (
                <TableRow key={item.id}>
                  <TableCell>
                    <Link to={`/corporate-actions/${item.id}`} className={recordLinkClass}>{item.instrument.symbol}</Link>
                    <p className="text-muted-foreground text-xs">{item.instrument.name}</p>
                  </TableCell>
                  <TableCell>{kindLabel(item.kind)}</TableCell>
                  <TableCell>{ratioText(item)}</TableCell>
                  <TableCell>{item.exDate}</TableCell>
                  <TableCell>{originLabel(item.origin)}</TableCell>
                  <TableCell>
                    {item.kind === "Dividend" ? dividendStatusLabel(item.status, item.payment?.paidAt) : statusLabel(item.status)}
                  </TableCell>
                  <TableCell>
                    <p>{`${item.entitlements.posted} of ${item.entitlements.total}`}</p>
                    {item.entitlements.notApplied > 0 ? (
                      <p className="text-xs text-amber-600">{`${item.entitlements.notApplied} not applied`}</p>
                    ) : null}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
      <Pager page={page} pageSize={20} totalCount={data?.totalCount ?? 0} onPage={setPage} />
    </Page>
  );
}
