import { Link, useSearchParams } from "react-router";
import { FilterBar, Muted, Page, PageHeading, recordLinkClass, TextField } from "@/components/portal";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import {
  canReverse,
  ReverseTransactionButton,
} from "@/features/transactions/ReverseTransactionButton";
import {
  useGetTransactionsQuery,
  type TransactionCash,
  type TransactionSecurity,
} from "@/features/transactions/transactionsApi";
import { Pager } from "@/shared/components/Pager";

function isNotFound(error: unknown) {
  return typeof error === "object" && error !== null && "status" in error && error.status === 404;
}

function cashText(cash: TransactionCash | null | undefined) {
  if (!cash) {
    return "";
  }
  return `${cash.amount} ${cash.currency}`;
}

function securityText(security: TransactionSecurity[] | null | undefined) {
  const leg = security?.[0];
  if (!leg) {
    return "";
  }
  return `${leg.quantity} ${leg.cost} ${leg.costCurrency}`;
}

export function TransactionsListPage() {
  const [params, setParams] = useSearchParams();
  const page = Number(params.get("page") || "1") || 1;
  const accountId = params.get("accountId") ?? "";
  const customerId = params.get("customerId") ?? "";
  const { data, error } = useGetTransactionsQuery({
    page,
    accountId: accountId || undefined,
    customerId: customerId || undefined,
  });
  const notFound = isNotFound(error);

  function updateParam(key: string, value: string | null) {
    const updated = new URLSearchParams(params);
    if (value) {
      updated.set(key, value);
    } else {
      updated.delete(key);
    }
    if (key !== "page") {
      updated.set("page", "1");
    }
    setParams(updated);
  }

  return (
    <Page>
      <PageHeading title="Transactions" />
      <FilterBar>
        <TextField
          id="transactions-account"
          label="Account"
          className="w-72"
          value={accountId}
          onChange={(event) => updateParam("accountId", event.target.value || null)}
        />
        <TextField
          id="transactions-customer"
          label="Customer"
          className="w-72"
          value={customerId}
          onChange={(event) => updateParam("customerId", event.target.value || null)}
        />
      </FilterBar>
      {notFound ? <Muted>Not found</Muted> : (
        <Card className="gap-0 py-0">
          <CardContent className="px-0">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Type</TableHead>
                  <TableHead>Booked at</TableHead>
                  <TableHead>Account</TableHead>
                  <TableHead>Customer</TableHead>
                  <TableHead>Cash</TableHead>
                  <TableHead>Security</TableHead>
                  <TableHead>Memo</TableHead>
                  <TableHead>Reverse</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {(data?.items ?? []).map((item) => (
                  <TableRow key={item.id}>
                    <TableCell>
                      <Link to={`/transactions/${item.id}`} className={recordLinkClass}>
                        {item.type}
                      </Link>
                    </TableCell>
                    <TableCell>{item.bookedAt}</TableCell>
                    <TableCell>
                      <Link to={`/accounts/${item.accountId}`} className={recordLinkClass}>
                        {item.accountId}
                      </Link>
                    </TableCell>
                    <TableCell>
                      <Link to={`/customers/${item.customerId}`} className={recordLinkClass}>
                        {item.customerId}
                      </Link>
                    </TableCell>
                    <TableCell>{cashText(item.cash)}</TableCell>
                    <TableCell>{securityText(item.security)}</TableCell>
                    <TableCell>{item.memo ?? ""}</TableCell>
                    <TableCell className="whitespace-normal">
                      {canReverse(item) ? <ReverseTransactionButton id={item.id} /> : null}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}
      <Pager
        page={page}
        pageSize={20}
        totalCount={data?.totalCount ?? 0}
        onPage={(next) => updateParam("page", String(next))}
      />
    </Page>
  );
}
