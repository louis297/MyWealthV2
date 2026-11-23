import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { DetailGrid, DetailItem, Messages, Muted, Page, PageHeading, recordLinkClass, Section, StatusBadge } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useGetAccountQuery } from "@/features/accounts/accountsApi";
import { orderStatusLabel } from "@/features/orders/orderLabels";
import { useRebalanceAccountMutation } from "@/features/portfolios/portfoliosApi";
import { CancelRebalanceDialog } from "@/features/rebalances/CancelRebalanceDialog";
import {
  followUpSentence,
  isOpenRebalance,
  noChangeSentence,
  proposalSentence,
  rebalanceStatusText,
  rebalanceTriggerLabel,
  tradeLabel,
} from "@/features/rebalances/rebalanceLabels";
import { type Rebalance, useConfirmRebalanceMutation, useGetRebalanceQuery } from "@/features/rebalances/rebalancesApi";
import { problemMessages } from "@/features/statementLines/statementLineLabels";
import { formatAmount } from "@/shared/format/money";
import { formatInstant } from "@/shared/time/instant";

function AccountName({ id }: { id: string }) {
  const { data } = useGetAccountQuery(id);
  return <>{data?.name ?? "Account"}</>;
}

function messagesOf(error: unknown) {
  const result = problemMessages(error);
  return [...Object.values(result.fields).flat(), ...result.messages];
}

/** P15–P18: one rebalance: its proposal and confirmation, then its orders by wave. Manual refresh only. */
export function RebalanceDetailPage() {
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const { data } = useGetRebalanceQuery(id, { skip: !id });
  const [confirm, { isLoading: confirming }] = useConfirmRebalanceMutation();
  const [proposeAgain, { isLoading: proposing }] = useRebalanceAccountMutation();
  const [cancelling, setCancelling] = useState(false);
  const [messages, setMessages] = useState<string[]>([]);

  if (!data) {
    return <Muted>Loading…</Muted>;
  }

  const rebalance = data;
  const proposed = rebalance.status === "Proposed";
  const expired = proposed && Date.now() >= Date.parse(rebalance.expiresAt);

  async function onConfirm() {
    try {
      await confirm({ id: rebalance.id, rowVersion: rebalance.rowVersion }).unwrap();
      setMessages([]);
    } catch (error: unknown) {
      if (typeof error === "object" && error !== null && "status" in error && error.status === 409) {
        setMessages(["This rebalance has changed."]);
        return;
      }
      setMessages(messagesOf(error));
    }
  }

  async function onProposeAgain() {
    try {
      const result = await proposeAgain(rebalance.accountId).unwrap();
      setMessages(result.status === "NoChange" ? [noChangeSentence] : []);
      if (result.status === "Proposed") {
        void navigate(`/rebalances/${result.rebalanceId}`);
      }
    } catch (error: unknown) {
      setMessages(messagesOf(error));
    }
  }

  const actions = isOpenRebalance(rebalance.status) ? (
    <div className="flex flex-wrap gap-2">
      {proposed && !expired ? (
        <Button type="button" onClick={() => void onConfirm()} disabled={confirming}>Confirm</Button>
      ) : null}
      {expired ? (
        <Button type="button" onClick={() => void onProposeAgain()} disabled={proposing}>Propose again</Button>
      ) : null}
      {!rebalance.cancelRequest ? (
        <Button type="button" variant="outline" onClick={() => setCancelling(true)}>Cancel</Button>
      ) : null}
    </div>
  ) : null;

  return (
    <Page>
      <PageHeading title="Rebalance" actions={actions} />
      <Messages messages={messages} />
      <Card>
        <CardContent>
          <DetailGrid>
            <DetailItem label="Status"><StatusBadge status={rebalanceStatusText(rebalance)} /></DetailItem>
            <DetailItem label="Account">
              <Link to={`/accounts/${rebalance.accountId}`} className={recordLinkClass}><AccountName id={rebalance.accountId} /></Link>
            </DetailItem>
            <DetailItem label="Portfolio">
              <Link to={`/portfolios/${rebalance.portfolio.id}`} className={recordLinkClass}>{rebalance.portfolio.name}</Link>
            </DetailItem>
            <DetailItem label="Proposed">
              {`Proposed ${formatInstant(rebalance.proposedAt)} by ${rebalance.proposedBy?.name ?? "the platform"}`}
            </DetailItem>
            <DetailItem label="Started by">{rebalanceTriggerLabel(rebalance.trigger)}</DetailItem>
            {rebalance.confirmedAt ? (
              <DetailItem label="Confirmed">{`${formatInstant(rebalance.confirmedAt)} by ${rebalance.confirmedBy?.name ?? "—"}`}</DetailItem>
            ) : null}
            {rebalance.failure ? <DetailItem label="Why">{rebalance.failure.message}</DetailItem> : null}
            {rebalance.cancelRequest?.note ? <DetailItem label="Note">{rebalance.cancelRequest.note}</DetailItem> : null}
            {rebalance.endedAt ? <DetailItem label="Ended">{formatInstant(rebalance.endedAt)}</DetailItem> : null}
          </DetailGrid>
          {rebalance.followUp ? <p className="mt-4 text-sm">{followUpSentence}</p> : null}
          {expired ? <p className="mt-4 text-sm">This proposal has expired.</p> : null}
        </CardContent>
      </Card>
      {proposed ? <ProposalLines rebalance={rebalance} /> : null}
      {(rebalance.orders ?? []).length > 0 ? <OrdersByWave rebalance={rebalance} /> : null}
      {cancelling ? <CancelRebalanceDialog rebalance={rebalance} onClose={() => setCancelling(false)} /> : null}
    </Page>
  );
}

