import { useState } from "react";
import { Link, useParams } from "react-router";
import { DetailGrid, DetailItem, Messages, Muted, Page, PageHeading, recordLinkClass, Section } from "@/components/portal";
import { Button, buttonVariants } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useAppSelector } from "@/app/hooks";
import {
  type CorporateAction,
  useApplyCorporateActionMutation,
  useCancelCorporateActionMutation,
  useGetCorporateActionEntitlementsQuery,
  useGetCorporateActionQuery,
  usePayDividendInCashMutation,
} from "@/features/corporateActions/corporateActionsApi";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { formatMoney } from "@/shared/format/money";
import {
  adviserScopeNote,
  applyNowConfirmation,
  conflictMessage,
  dividendStatusLabel,
  electionLabel,
  entitlementLabel,
  payInCashSentence,
  perShare,
  taxSourceLabel,
  kindLabel,
  originLabel,
  personName,
  ratioSentence,
  ratioText,
  statusLabel,
  utcToday,
} from "@/features/corporateActions/corporateActionText";
import { roles } from "@/features/session/roles";
import { NoteDialog } from "@/features/statementLines/NoteDialog";
import { problemMessages, problemStatus } from "@/features/statementLines/statementLineLabels";
import { cn } from "cn";

function quantity(value: number | null) {
  return value == null ? "—" : String(value);
}

