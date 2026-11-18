import { Link } from "react-router";
import { Muted, Page, PageHeading, recordLinkClass, Section, StatusBadge } from "@/components/portal";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { type Account, useGetAccountsQuery } from "@/features/accounts/accountsApi";
import { custodyLabel, externalNote } from "@/features/accounts/custodyLabel";

// Accounts held on the platform first, then those held elsewhere (customer-portal C16).
export function AccountsListPage() {
  const custodial = useGetAccountsQuery("Custodial");
  const external = useGetAccountsQuery("External");
  const isLoading = custodial.isLoading || external.isLoading;
  const isError = custodial.isError || external.isError;
  const loaded = !isLoading && !isError && custodial.data && external.data;
  const custodialItems = custodial.data?.items ?? [];
  const externalItems = external.data?.items ?? [];

  return (
    <Page>
      <PageHeading title="Accounts" />
      {isLoading ? <Muted>Loading session…</Muted> : null}
      {isError ? <Muted>Accounts are not available.</Muted> : null}
      {loaded && custodialItems.length === 0 && externalItems.length === 0 ? <Muted>No accounts.</Muted> : null}
      {loaded && custodialItems.length > 0 ? (
        <Section title={custodyLabel("Custodial")}>
          <AccountsTable accounts={custodialItems} />
        </Section>
      ) : null}
      {loaded && externalItems.length > 0 ? (
        <Section title={custodyLabel("External")}>
          <Muted>{externalNote}</Muted>
          <AccountsTable accounts={externalItems} />
        </Section>
      ) : null}
    </Page>
  );
}

function AccountsTable({ accounts }: { accounts: Account[] }) {
  return (
    <Card className="gap-0 py-0">
      <CardContent className="px-0">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Type</TableHead>
              <TableHead>Custody</TableHead>
              <TableHead>Currency</TableHead>
              <TableHead>Status</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {accounts.map((account) => (
              <TableRow key={account.id}>
                <TableCell>
                  <Link to={`/accounts/${account.id}`} className={recordLinkClass}>{account.name}</Link>
                </TableCell>
                <TableCell>{account.type}</TableCell>
                <TableCell>{custodyLabel(account.custodyMode)}</TableCell>
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
  );
}
