import { useState } from "react";
import { Link, useParams } from "react-router";
import {
  ConflictNotice,
  DetailGrid,
  DetailItem,
  Messages,
  Muted,
  Page,
  PageHeading,
  Section,
  StatusBadge,
} from "@/components/portal";
import { Button, buttonVariants } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import {
  useCloseAccountMutation,
  useGetAccountHoldingsQuery,
  useGetAccountQuery,
  useReopenAccountMutation,
} from "@/features/accounts/accountsApi";
import { useGetTransactionsQuery } from "@/features/transactions/transactionsApi";
import { cn } from "cn";

function isStatus(error: unknown, status: number) {
  return typeof error === "object" && error !== null && "status" in error && error.status === status;
}

function problemMessages(error: unknown): string[] {
  if (typeof error !== "object" || error === null || !("data" in error)) {
    return [];
  }
  const data = (error as { data?: { errors?: Record<string, string[]>; detail?: string } }).data;
  if (data?.errors) {
    return Object.values(data.errors).flat();
  }
  if (data?.detail) {
    return [data.detail];
  }
  return [];
}

const cashActions = ["TransferIn", "TransferOut"] as const;
const securityActions = ["Buy", "Sell", "StockIn", "StockOut", "Split", "Bonus", "Scrip"] as const;

function isNotFound(error: unknown) {
  return typeof error === "object" && error !== null && "status" in error && error.status === 404;
}

function canPostCash(status: string) {
  return status === "Open";
}

function canPostSecurity(type: string, status: string) {
  return status === "Open" && (type === "Brokerage" || type === "Other");
}

export function AccountDetailPage() {
  const { id } = useParams();
  const account = useGetAccountQuery(id ?? "", { skip: !id });
  const holdings = useGetAccountHoldingsQuery(id ?? "", { skip: !id || !account.data });
  const transactions = useGetTransactionsQuery(
    { accountId: id },
    { skip: !id || !account.data },
  );
  const [closeAccount] = useCloseAccountMutation();
  const [reopenAccount] = useReopenAccountMutation();
  const [messages, setMessages] = useState<string[]>([]);
  const [conflict, setConflict] = useState(false);

  async function onLifecycle(action: "close" | "reopen") {
    const current = account.data;
    if (!current) {
      return;
    }
    const closing = action === "close";
    if (current.status !== (closing ? "Open" : "Closed")) {
      return;
    }
    if (!window.confirm(closing ? "Close this account?" : "Reopen this account?")) {
      return;
    }
    try {
      const body = { id: current.id, rowVersion: current.rowVersion };
      if (closing) {
        await closeAccount(body).unwrap();
      } else {
        await reopenAccount(body).unwrap();
      }
      setMessages([]);
      setConflict(false);
    } catch (error: unknown) {
      if (isStatus(error, 409)) {
        setConflict(true);
        setMessages([]);
        void account.refetch();
        return;
      }
      setConflict(false);
      setMessages(problemMessages(error));
    }
  }

  if (isNotFound(account.error)) {
    return <Muted>Not found</Muted>;
  }

  if (!account.data || !holdings.data) {
    return <Muted>Loading…</Muted>;
  }

  const data = account.data;
  const items = holdings.data.items;
  const actions = [
    ...(canPostCash(data.status) ? cashActions : []),
    ...(canPostSecurity(data.type, data.status) ? securityActions : []),
  ];

  return (
    <Page>
      <PageHeading
        title={data.name}
        actions={
          data.status === "Open" ? (
            <Button type="button" variant="destructive" onClick={() => void onLifecycle("close")}>
              Close
            </Button>
          ) : (
            <Button type="button" variant="outline" onClick={() => void onLifecycle("reopen")}>
              Reopen
            </Button>
          )
        }
      />
      {actions.length > 0 ? (
        <nav aria-label="Transaction actions" className="flex flex-wrap gap-2">
          {actions.map((action) => (
            <Link
              key={action}
              to={`/accounts/${data.id}/transactions/new?type=${action}`}
              className={cn(buttonVariants({ variant: "outline", size: "sm" }))}
            >
              {action}
            </Link>
          ))}
        </nav>
      ) : null}
      {conflict ? <ConflictNotice /> : null}
      <Messages messages={messages} />
      <Card>
        <CardContent>
          <DetailGrid>
            <DetailItem label="Type">{data.type}</DetailItem>
            <DetailItem label="Currency">{data.currency}</DetailItem>
            <DetailItem label="Status">
              <StatusBadge status={data.status} />
            </DetailItem>
            <DetailItem label="Cash balance">{data.cashBalance}</DetailItem>
          </DetailGrid>
        </CardContent>
      </Card>
      <Section title="Holdings">
        {items.length === 0 ? <Muted>No holdings.</Muted> : (
          <Card className="gap-0 py-0">
            <CardContent className="px-0">
              <Table aria-label="Holdings">
                <TableHeader>
                  <TableRow>
                    <TableHead>Symbol</TableHead>
                    <TableHead>Name</TableHead>
                    <TableHead>Quantity</TableHead>
                    <TableHead>Cost</TableHead>
                    <TableHead>Cost currency</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {items.map((item) => (
                    <TableRow key={item.id}>
                      <TableCell>{item.symbol}</TableCell>
                      <TableCell>{item.name}</TableCell>
                      <TableCell>{item.quantity}</TableCell>
                      <TableCell>{item.cost}</TableCell>
                      <TableCell>{item.costCurrency}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        )}
      </Section>
      <Section title="Activity">
        {(transactions.data?.items.length ?? 0) === 0 ? <Muted>No activity.</Muted> : (
          <Card className="gap-0 py-0">
            <CardContent className="px-0">
              <Table aria-label="Activity">
                <TableHeader>
                  <TableRow>
                    <TableHead>Transaction</TableHead>
                    <TableHead>Type</TableHead>
                    <TableHead>Booked at</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {transactions.data?.items.map((item) => (
                    <TableRow key={item.id}>
                      <TableCell>
                        <Link to={`/transactions/${item.id}`} className="font-medium underline-offset-4 hover:underline">
                          {item.id}
                        </Link>
                      </TableCell>
                      <TableCell>{item.type}</TableCell>
                      <TableCell>{item.bookedAt}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        )}
      </Section>
    </Page>
  );
}
