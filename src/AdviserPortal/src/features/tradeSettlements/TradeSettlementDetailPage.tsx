import { useState } from "react";
import { Link, useParams } from "react-router";
import { DetailGrid, DetailItem, Messages, Muted, Page, PageHeading, recordLinkClass, Section, StatusBadge } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { NoteDialog } from "@/features/statementLines/NoteDialog";
import { problemMessages } from "@/features/statementLines/statementLineLabels";
import {
  failureLabel,
  netLabel,
  paymentStatusLabel,
  setAsideSentence,
  tradeSettlementStatusLabel,
} from "@/features/tradeSettlements/tradeSettlementLabels";
import {
  useGetTradeSettlementQuery,
  usePayTradeSettlementMutation,
  useSetAsideTradeSettlementMutation,
} from "@/features/tradeSettlements/tradeSettlementsApi";
import { paymentFailureLabel } from "@/features/withdrawals/withdrawalLabels";
import { formatAmount } from "@/shared/format/money";
import { formatInstant } from "@/shared/time/instant";

const setAsideStatuses = ["AwaitingPayment", "AwaitingReceipt", "PaymentFailed", "Disputed"];

/** D76: one trade settlement with the platform's and the broker's figures, its fills and its payments. */
export function TradeSettlementDetailPage() {
  const { id = "" } = useParams();
  const { data } = useGetTradeSettlementQuery(id, { skip: !id });
  const [pay, { isLoading: paying }] = usePayTradeSettlementMutation();
  const [setAside, { isLoading: settingAside }] = useSetAsideTradeSettlementMutation();
  const [dialog, setDialog] = useState(false);
  const [messages, setMessages] = useState<string[]>([]);

  if (!data) {
    return <Muted>Loading…</Muted>;
  }

  const currency = data.clientMoneyAccount.currency;
  const amount = (value: number) => `${formatAmount(value, 2)} ${currency}`;
  const differs = {
    fillCount: data.fillCount !== data.broker.fillCount,
    netAmount: data.netAmount !== data.broker.netAmount,
    account: data.broker.settlementAccount.replace(/[\s-]/g, "").toUpperCase()
      !== data.clientMoneyAccount.accountNumber.replace(/[\s-]/g, "").toUpperCase(),
  };

  function failed(error: unknown) {
    if (typeof error === "object" && error !== null && "status" in error && error.status === 409) {
      setMessages(["This settlement has changed. It shows as it is now."]);
      return;
    }
    const result = problemMessages(error);
    setMessages([...Object.values(result.fields).flat(), ...result.messages]);
  }

  async function onPay() {
    try {
      await pay({ id: data!.id, rowVersion: data!.rowVersion }).unwrap();
      setMessages([]);
    } catch (error: unknown) {
      failed(error);
    }
  }

  async function onSetAside(note: string) {
    try {
      await setAside({ id: data!.id, rowVersion: data!.rowVersion, note }).unwrap();
      setDialog(false);
      setMessages([]);
    } catch (error: unknown) {
      setDialog(false);
      failed(error);
    }
  }

  const actions = (
    <div className="flex flex-wrap gap-2">
      {data.status === "PaymentFailed" ? (
        <Button type="button" onClick={() => void onPay()} disabled={paying}>Pay again</Button>
      ) : null}
      {setAsideStatuses.includes(data.status) ? (
        <Button type="button" variant="outline" onClick={() => setDialog(true)}>Set aside</Button>
      ) : null}
    </div>
  );

  return (
    <Page>
      <PageHeading title={`Trade settlement ${data.brokerSettlementId}`} actions={actions} />
      <Messages messages={messages} />
      <Card>
        <CardContent>
          <DetailGrid>
            <DetailItem label="Status"><StatusBadge status={tradeSettlementStatusLabel(data.status)} /></DetailItem>
            <DetailItem label="Settled date">{data.settledDate}</DetailItem>
            <DetailItem label="Net">{netLabel(data.netAmount)}</DetailItem>
            <DetailItem label="Trust account">{`${data.clientMoneyAccount.accountNumber} · ${currency}`}</DetailItem>
            {data.failure ? (
              <DetailItem label="Failure">{failureLabel(data.failure, data.failureDetail, currency)}</DetailItem>
            ) : null}
            {data.completedAt ? <DetailItem label="Completed">{formatInstant(data.completedAt)}</DetailItem> : null}
            {data.statementLineId ? (
              <DetailItem label="Statement line">
                <Link to={`/statement-lines/${data.statementLineId}`} className={recordLinkClass}>Bank line</Link>
              </DetailItem>
            ) : null}
            {data.setAside ? (
              <DetailItem label="Set aside">
                {`${formatInstant(data.setAside.at)} by ${data.setAside.by?.name ?? "—"}`}
                <p className="text-muted-foreground text-xs">{data.setAside.note}</p>
              </DetailItem>
            ) : null}
          </DetailGrid>
        </CardContent>
      </Card>
      <Section title="Figures">
        <Card className="gap-0 py-0">
          <CardContent className="px-0">
            <Table aria-label="Figures">
              <TableHeader>
                <TableRow>
                  <TableHead />
                  <TableHead>Platform</TableHead>
                  <TableHead>Broker</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow>
                  <TableCell>Trades</TableCell>
                  <TableCell>{data.fillCount}</TableCell>
                  <TableCell className={differs.fillCount ? "text-destructive" : undefined}>{data.broker.fillCount}</TableCell>
                </TableRow>
                <TableRow>
                  <TableCell>Net</TableCell>
                  <TableCell>{amount(data.netAmount)}</TableCell>
                  <TableCell className={differs.netAmount ? "text-destructive" : undefined}>{amount(data.broker.netAmount)}</TableCell>
                </TableRow>
                <TableRow>
                  <TableCell>Settlement account</TableCell>
                  <TableCell>{data.clientMoneyAccount.accountNumber}</TableCell>
                  <TableCell className={differs.account ? "text-destructive" : undefined}>{data.broker.settlementAccount}</TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      </Section>
      <Section title="Trades">
        <Card className="gap-0 py-0">
          <CardContent className="px-0">
            <Table aria-label="Trades">
              <TableHeader>
                <TableRow>
                  <TableHead>Order</TableHead>
                  <TableHead>Side</TableHead>
                  <TableHead>Quantity</TableHead>
                  <TableHead>Price</TableHead>
                  <TableHead>Gross</TableHead>
                  <TableHead>Status</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {(data.fills ?? []).map((fill) => (
                  <TableRow key={fill.id}>
                    <TableCell>
                      <Link to={`/orders/${fill.orderId}`} className={recordLinkClass}>Order</Link>
                    </TableCell>
                    <TableCell>{fill.side}</TableCell>
                    <TableCell>{fill.quantity}</TableCell>
                    <TableCell>{formatAmount(fill.price, 2)}</TableCell>
                    <TableCell>{amount(fill.gross)}</TableCell>
                    <TableCell>{fill.status === "Posted" ? "Booked" : fill.status}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      </Section>
      <Section title="Payments">
        {(data.payments ?? []).length === 0 ? <Muted>No payment.</Muted> : (
          <Card className="gap-0 py-0">
            <CardContent className="px-0">
              <Table aria-label="Payments">
                <TableHeader>
                  <TableRow>
                    <TableHead>Created</TableHead>
                    <TableHead>Status</TableHead>
                    <TableHead>Attempts</TableHead>
                    <TableHead>Last failure</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {(data.payments ?? []).map((payment) => (
                    <TableRow key={payment.createdAt}>
                      <TableCell>{formatInstant(payment.createdAt)}</TableCell>
                      <TableCell>{paymentStatusLabel(payment.status)}</TableCell>
                      <TableCell>{payment.attempts}</TableCell>
                      <TableCell>{payment.lastFailure ? paymentFailureLabel(payment.lastFailure) : "—"}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        )}
      </Section>
      {dialog ? (
        <NoteDialog
          open
          title="Set this settlement aside"
          warning={setAsideSentence}
          busy={settingAside}
          onConfirm={(note) => void onSetAside(note)}
          onClose={() => setDialog(false)}
        />
      ) : null}
    </Page>
  );
}
