import { useState } from "react";
import { Link, useParams } from "react-router";
import { DetailGrid, DetailItem, Messages, Muted, Page, PageHeading, recordLinkClass, Section, StatusBadge } from "@/components/portal";
import { Button, buttonVariants } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { useGetAccountQuery } from "@/features/accounts/accountsApi";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { useGetCustomerQuery } from "@/features/customers/customersApi";
import { paymentFailureLabel } from "@/features/withdrawals/withdrawalLabels";
import { NoteDialog } from "@/features/statementLines/NoteDialog";
import { forbiddenSentence } from "@/features/statementLines/StatementLinesListPage";
import {
  actionLabel,
  methodLabel,
  problemMessages,
  problemStatus,
  reasonLabel,
  statusLabel, settlementMismatchSentence } from "@/features/statementLines/statementLineLabels";
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

export const adjustSentence = "Correct the account with an adjustment, then ignore this line.";

/** D50: a match the platform made for a payment, a returned payment or a recall is undone only by its flow. */
const platformMethods = ["Payment", "PaymentReturn", "Recall", "Settlement"];

/** D35, D50: one bank statement line with its match, its last action and the actions its status allows (R23,
 * bank-returns R9). */
export function StatementLineDetailPage() {
  const { id = "" } = useParams();
  const { data, error, refetch } = useGetStatementLineQuery(id, { skip: !id });
  const currencies = useGetCurrenciesQuery();
  // D50: a recall the cash no longer covers links to the account of the recalled receipt's deposit.
  const notCovered = data?.unmatchedReason === "RecallNotCovered" && !!data.relatedLineId;
  const recalled = useGetStatementLineQuery(data?.relatedLineId ?? "", { skip: !notCovered });
  const accountId = data?.match?.accountId ?? (notCovered ? recalled.data?.match?.accountId : null) ?? null;
  const account = useGetAccountQuery(accountId ?? "", { skip: !accountId });
  const customer = useGetCustomerQuery(data?.match?.customerId ?? "", { skip: !data?.match?.customerId });
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
  // D49, D50: a line the platform matched offers no action; a debit line offers only ignore and reopen.
  const byPlatform = platformMethods.includes(line.match?.method ?? "");
  const credit = line.direction === "Credit";
  // D50: a receipt to be returned is reopened only while the bank was not asked yet.
  const returnAttempted = (line.returnPayment?.attempts ?? 0) > 0;
  const accountName = account.data?.name ?? accountId ?? "";

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

  const debitActions = line.status === "Unmatched" ? (
    <Button size="sm" variant="outline" onClick={() => setDialog("ignore")}>
      Ignore
    </Button>
  ) : line.status === "Ignored" ? (
    <Button size="sm" variant="outline" onClick={() => void onReopen()} disabled={reopening}>
      Reopen
    </Button>
  ) : null;

  const actions = byPlatform ? null : !credit ? debitActions : line.status === "Unmatched" ? (
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
  ) : line.status === "Ignored" || (line.status === "ReturnRequested" && !returnAttempted) ? (
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
              {line.status === "ReturnRequested" && line.returnPayment ? (
                <p className="text-muted-foreground mt-1 text-sm">
                  {line.returnPayment.lastFailure
                    ? `Sending failed: ${paymentFailureLabel(line.returnPayment.lastFailure)}`
                    : "Waiting to send"}
                </p>
              ) : null}
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
            {line.tradeSettlementId ? (
              // trade-settlement D78.
              <DetailItem label="Trade settlement">
                <Link to={`/trade-settlements/${line.tradeSettlementId}`} className={recordLinkClass}>Trade settlement</Link>
              </DetailItem>
            ) : null}
          </DetailGrid>
        </CardContent>
      </Card>
      {line.status === "Unmatched" && line.unmatchedReason === "SettlementMismatch" ? (
        <p className="text-sm">{settlementMismatchSentence}</p>
      ) : null}
      {line.status === "Unmatched" && line.unmatchedReason === "RecallNotCovered" ? (
        <p className="text-sm">
          {adjustSentence}{" "}
          {accountId ? (
            <Link to={`/accounts/${accountId}`} className={recordLinkClass}>
              {accountName}
            </Link>
          ) : null}
        </p>
      ) : null}
      {line.match ? (
        <Section title="Match">
          <Card>
            <CardContent className="flex flex-col gap-3">
              <p className="text-sm">{methodLabel(line.match.method)}</p>
              <DetailGrid>
                {/* D50: a match with no Transaction (a receipt return paid, a receipt never credited recalled) has no
                    account and no transaction. */}
                {line.match.accountId ? (
                  <DetailItem label="Account">
                    <Link to={`/accounts/${line.match.accountId}`} className={recordLinkClass}>
                      {accountName}
                    </Link>
                  </DetailItem>
                ) : null}
                {line.match.customerId ? (
                  <DetailItem label="Customer">{customer.data?.name ?? line.match.customerId}</DetailItem>
                ) : null}
                {line.match.transactionId ? (
                  <DetailItem label="Transaction">
                    <Link to={`/transactions/${line.match.transactionId}`} className={recordLinkClass}>
                      {transactionLabel(line.match.method)}
                    </Link>
                  </DetailItem>
                ) : null}
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
                <DetailItem label="By">{line.lastAction.by?.name ?? "The platform"}</DetailItem>
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

function transactionLabel(method: string) {
  switch (method) {
    case "Payment":
      return "Payout";
    case "PaymentReturn":
    case "Recall":
      return "Reversal";
    default:
      return "Deposit";
  }
}
