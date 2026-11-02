import { Link } from "react-router";
import { Muted, Page, PageHeading, recordLinkClass, StatusBadge } from "@/components/portal";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useGetAccountsQuery } from "@/features/accounts/accountsApi";

export function AccountsListPage() {
  const { data, isLoading, isError } = useGetAccountsQuery();

  return (
    <Page>
      <PageHeading title="Accounts" />
      {isLoading ? <Muted>Loading session…</Muted> : null}
      {isError ? <Muted>Accounts are not available.</Muted> : null}
      {data && data.items.length === 0 ? <Muted>No accounts.</Muted> : null}
      {data && data.items.length > 0 ? (
        <Card className="gap-0 py-0">
          <CardContent className="px-0">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Name</TableHead>
                  <TableHead>Type</TableHead>
                  <TableHead>Currency</TableHead>
                  <TableHead>Status</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.items.map((account) => (
                  <TableRow key={account.id}>
                    <TableCell>
                      <Link to={`/accounts/${account.id}`} className={recordLinkClass}>{account.name}</Link>
                    </TableCell>
                    <TableCell>{account.type}</TableCell>
                    <TableCell>{account.currency}</TableCell>
                    <TableCell>
                      <StatusBadge status={account.status} />
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      ) : null}
    </Page>
  );
}