/** D67: Edit, Cancel and Apply now of a hand-entered corporate action that is still Announced. */
function Actions({ action, onMessages }: { action: CorporateAction; onMessages: (messages: string[]) => void }) {
  const [cancel, cancelling] = useCancelCorporateActionMutation();
  const [apply, applying] = useApplyCorporateActionMutation();
  const [cancelOpen, setCancelOpen] = useState(false);
  const [applyOpen, setApplyOpen] = useState(false);
  const { refetch } = useGetCorporateActionQuery(action.id);

  async function run(request: () => Promise<unknown>) {
    try {
      await request();
      onMessages([]);
    } catch (error: unknown) {
      if (problemStatus(error) === 409) {
        onMessages([conflictMessage]);
        void refetch();
        return;
      }
      const result = problemMessages(error);
      onMessages([...result.messages, ...Object.values(result.fields).flat()]);
    }
  }

  return (
    <div className="flex flex-wrap gap-2">
      <Link to={`/corporate-actions/${action.id}/edit`} className={cn(buttonVariants({ variant: "outline", size: "sm" }))}>Edit</Link>
      <Button variant="outline" size="sm" onClick={() => setCancelOpen(true)}>Cancel</Button>
      {utcToday() >= action.exDate ? (
        // dividends D97: for a dividend "apply" fixes the entitlements and pays nothing.
        <Button size="sm" onClick={() => setApplyOpen(true)}>
          {action.kind === "Dividend" ? "Fix the entitlements now" : "Apply now"}
        </Button>
      ) : null}
      <NoteDialog
        open={cancelOpen}
        title="Cancel this corporate action"
        busy={cancelling.isLoading}
        onClose={() => setCancelOpen(false)}
        onConfirm={(note) => {
          setCancelOpen(false);
          void run(() => cancel({ id: action.id, rowVersion: action.rowVersion, note }).unwrap());
        }}
      />
      <Dialog open={applyOpen} onOpenChange={(next) => { if (!next) setApplyOpen(false); }}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Apply now</DialogTitle>
            <DialogDescription>{applyNowConfirmation}</DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setApplyOpen(false)} disabled={applying.isLoading}>Cancel</Button>
            <Button disabled={applying.isLoading} onClick={() => {
              setApplyOpen(false);
              void run(() => apply({ id: action.id, rowVersion: action.rowVersion }).unwrap());
            }}>Apply</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

/** dividends D97: a TenantAdmin pays in cash a dividend whose accepted election delivered no shares. */
function PayInCash({ action, onMessages }: { action: CorporateAction; onMessages: (messages: string[]) => void }) {
  const [pay, paying] = usePayDividendInCashMutation();
  const [open, setOpen] = useState(false);
  const { refetch } = useGetCorporateActionQuery(action.id);

  return (
    <>
      <Button size="sm" variant="outline" className="w-fit" onClick={() => setOpen(true)}>Pay in cash</Button>
      <NoteDialog
        open={open}
        title="Pay in cash"
        warning={payInCashSentence}
        busy={paying.isLoading}
        onClose={() => setOpen(false)}
        onConfirm={(note) => {
          setOpen(false);
          void pay({ id: action.id, rowVersion: action.rowVersion, note }).unwrap().then(
            () => onMessages([]),
            (error: unknown) => {
              if (problemStatus(error) === 409) {
                onMessages([conflictMessage]);
                void refetch();
                return;
              }
              const result = problemMessages(error);
              onMessages([...result.messages, ...Object.values(result.fields).flat()]);
            });
        }}
      />
    </>
  );
}

/** dividends D95: the terms, the payment and the election of a dividend. */
function DividendBlock({ action, places }: { action: CorporateAction; places?: number }) {
  const terms = action.dividend!;
  const payment = action.payment;
  const election = action.election;
  const money = (value: number) => formatMoney(value, terms.currency, places);
  return (
    <>
      <DetailGrid>
        <DetailItem label="Amount per share">{`${perShare(terms.amountPerShare)} ${terms.currency}`}</DetailItem>
        <DetailItem label="Company">{taxSourceLabel(terms.taxSource)}</DetailItem>
        {terms.taxSource === "NewZealand" ? (
          <DetailItem label="Imputation credit per share">{`${perShare(terms.imputationCreditPerShare)} ${terms.currency}`}</DetailItem>
        ) : (
          <DetailItem label="Foreign tax rate">{`${terms.foreignTaxRatePercent} %`}</DetailItem>
        )}
        <DetailItem label="Plan price">{terms.reinvestmentPrice == null ? "No reinvestment plan" : money(terms.reinvestmentPrice)}</DetailItem>
      </DetailGrid>
      {payment ? (
        <Section title="Payment">
          <p className="text-sm">{`Customers are owed ${money(payment.expectedAmount)}`}</p>
          <p className="text-sm">{payment.receivedAmount == null ? "Not received yet" : `The custodian paid ${money(payment.receivedAmount)}`}</p>
          {payment.difference != null && payment.difference !== 0 ? (
            <p className="text-sm text-amber-600">{`Difference ${money(payment.difference)}`}</p>
          ) : null}
          {payment.statementLineId ? (
            <Link to={`/statement-lines/${payment.statementLineId}`} className={recordLinkClass}>Statement line</Link>
          ) : null}
        </Section>
      ) : null}
      {election && election.status !== "None" ? (
        <Section title="Reinvestment">
          <p className="text-sm">{electionLabel(election.status, election.reason)}</p>
          {election.quantity != null ? <p className="text-sm">{`${election.quantity} shares take part`}</p> : null}
          {election.sharesReceived != null ? <p className="text-sm">{`${election.sharesReceived} shares received`}</p> : null}
          {election.note ? <p className="text-sm">{`${personName(election.releasedBy)}: ${election.note}`}</p> : null}
        </Section>
      ) : null}
    </>
  );
}

/** D65: one corporate action, its ratio as a sentence, who did what, and the entitlements. */
export function CorporateActionPage() {
  const { id } = useParams();
  const role = useAppSelector((state) => state.session.currentUser?.role);
  const { data, error } = useGetCorporateActionQuery(id ?? "", { skip: !id });
  const entitlements = useGetCorporateActionEntitlementsQuery(id ?? "", { skip: !id });
  const [messages, setMessages] = useState<string[]>([]);
  const currencies = useGetCurrenciesQuery();
  const isDividend = data?.kind === "Dividend";
  const places = currencies.data?.find((item) => item.code === data?.dividend?.currency)?.decimalPlaces;
  const money = (value: number | null | undefined, currency: string | null | undefined) =>
    value == null || !currency ? "—" : formatMoney(value, currency, currencies.data?.find((item) => item.code === currency)?.decimalPlaces);
  const waitsForShares = isDividend && data?.status === "Effective" && data.payment?.receivedAmount != null
    && data.election?.status === "Accepted" && data.election.sharesReceived == null;

  return (
    <Page className="max-w-3xl">
      <PageHeading title="Corporate action" />
      {problemStatus(error) === 404 ? <Muted>Not found</Muted> : null}
      {data ? (
        <Card>
          <CardContent className="flex flex-col gap-4">
            <Messages messages={messages} />
            <DetailGrid>
              <DetailItem label="Instrument">{`${data.instrument.symbol} · ${data.instrument.name}`}</DetailItem>
              <DetailItem label="Kind">{kindLabel(data.kind)}</DetailItem>
              {isDividend ? null : <DetailItem label="Ratio">{ratioText(data)}</DetailItem>}
              <DetailItem label="Status">
                {isDividend ? dividendStatusLabel(data.status, data.payment?.paidAt) : statusLabel(data.status)}
              </DetailItem>
              <DetailItem label="Ex-date">{data.exDate}</DetailItem>
              <DetailItem label="Record date">{data.recordDate ?? "—"}</DetailItem>
              <DetailItem label="Payment date">{data.paymentDate ?? "—"}</DetailItem>
              <DetailItem label="Source">{originLabel(data.origin)}</DetailItem>
              <DetailItem label="Entered by">{personName(data.createdBy)}</DetailItem>
              {data.effectiveAt ? <DetailItem label="Applied by">{personName(data.effectiveBy)}</DetailItem> : null}
              {data.cancelledAt ? <DetailItem label="Cancelled by">{personName(data.cancelledBy)}</DetailItem> : null}
              {data.cancelNote ? <DetailItem label="Cancel note">{data.cancelNote}</DetailItem> : null}
              {data.memo ? <DetailItem label="Memo">{data.memo}</DetailItem> : null}
            </DetailGrid>
            {isDividend ? <DividendBlock action={data} places={places} /> : <p>{ratioSentence(data.kind, data.ratioNew ?? 0, data.ratioHeld ?? 0)}</p>}
            {role === roles.tenantAdmin && waitsForShares ? <PayInCash action={data} onMessages={setMessages} /> : null}
            {role === roles.tenantAdmin && data.origin === "Manual" && data.status === "Announced" ? (
              <Actions action={data} onMessages={setMessages} />
            ) : null}
            <Section title="Accounts">
              {role === roles.adviser ? <Muted>{adviserScopeNote}</Muted> : null}
              <Table aria-label="Entitlements">
                <TableHeader>
                  <TableRow>
                    <TableHead>Customer</TableHead>
                    <TableHead>Account</TableHead>
                    {isDividend ? (
                      <>
                        <TableHead>Shares</TableHead>
                        <TableHead>Gross</TableHead>
                        <TableHead>Foreign tax</TableHead>
                        <TableHead>Net</TableHead>
                      </>
                    ) : (
                      <>
                        <TableHead>Before</TableHead>
                        <TableHead>After</TableHead>
                      </>
                    )}
                    <TableHead>Status</TableHead>
                    <TableHead>Transaction</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {(entitlements.data?.items ?? []).map((row) => (
                    <TableRow key={row.accountId}>
                      <TableCell>{row.customerName}</TableCell>
                      <TableCell>
                        <Link to={`/accounts/${row.accountId}`} className={recordLinkClass}>{row.accountName}</Link>
                      </TableCell>
                      {isDividend ? (
                        <>
                          <TableCell>{quantity(row.entitledQuantity ?? null)}</TableCell>
                          <TableCell>{money(row.grossAmount, row.currency)}</TableCell>
                          <TableCell>{money(row.foreignTax, row.currency)}</TableCell>
                          <TableCell>
                            <p>{money(row.netAmount, row.netCurrency)}</p>
                            {row.reinvestedQuantity != null ? <p className="text-xs">{`${row.reinvestedQuantity} new shares`}</p> : null}
                          </TableCell>
                        </>
                      ) : (
                        <>
                          <TableCell>{quantity(row.quantityBefore)}</TableCell>
                          <TableCell>{quantity(row.quantityAfter)}</TableCell>
                        </>
                      )}
                      <TableCell>{entitlementLabel(row.status, row.reason)}</TableCell>
                      <TableCell>
                        {row.transactionId ? (
                          <Link to={`/transactions/${row.transactionId}`} className={recordLinkClass}>View</Link>
                        ) : "—"}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Section>
          </CardContent>
        </Card>
      ) : null}
    </Page>
  );
}
