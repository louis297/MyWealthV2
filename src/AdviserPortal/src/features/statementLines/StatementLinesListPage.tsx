import { Link, useSearchParams } from "react-router";
import { FilterBar, Muted, Page, PageHeading, recordLinkClass, SelectField, StatusBadge } from "@/components/portal";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent } from "@/components/ui/card";
import { SelectItem } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { problemStatus, reasonLabel, statusLabel } from "@/features/statementLines/statementLineLabels";
import { useGetStatementLinesQuery } from "@/features/statementLines/statementLinesApi";
import { Pager } from "@/shared/components/Pager";
import { formatAmount } from "@/shared/format/money";
import { formatInstant } from "@/shared/time/instant";

/** D34, D50: the filter values; All sends every status but Received, which is not shown. */
const filters: Record<string, { label: string; status: string }> = {
  Unmatched: { label: "Unmatched", status: "Unmatched" },
  Matched: { label: "Matched", status: "Matched" },
  Ignored: { label: "Ignored", status: "Ignored" },
  ReturnRequested: { label: "To be returned", status: "ReturnRequested" },
  ReturnSent: { label: "Return sent", status: "ReturnSent" },
  Returned: { label: "Returned", status: "Returned" },
  All: { label: "All", status: "Unmatched,Matched,Ignored,ReturnRequested,ReturnSent,Returned" },
};

/** D49: receipts by default; payments out; or both. */
const directions: Record<string, { label: string; direction?: string }> = {
  Credit: { label: "Receipts", direction: "Credit" },
  Debit: { label: "Payments out", direction: "Debit" },
  All: { label: "All" },
};

/** D63: transfers, the bank's interest, or both. */
const kinds: Record<string, { label: string; kind?: string }> = {
  All: { label: "All" },
  Transfer: { label: "Payments and receipts", kind: "Transfer" },
  Interest: { label: "Bank interest", kind: "Interest" },
};

export const forbiddenSentence = "Only a firm administrator can see bank receipts.";

export function StatementLinesListPage() {
  const [params, setParams] = useSearchParams();
  const filter = filters[params.get("status") ?? ""] ? params.get("status")! : "Unmatched";
  const direction = directions[params.get("direction") ?? ""] ? params.get("direction")! : "Credit";
  const kind = kinds[params.get("kind") ?? ""] ? params.get("kind")! : "All";
  const page = Number(params.get("page") ?? "1") || 1;
  const { data, error } = useGetStatementLinesQuery({
    status: filters[filter].status,
    direction: directions[direction].direction,
    kind: kinds[kind].kind,
    page,
  });
  const currencies = useGetCurrenciesQuery();
  const places = (code: string) => currencies.data?.find((item) => item.code === code)?.decimalPlaces;

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

  return (
    <Page>
      <PageHeading title="Bank receipts" />
      {problemStatus(error) === 403 ? (
        <Muted>{forbiddenSentence}</Muted>
      ) : (
        <>
          <FilterBar>
            <SelectField
              id="statement-lines-status"
              label="Status"
              className="w-56"
              value={filter}
              onChange={(event) => update("status", event.target.value || null)}
            >
              {Object.entries(filters).map(([value, item]) => (
                <SelectItem key={value} value={value}>
                  {item.label}
                </SelectItem>
              ))}
            </SelectField>
            <SelectField
              id="statement-lines-direction"
              label="Direction"
              className="w-48"
              value={direction}
              onChange={(event) => update("direction", event.target.value || null)}
            >
              {Object.entries(directions).map(([value, item]) => (
                <SelectItem key={value} value={value}>
                  {item.label}
                </SelectItem>
              ))}
            </SelectField>
            <SelectField
              id="statement-lines-kind"
              label="Kind"
              className="w-56"
              value={kind}
              onChange={(event) => update("kind", event.target.value || null)}
            >
              {Object.entries(kinds).map(([value, item]) => (
                <SelectItem key={value} value={value}>
                  {item.label}
                </SelectItem>
              ))}
            </SelectField>
          </FilterBar>
          <Card className="gap-0 py-0">
            <CardContent className="px-0">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Value date</TableHead>
                    <TableHead>Received</TableHead>
                    <TableHead>Amount</TableHead>
                    <TableHead>Currency</TableHead>
                    <TableHead>Payer</TableHead>
                    <TableHead>Reference</TableHead>
                    <TableHead>Status</TableHead>
                    <TableHead>Reason</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {(data?.items ?? []).map((item) => (
                    <TableRow key={item.id}>
                      <TableCell>
                        <Link to={`/statement-lines/${item.id}`} className={recordLinkClass}>
                          {item.valueDate}
                        </Link>
                      </TableCell>
                      <TableCell>{formatInstant(item.receivedAt)}</TableCell>
                      <TableCell>{formatAmount(item.amount, places(item.currency))}</TableCell>
                      <TableCell>{item.currency}</TableCell>
                      <TableCell>{item.payerName ?? ""}</TableCell>
                      <TableCell>
                        {item.reference ?? ""}
                        {item.kind === "Interest" ? (
                          // D63: the bank's interest on the trust account.
                          <Badge variant="secondary" className="ml-2">Bank interest</Badge>
                        ) : null}
                      </TableCell>
                      <TableCell>
                        <StatusBadge status={statusLabel(item.status)} />
                      </TableCell>
                      <TableCell>{item.status === "Unmatched" ? reasonLabel(item.unmatchedReason) : ""}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
          <Pager page={page} pageSize={20} totalCount={data?.totalCount ?? 0} onPage={(next) => update("page", String(next))} />
        </>
      )}
    </Page>
  );
}
