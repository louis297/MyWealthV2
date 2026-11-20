import { useParams } from "react-router";
import { DetailGrid, DetailItem, Muted, Page, PageHeading, Section, StatusBadge } from "@/components/portal";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import {
  type Activity,
  useGetAccountQuery,
  useGetActivityQuery,
  useGetCashBalanceQuery,
  useGetDepositInstructionsQuery,
  useGetHoldingsQuery,
} from "@/features/accounts/accountsApi";
import { custodyLabel, externalNote } from "@/features/accounts/custodyLabel";
import { formatInstant } from "@/shared/time/instant";

const typeLabels: Record<string, string> = {
  InternalTransfer: "Transfer",
  FxConversion: "Currency exchange",
  Adjustment: "Adjustment",
};

function typeLabel(row: Activity) {
  // C21: a deposit from the bank statement is a TransferIn with a bank receipt; its Reversal stays "Reversal".
  if (row.type === "TransferIn" && row.bankReceipt) {
    return "Deposit";
  }

  return typeLabels[row.type] ?? row.type;
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
  // C19, C20: only an Open Custodial account has deposit instructions; nothing is called for any other.
  const payable = account.data?.custodyMode === "Custodial" && account.data.status === "Open";
  const instructions = useGetDepositInstructionsQuery(id, { skip: !payable });

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
            <DetailItem label="Custody">
              {custodyLabel(account.data.custodyMode)}
              {account.data.custodyMode === "External" ? (
                <span className="mt-1 block text-xs text-muted-foreground">{externalNote}</span>
              ) : null}
            </DetailItem>
            {cash.data ? (
              <DetailItem label="Cash balance">
                {cash.data.cashBalance} {cash.data.currency}
              </DetailItem>
            ) : null}
          </DetailGrid>
        </CardContent>
      </Card>
      {payable ? (
        <Section title="How to pay in">
          {instructions.data ? (
            <Card>
              <CardContent className="flex flex-col gap-3">
                <DetailGrid>
                  <DetailItem label="Bank">{instructions.data.bankName}</DetailItem>
                  <DetailItem label="Account name">{instructions.data.accountName}</DetailItem>
                  <DetailItem label="Account number">{instructions.data.accountNumber}</DetailItem>
                  <DetailItem label="Reference">{instructions.data.reference}</DetailItem>
                </DetailGrid>
                <p className="text-sm">
                  {`Pay in ${instructions.data.currency} only, and quote the reference exactly so the payment is matched to this account.`}
                </p>
              </CardContent>
            </Card>
          ) : instructions.isError ? (
            <Muted>Deposit instructions are not available yet. Contact your adviser.</Muted>
          ) : (
            <Muted>Loading…</Muted>
          )}
        </Section>
      ) : null}
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
                      <TableCell>
                        {typeLabel(row)}
                        {row.type === "TransferIn" && row.bankReceipt ? (
                          <p className="text-muted-foreground text-xs">{`Value date ${row.bankReceipt.valueDate}`}</p>
                        ) : null}
                        {/* C18: an adjustment shows its memo, which the TenantAdmin wrote for the customer; not its reason. */}
                        {row.type === "Adjustment" && row.memo ? (
                          <p className="text-muted-foreground text-xs">{row.memo}</p>
                        ) : null}
                      </TableCell>
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
