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
  SelectField,
  StatusBadge,
} from "@/components/portal";
import { Button, buttonVariants } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { SelectItem } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import {
  useCloseAccountMutation,
  useGetAccountHoldingsQuery,
  useGetAccountQuery,
  useReopenAccountMutation,
} from "@/features/accounts/accountsApi";
import { useGetInstrumentsQuery } from "@/features/instruments/instrumentsApi";
import {
  useAssignPortfolioMutation,
  useGetAccountPortfolioQuery,
  useGetPortfolioQuery,
  useGetPortfoliosQuery,
  useRebalanceAccountMutation,
  useUnassignPortfolioMutation,
} from "@/features/portfolios/portfoliosApi";
import { rateLine } from "@/features/transactions/fxFormat";
import { useGetTransactionsQuery } from "@/features/transactions/transactionsApi";
import { formatInstant } from "@/shared/time/instant";
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
  const eligible = Boolean(
    account.data && account.data.status === "Open" && (account.data.type === "Brokerage" || account.data.type === "Other"),
  );
  const assignment = useGetAccountPortfolioQuery(id ?? "", { skip: !id || !eligible });
  const targetPortfolio = useGetPortfolioQuery(assignment.data?.portfolioId ?? "", {
    skip: assignment.data?.status !== "Active",
  });
  const catalog = useGetInstrumentsQuery(undefined, { skip: assignment.data?.status !== "Active" });
  const portfolioChoices = useGetPortfoliosQuery({ pageSize: 100 }, { skip: !eligible });
  const [closeAccount] = useCloseAccountMutation();
  const [reopenAccount] = useReopenAccountMutation();
  const [assignPortfolio] = useAssignPortfolioMutation();
  const [unassignPortfolio] = useUnassignPortfolioMutation();
  const [rebalanceAccount] = useRebalanceAccountMutation();
  const [portfolioId, setPortfolioId] = useState("");
  const [assignStatus, setAssignStatus] = useState<string | null>(null);
  const [messages, setMessages] = useState<string[]>([]);
  const [conflict, setConflict] = useState(false);

  async function onRetry() {
    if (!id) {
      return;
    }
    try {
      const result = await rebalanceAccount(id).unwrap();
      setAssignStatus(result.status);
      setMessages([]);
    } catch (error: unknown) {
      const problems = problemMessages(error);
      setAssignStatus(null);
      setMessages(problems.length > 0 ? problems : ["Rebalance failed. Check the message and try again."]);
    }
  }

  async function onUnassign() {
    if (!id) {
      return;
    }
    try {
      await unassignPortfolio(id).unwrap();
      setAssignStatus(null);
      setPortfolioId("");
      setMessages([]);
    } catch (error: unknown) {
      setMessages(problemMessages(error));
    }
  }

  async function onAssign() {
    if (!id || !portfolioId) {
      return;
    }
    try {
      const result = await assignPortfolio({ accountId: id, portfolioId }).unwrap();
      setAssignStatus(result.status);
      setConflict(false);
      setMessages([]);
    } catch (error: unknown) {
      setAssignStatus(null);
      setConflict(false);
      setMessages(problemMessages(error));
    }
  }

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
  const locked = assignment.isSuccess && assignment.data?.status === "Active";
  const weights = new Map((targetPortfolio.data?.lines ?? []).map((line) => [line.instrumentId, line.targetWeight]));
  const catalogById = new Map((catalog.data?.items ?? []).map((item) => [item.id, item]));
  const held = new Set(items.map((item) => item.instrumentId));
  const holdingRows = [
    ...items.map((item) => ({
      key: item.id,
      symbol: item.symbol,
      name: item.name,
      quantity: String(item.quantity),
      cost: String(item.cost),
      costCurrency: item.costCurrency,
      target: weights.has(item.instrumentId) ? String(weights.get(item.instrumentId)) : "—",
    })),
    ...(targetPortfolio.data?.lines ?? [])
      .filter((line) => !held.has(line.instrumentId))
      .map((line) => {
        const instrument = catalogById.get(line.instrumentId);
        return {
          key: line.instrumentId,
          symbol: instrument?.symbol ?? "—",
          name: instrument?.name ?? "—",
          quantity: "—",
          cost: "—",
          costCurrency: "—",
          target: String(line.targetWeight),
        };
      }),
  ];
  const actions = [
    ...(canPostCash(data.status) ? cashActions : []),
    ...(canPostSecurity(data.type, data.status) && !locked ? securityActions : []),
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
          {canPostCash(data.status) ? (
            <Link
              to={`/accounts/${data.id}/transfer`}
              className={cn(buttonVariants({ variant: "outline", size: "sm" }))}
            >
              Transfer
            </Link>
          ) : null}
          {canPostCash(data.status) ? (
            <Link
              to={`/accounts/${data.id}/convert`}
              className={cn(buttonVariants({ variant: "outline", size: "sm" }))}
            >
              Convert
            </Link>
          ) : null}
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
      {eligible && (portfolioChoices.data || portfolioChoices.isError) ? (
        <Section title="Portfolio">
          {locked ? (
            <div className="flex flex-wrap items-end gap-3">
              <Button type="button" variant="outline" onClick={() => void onUnassign()}>
                Unassign
              </Button>
              <Button type="button" variant="outline" onClick={() => void onRetry()}>
                Rebalance
              </Button>
            </div>
          ) : (
            <div className="flex flex-wrap items-end gap-3">
              <SelectField
                id="account-portfolio"
                label="Portfolio"
                value={portfolioId}
                onChange={(event) => setPortfolioId(event.target.value)}
                className="w-64"
              >
                <SelectItem value="">Select a portfolio</SelectItem>
                {(portfolioChoices.data?.items ?? [])
                  .filter((item) => item.isActive)
                  .map((item) => (
                    <SelectItem key={item.id} value={item.id}>
                      {item.name}
                    </SelectItem>
                  ))}
              </SelectField>
              <Button type="button" onClick={() => void onAssign()} disabled={!portfolioId}>
                Assign
              </Button>
            </div>
          )}
          {locked ? (
            <p>
              {assignment.data?.lastRebalanceStatus
                ? assignment.data.lastRebalanceStatus === "Failed"
                  ? assignment.data.lastRebalanceMessage || "Rebalance failed. Check the message and try again."
                  : assignment.data.lastRebalanceStatus
                : "No rebalance has run yet."}
            </p>
          ) : null}
          {assignStatus ? <p>{assignStatus}</p> : null}
          {locked && assignment.data?.lastRebalanceStatus === "Failed" ? (
            <Button type="button" variant="outline" onClick={() => void onRetry()}>
              Retry
            </Button>
          ) : null}
        </Section>
      ) : null}
      <Section title="Holdings">
        {items.length === 0 ? <Muted>No holdings.</Muted> : (
          <Card className="gap-0 py-0">
            <CardContent className="px-0">
              <Table aria-label="Holdings" className={locked ? "max-sm:[&_[data-slot=table-cell]]:block max-sm:[&_[data-slot=table-head]]:block" : undefined}>
                <TableHeader>
                  <TableRow>
                    <TableHead>Symbol</TableHead>
                    <TableHead>Name</TableHead>
                    <TableHead>Quantity</TableHead>
                    <TableHead>Cost</TableHead>
                    <TableHead>Cost currency</TableHead>
                    {locked ? <TableHead>Target</TableHead> : null}
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {(locked ? holdingRows : items.map((item) => ({
                    key: item.id,
                    symbol: item.symbol,
                    name: item.name,
                    quantity: String(item.quantity),
                    cost: String(item.cost),
                    costCurrency: item.costCurrency,
                    target: "",
                  }))).map((item) => (
                    <TableRow key={item.key}>
                      <TableCell>{item.symbol}</TableCell>
                      <TableCell>{item.name}</TableCell>
                      <TableCell>{item.quantity}</TableCell>
                      <TableCell>{item.cost}</TableCell>
                      <TableCell>{item.costCurrency}</TableCell>
                      {locked ? <TableCell>{item.target}</TableCell> : null}
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
                    <TableHead>Amount</TableHead>
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
                      <TableCell>{formatInstant(item.bookedAt)}</TableCell>
                      <TableCell>
                        {item.cash
                          .filter((leg) => leg.accountId === data.id)
                          .map((leg) => `${leg.amount} ${leg.currency}`)
                          .join(", ")}
                        {item.fx ? <p className="text-muted-foreground text-xs">{rateLine(item.fx)}</p> : null}
                      </TableCell>
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
