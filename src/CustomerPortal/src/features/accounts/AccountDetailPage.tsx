import { useParams } from "react-router";
import { DetailGrid, DetailItem, Muted, Page, PageHeading, Section, StatusBadge } from "@/components/portal";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import {
  type Activity,
  type CustomerOrder,
  useGetAccountOrdersQuery,
  useGetAccountQuery,
  useGetAccountWithdrawalsQuery,
  useGetAccountInKindTransfersQuery,
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

const corporateActionKinds: Record<string, string> = {
  Split: "Share split",
  Consolidation: "Share consolidation",
  Bonus: "Bonus issue",
};

function typeLabel(row: Activity) {
  // C42: a dividend reads as the dividend of its instrument.
  if (row.dividend) {
    return `Dividend ${row.dividend.instrument.symbol}`;
  }

  // C31: an entitlement posting reads as its corporate action; a hand Split on an External account keeps its label.
  if (row.corporateAction) {
    const { kind, ratioNew, ratioHeld } = row.corporateAction;
    return `${corporateActionKinds[kind] ?? kind} ${ratioNew} for ${ratioHeld}`;
  }

  // C38: an opening balance a migration batch wrote says so; a hand Opening on an External account keeps its label.
  if (row.migration && row.type === "Opening") {
    return "Opening balance";
  }

  if (row.migration && row.type === "OpeningHoldings") {
    return "Holdings transferred in";
  }

  // C40: the postings of an in-kind transfer; a hand StockIn or StockOut keeps its label.
  if (row.inKindTransfer && row.type === "StockIn") {
    return "Shares transferred in";
  }

  if (row.inKindTransfer && row.type === "StockOut") {
    return "Shares transferred out";
  }

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

/** C39: the status of an in-kind transfer in the Customer's words. */
function transferStatusLabel(status: string) {
  switch (status) {
    case "Requested":
      return "Being checked";
    case "Approved":
    case "Sent":
      return "On its way";
    case "Completed":
      return "Done";
    case "Rejected":
      return "Not accepted";
    case "Cancelled":
      return "Cancelled";
    default:
      return "Not completed";
  }
}

const openTransferStatuses = new Set(["Requested", "Approved", "Sent"]);

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

const openOrderStatuses = new Set(["Placed", "Working", "PartlyFilled"]);
const thirtyDays = 30 * 24 * 60 * 60 * 1000;

/** C32: the status of an order in the Customer's words. */
function orderStatusLabel(order: CustomerOrder) {
  switch (order.status) {
    case "Placed":
    case "Working":
      return "With the broker";
    case "PartlyFilled":
      return `Partly filled (${order.filledQuantity} of ${order.quantity})`;
    case "Filled":
      return "Done";
    case "Rejected":
      return "Not accepted";
    default:
      return order.status;
  }
}

const months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/** "25 Nov" from a `yyyy-MM-dd` date. */
function shortDay(date: string) {
  const [, month, day] = date.slice(0, 10).split("-").map(Number);
  return month >= 1 && month <= 12 && day ? `${day} ${months[month - 1]}` : date;
}

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

/** An amount per share or a price with at least two decimals: "1.00", "0.2575". */
function perShare(value: number) {
  return new Intl.NumberFormat("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 8, useGrouping: false }).format(value);
}

function taxLabel(kind: string) {
  return kind === "ForeignWithholding" ? "Foreign tax" : kind === "Nrwt" ? "NRWT" : "RWT";
}

/** C42: the gross, each tax and the imputation credit of a dividend; the amount above is the net cash. */
function DividendLines({ row }: { row: Activity }) {
  const dividend = row.dividend!;
  const principal = row.cash.find((leg) => leg.role === "Principal");
  const currency = principal?.currency ?? dividend.currency;
  const taxes = row.cash.filter((leg) => leg.role === "Tax" && leg.tax);
  return (
    <>
      {principal ? <p className="text-muted-foreground text-xs">{`Gross ${formatPayout(principal.amount, currency)}`}</p> : null}
      {taxes.map((leg) => (
        <p key={leg.tax!.kind} className="text-muted-foreground text-xs">
          {`${taxLabel(leg.tax!.kind)} ${leg.tax!.ratePercent} % ${formatPayout(leg.tax!.amount, currency)}`}
        </p>
      ))}
      {dividend.imputationCredit > 0 ? (
        <p className="text-muted-foreground text-xs">{`Imputation credit ${formatPayout(dividend.imputationCredit, dividend.currency)}`}</p>
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
  const orders = useGetAccountOrdersQuery(id, { skip: !custodial });
  const transfers = useGetAccountInKindTransfersQuery(id, { skip: !custodial });

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
  // C39: open transfers, and those that ended in the last 30 days.
  const transferItems = transfers.data?.items ?? [];
  const shownTransfers = transferItems.filter((item) => {
    const ended = item.completedAt ?? item.setAside?.at ?? item.decision?.at ?? item.requestedAt;
    return openTransferStatuses.has(item.status) || Date.now() - new Date(ended).getTime() <= thirtyDays;
  });

  /** C40: "100 ACME.SIM, Harbour Brokers" under a posting of an in-kind transfer. */
  function transferLine(row: Activity) {
    if (!row.inKindTransfer) {
      return null;
    }
    const leg = (row.security ?? []).find((item) => item.accountId === id) ?? row.security?.[0];
    const symbol = transferItems.find((item) => item.id === row.inKindTransfer?.transferId)?.instrument.symbol
      ?? holdingsItems.find((item) => item.instrumentId === leg?.instrumentId)?.symbol;
    const what = [leg ? String(Math.abs(leg.quantity)) : null, symbol].filter(Boolean).join(" ");
    return `${what}, ${row.inKindTransfer.counterpartyName}`;
  }
  // C32: open orders, and those that ended in the last 30 days.
  const orderItems = orders.data?.items ?? [];
  const shownOrders = orderItems.filter((item) =>
    openOrderStatuses.has(item.status)
    || (item.closedAt !== null && Date.now() - new Date(item.closedAt).getTime() <= thirtyDays));

  /** C34: "Bought 4 ACME.SIM at 100.12" for a fill; C36: "Trade cancelled by the broker: …" for its Reversal; null
   * for any other row. */
  function fillLabel(row: Activity) {
    if (!row.execution || (row.type !== "Buy" && row.type !== "Sell" && row.type !== "Reversal")) {
      return null;
    }
    const leg = (row.security ?? []).find((item) => item.accountId === id) ?? row.security?.[0];
    const symbol = orderItems.find((item) => item.id === row.execution?.orderId)?.instrument.symbol
      ?? holdingsItems.find((item) => item.instrumentId === leg?.instrumentId)?.symbol;
    const what = [leg ? String(Math.abs(leg.quantity)) : null, symbol].filter(Boolean).join(" ");
    // A Reversal negates the fill's leg, so a reversed buy has a negative quantity.
    const bought = row.type === "Reversal" ? (leg?.quantity ?? 0) < 0 : row.type === "Buy";
    const trade = `${bought ? "Bought" : "Sold"}${what ? ` ${what}` : ""} at ${row.execution.price}`;
    return row.type === "Reversal" ? `Trade cancelled by the broker: ${trade}` : trade;
  }
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
              <DetailItem label={custodial ? "Cash" : "Cash balance"}>
                {cash.data.cashBalance} {cash.data.currency}
              </DetailItem>
            ) : null}
            {custodial && cash.data && (cash.data.reservedCash ?? 0) !== 0 ? (
              // C33: withdrawals in progress and open buy orders.
              <DetailItem label="Reserved">
                {cash.data.reservedCash} {cash.data.currency}
              </DetailItem>
            ) : null}
            {custodial && cash.data && (cash.data.unsettledCash ?? 0) !== 0 ? (
              <DetailItem label="From sales, not settled yet">
                {cash.data.unsettledCash} {cash.data.currency}
              </DetailItem>
            ) : null}
            {custodial && cash.data ? (
              <DetailItem label="Available to withdraw">
                {cash.data.withdrawableCash ?? cash.data.availableCash ?? cash.data.cashBalance} {cash.data.currency}
              </DetailItem>
            ) : null}
            {custodial && (account.data.accruedInterest ?? 0) > 0 ? (
              // C29: the interest accrued so far, rounded down to the currency's minor unit.
              <DetailItem label="Interest">
                {`Interest earned so far this period: ${formatDown(account.data.accruedInterest ?? 0, account.data.currency)}`}
                <span className="mt-1 block text-xs text-muted-foreground">Paid after the end of the month.</span>
              </DetailItem>
            ) : null}
            {custodial ? (
              // C44: the account's reinvestment election, read only.
              <DetailItem label="Dividends">
                {account.data.reinvestDividends ? "Dividends are reinvested" : "Dividends are paid in cash"}
                <span className="mt-1 block text-xs text-muted-foreground">Ask your adviser to change this.</span>
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
                      <TableCell>
                        {holding.quantity}
                        {(holding.reservedQuantity ?? 0) > 0 ? (
                          <p className="text-muted-foreground text-xs">{`${holding.reservedQuantity} reserved for a sale or a transfer`}</p>
                        ) : null}
                        {(holding.unsettledQuantity ?? 0) > 0 ? (
                          // C41: bought and not settled yet.
                          <p className="text-muted-foreground text-xs">{`${holding.unsettledQuantity} not settled yet`}</p>
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
      {custodial && shownOrders.length > 0 ? (
        <Section title="Orders">
          <Card className="gap-0 py-0">
            <CardContent className="px-0">
              <Table aria-label="Orders">
                <TableHeader>
                  <TableRow>
                    <TableHead>Order</TableHead>
                    <TableHead>Status</TableHead>
                    <TableHead>Placed</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {shownOrders.map((item) => (
                    <TableRow key={item.id}>
                      <TableCell>
                        {`${item.side} ${item.quantity} ${item.instrument.symbol}`}
                        {/* C37: an order of a rebalance says so, with no link and no action. */}
                        {item.rebalanceId ? <p className="text-muted-foreground text-xs">Rebalance</p> : null}
                      </TableCell>
                      <TableCell>{orderStatusLabel(item)}</TableCell>
                      <TableCell>{formatInstant(item.placedAt).slice(0, 10)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        </Section>
      ) : null}
      {custodial && shownTransfers.length > 0 ? (
        <Section title="Transfers">
          <Card className="gap-0 py-0">
            <CardContent className="px-0">
              <Table aria-label="Transfers">
                <TableHeader>
                  <TableRow>
                    <TableHead>Transfer</TableHead>
                    <TableHead>Status</TableHead>
                    <TableHead>Requested</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {shownTransfers.map((item) => (
                    <TableRow key={item.id}>
                      <TableCell>
                        {`${item.quantity} ${item.instrument.symbol} ${item.direction === "In" ? "from" : "to"} ${item.counterparty.name}`}
                      </TableCell>
                      <TableCell>{transferStatusLabel(item.status)}</TableCell>
                      <TableCell>{formatInstant(item.requestedAt).slice(0, 10)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        </Section>
      ) : null}
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
                        {fillLabel(row) ?? typeLabel(row)}
                        {transferLine(row) ? <p className="text-muted-foreground text-xs">{transferLine(row)}</p> : null}
                        {row.migration && (row.type === "Opening" || row.type === "OpeningHoldings") ? (
                          <p className="text-muted-foreground text-xs">
                            {`From ${row.migration.previousProvider}, as at ${row.migration.asOfDate}`}
                          </p>
                        ) : null}
                        {fillLabel(row) && row.execution && row.type !== "Reversal" ? (
                          // C34: the trade date, the settlement and the contract note of a fill.
                          <>
                            <p className="text-muted-foreground text-xs">{`Trade date ${row.execution.tradeDate}`}</p>
                            <p className="text-muted-foreground text-xs">
                              {row.execution.brokenAt
                                ? "Cancelled"
                                : row.execution.settledAt ? "Settled" : `Settles ${shortDay(row.execution.settlementDate)}`}
                            </p>
                            {row.execution.brokerReference ? (
                              <p className="text-muted-foreground text-xs">{`Contract note ${row.execution.brokerReference}`}</p>
                            ) : null}
                          </>
                        ) : null}
                        {row.type === "TransferIn" && row.bankReceipt ? (
                          <p className="text-muted-foreground text-xs">{`Value date ${row.bankReceipt.valueDate}`}</p>
                        ) : null}
                        {row.type === "TransferOut" && row.payment ? (
                          <p className="text-muted-foreground text-xs">{`Value date ${row.payment.valueDate}`}</p>
                        ) : null}
                        {row.interest ? (
                          <p className="text-muted-foreground text-xs">{`${row.interest.periodFrom} to ${row.interest.periodTo}`}</p>
                        ) : null}
                        {row.dividend ? (
                          <>
                            <p className="text-muted-foreground text-xs">
                              {`${row.dividend.quantity} shares × ${perShare(row.dividend.amountPerShare)}`}
                            </p>
                            {row.dividend.reinvestedQuantity != null && row.dividend.reinvestmentPrice != null ? (
                              // C43.
                              <p className="text-muted-foreground text-xs">
                                {`Reinvested in ${row.dividend.reinvestedQuantity} shares at ${perShare(row.dividend.reinvestmentPrice)}`}
                              </p>
                            ) : null}
                          </>
                        ) : null}
                        {row.corporateAction ? (
                          <>
                            <p className="text-muted-foreground text-xs">
                              {`${row.corporateAction.quantityBefore} → ${row.corporateAction.quantityAfter} shares`}
                            </p>
                            <p className="text-muted-foreground text-xs">{`Ex-date ${row.corporateAction.exDate}`}</p>
                          </>
                        ) : null}
                        {/* C18: an adjustment shows its memo, which the TenantAdmin wrote for the customer; not its reason. */}
                        {row.type === "Adjustment" && row.memo ? (
                          <p className="text-muted-foreground text-xs">{row.memo}</p>
                        ) : null}
                      </TableCell>
                      <TableCell>{formatInstant(row.bookedAt)}</TableCell>
                      <TableCell>
                        {row.interest || row.dividend
                          ? formatPayout(
                            (row.cash ?? []).filter((leg) => leg.accountId === id)
                              .reduce((sum, leg) => sum + Math.round(leg.amount * 10000), 0) / 10000,
                            row.cash[0]?.currency ?? "")
                          : (row.cash ?? [])
                            .filter((leg) => leg.accountId === id)
                            .map((leg) => `${leg.amount} ${leg.currency}`)
                            .join(", ")}
                        {row.interest ? <InterestLines row={row} /> : null}
                        {row.dividend ? <DividendLines row={row} /> : null}
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
