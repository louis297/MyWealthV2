import { useState } from "react";
import { Link, useParams } from "react-router";
import { DetailGrid, DetailItem, Messages, Muted, Page, PageHeading, recordLinkClass, Section, StatusBadge } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Textarea } from "@/components/ui/textarea";
import { useAppSelector } from "@/app/hooks";
import { useGetAccountQuery } from "@/features/accounts/accountsApi";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import {
  changedTransferSentence,
  custodianFailureLabel,
  ownerChangesText,
  requesterSentence,
  setAsideSentence,
  transferFailureReasonLabel,
  transferStatusLabel,
  transferSummary,
} from "@/features/inKindTransfers/inKindTransferLabels";
import {
  type InKindTransferAction,
  useActOnInKindTransferMutation,
  useGetInKindTransferQuery,
} from "@/features/inKindTransfers/inKindTransfersApi";
import { roles } from "@/features/session/roles";
import { NoteDialog } from "@/features/statementLines/NoteDialog";
import { problemMessages, problemStatus } from "@/features/statementLines/statementLineLabels";
import { formatMoney } from "@/shared/format/money";
import { formatInstant } from "@/shared/time/instant";

/** D89: one in-kind transfer, its decision and send attempts, and the actions the caller may take. */
export function TransferDetailPage() {
  const { id = "" } = useParams();
  const { data, error, refetch } = useGetInKindTransferQuery(id, { skip: !id });
  const me = useAppSelector((state) => state.session.currentUser);
  const account = useGetAccountQuery(data?.accountId ?? "", { skip: !data });
  const currencies = useGetCurrenciesQuery();
  const [act, { isLoading }] = useActOnInKindTransferMutation();
  const [note, setNote] = useState("");
  const [dialog, setDialog] = useState<"reject" | "set-aside" | null>(null);
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
  const attempts = item.send?.attempts ?? 0;
  const requested = item.status === "Requested";
  // R31: cancel until the custodian has been asked; R33: set aside once it has.
  const cancellable = requested || (item.status === "Approved" && attempts === 0);
  const asked = item.status === "Sent" || (item.status === "Approved" && attempts > 0);
  const optionalNote = note.trim() === "" ? null : note.trim();

  async function run(action: InKindTransferAction, actionNote: string | null) {
    try {
      await act({ id: item.id, action, note: actionNote, rowVersion: item.rowVersion }).unwrap();
      setMessages([]);
      setNote("");
    } catch (problem: unknown) {
      if (problemStatus(problem) === 409) {
        setMessages([changedTransferSentence]);
        void refetch();
        return;
      }
      const result = problemMessages(problem);
      setMessages([...result.messages, ...Object.values(result.fields).flat()]);
    }
  }

  return (
    <Page className="max-w-2xl">
      <PageHeading title="Transfer" description={transferSummary(item)} />
      <Messages messages={messages} />
      <Card>
        <CardContent>
          <DetailGrid>
            <DetailItem label="Status">
              <StatusBadge status={transferStatusLabel(item.status)} />
            </DetailItem>
            <DetailItem label="Direction">{item.direction}</DetailItem>
            <DetailItem label="Instrument">{`${item.instrument.symbol} ${item.instrument.name}`}</DetailItem>
            <DetailItem label="Quantity">{item.quantity}</DetailItem>
            <DetailItem label="Account">
              <Link to={`/accounts/${item.accountId}`} className={recordLinkClass}>
                {account.data?.name ?? item.accountId}
              </Link>
            </DetailItem>
            {item.cost !== null ? <DetailItem label="Cost">{formatMoney(item.cost, item.currency, places)}</DetailItem> : null}
            {item.acquiredOn ? <DetailItem label="Acquired on">{item.acquiredOn}</DetailItem> : null}
            <DetailItem label="Owner">{ownerChangesText(item.changesBeneficialOwner)}</DetailItem>
            <DetailItem label="Broker or registry">{item.counterparty.name}</DetailItem>
            <DetailItem label="Account or holder number">{item.counterparty.account}</DetailItem>
            <DetailItem label="Name on the account">{item.counterparty.holderName}</DetailItem>
            <DetailItem label="Memo">{item.memo ?? "—"}</DetailItem>
            <DetailItem label="Requested by">{item.requestedBy.name}</DetailItem>
            <DetailItem label="Requested">{formatInstant(item.requestedAt)}</DetailItem>
            {item.failureReason ? (
              <DetailItem label="Custodian refused">{transferFailureReasonLabel(item.failureReason)}</DetailItem>
            ) : null}
            {item.transactionId ? (
              <DetailItem label="Booked">
                <Link to={`/transactions/${item.transactionId}`} className={recordLinkClass}>View the transaction</Link>
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
      {item.send ? (
        <Section title="Sending">
          <Card>
            <CardContent>
              <DetailGrid>
                <DetailItem label="Attempts">{item.send.attempts}</DetailItem>
                <DetailItem label="Last attempt">{item.send.lastAttemptAt ? formatInstant(item.send.lastAttemptAt) : "—"}</DetailItem>
                <DetailItem label="Last failure">{custodianFailureLabel(item.send.lastFailure)}</DetailItem>
                <DetailItem label="Accepted">{item.send.acceptedAt ? formatInstant(item.send.acceptedAt) : "—"}</DetailItem>
                <DetailItem label="Custodian's id">{item.send.custodianTransferId ?? "—"}</DetailItem>
              </DetailGrid>
            </CardContent>
          </Card>
        </Section>
      ) : null}
      {item.setAside ? (
        <Section title="Set aside">
          <p className="text-sm">{`${item.setAside.by.name}, ${formatInstant(item.setAside.at)}: ${item.setAside.note}`}</p>
        </Section>
      ) : null}
      {(requested && tenantAdmin) || cancellable || (asked && tenantAdmin) ? (
        <Section title="Actions">
          {requested && tenantAdmin && requester ? <p className="text-sm">{requesterSentence}</p> : null}
          {(requested && tenantAdmin) || cancellable ? (
            <>
              <label htmlFor="transfer-note" className="text-sm font-medium">Note (optional)</label>
              <Textarea id="transfer-note" value={note} maxLength={200} onChange={(event) => setNote(event.target.value)} />
            </>
          ) : null}
          <div className="flex flex-wrap gap-2">
            {requested && tenantAdmin ? (
              <>
                <Button size="sm" disabled={isLoading || requester} onClick={() => void run("approve", optionalNote)}>Approve</Button>
                <Button size="sm" variant="outline" disabled={isLoading || requester} onClick={() => setDialog("reject")}>Reject</Button>
              </>
            ) : null}
            {cancellable ? (
              <Button size="sm" variant="outline" disabled={isLoading} onClick={() => void run("cancel", optionalNote)}>
                Cancel transfer
              </Button>
            ) : null}
            {asked && tenantAdmin ? (
              <Button size="sm" variant="outline" disabled={isLoading} onClick={() => setDialog("set-aside")}>Set aside</Button>
            ) : null}
          </div>
        </Section>
      ) : null}
      {dialog ? (
        <NoteDialog
          open
          title={dialog === "reject" ? "Reject this transfer" : "Set this transfer aside"}
          warning={dialog === "set-aside" ? setAsideSentence : undefined}
          busy={isLoading}
          onConfirm={(reason) => { const action = dialog; setDialog(null); void run(action, reason); }}
          onClose={() => setDialog(null)}
        />
      ) : null}
    </Page>
  );
}
