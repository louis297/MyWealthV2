import { useState } from "react";
import { Link, useParams } from "react-router";
import { useAppSelector } from "@/app/hooks";
import { DetailGrid, DetailItem, Muted, Page, PageHeading, recordLinkClass, Section } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useGetAccountQuery } from "@/features/accounts/accountsApi";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { CustomerName } from "@/features/nominatedBankAccounts/BankAccountChecksPage";
import { CancelOrderDialog } from "@/features/orders/CancelOrderDialog";
import {
  breakOutcomeLabel,
  brokerFailureLabel,
  canCancelOrder,
  fillHoldReasonLabel,
  fillNeedsAttention,
  orderRejectReasonLabel,
  settlementLabel,
  shortDay,
} from "@/features/orders/orderLabels";
import { type OrderFill, useGetOrderQuery, useResolveBrokenFillMutation } from "@/features/orders/ordersApi";
import { NoteDialog } from "@/features/statementLines/NoteDialog";
import { problemMessages } from "@/features/statementLines/statementLineLabels";
import { OrderStatus } from "@/features/orders/OrdersTable";
import { roles } from "@/features/session/roles";
import { formatAmount, formatMoney } from "@/shared/format/money";
import { formatInstant } from "@/shared/time/instant";

/** D72: every field of an order with its fills; D73: cancel. */
export function OrderDetailPage() {
  const { id = "" } = useParams();
  const role = useAppSelector((state) => state.session.currentUser?.role);
  const order = useGetOrderQuery(id, { skip: !id });
  const account = useGetAccountQuery(order.data?.accountId ?? "", { skip: !order.data });
  const currencies = useGetCurrenciesQuery();
  const [cancelling, setCancelling] = useState(false);
  const [resolving, setResolving] = useState<OrderFill | null>(null);
  const [resolve, { isLoading: resolveBusy }] = useResolveBrokenFillMutation();
  const [messages, setMessages] = useState<string[]>([]);

  if (!order.data) {
    return <Muted>Loading…</Muted>;
  }

  const data = order.data;
  const places = currencies.data?.find((item) => item.code === data.currency)?.decimalPlaces;
  const fills = data.fills ?? [];
  const waiting = fills.some((fill) => fill.status === "Pending");
  const attention = fills.some(fillNeedsAttention);
  const tenantAdmin = role === roles.tenantAdmin;

  async function onResolve(fill: OrderFill, note: string) {
    try {
      await resolve({ id: data.id, fillId: fill.id, note, rowVersion: data.rowVersion }).unwrap();
      setMessages([]);
    } catch (error: unknown) {
      const result = problemMessages(error);
      setMessages([...Object.values(result.fields).flat(), ...result.messages]);
    }
    setResolving(null);
  }
  const buy = data.side === "Buy";

  return (
    <Page>
      <PageHeading
        title={`${data.side} ${data.quantity} ${data.instrument.symbol}`}
        actions={canCancelOrder(data) ? (
          <Button type="button" variant="outline" onClick={() => setCancelling(true)}>
            Cancel order
          </Button>
        ) : null}
      />
      <Card>
        <CardContent>
          <DetailGrid>
            <DetailItem label="Status">
              <OrderStatus order={data} needsAttention={attention} />
            </DetailItem>
            <DetailItem label="Customer">
              <CustomerName id={data.customerId} />
            </DetailItem>
            <DetailItem label="Account">
              <Link to={`/accounts/${data.accountId}`} className={recordLinkClass}>
                {account.data?.name ?? "Account"}
              </Link>
            </DetailItem>
            <DetailItem label="Instrument">{`${data.instrument.symbol} ${data.instrument.name}`}</DetailItem>
            <DetailItem label="Side">{data.side}</DetailItem>
            <DetailItem label="Quantity">{data.quantity}</DetailItem>
            <DetailItem label="Filled">{`${data.filledQuantity} of ${data.quantity}`}</DetailItem>
            <DetailItem label="Average price">{data.averagePrice === null ? "—" : formatMoney(data.averagePrice, data.currency, 2)}</DetailItem>
            <DetailItem label="Last price when placed">
              {`${formatAmount(data.referencePrice, 2)} ${data.currency} (${shortDay(data.referencePriceDate)})`}
            </DetailItem>
            <DetailItem label="Price limit">
              {`The broker ${buy ? "may buy up to" : "may sell down to"} ${formatAmount(data.priceLimit, 2)}`}
            </DetailItem>
            <DetailItem label="Reserved now">
              {buy ? formatMoney(data.reservedCash, data.currency, places ?? 2) : `${data.reservedQuantity} ${data.instrument.symbol}`}
            </DetailItem>
            <DetailItem label="Placed">{`${formatInstant(data.placedAt)} by ${data.placedBy.name}`}</DetailItem>
            {data.memo ? <DetailItem label="Memo">{data.memo}</DetailItem> : null}
            {data.send ? (
              <DetailItem label="Sent to the broker">
                {data.send.acceptedAt
                  ? `Accepted ${formatInstant(data.send.acceptedAt)}`
                  : `${data.send.attempts} ${data.send.attempts === 1 ? "attempt" : "attempts"}`}
                {data.send.lastFailure ? (
                  <p className="text-muted-foreground text-xs">
                    {`Last failure: ${brokerFailureLabel(data.send.lastFailure)}${data.send.lastAttemptAt ? ` at ${formatInstant(data.send.lastAttemptAt)}` : ""}`}
                  </p>
                ) : null}
              </DetailItem>
            ) : null}
            {data.rejectReason ? <DetailItem label="Refused">{orderRejectReasonLabel(data.rejectReason)}</DetailItem> : null}
            {data.cancelRequest ? (
              <DetailItem label="Cancel requested">
                {`${formatInstant(data.cancelRequest.at)} by ${data.cancelRequest.by?.name ?? "the platform"}`}
                {data.cancelRequest.note ? <p className="text-muted-foreground text-xs">{data.cancelRequest.note}</p> : null}
              </DetailItem>
            ) : null}
            {data.closedAt ? (
              <DetailItem label="Ended">
                {`${formatInstant(data.closedAt)}${data.closeReason ? ` (${data.closeReason})` : ""}`}
              </DetailItem>
            ) : null}
          </DetailGrid>
        </CardContent>
      </Card>
      <Section title="Fills">
        {fills.length === 0 ? <Muted>No fills.</Muted> : (
          <Card className="gap-0 py-0">
            <CardContent className="px-0">
              <Table aria-label="Fills">
                <TableHeader>
                  <TableRow>
                    <TableHead>Trade date</TableHead>
                    <TableHead>Quantity</TableHead>
                    <TableHead>Price</TableHead>
                    <TableHead>Settlement</TableHead>
                    <TableHead>Contract note</TableHead>
                    <TableHead>Status</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {fills.map((fill) => (
                    <TableRow key={fill.id}>
                      <TableCell className={fill.break ? "line-through" : undefined}>{fill.tradeDate}</TableCell>
                      <TableCell className={fill.break ? "line-through" : undefined}>{fill.quantity}</TableCell>
                      <TableCell className={fill.break ? "line-through" : undefined}>{formatAmount(fill.price, 2)}</TableCell>
                      <TableCell>{fill.break ? "—" : settlementLabel(fill.settlementDate, fill.settledAt)}</TableCell>
                      <TableCell>{fill.brokerReference ?? "—"}</TableCell>
                      <TableCell>
                        {fill.break ? (
                          <BrokenFill fill={fill} tenantAdmin={tenantAdmin} onResolve={() => setResolving(fill)} />
                        ) : fill.status === "Posted" && fill.transactionId ? (
                          <Link to={`/transactions/${fill.transactionId}`} className={recordLinkClass}>
                            Booked
                          </Link>
                        ) : (
                          <span className="text-amber-600">{`Waiting: ${fillHoldReasonLabel(fill.holdReason)}`}</span>
                        )}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        )}
        {waiting && role === roles.tenantAdmin ? (
          <p className="text-sm">Correct the account with an adjustment. The platform books the fill on its next run.</p>
        ) : null}
      </Section>
      {messages.map((message) => (
        <p key={message} className="text-sm text-destructive">{message}</p>
      ))}
      {cancelling ? <CancelOrderDialog order={data} onClose={() => setCancelling(false)} /> : null}
      {resolving ? (
        <NoteDialog
          open
          title="Mark this broken trade resolved"
          warning="Correct the account with an adjustment first. This writes nothing to the ledger."
          busy={resolveBusy}
          onConfirm={(note) => void onResolve(resolving, note)}
          onClose={() => setResolving(null)}
        />
      ) : null}
    </Page>
  );
}

/** D77: a fill the broker took back, with what the platform did, and Mark resolved for a TenantAdmin. */
function BrokenFill({ fill, tenantAdmin, onResolve }: { fill: OrderFill; tenantAdmin: boolean; onResolve: () => void }) {
  const fillBreak = fill.break!;
  return (
    <div className="flex flex-col gap-1">
      <span>{`Cancelled by the broker ${shortDay(fillBreak.at)}${fillBreak.reason ? `: ${fillBreak.reason}` : ""}`}</span>
      {fillBreak.outcome === "Reversed" && fillBreak.reversalTransactionId ? (
        <Link to={`/transactions/${fillBreak.reversalTransactionId}`} className={recordLinkClass}>
          Reversed
        </Link>
      ) : (
        <span className={fillBreak.outcome === "NotApplied" ? "text-amber-600" : "text-muted-foreground"}>
          {breakOutcomeLabel(fillBreak)}
        </span>
      )}
      {fillBreak.outcome === "NotApplied" && tenantAdmin ? (
        <>
          <span className="text-xs">Correct the account with an adjustment, then mark this resolved.</span>
          <Button type="button" size="sm" variant="outline" onClick={onResolve}>Mark resolved</Button>
        </>
      ) : null}
    </div>
  );
}
