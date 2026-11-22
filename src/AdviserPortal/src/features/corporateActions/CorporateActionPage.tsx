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
} from "@/features/corporateActions/corporateActionsApi";
import {
  adviserScopeNote,
  applyNowConfirmation,
  conflictMessage,
  entitlementLabel,
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
        <Button size="sm" onClick={() => setApplyOpen(true)}>Apply now</Button>
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

/** D65: one corporate action, its ratio as a sentence, who did what, and the entitlements. */
export function CorporateActionPage() {
  const { id } = useParams();
  const role = useAppSelector((state) => state.session.currentUser?.role);
  const { data, error } = useGetCorporateActionQuery(id ?? "", { skip: !id });
  const entitlements = useGetCorporateActionEntitlementsQuery(id ?? "", { skip: !id });
  const [messages, setMessages] = useState<string[]>([]);

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
              <DetailItem label="Ratio">{ratioText(data)}</DetailItem>
              <DetailItem label="Status">{statusLabel(data.status)}</DetailItem>
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
            <p>{ratioSentence(data.kind, data.ratioNew, data.ratioHeld)}</p>
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
                    <TableHead>Before</TableHead>
                    <TableHead>After</TableHead>
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
                      <TableCell>{quantity(row.quantityBefore)}</TableCell>
                      <TableCell>{quantity(row.quantityAfter)}</TableCell>
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
