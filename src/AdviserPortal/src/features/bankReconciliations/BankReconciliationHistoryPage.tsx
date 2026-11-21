import { Link, useSearchParams } from "react-router";
import { FilterBar, Muted, Page, PageHeading, recordLinkClass, SelectField, StatusBadge } from "@/components/portal";
import { Card, CardContent } from "@/components/ui/card";
import { SelectItem } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { runStatusLabel } from "@/features/bankReconciliations/bankReconciliationLabels";
import { useGetBankReconciliationsQuery } from "@/features/bankReconciliations/bankReconciliationsApi";
import { forbiddenSentence } from "@/features/bankReconciliations/BankReconciliationOverviewPage";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { problemStatus } from "@/features/statementLines/statementLineLabels";
import { Pager } from "@/shared/components/Pager";
import { formatAmount } from "@/shared/format/money";
import { formatInstant } from "@/shared/time/instant";

/** D51: the status filter of the history. */
const statuses: Record<string, string> = {
  All: "All",
  Agreed: "Agreed",
  Breaks: "Breaks",
  NoBankBalance: "No bank balance yet",
};

export function BankReconciliationHistoryPage() {
  const [params, setParams] = useSearchParams();
  const clientMoneyAccountId = params.get("clientMoneyAccountId") ?? undefined;
  const status = statuses[params.get("status") ?? ""] ? params.get("status")! : "All";
  const page = Number(params.get("page") ?? "1") || 1;
  const { data, error } = useGetBankReconciliationsQuery({
    clientMoneyAccountId,
    status: status === "All" ? undefined : status,
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
      <PageHeading title="Bank reconciliation history" />
      {problemStatus(error) === 403 ? (
        <Muted>{forbiddenSentence}</Muted>
      ) : (
        <>
          <FilterBar>
            <SelectField
              id="bank-reconciliations-status"
              label="Status"
              className="w-56"
              value={status}
              onChange={(event) => update("status", event.target.value || null)}
            >
              {Object.entries(statuses).map(([value, label]) => (
                <SelectItem key={value} value={value}>
                  {label}
                </SelectItem>
              ))}
            </SelectField>
          </FilterBar>
          <Card className="gap-0 py-0">
            <CardContent className="px-0">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Ran at</TableHead>
                    <TableHead>Bank account</TableHead>
                    <TableHead>By</TableHead>
                    <TableHead>Status</TableHead>
                    <TableHead>Difference</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {(data?.items ?? []).map((item) => (
                    <TableRow key={item.id}>
                      <TableCell>
                        <Link to={`/bank-reconciliations/${item.id}`} className={recordLinkClass}>
                          {formatInstant(item.ranAt)}
                        </Link>
                      </TableCell>
                      <TableCell>
                        {item.clientMoneyAccount.currency} {item.clientMoneyAccount.accountNumber}
                      </TableCell>
                      <TableCell>{item.ranBy ? item.ranBy.name : "Daily run"}</TableCell>
                      <TableCell>
                        <StatusBadge status={runStatusLabel(item)} />
                      </TableCell>
                      <TableCell>
                        {item.difference === null ? "" : formatAmount(item.difference, places(item.clientMoneyAccount.currency))}
                      </TableCell>
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
