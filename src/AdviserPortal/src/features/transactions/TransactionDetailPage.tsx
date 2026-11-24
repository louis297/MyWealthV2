import { Link, useParams } from "react-router";
import { shortDay } from "@/features/orders/orderLabels";
import { DetailGrid, DetailItem, Muted, Page, PageHeading, recordLinkClass, Section } from "@/components/portal";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { useAppSelector } from "@/app/hooks";
import { roles } from "@/features/session/roles";
import {
  adjustmentReasonLabel,
  canReverse,
  ReverseTransactionButton,
  sourceLabel,
  transactionTypeLabel,
} from "@/features/transactions/ReverseTransactionButton";
import { CorporateActionPostingBlock } from "@/features/corporateActions/CorporateActionPostingBlock";
import { DividendPostingBlock } from "@/features/corporateActions/DividendPostingBlock";
import { InterestPostingBlock } from "@/features/interest/InterestPostingBlock";
import { useGetTransactionQuery } from "@/features/transactions/transactionsApi";
import { formatAmount, formatMoney } from "@/shared/format/money";

function isNotFound(error: unknown) {
  return typeof error === "object" && error !== null && "status" in error && error.status === 404;
}

export function TransactionDetailPage() {
  const { id } = useParams();
  const { data, error } = useGetTransactionQuery(id ?? "", { skip: !id });
  const role = useAppSelector((state) => state.session.currentUser?.role);
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
            <Badge variant="secondary">{transactionTypeLabel(data)}</Badge>
            {data.source ? <p className="text-muted-foreground text-sm">{`Source: ${sourceLabel(data.source.kind)}`}</p> : null}
            {data.source?.kind === "BankFeed" && data.source.id && role === roles.tenantAdmin ? (
              <Link to={`/statement-lines/${data.source.id}`} className={recordLinkClass}>
                Statement line
              </Link>
            ) : null}
            {data.source?.kind === "Migration" && data.source.id && role === roles.tenantAdmin ? (
              // migration-batches D85.
              <Link to={`/migration-batches/${data.source.id}`} className={recordLinkClass}>
                Migration batch
              </Link>
            ) : null}
            {data.source?.kind === "InKindTransfer" && data.source.id ? (
              // in-kind-transfers D93.
              <Link to={`/transfers/${data.source.id}`} className={recordLinkClass}>
                In-kind transfer
              </Link>
            ) : null}
            {data.inKindTransfer ? (
              <Section title="Transfer">
                <DetailGrid>
                  <DetailItem label="Counterparty">{`${data.inKindTransfer.counterpartyName} ${data.inKindTransfer.counterpartyAccount}`}</DetailItem>
                  <DetailItem label="Owner">{`Owner changes: ${data.inKindTransfer.changesBeneficialOwner ? "yes" : "no"}`}</DetailItem>
                  <DetailItem label="Acquired on">{data.inKindTransfer.acquiredOn ?? "—"}</DetailItem>
                  <DetailItem label="Settlement date">{data.inKindTransfer.settlementDate}</DetailItem>
                  <DetailItem label="Custodian's reference">{data.inKindTransfer.custodianReference ?? "—"}</DetailItem>
                </DetailGrid>
              </Section>
            ) : null}
            {data.migration ? (
              <p className="text-sm">{`From ${data.migration.previousProvider}, balances as at ${data.migration.asOfDate}`}</p>
            ) : null}
            {data.source?.kind === "Payment" && data.source.id ? (
              <Link to={`/withdrawals/${data.source.id}`} className={recordLinkClass}>
                Withdrawal
              </Link>
            ) : null}
            {data.source?.kind === "AccountClosure" && data.cash[0] ? (
              // D57: the write-off of an account closure links to the account it closed.
              <Link to={`/accounts/${data.cash[0].accountId}`} className={recordLinkClass}>
                Account closure
              </Link>
            ) : null}
            {data.payment ? (
              <Section title="Payment">
                <DetailGrid>
                  <DetailItem label="Value date">{data.payment.valueDate}</DetailItem>
                  <DetailItem label="Reference">{data.payment.reference ?? "—"}</DetailItem>
                  <DetailItem label="Payee">{data.payment.payeeName ?? "—"}</DetailItem>
                  <DetailItem label="Payee account">{data.payment.payeeAccount ?? "—"}</DetailItem>
                </DetailGrid>
              </Section>
            ) : null}
            {data.bankReceipt ? (
              <Section title="Bank receipt">
                <DetailGrid>
                  <DetailItem label="Value date">{data.bankReceipt.valueDate}</DetailItem>
                  <DetailItem label="Reference">{data.bankReceipt.reference ?? "—"}</DetailItem>
                  <DetailItem label="Payer">{data.bankReceipt.payerName ?? "—"}</DetailItem>
                  <DetailItem label="Payer account">{data.bankReceipt.payerAccount ?? "—"}</DetailItem>
                </DetailGrid>
              </Section>
            ) : null}
            {data.execution ? (
              // D74: the broker's execution behind an order fill.
              <Section title="Trade">
                <DetailGrid>
                  <DetailItem label="Price">{data.execution.price}</DetailItem>
                  <DetailItem label="Trade date">{data.execution.tradeDate}</DetailItem>
                  <DetailItem label="Settlement date">
                    {`${data.execution.settlementDate} (${
                      data.execution.settledAt ? `Settled ${shortDay(data.execution.settlementDate)}` : "Not settled yet"
                    })`}
                  </DetailItem>
                  <DetailItem label="Contract note">{data.execution.brokerReference ?? "—"}</DetailItem>
                  {data.execution.brokenAt ? (
                    // trade-settlement D77.
                    <DetailItem label="Broken">{`Cancelled by the broker ${shortDay(data.execution.brokenAt)}`}</DetailItem>
                  ) : null}
                </DetailGrid>
                <Link to={`/orders/${data.execution.orderId}`} className={recordLinkClass}>
                  View the order
                </Link>
              </Section>
            ) : null}
            {data.interest ? <InterestPostingBlock transaction={data} /> : null}
            {data.corporateAction ? <CorporateActionPostingBlock detail={data.corporateAction} /> : null}
            {data.dividend ? <DividendPostingBlock transaction={data} /> : null}
            {data.adjustment ? (
              <p className="text-muted-foreground text-sm">{`Reason: ${adjustmentReasonLabel(data.adjustment.reason)}`}</p>
            ) : null}
            {canReverse(data, role) ? <ReverseTransactionButton id={data.id} /> : null}
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
                            {`${leg.fee.kind} ${formatMoney(leg.fee.amount, leg.fee.currency, places(leg.fee.currency))} on ${formatMoney(leg.fee.basisAmount, leg.fee.currency, places(leg.fee.currency))}`}
                            {leg.fee.scheduleId ? (
                              <>
                                {" "}
                                <Link to={`/fees/trade/${leg.fee.scheduleId}`} className={recordLinkClass}>
                                  View schedule
                                </Link>
                              </>
                            ) : null}
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
