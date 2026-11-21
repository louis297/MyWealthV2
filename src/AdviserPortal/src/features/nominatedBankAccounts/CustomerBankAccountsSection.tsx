import { useState } from "react";
import { Link } from "react-router";
import { CheckField, Messages, Muted, Section, StatusBadge } from "@/components/portal";
import { Button, buttonVariants } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import {
  bankAccountStatusLabel,
  type NominatedBankAccount,
  useActOnNominatedBankAccountMutation,
  useGetNominatedBankAccountsQuery,
} from "@/features/nominatedBankAccounts/nominatedBankAccountsApi";
import { NoteDialog } from "@/features/statementLines/NoteDialog";
import { problemMessages, problemStatus } from "@/features/statementLines/statementLineLabels";
import { cn } from "cn";

export const changedBankAccountSentence = "This bank account changed. Check it and try again.";

/** D40, D43: the Customer page's bank accounts; Removed and Rejected rows are behind "Show removed". */
export function CustomerBankAccountsSection({ customerId }: { customerId: string }) {
  const { data, refetch } = useGetNominatedBankAccountsQuery({ customerId });
  const [act, { isLoading }] = useActOnNominatedBankAccountMutation();
  const [showRemoved, setShowRemoved] = useState(false);
  const [removing, setRemoving] = useState<NominatedBankAccount | null>(null);
  const [messages, setMessages] = useState<string[]>([]);

  const rows = (data?.items ?? []).filter(
    (item) => showRemoved || item.status === "Pending" || item.status === "Verified");

  async function onRemove(row: NominatedBankAccount, note: string) {
    try {
      await act({ id: row.id, action: "remove", note, rowVersion: row.rowVersion }).unwrap();
      setMessages([]);
    } catch (error: unknown) {
      if (problemStatus(error) === 409) {
        setMessages([changedBankAccountSentence]);
        void refetch();
      } else {
        const result = problemMessages(error);
        setMessages([...result.messages, ...Object.values(result.fields).flat()]);
      }
    }
    setRemoving(null);
  }

  return (
    <Section
      title="Bank accounts"
      actions={
        <Link to={`/customers/${customerId}/bank-accounts/new`} className={cn(buttonVariants({ size: "sm" }))}>
          Add bank account
        </Link>
      }
    >
      <Messages messages={messages} />
      <CheckField checked={showRemoved} onChange={(event) => setShowRemoved(event.target.checked)}>
        Show removed
      </CheckField>
      {rows.length === 0 ? (
        <Muted>No bank accounts.</Muted>
      ) : (
        <Card className="gap-0 py-0">
          <CardContent className="px-0">
            <Table aria-label="Bank accounts">
              <TableHeader>
                <TableRow>
                  <TableHead>Bank</TableHead>
                  <TableHead>Account name</TableHead>
                  <TableHead>Account number</TableHead>
                  <TableHead>Currency</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Added by</TableHead>
                  <TableHead />
                </TableRow>
              </TableHeader>
              <TableBody>
                {rows.map((row) => (
                  <TableRow key={row.id}>
                    <TableCell>{row.bankName}</TableCell>
                    <TableCell>{row.accountName}</TableCell>
                    <TableCell>{row.accountNumber}</TableCell>
                    <TableCell>{row.currency}</TableCell>
                    <TableCell>
                      <StatusBadge status={bankAccountStatusLabel(row.status)} />
                    </TableCell>
                    <TableCell>{row.added.by.name}</TableCell>
                    <TableCell>
                      {row.status === "Pending" || row.status === "Verified" ? (
                        <Button size="sm" variant="outline" onClick={() => setRemoving(row)}>
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
      {removing ? (
        <NoteDialog
          open
          title="Remove this bank account?"
          warning="Withdrawals already approved to it are still paid."
          busy={isLoading}
          onConfirm={(note) => void onRemove(removing, note)}
          onClose={() => setRemoving(null)}
        />
      ) : null}
    </Section>
  );
}
