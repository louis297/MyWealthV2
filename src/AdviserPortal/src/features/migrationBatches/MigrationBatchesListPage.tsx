import { Link, useSearchParams } from "react-router";
import { FilterBar, Muted, Page, PageHeading, recordLinkClass, SelectField, StatusBadge } from "@/components/portal";
import { buttonVariants } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { SelectItem } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { forbiddenSentence, migrationBatchFilters, migrationBatchStatusLabel } from "@/features/migrationBatches/migrationBatchLabels";
import { useGetMigrationBatchesQuery } from "@/features/migrationBatches/migrationBatchesApi";
import { problemStatus } from "@/features/statementLines/statementLineLabels";
import { cn } from "@/lib/utils";
import { Pager } from "@/shared/components/Pager";
import { formatInstant } from "@/shared/time/instant";

/** D80: the migration batches of the firm, the Draft and Submitted ones by default. */
export function MigrationBatchesListPage() {
  const [params, setParams] = useSearchParams();
  const requested = params.get("status") ?? "";
  const filter = migrationBatchFilters[requested] ? requested : "Open";
  const page = Number(params.get("page") ?? "1") || 1;
  const { data, error } = useGetMigrationBatchesQuery({ status: migrationBatchFilters[filter].status, page });

  function update(key: string, value: string | null) {
    const next = new URLSearchParams(params);
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

  if (problemStatus(error) === 403) {
    return (
      <Page>
        <PageHeading title="Migration batches" />
        <Muted>{forbiddenSentence}</Muted>
      </Page>
    );
  }

  const items = data?.items ?? [];
  return (
    <Page>
      <PageHeading
        title="Migration batches"
        actions={<Link to="/migration-batches/new" className={cn(buttonVariants({ size: "sm" }))}>New batch</Link>}
      />
      <FilterBar>
        <SelectField id="migration-batches-status" label="Status" className="w-64" value={filter}
          onChange={(event) => update("status", event.target.value || null)}>
          {Object.entries(migrationBatchFilters).map(([value, item]) => (
            <SelectItem key={value} value={value}>
              {item.label}
            </SelectItem>
          ))}
        </SelectField>
      </FilterBar>
      {items.length === 0 ? <Muted>No migration batches.</Muted> : (
        <Card className="gap-0 py-0">
          <CardContent className="px-0">
            <Table aria-label="Migration batches">
              <TableHeader>
                <TableRow>
                  <TableHead>Name</TableHead>
                  <TableHead>Previous provider</TableHead>
                  <TableHead>As at</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Accounts</TableHead>
                  <TableHead>Holdings</TableHead>
                  <TableHead>Created</TableHead>
                  <TableHead>Submitted by</TableHead>
                  <TableHead>Posted</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {items.map((item) => (
                  <TableRow key={item.id}>
                    <TableCell>
                      <Link to={`/migration-batches/${item.id}`} className={recordLinkClass}>{item.name}</Link>
                    </TableCell>
                    <TableCell>{item.previousProvider}</TableCell>
                    <TableCell>{item.asOfDate}</TableCell>
                    <TableCell><StatusBadge status={migrationBatchStatusLabel(item.status)} /></TableCell>
                    <TableCell>{item.entryCount}</TableCell>
                    <TableCell>{item.holdingCount}</TableCell>
                    <TableCell>{formatInstant(item.createdAt)}</TableCell>
                    <TableCell>{item.submittedBy?.name ?? "—"}</TableCell>
                    <TableCell>{item.postedAt ? formatInstant(item.postedAt) : "—"}</TableCell>
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
