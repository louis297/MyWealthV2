import { useState } from "react";
import { Link, useParams } from "react-router";
import { DetailGrid, DetailItem, Messages, Muted, Page, PageHeading, recordLinkClass, Section, StatusBadge } from "@/components/portal";
import { Button, buttonVariants } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { useGetAccountQuery } from "@/features/accounts/accountsApi";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { useGetCustomerQuery } from "@/features/customers/customersApi";
import { NoteDialog } from "@/features/statementLines/NoteDialog";
import { forbiddenSentence } from "@/features/statementLines/StatementLinesListPage";
import {
  actionLabel,
  methodLabel,
  problemMessages,
  problemStatus,
  reasonLabel,
  statusLabel,
} from "@/features/statementLines/statementLineLabels";
import {
  type NoteAction,
  useGetStatementLineQuery,
  useNoteStatementLineMutation,
  useReopenStatementLineMutation,
} from "@/features/statementLines/statementLinesApi";
import { formatMoney } from "@/shared/format/money";
import { formatInstant } from "@/shared/time/instant";
import { cn } from "cn";

export const changedSentence = "This receipt changed. Check it and try again.";

const dialogs: Record<NoteAction, string> = {
  ignore: "Ignore this receipt",
  "request-return": "Return this receipt to the payer",
  unmatch: "Undo this match",
};

