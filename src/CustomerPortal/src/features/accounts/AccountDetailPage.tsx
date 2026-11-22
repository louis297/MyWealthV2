import { useParams } from "react-router";
import { DetailGrid, DetailItem, Muted, Page, PageHeading, Section, StatusBadge } from "@/components/portal";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import {
  type Activity,
  useGetAccountQuery,
  useGetAccountWithdrawalsQuery,
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

  // C25: a payout of a withdrawal is a TransferOut with a payment; its Reversal stays "Reversal".
  if (row.type === "TransferOut" && row.payment) {
    return "Withdrawal";
  }

  // C27: the remainder an account closure writes off; a CloseOut with another source keeps its label.
  if (row.type === "CloseOut" && row.source?.kind === "AccountClosure") {
    return "Rounding write-off";
  }

  return typeLabels[row.type] ?? row.type;
}

/** C24: the status words a Customer reads. */
function withdrawalStatusLabel(status: string) {
  switch (status) {
    case "Requested":
      return "Waiting for approval";
    case "Sent":
      return "Sent to bank";
    case "Failed":
      return "Bank refused";
    case "Returned":
      return "Returned by bank";
    default:
      return status;
  }
}

const openStatuses = new Set(["Requested", "Approved", "Sent"]);
const ninetyDays = 90 * 24 * 60 * 60 * 1000;

/** C26: an amount with the currency's minor unit, as "300.00 USD". */
function formatPayout(amount: number, currency: string) {
  let digits = 2;
  try {
    digits = new Intl.NumberFormat("en-US", { style: "currency", currency }).resolvedOptions().minimumFractionDigits ?? 2;
  } catch {
    // An unknown code keeps two decimals.
  }
  return `${new Intl.NumberFormat("en-US", { minimumFractionDigits: digits, maximumFractionDigits: 4 }).format(amount)} ${currency}`;
}

/** C29: an amount rounded down to the currency's minor unit, as "4.52 USD". */
function formatDown(amount: number, currency: string) {
  const digits = minorDigits(currency);
  const factor = 10 ** digits;
  const down = Math.floor(amount * factor + 1e-9) / factor;
  return `${new Intl.NumberFormat("en-US", { minimumFractionDigits: digits, maximumFractionDigits: digits }).format(down)} ${currency}`;
}

function minorDigits(currency: string) {
  try {
    return new Intl.NumberFormat("en-US", { style: "currency", currency }).resolvedOptions().minimumFractionDigits ?? 2;
  } catch {
    return 2;
  }
}

