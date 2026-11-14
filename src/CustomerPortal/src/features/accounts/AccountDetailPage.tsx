import { useParams } from "react-router";
import { DetailGrid, DetailItem, Muted, Page, PageHeading, Section, StatusBadge } from "@/components/portal";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import {
  useGetAccountQuery,
  useGetActivityQuery,
  useGetCashBalanceQuery,
  useGetHoldingsQuery,
} from "@/features/accounts/accountsApi";
import { formatInstant } from "@/shared/time/instant";

const typeLabels: Record<string, string> = {
  InternalTransfer: "Transfer",
  FxConversion: "Currency exchange",
};

function typeLabel(type: string) {
  return typeLabels[type] ?? type;
}

function isNotFound(error: unknown) {
  return typeof error === "object" && error !== null && "status" in error && error.status === 404;
}

export function AccountDetailPage() {
  const { id = "" } = useParams();
  const account = useGetAccountQuery(id);
  const cash = useGetCashBalanceQuery(id, { skip: account.isError });
  const holdings = useGetHoldingsQuery(id, { skip: account.isError });
  const activity = useGetActivityQuery(id, { skip: account.isError });

  if (isNotFound(account.error)) {
    return <Muted>Not found</Muted>;
  }

  if (account.isLoading || !account.data) {
    return <Muted>Loading session…</Muted>;
  }

  const holdingsItems = holdings.data?.items ?? [];
  const activityItems = activity.data?.items ?? [];

  return (
    <Page>
      <PageHeading title={account.data.name} />
      <Card>
        <CardContent>
          <DetailGrid>
            <DetailItem label="Status">
              <StatusBadge status={account.data.status} />
            </DetailItem>
            <DetailItem label="Type">{account.data.type}</DetailItem>
            {cash.data ? (
              <DetailItem label="Cash balance">
                {cash.data.cashBalance} {cash.data.currency}
              </DetailItem>
            ) : null}
          </DetailGrid>
        </CardContent>
      </Card>
      <Section title="Holdings">
        {holdings.data && holdingsItems.length === 0 ? <Muted>No holdings.</Muted> : null}
        {holdingsItems.length > 0 ? (
          <Card className="gap-0 py-0">
            <CardContent className="px-0">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Symbol</TableHead>
                    <TableHead>Quantity</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {holdingsItems.map((holding) => (
                    <TableRow key={holding.id}>
                      <TableCell>{holding.symbol}</TableCell>
                      <TableCell>{holding.quantity}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        ) : null}
      </Section>
      <Section title="Activity">
        {activity.data && activityItems.length === 0 ? <Muted>No activity.</Muted> : null}
        {activityItems.length > 0 ? (
          <Card className="gap-0 py-0">
            <CardContent className="px-0">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Type</TableHead>
                    <TableHead>Booked at</TableHead>
                    <TableHead>Amount</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {activityItems.map((row) => (
                    <TableRow key={row.id}>
                      <TableCell>{typeLabel(row.type)}</TableCell>
                      <TableCell>{formatInstant(row.bookedAt)}</TableCell>
                      <TableCell>
                        {(row.cash ?? [])
                          .filter((leg) => leg.accountId === id)
                          .map((leg) => `${leg.amount} ${leg.currency}`)
                          .join(", ")}
                        {row.fx ? (
                          <p className="text-muted-foreground text-xs">
                            {`1 ${row.fx.baseCurrency} = ${Number(row.fx.rate)} ${row.fx.quoteCurrency} (as of ${row.fx.rateAsOfDate})`}
                          </p>
                        ) : null}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        ) : null}
      </Section>
    </Page>
  );
}
