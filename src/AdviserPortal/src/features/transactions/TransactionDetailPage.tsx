import { Link, useParams } from "react-router";
import { Muted, Page, PageHeading, recordLinkClass } from "@/components/portal";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import {
  canReverse,
  ReverseTransactionButton,
} from "@/features/transactions/ReverseTransactionButton";
import { useGetTransactionQuery } from "@/features/transactions/transactionsApi";
import { formatAmount, formatMoney } from "@/shared/format/money";

function isNotFound(error: unknown) {
  return typeof error === "object" && error !== null && "status" in error && error.status === 404;
}

export function TransactionDetailPage() {
  const { id } = useParams();
  const { data, error } = useGetTransactionQuery(id ?? "", { skip: !id });
  const currencies = useGetCurrenciesQuery();
  const places = (code: string) => currencies.data?.find((item) => item.code === code)?.decimalPlaces;

  return (
    <Page className="max-w-2xl">
      <PageHeading title="Transaction" />
      {isNotFound(error) ? <Muted>Not found</Muted> : null}
      {!data && !isNotFound(error) ? <Muted>Loading…</Muted> : null}
      {data ? (
        <Card>
          <CardContent className="flex flex-col items-start gap-3">
            <p className="font-mono text-xs break-all">{data.id}</p>
            <Badge variant="secondary">{data.type}</Badge>
            {canReverse(data) ? <ReverseTransactionButton id={data.id} /> : null}
            {data.cash.length > 0 ? (
              <Table aria-label="Cash legs">
                <TableHeader>
                  <TableRow>
                    <TableHead>Account</TableHead>
                    <TableHead>Role</TableHead>
                    <TableHead>Amount</TableHead>
                    <TableHead>Currency</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {data.cash.map((leg, index) => (
                    <TableRow key={`${leg.accountId}-${leg.role}-${index}`}>
                      <TableCell>
                        <Link to={`/accounts/${leg.accountId}`} className={recordLinkClass}>
                          {leg.accountId}
                        </Link>
                      </TableCell>
                      <TableCell>{leg.role}</TableCell>
                      <TableCell>
                        <p>{formatAmount(leg.amount, places(leg.currency))}</p>
                        {leg.fee ? (
                          <p className="text-muted-foreground text-xs">
                            {`${leg.fee.kind} ${formatMoney(leg.fee.amount, leg.fee.currency, places(leg.fee.currency))} on ${formatMoney(leg.fee.basisAmount, leg.fee.currency, places(leg.fee.currency))}`}{" "}
                            <Link to={`/fees/trade/${leg.fee.scheduleId}`} className={recordLinkClass}>
                              View schedule
                            </Link>
                          </p>
                        ) : null}
                      </TableCell>
                      <TableCell>{leg.currency}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            ) : null}
          </CardContent>
        </Card>
      ) : null}
    </Page>
  );
}
