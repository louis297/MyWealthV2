import { Link, useSearchParams } from "react-router";
import { FilterBar, Muted, Page, PageHeading, recordLinkClass, TextField } from "@/components/portal";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import {
  canReverse,
  ReverseTransactionButton,
  transactionTypeLabel,
} from "@/features/transactions/ReverseTransactionButton";
import {
  useGetTransactionsQuery,
  type Transaction,
  type TransactionCash,
  type TransactionSecurity,
} from "@/features/transactions/transactionsApi";
import { shortDay } from "@/features/orders/orderLabels";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { formatMoney, sumAmounts } from "@/shared/format/money";
import { Pager } from "@/shared/components/Pager";
import { formatInstant } from "@/shared/time/instant";

function isNotFound(error: unknown) {
  return typeof error === "object" && error !== null && "status" in error && error.status === 404;
}

/** One entry per visible cash leg; a `Fee` leg is labelled "fee" (adviser-portal-fees F13), a `Tax` leg "tax" (D61). */
function cashText(cash: TransactionCash[]) {
  return cash
    .map((leg) => `${leg.role === "Fee" ? "fee " : leg.role === "Tax" ? "tax " : ""}${leg.amount} ${leg.currency}`)
    .join(", ");
}

/**
 * interest-run D61, external-income D113: a row with `Tax` legs shows its net, and the tax withheld under it, as a fee
 * is shown on the account page.
 */
function TaxedCash({ cash, places }: { cash: TransactionCash[]; places: (code: string) => number | undefined }) {
  const currency = cash[0]?.currency ?? "";
  const tax = Math.abs(sumAmounts(cash.filter((leg) => leg.role === "Tax").map((leg) => leg.amount)));
  return (
    <>
      {formatMoney(sumAmounts(cash.map((leg) => leg.amount)), currency, places(currency))}
      <p className="text-muted-foreground text-xs">{`incl. tax ${formatMoney(tax, currency, places(currency))}`}</p>
    </>
  );
}

function securityText(security: TransactionSecurity[]) {
  return security.map((leg) => `${leg.quantity} ${leg.cost} ${leg.costCurrency}`).join(", ");
}

/** Distinct leg accounts and customers, in leg order. */
function legOwners(item: Transaction) {
  const legs = [...item.cash, ...item.security];
  return {
    accountIds: [...new Set(legs.map((leg) => leg.accountId))],
    customerIds: [...new Set(legs.map((leg) => leg.customerId))],
  };
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
  const currencies = useGetCurrenciesQuery();
  const places = (code: string) => currencies.data?.find((item) => item.code === code)?.decimalPlaces;

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
                {(data?.items ?? []).map((item) => {
                  const owners = legOwners(item);
                  return (
                    <TableRow key={item.id}>
                      <TableCell>
                        <Link to={`/transactions/${item.id}`} className={recordLinkClass}>
                          {item.corporateAction || item.dividend ? transactionTypeLabel(item) : item.type}
                        </Link>
                      </TableCell>
                      <TableCell>{formatInstant(item.bookedAt)}</TableCell>
                      <TableCell>
                        <div className="flex flex-col">
                          {owners.accountIds.map((accountId) => (
                            <Link key={accountId} to={`/accounts/${accountId}`} className={recordLinkClass}>
                              {accountId}
                            </Link>
                          ))}
                        </div>
                      </TableCell>
                      <TableCell>
                        <div className="flex flex-col">
                          {owners.customerIds.map((customerId) => (
                            <Link key={customerId} to={`/customers/${customerId}`} className={recordLinkClass}>
                              {customerId}
                            </Link>
                          ))}
                        </div>
                      </TableCell>
                      <TableCell>
                        {item.cash.some((leg) => leg.role === "Tax")
                          ? <TaxedCash cash={item.cash} places={places} />
                          : cashText(item.cash)}
                        {/* D74: a fill that has not settled. */}
                        {item.execution && !item.execution.settledAt ? (
                          <p className="text-muted-foreground text-xs">{`Settles ${shortDay(item.execution.settlementDate)}`}</p>
                        ) : null}
                      </TableCell>
                      <TableCell>{securityText(item.security)}</TableCell>
                      <TableCell>{item.memo ?? ""}</TableCell>
                      <TableCell className="whitespace-normal">
                        {canReverse(item) ? <ReverseTransactionButton id={item.id} /> : null}
                      </TableCell>
                    </TableRow>
                  );
                })}
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
