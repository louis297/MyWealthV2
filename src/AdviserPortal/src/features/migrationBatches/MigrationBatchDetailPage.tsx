import { useState } from "react";
import { Link, useParams } from "react-router";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { DetailGrid, DetailItem, Messages, Muted, Page, PageHeading, recordLinkClass, Section, StatusBadge } from "@/components/portal";
import { Button, buttonVariants } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import {
  forbiddenSentence,
  migrationBatchStatusLabel,
  moneyText,
  postQuestion,
} from "@/features/migrationBatches/migrationBatchLabels";
import {
  type MigrationBatch,
  type MigrationBatchAction,
  type MigrationMoney,
  useActOnMigrationBatchMutation,
  useGetMigrationBatchQuery,
} from "@/features/migrationBatches/migrationBatchesApi";
import { NoteDialog } from "@/features/statementLines/NoteDialog";
import { problemMessages, problemStatus } from "@/features/statementLines/statementLineLabels";
import {
  statementLinesApi,
  useGetStatementLinesQuery,
  useMatchStatementLineMutation,
  useNoteStatementLineMutation,
} from "@/features/statementLines/statementLinesApi";
import { cn } from "@/lib/utils";
import { formatAmount } from "@/shared/format/money";
import { formatInstant } from "@/shared/time/instant";

type Dialog = { kind: "return" | "cancel" } | { kind: "remove"; statementLineId: string };

type TotalRow = { key: string; label: string; control: string; computed: string; bank: string | null; differs: boolean };

function amountOf(rows: MigrationMoney[], currency: string) {
  return rows.find((row) => row.currency === currency)?.amount;
}

/** D82: the four totals side by side, with the bank's figure for cash. */
function totalRows(batch: MigrationBatch): TotalRow[] {
  const symbols = new Map(batch.entries.flatMap((entry) => entry.holdings.map((holding) => [holding.instrument.id, holding.instrument.symbol])));
  const rows: TotalRow[] = [{
    key: "accounts",
    label: "Accounts",
    control: String(batch.controlTotals.accountCount),
    computed: String(batch.computedTotals.accountCount),
    bank: null,
    differs: batch.controlTotals.accountCount !== batch.computedTotals.accountCount,
  }];
  const show = (value: number | undefined) => (value === undefined ? "—" : formatAmount(value, 2));
  const currencies = (a: MigrationMoney[], b: MigrationMoney[], c: MigrationMoney[] = []) =>
    [...new Set([...a, ...b, ...c].map((row) => row.currency))];
  for (const currency of currencies(batch.controlTotals.cash, batch.computedTotals.cash, batch.fundingTotals)) {
    const control = amountOf(batch.controlTotals.cash, currency);
    const computed = amountOf(batch.computedTotals.cash, currency);
    const bank = amountOf(batch.fundingTotals, currency) ?? 0;
    rows.push({
      key: `cash-${currency}`,
      label: `Cash ${currency}`,
      control: show(control),
      computed: show(computed),
      bank: formatAmount(bank, 2),
      differs: control !== computed || (computed ?? 0) !== bank,
    });
  }
  const instruments = [...new Set([...batch.controlTotals.quantity, ...batch.computedTotals.quantity].map((row) => row.instrumentId))];
  for (const instrumentId of instruments) {
    const control = batch.controlTotals.quantity.find((row) => row.instrumentId === instrumentId)?.quantity;
    const computed = batch.computedTotals.quantity.find((row) => row.instrumentId === instrumentId)?.quantity;
    rows.push({
      key: `quantity-${instrumentId}`,
      label: symbols.get(instrumentId) ?? "Instrument",
      control: control === undefined ? "—" : formatAmount(control),
      computed: computed === undefined ? "—" : formatAmount(computed),
      bank: null,
      differs: control !== computed,
    });
  }
  for (const currency of currencies(batch.controlTotals.cost, batch.computedTotals.cost)) {
    const control = amountOf(batch.controlTotals.cost, currency);
    const computed = amountOf(batch.computedTotals.cost, currency);
    rows.push({ key: `cost-${currency}`, label: `Cost ${currency}`, control: show(control), computed: show(computed), bank: null, differs: control !== computed });
  }
  return rows;
}

