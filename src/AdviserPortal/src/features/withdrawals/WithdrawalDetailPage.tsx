import { useState } from "react";
import { Link, useParams } from "react-router";
import { DetailGrid, DetailItem, Messages, Muted, Page, PageHeading, recordLinkClass, Section, StatusBadge } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Textarea } from "@/components/ui/textarea";
import { useAppSelector } from "@/app/hooks";
import { useGetAccountQuery } from "@/features/accounts/accountsApi";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { roles } from "@/features/session/roles";
import { NoteDialog } from "@/features/statementLines/NoteDialog";
import { problemMessages, problemStatus } from "@/features/statementLines/statementLineLabels";
import {
  paymentFailureLabel,
  payeeLabel,
  rejectReasonLabel,
  withdrawalStatusLabel,
} from "@/features/withdrawals/withdrawalLabels";
import {
  type WithdrawalAction,
  useActOnWithdrawalMutation,
  useGetWithdrawalQuery,
} from "@/features/withdrawals/withdrawalsApi";
import { formatMoney } from "@/shared/format/money";
import { formatInstant } from "@/shared/time/instant";

export const changedWithdrawalSentence = "This withdrawal changed. Check it and try again.";

/** D47: one withdrawal, its decision and payment, and the actions the caller may take. */
export function WithdrawalDetailPage() {
  const { id = "" } = useParams();
  const { data, error, refetch } = useGetWithdrawalQuery(id, { skip: !id });
  const me = useAppSelector((state) => state.session.currentUser);
  const account = useGetAccountQuery(data?.accountId ?? "", { skip: !data });
  const currencies = useGetCurrenciesQuery();
  const [act, { isLoading }] = useActOnWithdrawalMutation();
  const [note, setNote] = useState("");
  const [rejecting, setRejecting] = useState(false);
  const [messages, setMessages] = useState<string[]>([]);

  if (problemStatus(error) === 404) {
    return <Muted>Not found</Muted>;
  }
  if (!data) {
    return <Muted>Loading…</Muted>;
  }

  const item = data;
  const places = currencies.data?.find((row) => row.code === item.currency)?.decimalPlaces;
  const requester = item.requestedBy.id === me?.id;
  const tenantAdmin = me?.role === roles.tenantAdmin;
  const requested = item.status === "Requested";
  const cancellable = requested || (item.status === "Approved" && (item.payment?.attempts ?? 0) === 0);

  async function run(action: WithdrawalAction, actionNote: string | null) {
    try {
      await act({ id: item.id, action, note: actionNote, rowVersion: item.rowVersion }).unwrap();
      setMessages([]);
      setNote("");
    } catch (problem: unknown) {
      if (problemStatus(problem) === 409) {
        setMessages([changedWithdrawalSentence]);
        void refetch();
        return;
      }
      const result = problemMessages(problem);
      setMessages([...result.messages, ...Object.values(result.fields).flat()]);
    }
  }

  const optionalNote = note.trim() === "" ? null : note.trim();

  return (
    <Page className="max-w-2xl">
      <PageHeading title="Withdrawal" />
      <Messages messages={messages} />
      <Card>
        <CardContent>
          <DetailGrid>
            <DetailItem label="Status">
              <StatusBadge status={withdrawalStatusLabel(item.status)} />
            </DetailItem>
            <DetailItem label="Amount">{formatMoney(item.amount, item.currency, places)}</DetailItem>
            <DetailItem label="Account">
              <Link to={`/accounts/${item.accountId}`} className={recordLinkClass}>
                {account.data?.name ?? item.accountId}
              </Link>
            </DetailItem>
            <DetailItem label="To">{`${payeeLabel(item.nominatedBankAccount)} (${item.nominatedBankAccount.accountName})`}</DetailItem>
            <DetailItem label="Reference">{item.reference}</DetailItem>
            <DetailItem label="Memo">{item.memo ?? "—"}</DetailItem>
            <DetailItem label="Requested by">{item.requestedBy.name}</DetailItem>
            <DetailItem label="Requested">{formatInstant(item.requestedAt)}</DetailItem>
            {item.failureReason ? <DetailItem label="Bank refused">{rejectReasonLabel(item.failureReason)}</DetailItem> : null}
            {item.returnReason ? <DetailItem label="Return reason">{item.returnReason}</DetailItem> : null}
            {item.payoutTransactionId ? (
              <DetailItem label="Payout">
                <Link to={`/transactions/${item.payoutTransactionId}`} className={recordLinkClass}>Payout</Link>
              </DetailItem>
            ) : null}
            {item.returnTransactionId ? (
              <DetailItem label="Returned">
                <Link to={`/transactions/${item.returnTransactionId}`} className={recordLinkClass}>Return</Link>
              </DetailItem>
            ) : null}
          </DetailGrid>
        </CardContent>
      </Card>
      {item.decision ? (
        <Section title="Decision">
          <Card>
            <CardContent>
              <DetailGrid>
                <DetailItem label="Action">{item.decision.action}</DetailItem>
                <DetailItem label="By">{item.decision.by.name}</DetailItem>
                <DetailItem label="At">{formatInstant(item.decision.at)}</DetailItem>
                <DetailItem label="Note">{item.decision.note ?? "—"}</DetailItem>
              </DetailGrid>
            </CardContent>
          </Card>
        </Section>
      ) : null}
      {item.payment ? (
        <Section title="Payment">
          <Card>
            <CardContent>
              <DetailGrid>
                <DetailItem label="Payment status">{item.payment.status}</DetailItem>
                <DetailItem label="Attempts">{item.payment.attempts}</DetailItem>
                <DetailItem label="Last attempt">{item.payment.lastAttemptAt ? formatInstant(item.payment.lastAttemptAt) : "—"}</DetailItem>
                <DetailItem label="Last failure">{paymentFailureLabel(item.payment.lastFailure)}</DetailItem>
                <DetailItem label="Sent">{item.payment.sentAt ? formatInstant(item.payment.sentAt) : "—"}</DetailItem>
              </DetailGrid>
            </CardContent>
          </Card>
        </Section>
      ) : null}
      {requested || cancellable ? (
        <Section title="Actions">
          {requested && tenantAdmin && requester ? <p className="text-sm">Waiting for another administrator to approve.</p> : null}
          <label htmlFor="withdrawal-note" className="text-sm font-medium">Note (optional)</label>
          <Textarea id="withdrawal-note" value={note} maxLength={200} onChange={(event) => setNote(event.target.value)} />
          <div className="flex flex-wrap gap-2">
            {requested && tenantAdmin && !requester ? (
              <>
                <Button size="sm" disabled={isLoading} onClick={() => void run("approve", optionalNote)}>Approve</Button>
                <Button size="sm" variant="outline" disabled={isLoading} onClick={() => setRejecting(true)}>Reject</Button>
              </>
            ) : null}
            {cancellable ? (
              <Button size="sm" variant="outline" disabled={isLoading} onClick={() => void run("cancel", optionalNote)}>
                Cancel withdrawal
              </Button>
            ) : null}
          </div>
        </Section>
      ) : null}
      {rejecting ? (
        <NoteDialog
          open
          title="Reject this withdrawal"
          busy={isLoading}
          onConfirm={(reason) => { setRejecting(false); void run("reject", reason); }}
          onClose={() => setRejecting(false)}
        />
      ) : null}
    </Page>
  );
}
