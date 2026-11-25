import { useState } from "react";
import { Link, useParams } from "react-router";
import { DetailGrid, DetailItem, Messages, Muted, Page, PageHeading, recordLinkClass, Section, StatusBadge } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Textarea } from "@/components/ui/textarea";
import { useAppSelector } from "@/app/hooks";
import { useGetAccountQuery } from "@/features/accounts/accountsApi";
import {
  adjustmentsForbiddenSentence,
  adjustmentStatusLabel,
  cashChangeText,
  changedAdjustmentSentence,
  customerSeesMemoSentence,
  holdingChangeText,
  requesterSentence,
} from "@/features/adjustmentRequests/adjustmentRequestLabels";
import {
  type AdjustmentRequestAction,
  useActOnAdjustmentRequestMutation,
  useGetAdjustmentRequestQuery,
} from "@/features/adjustmentRequests/adjustmentRequestsApi";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { NoteDialog } from "@/features/statementLines/NoteDialog";
import { problemMessages, problemStatus } from "@/features/statementLines/statementLineLabels";
import { adjustmentReasonLabel } from "@/features/transactions/ReverseTransactionButton";
import { formatInstant } from "@/shared/time/instant";

/** adjustment-review D104: one adjustment request, its decision, and the actions on a request waiting for approval. */
export function AdjustmentDetailPage() {
  const { id = "" } = useParams();
  const { data, error, refetch } = useGetAdjustmentRequestQuery(id, { skip: !id });
  const me = useAppSelector((state) => state.session.currentUser);
  const account = useGetAccountQuery(data?.accountId ?? "", { skip: !data });
  const currencies = useGetCurrenciesQuery();
  const [act, { isLoading }] = useActOnAdjustmentRequestMutation();
  const [note, setNote] = useState("");
  const [rejecting, setRejecting] = useState(false);
  const [messages, setMessages] = useState<string[]>([]);

  if (problemStatus(error) === 404) {
    return <Muted>Not found</Muted>;
  }
  if (problemStatus(error) === 403) {
    return <Muted>{adjustmentsForbiddenSentence}</Muted>;
  }
  if (!data) {
    return <Muted>Loading…</Muted>;
  }

  const item = data;
  const places = (code: string) => currencies.data?.find((row) => row.code === code)?.decimalPlaces;
  const requester = item.requestedBy.id === me?.id;
  const requested = item.status === "Requested";
  const optionalNote = note.trim() === "" ? null : note.trim();

  async function run(action: AdjustmentRequestAction, actionNote: string | null) {
    try {
      await act({ id: item.id, action, note: actionNote, rowVersion: item.rowVersion }).unwrap();
      setMessages([]);
      setNote("");
    } catch (problem: unknown) {
      if (problemStatus(problem) === 409) {
        setMessages([changedAdjustmentSentence]);
        void refetch();
        return;
      }
      const result = problemMessages(problem);
      setMessages([...result.messages, ...Object.values(result.fields).flat()]);
    }
  }

  return (
    <Page className="max-w-2xl">
      <PageHeading title="Adjustment" description={account.data?.name} />
      <Messages messages={messages} />
      <Card>
        <CardContent>
          <DetailGrid>
            <DetailItem label="Status">
              <StatusBadge status={adjustmentStatusLabel(item.status)} />
            </DetailItem>
            <DetailItem label="Account">
              <Link to={`/accounts/${item.accountId}`} className={recordLinkClass}>
                {account.data?.name ?? item.accountId}
              </Link>
            </DetailItem>
            <DetailItem label="Reason">{adjustmentReasonLabel(item.reason)}</DetailItem>
            <DetailItem label="Cash">{cashChangeText(item.amount, item.currency, places(item.currency))}</DetailItem>
            <DetailItem label="Holding">
              {holdingChangeText(item.security, item.security ? places(item.security.costCurrency) : undefined)}
            </DetailItem>
            <DetailItem label="Memo">
              {item.memo}
              <p className="text-muted-foreground text-xs">{customerSeesMemoSentence}</p>
            </DetailItem>
            <DetailItem label="Reference">{item.reference ?? "—"}</DetailItem>
            <DetailItem label="Requested by">{item.requestedBy.name}</DetailItem>
            <DetailItem label="Requested">{formatInstant(item.requestedAt)}</DetailItem>
            {item.transactionId ? (
              <DetailItem label="Posted">
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
      {requested ? (
        <Section title="Actions">
          {requester ? <p className="text-sm">{requesterSentence}</p> : null}
          <label htmlFor="adjustment-note" className="text-sm font-medium">Note (optional)</label>
          <Textarea id="adjustment-note" value={note} maxLength={200} onChange={(event) => setNote(event.target.value)} />
          <div className="flex flex-wrap gap-2">
            <Button size="sm" disabled={isLoading || requester} onClick={() => void run("approve", optionalNote)}>Approve</Button>
            <Button size="sm" variant="outline" disabled={isLoading || requester} onClick={() => setRejecting(true)}>Reject</Button>
            <Button size="sm" variant="outline" disabled={isLoading} onClick={() => void run("cancel", optionalNote)}>
              Cancel adjustment
            </Button>
          </div>
        </Section>
      ) : null}
      {rejecting ? (
        <NoteDialog
          open
          title="Reject this adjustment"
          busy={isLoading}
          onConfirm={(reason) => { setRejecting(false); void run("reject", reason); }}
          onClose={() => setRejecting(false)}
        />
      ) : null}
    </Page>
  );
}
