import { useState } from "react";
import { Link, useParams } from "react-router";
import {
  ConflictNotice,
  DetailGrid,
  DetailItem,
  Muted,
  Page,
  PageHeading,
  recordLinkClass,
  Section,
  StatusBadge,
} from "@/components/portal";
import { Button, buttonVariants } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useGetAccountsQuery } from "@/features/accounts/accountsApi";
import { CustomerReportsSection } from "@/features/reports/CustomerReportsSection";
import {
  useDisableCustomerMutation,
  useEnableCustomerMutation,
  useGetCustomerQuery,
} from "@/features/customers/customersApi";
import { Pager } from "@/shared/components/Pager";
import { cn } from "cn";

function isStatus(error: unknown, status: number) {
  return typeof error === "object" && error !== null && "status" in error && error.status === status;
}

export function CustomerDetailPage() {
  const { id } = useParams();
  const [accountsPage, setAccountsPage] = useState(1);
  const { data, error, isLoading, refetch } = useGetCustomerQuery(id ?? "", { skip: !id });
  const accounts = useGetAccountsQuery(
    { page: accountsPage, customerId: id },
    { skip: !id },
  );
  const [disableCustomer] = useDisableCustomerMutation();
  const [enableCustomer] = useEnableCustomerMutation();
  const [conflict, setConflict] = useState(false);

  async function runStatusChange(action: "disable" | "enable") {
    if (!data) {
      return;
    }
    if (action === "disable" && !window.confirm("Disable this customer?")) {
      return;
    }

    try {
      const body = { id: data.id, rowVersion: data.rowVersion };
      if (action === "disable") {
        await disableCustomer(body).unwrap();
      } else {
        await enableCustomer(body).unwrap();
      }
      setConflict(false);
    } catch (reason: unknown) {
      if (isStatus(reason, 409)) {
        setConflict(true);
        void refetch();
      }
    }
  }

  if (isStatus(error, 404)) {
    return <Muted>Not found</Muted>;
  }

  if (isLoading || !data) {
    return <Muted>Loading…</Muted>;
  }

  return (
    <Page>
      <PageHeading
        title={data.name}
        actions={
          <>
            <Link to={`/customers/${data.id}/edit`} className={cn(buttonVariants({ variant: "outline", size: "sm" }))}>
              Edit
            </Link>
            {data.status === "disabled" ? (
              <Button type="button" variant="outline" size="sm" onClick={() => void runStatusChange("enable")}>
                Enable
              </Button>
            ) : (
              <Button type="button" variant="destructive" size="sm" onClick={() => void runStatusChange("disable")}>
                Disable
              </Button>
            )}
          </>
        }
      />
      {conflict ? <ConflictNotice /> : null}
      <Card>
        <CardContent>
          <DetailGrid>
            <DetailItem label="Email">{data.email}</DetailItem>
            <DetailItem label="Status">
              <StatusBadge status={data.status} />
            </DetailItem>
            <DetailItem label="Created">{data.created.slice(0, 10)}</DetailItem>
            <DetailItem label="Id">{data.id}</DetailItem>
          </DetailGrid>
        </CardContent>
      </Card>
      {accounts.data ? (
        <Section
          title="Accounts"
          actions={
            <Link to={`/customers/${data.id}/accounts/new`} className={cn(buttonVariants({ size: "sm" }))}>
              New account
            </Link>
          }
        >
          <Card className="gap-0 py-0">
            <CardContent className="px-0">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Name</TableHead>
                    <TableHead>Type</TableHead>
                    <TableHead>Currency</TableHead>
                    <TableHead>Cash balance</TableHead>
                    <TableHead>Status</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {accounts.data.items.map((item) => (
                    <TableRow key={item.id}>
                      <TableCell>
                        <Link to={`/accounts/${item.id}`} className={recordLinkClass}>
                          {item.name}
                        </Link>
                      </TableCell>
                      <TableCell>{item.type}</TableCell>
                      <TableCell>{item.currency}</TableCell>
                      <TableCell>{item.cashBalance}</TableCell>
                      <TableCell>
                        <StatusBadge status={item.status} />
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
          <Pager
            page={accountsPage}
            pageSize={20}
            totalCount={accounts.data.totalCount}
            onPage={setAccountsPage}
          />
        </Section>
      ) : null}
      <CustomerReportsSection customerId={data.id} />
    </Page>
  );
}