/** C30: the gross and the tax withheld of an interest posting; the amount above is the net. */
function InterestLines({ row }: { row: Activity }) {
  const tax = row.cash.find((leg) => leg.role === "Tax")?.tax;
  const currency = row.cash[0]?.currency ?? "";
  return (
    <>
      <p className="text-muted-foreground text-xs">{`Gross ${formatPayout(row.interest!.grossAmount, currency)}`}</p>
      {tax ? (
        <p className="text-muted-foreground text-xs">
          {`Tax withheld (${tax.kind === "Nrwt" ? "NRWT" : "RWT"} ${tax.ratePercent} %) ${formatPayout(tax.amount, currency)}`}
        </p>
      ) : null}
    </>
  );
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
  const custodial = account.data?.custodyMode === "Custodial";
  const withdrawals = useGetAccountWithdrawalsQuery(id, { skip: !custodial });

  if (isNotFound(account.error)) {
    return <Muted>Not found</Muted>;
  }

  if (account.isLoading || !account.data) {
    return <Muted>Loading session…</Muted>;
  }

  const holdingsItems = holdings.data?.items ?? [];
  // C24: open withdrawals, and those requested in the last 90 days.
  const shownWithdrawals = (withdrawals.data?.items ?? []).filter((item) =>
    openStatuses.has(item.status) || Date.now() - new Date(item.requestedAt).getTime() <= ninetyDays);
  const activityItems = activity.data?.items ?? [];
  // C26: a closing account, and a Closed one whose latest closure is Completed.
  const closure = account.data.closure;
  const closureWithdrawal = (withdrawals.data?.items ?? []).find((item) => item.id === closure?.withdrawalId);
  const closingNote = closure?.status === "InProgress"
    ? `This account is being closed. ${formatPayout(closure.payoutAmount, account.data.currency)} is being paid to your bank account${
      closureWithdrawal ? ` ${closureWithdrawal.nominatedBankAccount.accountNumber}` : ""}.`
    : null;
  const closedOn = account.data.status === "Closed" && closure?.status === "Completed" && closure.completedAt
    ? `Closed on ${formatInstant(closure.completedAt).slice(0, 10)}.`
    : null;

  return (
    <Page>
      <PageHeading title={account.data.name} />
      {closingNote ? <p className="text-sm">{closingNote}</p> : null}
      {closedOn ? <p className="text-sm text-muted-foreground">{closedOn}</p> : null}
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
            {custodial && cash.data ? (
              <DetailItem label="Available to withdraw">
                {cash.data.availableCash ?? cash.data.cashBalance} {cash.data.currency}
              </DetailItem>
            ) : null}
            {custodial && (account.data.accruedInterest ?? 0) > 0 ? (
              // C29: the interest accrued so far, rounded down to the currency's minor unit.
              <DetailItem label="Interest">
                {`Interest earned so far this period: ${formatDown(account.data.accruedInterest ?? 0, account.data.currency)}`}
                <span className="mt-1 block text-xs text-muted-foreground">Paid after the end of the month.</span>
              </DetailItem>
            ) : null}
            {custodial && cash.data && (cash.data.reservedCash ?? 0) !== 0 ? (
              <DetailItem label="Withdrawals in progress">
                {cash.data.reservedCash} {cash.data.currency}
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
      {custodial && shownWithdrawals.length > 0 ? (
        <Section title="Withdrawals">
          <Card className="gap-0 py-0">
            <CardContent className="px-0">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Requested</TableHead>
                    <TableHead>Amount</TableHead>
                    <TableHead>To</TableHead>
                    <TableHead>Status</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {shownWithdrawals.map((item) => (
                    <TableRow key={item.id}>
                      <TableCell>{formatInstant(item.requestedAt)}</TableCell>
                      <TableCell>{`${item.amount} ${item.currency}`}</TableCell>
                      <TableCell>{item.nominatedBankAccount.accountNumber}</TableCell>
                      <TableCell>{withdrawalStatusLabel(item.status)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        </Section>
      ) : null}
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
                        {row.type === "TransferOut" && row.payment ? (
                          <p className="text-muted-foreground text-xs">{`Value date ${row.payment.valueDate}`}</p>
                        ) : null}
                        {row.interest ? (
                          <p className="text-muted-foreground text-xs">{`${row.interest.periodFrom} to ${row.interest.periodTo}`}</p>
                        ) : null}
                        {/* C18: an adjustment shows its memo, which the TenantAdmin wrote for the customer; not its reason. */}
                        {row.type === "Adjustment" && row.memo ? (
                          <p className="text-muted-foreground text-xs">{row.memo}</p>
                        ) : null}
                      </TableCell>
                      <TableCell>{formatInstant(row.bookedAt)}</TableCell>
                      <TableCell>
                        {row.interest
                          ? formatPayout(
                            (row.cash ?? []).filter((leg) => leg.accountId === id)
                              .reduce((sum, leg) => sum + Math.round(leg.amount * 10000), 0) / 10000,
                            row.cash[0]?.currency ?? "")
                          : (row.cash ?? [])
                            .filter((leg) => leg.accountId === id)
                            .map((leg) => `${leg.amount} ${leg.currency}`)
                            .join(", ")}
                        {row.interest ? <InterestLines row={row} /> : null}
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