/** P15: the lines of a proposal and what happens when it is confirmed. */
function ProposalLines({ rebalance }: { rebalance: Rebalance }) {
  const amount = (value: number | null, currency: string) => (value === null ? "—" : `${formatAmount(value, 2)} ${currency}`);
  return (
    <Section title="Proposal">
      <Card className="gap-0 py-0">
        <CardContent className="px-0">
          <Table aria-label="Proposal">
            <TableHeader>
              <TableRow>
                <TableHead>Instrument</TableHead>
                <TableHead>Holding</TableHead>
                <TableHead>Now %</TableHead>
                <TableHead>Target %</TableHead>
                <TableHead>Trade</TableHead>
                <TableHead>About</TableHead>
                <TableHead>Fee</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {(rebalance.lines ?? []).map((line) => (
                <TableRow key={line.instrument.id}>
                  <TableCell>{line.instrument.symbol}</TableCell>
                  <TableCell>{Number(line.quantity)}</TableCell>
                  <TableCell>{formatAmount(line.weight, 2)}</TableCell>
                  <TableCell>{line.targetWeight === null ? "—" : formatAmount(line.targetWeight, 2)}</TableCell>
                  <TableCell>{tradeLabel(line)}</TableCell>
                  <TableCell>{amount(line.estimatedGross, line.currency)}</TableCell>
                  <TableCell>{amount(line.estimatedFee, line.currency)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
      <p className="text-sm">{proposalSentence}</p>
    </Section>
  );
}

/** P16: the orders a rebalance placed, by wave, each linked to its order. */
function OrdersByWave({ rebalance }: { rebalance: Rebalance }) {
  return (
    <Section title="Orders">
      <Card className="gap-0 py-0">
        <CardContent className="px-0">
          <Table aria-label="Rebalance orders">
            <TableHeader>
              <TableRow>
                <TableHead>Wave</TableHead>
                <TableHead>Side</TableHead>
                <TableHead>Instrument</TableHead>
                <TableHead>Quantity</TableHead>
                <TableHead>Filled</TableHead>
                <TableHead>Status</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {(rebalance.orders ?? []).map((order) => (
                <TableRow key={order.id}>
                  <TableCell>
                    <Link to={`/orders/${order.id}`} className={recordLinkClass}>{order.wave}</Link>
                  </TableCell>
                  <TableCell>{order.side}</TableCell>
                  <TableCell>{order.instrument.symbol}</TableCell>
                  <TableCell>{Number(order.quantity)}</TableCell>
                  <TableCell>{Number(order.filledQuantity)}</TableCell>
                  <TableCell><StatusBadge status={orderStatusLabel(order.status)} /></TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
    </Section>
  );
}
