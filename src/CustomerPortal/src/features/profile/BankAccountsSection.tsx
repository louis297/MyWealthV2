import { Muted, Section } from "@/components/portal";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useGetMyBankAccountsQuery } from "@/features/profile/bankAccountsApi";

/** C22: the Customer's Pending and Verified bank accounts, read-only (roadmap D6). */
export function BankAccountsSection() {
  const { data } = useGetMyBankAccountsQuery();
  const rows = (data?.items ?? []).filter((row) => row.status === "Pending" || row.status === "Verified");

  return (
    <Section title="Bank accounts">
      {rows.length === 0 ? <Muted>No bank accounts.</Muted> : (
        <Card className="gap-0 py-0">
          <CardContent className="px-0">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Bank</TableHead>
                  <TableHead>Account name</TableHead>
                  <TableHead>Account number</TableHead>
                  <TableHead>Currency</TableHead>
                  <TableHead>Status</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {rows.map((row) => (
                  <TableRow key={row.id}>
                    <TableCell>{row.bankName}</TableCell>
                    <TableCell>{row.accountName}</TableCell>
                    <TableCell>{row.accountNumber}</TableCell>
                    <TableCell>{row.currency}</TableCell>
                    <TableCell>{row.status === "Verified" ? "Verified" : "Being checked"}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}
      <p className="text-sm">To change your bank accounts, contact your adviser.</p>
    </Section>
  );
}