/** D35: one bank receipt with its match, its last action and the actions its status allows (R23). */
export function StatementLineDetailPage() {
  const { id = "" } = useParams();
  const { data, error, refetch } = useGetStatementLineQuery(id, { skip: !id });
  const currencies = useGetCurrenciesQuery();
  const account = useGetAccountQuery(data?.match?.accountId ?? "", { skip: !data?.match });
  const customer = useGetCustomerQuery(data?.match?.customerId ?? "", { skip: !data?.match });
  const [noteAction, { isLoading: noting }] = useNoteStatementLineMutation();
  const [reopen, { isLoading: reopening }] = useReopenStatementLineMutation();
  const [dialog, setDialog] = useState<NoteAction | null>(null);
  const [messages, setMessages] = useState<string[]>([]);

  if (problemStatus(error) === 403) {
    return (
      <Page>
        <PageHeading title="Bank receipt" />
        <Muted>{forbiddenSentence}</Muted>
      </Page>
    );
  }

  if (problemStatus(error) === 404) {
    return <Muted>Not found</Muted>;
  }

  if (!data) {
    return <Muted>Loading…</Muted>;
  }

  const line = data;
  const places = currencies.data?.find((item) => item.code === line.currency)?.decimalPlaces;
  // D49: a debit line, or a line the platform matched to a payment, offers no action.
  const byPayment = line.match?.method === "Payment" || line.match?.method === "PaymentReturn";
  const credit = line.direction === "Credit" && !byPayment;
  const accountName = account.data?.name ?? line.match?.accountId ?? "";

  function failed(problem: unknown) {
    if (problemStatus(problem) === 409) {
      setMessages([changedSentence]);
      void refetch();
      return;
    }
    const result = problemMessages(problem);
    setMessages([...result.messages, ...Object.values(result.fields).flat()]);
  }

  async function onNote(action: NoteAction, note: string) {
    try {
      await noteAction({ id: line.id, action, note, rowVersion: line.rowVersion }).unwrap();
      setDialog(null);
      setMessages([]);
    } catch (problem: unknown) {
      setDialog(null);
      failed(problem);
    }
  }

  async function onReopen() {
    if (!window.confirm("Reopen this receipt?")) {
      return;
    }
    try {
      await reopen({ id: line.id, rowVersion: line.rowVersion }).unwrap();
      setMessages([]);
    } catch (problem: unknown) {
      failed(problem);
    }
  }

  const actions = !credit ? null : line.status === "Unmatched" ? (
    <div className="flex flex-wrap gap-2">
      <Link to={`/statement-lines/${line.id}/match`} className={cn(buttonVariants({ size: "sm" }))}>
        Match
      </Link>
      <Button size="sm" variant="outline" onClick={() => setDialog("ignore")}>
        Ignore
      </Button>
      <Button size="sm" variant="outline" onClick={() => setDialog("request-return")}>
        Return to payer
      </Button>
    </div>
  ) : line.status === "Ignored" || line.status === "ReturnRequested" ? (
    <Button size="sm" variant="outline" onClick={() => void onReopen()} disabled={reopening}>
      Reopen
    </Button>
  ) : line.status === "Matched" ? (
    <Button size="sm" variant="outline" onClick={() => setDialog("unmatch")}>
      Undo match
    </Button>
  ) : null;

  return (
    <Page className="max-w-2xl">
      <PageHeading title="Bank receipt" actions={actions} />
      <Messages messages={messages} />
      <Card>
        <CardContent>
          <DetailGrid>
            <DetailItem label="Status">
              <StatusBadge status={statusLabel(line.status)} />
            </DetailItem>
            {line.status === "Unmatched" || line.unmatchedReason ? (
              <DetailItem label="Reason">{reasonLabel(line.unmatchedReason)}</DetailItem>
            ) : null}
            <DetailItem label="Amount">{formatMoney(line.amount, line.currency, places)}</DetailItem>
            <DetailItem label="Direction">{line.direction}</DetailItem>
            <DetailItem label="Value date">{line.valueDate}</DetailItem>
            <DetailItem label="Received">{formatInstant(line.receivedAt)}</DetailItem>
            <DetailItem label="Payer">{line.payerName ?? "—"}</DetailItem>
            <DetailItem label="Payer account">{line.payerAccount ?? "—"}</DetailItem>
            <DetailItem label="Reference">{line.reference ?? "—"}</DetailItem>
            {line.withdrawalId ? (
              <DetailItem label="Withdrawal">
                <Link to={`/withdrawals/${line.withdrawalId}`} className={recordLinkClass}>Withdrawal</Link>
              </DetailItem>
            ) : null}
            {line.relatedLineId ? (
              <DetailItem label="Returns">
                <Link to={`/statement-lines/${line.relatedLineId}`} className={recordLinkClass}>Returned line</Link>
              </DetailItem>
            ) : null}
            {line.returnReason ? <DetailItem label="Return reason">{line.returnReason}</DetailItem> : null}
          </DetailGrid>
        </CardContent>
      </Card>
      {line.match ? (
        <Section title="Match">
          <Card>
            <CardContent className="flex flex-col gap-3">
              <p className="text-sm">{methodLabel(line.match.method)}</p>
              <DetailGrid>
                <DetailItem label="Account">
                  <Link to={`/accounts/${line.match.accountId}`} className={recordLinkClass}>
                    {accountName}
                  </Link>
                </DetailItem>
                <DetailItem label="Customer">{customer.data?.name ?? line.match.customerId}</DetailItem>
                <DetailItem label="Transaction">
                  <Link to={`/transactions/${line.match.transactionId}`} className={recordLinkClass}>
                    {line.match.method === "Payment" ? "Payout" : line.match.method === "PaymentReturn" ? "Reversal" : "Deposit"}
                  </Link>
                </DetailItem>
                <DetailItem label="Matched at">{formatInstant(line.match.at)}</DetailItem>
              </DetailGrid>
            </CardContent>
          </Card>
        </Section>
      ) : null}
      {line.lastAction ? (
        <Section title="Last action">
          <Card>
            <CardContent>
              <DetailGrid>
                <DetailItem label="Action">{actionLabel(line.lastAction.action)}</DetailItem>
                <DetailItem label="Note">{line.lastAction.note ?? "—"}</DetailItem>
                <DetailItem label="By">{line.lastAction.by.name}</DetailItem>
                <DetailItem label="At">{formatInstant(line.lastAction.at)}</DetailItem>
              </DetailGrid>
            </CardContent>
          </Card>
        </Section>
      ) : null}
      {dialog ? (
        <NoteDialog
          open
          title={dialogs[dialog]}
          warning={dialog === "unmatch" ? `This posts a reversal of the deposit on ${accountName}.` : undefined}
          busy={noting}
          onConfirm={(note) => void onNote(dialog, note)}
          onClose={() => setDialog(null)}
        />
      ) : null}
    </Page>
  );
}
