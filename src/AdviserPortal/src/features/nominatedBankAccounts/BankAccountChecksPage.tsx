import { useState } from "react";
import { DetailGrid, DetailItem, Messages, Muted, Page, PageHeading } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Textarea } from "@/components/ui/textarea";
import { useAppSelector } from "@/app/hooks";
import { useGetCustomerQuery } from "@/features/customers/customersApi";
import { changedBankAccountSentence } from "@/features/nominatedBankAccounts/CustomerBankAccountsSection";
import {
  type NominatedBankAccount,
  useActOnNominatedBankAccountMutation,
  useGetNominatedBankAccountsQuery,
} from "@/features/nominatedBankAccounts/nominatedBankAccountsApi";
import { problemMessages, problemStatus } from "@/features/statementLines/statementLineLabels";
import { formatInstant } from "@/shared/time/instant";

export const checksForbiddenSentence = "Only a firm administrator can check bank accounts.";

export function CustomerName({ id }: { id: string }) {
  const { data } = useGetCustomerQuery(id);
  return <>{data?.name ?? ""}</>;
}

/** D42: the firm's Pending bank accounts; a TenantAdmin other than the adder verifies or rejects each, with a note. */
export function BankAccountChecksPage() {
  const { data, error, refetch } = useGetNominatedBankAccountsQuery({ status: "Pending" });
  const [checking, setChecking] = useState<NominatedBankAccount | null>(null);
  const [messages, setMessages] = useState<string[]>([]);

  return (
    <Page>
      <PageHeading title="Bank account checks" />
      {problemStatus(error) === 403 ? (
        <Muted>{checksForbiddenSentence}</Muted>
      ) : (
        <>
          <Messages messages={messages} />
          <Card className="gap-0 py-0">
            <CardContent className="px-0">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Customer</TableHead>
                    <TableHead>Bank</TableHead>
                    <TableHead>Account name</TableHead>
                    <TableHead>Account number</TableHead>
                    <TableHead>Currency</TableHead>
                    <TableHead>Added by</TableHead>
                    <TableHead>Added</TableHead>
                    <TableHead />
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {(data?.items ?? []).map((row) => (
                    <TableRow key={row.id}>
                      <TableCell>
                        <CustomerName id={row.customerId} />
                      </TableCell>
                      <TableCell>{row.bankName}</TableCell>
                      <TableCell>{row.accountName}</TableCell>
                      <TableCell>{row.accountNumber}</TableCell>
                      <TableCell>{row.currency}</TableCell>
                      <TableCell>{row.added.by.name}</TableCell>
                      <TableCell>{formatInstant(row.added.at)}</TableCell>
                      <TableCell>
                        <Button size="sm" variant="outline" onClick={() => setChecking(row)}>
                          Check
                        </Button>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
          {(data?.items.length ?? 0) === 0 && data ? <Muted>No bank account is waiting for a check.</Muted> : null}
        </>
      )}
      {checking ? (
        <CheckDialog
          row={checking}
          onDone={(next) => {
            setChecking(null);
            setMessages(next);
            if (next.length > 0) {
              void refetch();
            }
          }}
        />
      ) : null}
    </Page>
  );
}

function CheckDialog({ row, onDone }: { row: NominatedBankAccount; onDone: (messages: string[]) => void }) {
  const me = useAppSelector((state) => state.session.currentUser);
  const [act, { isLoading }] = useActOnNominatedBankAccountMutation();
  const [verifyNote, setVerifyNote] = useState("");
  const [rejectNote, setRejectNote] = useState("");
  const [missing, setMissing] = useState<"verify" | "reject" | null>(null);
  const own = row.added.by.id === me?.id;

  async function submit(action: "verify" | "reject") {
    const note = (action === "verify" ? verifyNote : rejectNote).trim();
    if (!note) {
      setMissing(action);
      return;
    }
    try {
      await act({ id: row.id, action, note, rowVersion: row.rowVersion }).unwrap();
      onDone([]);
    } catch (error: unknown) {
      if (problemStatus(error) === 409) {
        onDone([changedBankAccountSentence]);
        return;
      }
      const result = problemMessages(error);
      onDone([...result.messages, ...Object.values(result.fields).flat()]);
    }
  }

  return (
    <Dialog open onOpenChange={(next) => (next ? undefined : onDone([]))}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Check this bank account</DialogTitle>
        </DialogHeader>
        <DetailGrid>
          <DetailItem label="Customer">
            <CustomerName id={row.customerId} />
          </DetailItem>
          <DetailItem label="Bank">{row.bankName}</DetailItem>
          <DetailItem label="Account name">{row.accountName}</DetailItem>
          <DetailItem label="Account number">{row.accountNumber}</DetailItem>
          <DetailItem label="Currency">{row.currency}</DetailItem>
          <DetailItem label="Added by">{row.added.by.name}</DetailItem>
        </DetailGrid>
        {own ? <p className="text-sm">Added by you. Another administrator must check it.</p> : null}
        <div className="flex flex-col gap-2">
          <label htmlFor="verify-note" className="text-sm font-medium">What did you check?</label>
          <Textarea id="verify-note" value={verifyNote} maxLength={200} disabled={own}
            onChange={(event) => { setVerifyNote(event.target.value); setMissing(null); }} />
          {missing === "verify" ? <p className="text-sm text-destructive">Enter a note.</p> : null}
          <label htmlFor="reject-note" className="text-sm font-medium">Why is it rejected?</label>
          <Textarea id="reject-note" value={rejectNote} maxLength={200} disabled={own}
            onChange={(event) => { setRejectNote(event.target.value); setMissing(null); }} />
          {missing === "reject" ? <p className="text-sm text-destructive">Enter a note.</p> : null}
        </div>
        <DialogFooter>
          <Button variant="outline" disabled={own || isLoading} onClick={() => void submit("reject")}>
            Reject
          </Button>
          <Button disabled={own || isLoading} onClick={() => void submit("verify")}>
            Verify
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