/** D82, D83: one migration batch with its totals, issues, entries, funding and actions. */
export function MigrationBatchDetailPage() {
  const { id = "" } = useParams();
  const dispatch = useAppDispatch();
  const me = useAppSelector((state) => state.session.currentUser);
  const { data, error, refetch } = useGetMigrationBatchQuery(id, { skip: !id });
  const [act, { isLoading: acting }] = useActOnMigrationBatchMutation();
  const [match] = useMatchStatementLineMutation();
  const [noteLine, { isLoading: removing }] = useNoteStatementLineMutation();
  const [dialog, setDialog] = useState<Dialog | null>(null);
  const [adding, setAdding] = useState(false);
  const [messages, setMessages] = useState<string[]>([]);
  const receipts = useGetStatementLinesQuery({ status: "Unmatched", direction: "Credit" }, { skip: !adding });

  if (problemStatus(error) === 403) {
    return (
      <Page>
        <PageHeading title="Migration batch" />
        <Muted>{forbiddenSentence}</Muted>
      </Page>
    );
  }

  if (!data) {
    return <Muted>{problemStatus(error) === 404 ? "Not found" : "Loading…"}</Muted>;
  }

  const batch = data;
  const draft = batch.status === "Draft";
  const issues = batch.validation?.issues ?? [];

  function failed(problem: unknown) {
    if (problemStatus(problem) === 409) {
      setMessages(["This batch has changed. It shows as it is now."]);
      void refetch();
      return;
    }
    const result = problemMessages(problem);
    setMessages([...result.messages, ...Object.values(result.fields).flat()]);
    // D82: a migration_batch_not_valid answer reloads the batch, which shows the issues.
    void refetch();
  }

  async function run(action: MigrationBatchAction, note?: string) {
    try {
      await act({ id: batch.id, action, rowVersion: batch.rowVersion, note }).unwrap();
      setMessages([]);
    } catch (problem: unknown) {
      failed(problem);
    }
  }

  async function onPost() {
    const cash = batch.computedTotals.cash;
    const holdings = batch.entries.reduce((sum, entry) => sum + entry.holdings.length, 0);
    if (!window.confirm(postQuestion(batch.entries.length, cash, holdings))) {
      return;
    }
    await run("post");
  }

  async function onRemove(statementLineId: string, note: string) {
    try {
      const line = await dispatch(statementLinesApi.endpoints.getStatementLine.initiate(statementLineId, { subscribe: false, forceRefetch: true })).unwrap();
      await noteLine({ id: statementLineId, action: "unmatch", note, rowVersion: line.rowVersion }).unwrap();
      setMessages([]);
      void refetch();
    } catch (problem: unknown) {
      failed(problem);
    }
  }

  async function onAdd(statementLineId: string, rowVersion: string) {
    try {
      await match({ id: statementLineId, migrationBatchId: batch.id, rowVersion, note: null }).unwrap();
      setAdding(false);
      setMessages([]);
      void refetch();
    } catch (problem: unknown) {
      failed(problem);
    }
  }

  async function onDialog(note: string) {
    const current = dialog;
    setDialog(null);
    if (current?.kind === "remove") {
      await onRemove(current.statementLineId, note);
    } else if (current) {
      await run(current.kind, note);
    }
  }

  const submittedByMe = batch.submittedBy !== null && batch.submittedBy.id === me?.id;
  const actions = draft ? (
    <div className="flex flex-wrap items-center gap-2">
      <Link to={`/migration-batches/${batch.id}/edit`} className={cn(buttonVariants({ size: "sm", variant: "outline" }))}>Edit</Link>
      <Button size="sm" onClick={() => void run("submit")} disabled={acting || batch.validation?.isValid !== true}>Submit</Button>
      <Button size="sm" variant="outline" onClick={() => setDialog({ kind: "cancel" })}>Cancel batch</Button>
      {batch.validation?.isValid === false ? <Muted>{`Fix ${issues.length} issues first.`}</Muted> : null}
    </div>
  ) : batch.status === "Submitted" ? (
    <div className="flex flex-wrap items-center gap-2">
      <Button size="sm" onClick={() => void onPost()} disabled={acting || submittedByMe}>Post</Button>
      <Button size="sm" variant="outline" onClick={() => setDialog({ kind: "return" })}>Return</Button>
      <Button size="sm" variant="outline" onClick={() => setDialog({ kind: "cancel" })}>Cancel batch</Button>
      {submittedByMe ? <Muted>Someone else must post this batch.</Muted> : null}
    </div>
  ) : null;

  return (
    <Page>
      <PageHeading title={batch.name} actions={actions} />
      <Messages messages={messages} />
      <Card>
        <CardContent>
          <DetailGrid>
            <DetailItem label="Status"><StatusBadge status={migrationBatchStatusLabel(batch.status)} /></DetailItem>
            <DetailItem label="Previous provider">{batch.previousProvider}</DetailItem>
            <DetailItem label="As at">{batch.asOfDate}</DetailItem>
            <DetailItem label="Created">{`${formatInstant(batch.createdAt)} by ${batch.createdBy?.name ?? "—"}`}</DetailItem>
            {batch.submittedAt ? <DetailItem label="Submitted">{`${formatInstant(batch.submittedAt)} by ${batch.submittedBy?.name ?? "—"}`}</DetailItem> : null}
            {batch.postedAt ? <DetailItem label="Posted">{`${formatInstant(batch.postedAt)} by ${batch.postedBy?.name ?? "—"}`}</DetailItem> : null}
            {batch.returned ? (
              <DetailItem label="Returned">
                {`${formatInstant(batch.returned.at)} by ${batch.returned.by?.name ?? "—"}`}
                <p className="text-muted-foreground text-xs">{batch.returned.note}</p>
              </DetailItem>
            ) : null}
            {batch.cancelled ? (
              <DetailItem label="Cancelled">
                {`${formatInstant(batch.cancelled.at)} by ${batch.cancelled.by?.name ?? "—"}`}
                <p className="text-muted-foreground text-xs">{batch.cancelled.note}</p>
              </DetailItem>
            ) : null}
          </DetailGrid>
        </CardContent>
      </Card>
      <Section title="Totals">
        <Card className="gap-0 py-0">
          <CardContent className="px-0">
            <Table aria-label="Totals">
              <TableHeader>
                <TableRow>
                  <TableHead />
                  <TableHead>Control</TableHead>
                  <TableHead>From the entries</TableHead>
                  <TableHead>From the bank</TableHead>
                  <TableHead />
                </TableRow>
              </TableHeader>
              <TableBody>
                {totalRows(batch).map((row) => (
                  <TableRow key={row.key}>
                    <TableCell>{row.label}</TableCell>
                    <TableCell>{row.control}</TableCell>
                    <TableCell>{row.computed}</TableCell>
                    <TableCell>{row.bank ?? ""}</TableCell>
                    <TableCell>{row.differs ? <span className="text-destructive">Differs</span> : null}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      </Section>
      {issues.length > 0 ? (
        <Section title="Issues">
          <ul className="flex flex-col gap-1 text-sm">
            {issues.map((issue, index) => (
              <li key={`${issue.code}-${index}`} className="text-destructive">
                {issue.message}{" "}
                {issue.accountId ? <Link to={`/accounts/${issue.accountId}`} className={recordLinkClass}>Account</Link> : null}
              </li>
            ))}
          </ul>
        </Section>
      ) : null}
      <Section title="Entries">
        {batch.entries.length === 0 ? <Muted>No entries.</Muted> : (
          <Card className="gap-0 py-0">
            <CardContent className="px-0">
              <Table aria-label="Entries">
                <TableHeader>
                  <TableRow>
                    <TableHead>Account</TableHead>
                    <TableHead>Customer</TableHead>
                    <TableHead>Deposit reference</TableHead>
                    <TableHead>Previous reference</TableHead>
                    <TableHead>Cash</TableHead>
                    <TableHead>Holdings</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {batch.entries.map((entry) => (
                    <TableRow key={entry.account.id}>
                      <TableCell>
                        <Link to={`/accounts/${entry.account.id}`} className={recordLinkClass}>{entry.account.name}</Link>
                      </TableCell>
                      <TableCell>{entry.account.customer.name}</TableCell>
                      <TableCell>{entry.account.depositReference ?? "—"}</TableCell>
                      <TableCell>{entry.externalReference ?? "—"}</TableCell>
                      <TableCell>
                        {entry.cash === null ? "—" : moneyText(entry.cash, entry.account.currency)}
                        {entry.openingTransactionId ? (
                          <Link to={`/transactions/${entry.openingTransactionId}`} className={cn(recordLinkClass, "block")}>Opening balance</Link>
                        ) : null}
                      </TableCell>
                      <TableCell>
                        {entry.holdings.length === 0 ? "—" : entry.holdings.map((holding) => `${formatAmount(holding.quantity)} ${holding.instrument.symbol}`).join(", ")}
                        {entry.openingHoldingsTransactionId ? (
                          <Link to={`/transactions/${entry.openingHoldingsTransactionId}`} className={cn(recordLinkClass, "block")}>Holdings transferred in</Link>
                        ) : null}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        )}
      </Section>
      <Section title="Bank receipts for this batch"
        actions={draft ? <Button size="sm" variant="outline" onClick={() => setAdding(true)}>Add receipt</Button> : null}>
        {batch.funding.length === 0 ? <Muted>No bank receipt yet.</Muted> : (
          <Card className="gap-0 py-0">
            <CardContent className="px-0">
              <Table aria-label="Bank receipts for this batch">
                <TableHeader>
                  <TableRow>
                    <TableHead>Value date</TableHead>
                    <TableHead>Payer</TableHead>
                    <TableHead>Reference</TableHead>
                    <TableHead>Amount</TableHead>
                    <TableHead />
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {batch.funding.map((line) => (
                    <TableRow key={line.statementLineId}>
                      <TableCell>
                        <Link to={`/statement-lines/${line.statementLineId}`} className={recordLinkClass}>{line.valueDate}</Link>
                      </TableCell>
                      <TableCell>{line.payerName ?? "—"}</TableCell>
                      <TableCell>{line.reference ?? "—"}</TableCell>
                      <TableCell>{moneyText(line.amount, line.currency)}</TableCell>
                      <TableCell>
                        {draft ? (
                          <Button size="sm" variant="ghost" disabled={removing}
                            onClick={() => setDialog({ kind: "remove", statementLineId: line.statementLineId })}>
                            Remove
                          </Button>
                        ) : null}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        )}
        {adding ? (
          (receipts.data?.items ?? []).length === 0 ? <Muted>{receipts.isSuccess ? "No receipt is waiting in suspense." : "Loading…"}</Muted> : (
            <Card className="gap-0 py-0">
              <CardContent className="px-0">
                <Table aria-label="Unmatched receipts">
                  <TableHeader>
                    <TableRow>
                      <TableHead>Value date</TableHead>
                      <TableHead>Payer</TableHead>
                      <TableHead>Reference</TableHead>
                      <TableHead>Amount</TableHead>
                      <TableHead />
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {(receipts.data?.items ?? []).map((line) => (
                      <TableRow key={line.id}>
                        <TableCell>{line.valueDate}</TableCell>
                        <TableCell>{line.payerName ?? "—"}</TableCell>
                        <TableCell>{line.reference ?? "—"}</TableCell>
                        <TableCell>{moneyText(line.amount, line.currency)}</TableCell>
                        <TableCell>
                          <Button size="sm" variant="outline" onClick={() => void onAdd(line.id, line.rowVersion)}>Add</Button>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </CardContent>
            </Card>
          )
        ) : null}
      </Section>
      {dialog ? (
        <NoteDialog
          open
          title={dialog.kind === "return" ? "Return this batch to draft" : dialog.kind === "cancel" ? "Cancel this batch" : "Take this receipt out of the batch"}
          warning={dialog.kind === "cancel" ? "Its bank receipts go back to suspense." : undefined}
          busy={acting || removing}
          onConfirm={(note) => void onDialog(note)}
          onClose={() => setDialog(null)}
        />
      ) : null}
    </Page>
  );
}
