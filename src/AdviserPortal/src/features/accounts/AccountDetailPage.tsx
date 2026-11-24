import { useState } from "react";
import { useAppSelector } from "@/app/hooks";
import { canAccessAccounts, roles } from "@/features/session/roles";
import { DividendReinvestmentSection } from "@/features/accounts/DividendReinvestmentSection";
import { Link, useNavigate, useParams } from "react-router";
import { AssignmentRebalanceLine } from "@/features/rebalances/AssignmentRebalanceLine";
import { noChangeSentence } from "@/features/rebalances/rebalanceLabels";
import {
  ConflictNotice,
  DetailGrid,
  DetailItem,
  Messages,
  Muted,
  Page,
  PageHeading,
  recordLinkClass,
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
  useGetDepositInstructionsQuery,
  useReopenAccountMutation,
} from "@/features/accounts/accountsApi";
import { allowsManualEntry, isCustodial } from "@/features/accounts/custodyRules";
import { AccountOrdersSection } from "@/features/orders/AccountOrdersSection";
import { AccountWithdrawalsSection } from "@/features/withdrawals/AccountWithdrawalsSection";
import { AccountTransfersSection } from "@/features/inKindTransfers/AccountTransfersSection";
import { withdrawalStatusLabel } from "@/features/withdrawals/withdrawalLabels";
import { useGetWithdrawalQuery } from "@/features/withdrawals/withdrawalsApi";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { useGetInstrumentsQuery } from "@/features/instruments/instrumentsApi";
import {
  type RebalanceProposal,
  useAssignPortfolioMutation,
  useGetAccountPortfolioQuery,
  useGetPortfolioQuery,
  useGetPortfoliosQuery,
  useRebalanceAccountMutation,
  useUnassignPortfolioMutation,
} from "@/features/portfolios/portfoliosApi";
import { shortDay } from "@/features/orders/orderLabels";
import { rateLine } from "@/features/transactions/fxFormat";
import { canReverse, ReverseTransactionButton, transactionTypeLabel } from "@/features/transactions/ReverseTransactionButton";
import { type TransactionCash, useGetTransactionsQuery } from "@/features/transactions/transactionsApi";
import { formatMoney, sumAmounts } from "@/shared/format/money";
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

/**
 * trade-fees R22: the activity row shows the sum of this account's cash legs, and the `Fee` legs in it
 * on a second line (adviser-portal-fees F13).
 */
function ActivityAmount({ legs, decimalPlaces }: { legs: TransactionCash[]; decimalPlaces: number | undefined }) {
  if (legs.length === 0) {
    return null;
  }
  const currency = legs[0].currency;
  const fees = legs.filter((leg) => leg.role === "Fee");
  const taxes = legs.filter((leg) => leg.role === "Tax");
  return (
    <>
      {`${sumAmounts(legs.map((leg) => leg.amount))} ${currency}`}
      {fees.length > 0 ? (
        <p className="text-muted-foreground text-xs">
          {`incl. fee ${formatMoney(Math.abs(sumAmounts(fees.map((leg) => leg.amount))), currency, decimalPlaces)}`}
        </p>
      ) : null}
      {taxes.length > 0 ? (
        // interest-run D61: the tax withheld from an interest posting, as a fee is shown.
        <p className="text-muted-foreground text-xs">
          {`incl. tax ${formatMoney(Math.abs(sumAmounts(taxes.map((leg) => leg.amount))), currency, decimalPlaces)}`}
        </p>
      ) : null}
    </>
  );
}

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
  const role = useAppSelector((state) => state.session.currentUser?.role);
  const account = useGetAccountQuery(id ?? "", { skip: !id });
  const holdings = useGetAccountHoldingsQuery(id ?? "", { skip: !id || !account.data });
  const transactions = useGetTransactionsQuery(
    { accountId: id },
    { skip: !id || !account.data },
  );
  // D55 (account-closure R11): an Account with a closure in progress is closing.
  const closing = account.data?.closure?.status === "InProgress";
  const closureWithdrawal = useGetWithdrawalQuery(account.data?.closure?.withdrawalId ?? "", {
    skip: !closing || !account.data?.closure?.withdrawalId,
  });
  // P13: only an Open Custodial account can hold a portfolio (custody-modes R14).
  const eligible = Boolean(account.data && account.data.status === "Open" && isCustodial(account.data));
  const assignment = useGetAccountPortfolioQuery(id ?? "", { skip: !id || !eligible });
  // D32: deposit instructions are read only for an Open Custodial account (client-money-accounts R21).
  const depositInstructions = useGetDepositInstructionsQuery(id ?? "", { skip: !id || !eligible });
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
  const currencies = useGetCurrenciesQuery();
  const [portfolioId, setPortfolioId] = useState("");
  const [assignStatus, setAssignStatus] = useState<string | null>(null);
  const [messages, setMessages] = useState<string[]>([]);
  const [conflict, setConflict] = useState(false);
  const navigate = useNavigate();

  /** P14: a proposal opens its page; nothing to trade says so; a refusal shows the server message. */
  function onProposal(result: RebalanceProposal) {
    setMessages([]);
    if (result.status === "Proposed") {
      setAssignStatus(null);
      void navigate(`/rebalances/${result.rebalanceId}`);
      return;
    }
    setAssignStatus(noChangeSentence);
  }

  async function onRebalance() {
    if (!id) {
      return;
    }
    try {
      onProposal(await rebalanceAccount(id).unwrap());
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
      setConflict(false);
      onProposal(result);
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
      reserved: item.reservedQuantity ?? 0,
      unsettled: item.unsettledQuantity ?? 0,
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
          reserved: 0,
          unsettled: 0,
          cost: "—",
          costCurrency: "—",
          target: String(line.targetWeight),
        };
      }),
  ];
  const places = currencies.data?.find((currency) => currency.code === data.currency)?.decimalPlaces;
  // D55: a closing account accepts no new activity, so every action is hidden.
  const open = data.status === "Open" && !closing;
  const actions = closing ? [] : [
    ...(canPostCash(data.status) ? cashActions : []),
    ...(canPostSecurity(data.type, data.status) && !locked ? securityActions : []),
  ].filter((action) => allowsManualEntry(data.custodyMode, action));
  const withdrawable = open && isCustodial(data);
  const closedByClosure = data.status === "Closed" && data.closure?.status === "Completed" && data.closure.completedAt
    ? formatInstant(data.closure.completedAt).slice(0, 10)
    : null;

  return (
    <Page>
      <PageHeading
        title={data.name}
        actions={
          closing ? null : data.status === "Open" && isCustodial(data) ? (
            // D53: a Custodial account is closed with an account closure.
            <Link to={`/accounts/${data.id}/close`} className={cn(buttonVariants({ variant: "destructive" }))}>
              Close
            </Link>
          ) : data.status === "Open" ? (
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
      {closing && data.closure ? (
        <Card>
          <CardContent className="flex flex-col gap-2">
            <p>
              {`Being closed. ${formatMoney(data.closure.payoutAmount, data.currency, places)} is being paid out (${
                closureWithdrawal.data ? withdrawalStatusLabel(closureWithdrawal.data.status) : "…"
              }). The account accepts no new activity.`}
            </p>
            {data.closure.withdrawalId ? (
              <Link to={`/withdrawals/${data.closure.withdrawalId}`} className={recordLinkClass}>
                View the withdrawal
              </Link>
            ) : null}
          </CardContent>
        </Card>
      ) : null}
      {closedByClosure ? <p className="text-muted-foreground text-sm">{`Closed by account closure on ${closedByClosure}`}</p> : null}
      {actions.length > 0 || withdrawable ? (
        <nav aria-label="Transaction actions" className="flex flex-wrap gap-2">
          {withdrawable && (data.type === "Brokerage" || data.type === "Other") ? (
            // D69: a trade on a Custodial account is an order; an assigned account follows its portfolio.
            locked ? (
              <span className="flex items-center gap-2">
                <Button type="button" variant="outline" size="sm" disabled>
                  Place order
                </Button>
                <span className="text-muted-foreground text-sm">This account follows a portfolio.</span>
              </span>
            ) : (
              <Link to={`/accounts/${data.id}/order`} className={cn(buttonVariants({ variant: "outline", size: "sm" }))}>
                Place order
              </Link>
            )
          ) : null}
          {withdrawable ? (
            // in-kind-transfers D86: securities move in or out by an in-kind transfer.
            locked ? (
              <span className="flex items-center gap-2">
                <Button type="button" variant="outline" size="sm" disabled>
                  Transfer securities
                </Button>
                <span className="text-muted-foreground text-sm">This account follows a portfolio.</span>
              </span>
            ) : (
              <Link to={`/accounts/${data.id}/transfer-securities`} className={cn(buttonVariants({ variant: "outline", size: "sm" }))}>
                Transfer securities
              </Link>
            )
          ) : null}
          {actions.map((action) => (
            <Link
              key={action}
              to={`/accounts/${data.id}/transactions/new?type=${action}`}
              className={cn(buttonVariants({ variant: "outline", size: "sm" }))}
            >
              {action}
            </Link>
          ))}
          {canPostCash(data.status) && !closing ? (
            <Link
              to={`/accounts/${data.id}/transfer`}
              className={cn(buttonVariants({ variant: "outline", size: "sm" }))}
            >
              Transfer
            </Link>
          ) : null}
          {canPostCash(data.status) && !closing ? (
            <Link
              to={`/accounts/${data.id}/convert`}
              className={cn(buttonVariants({ variant: "outline", size: "sm" }))}
            >
              Convert
            </Link>
          ) : null}
          {role === roles.tenantAdmin && open && isCustodial(data) ? (
            <Link
              to={`/accounts/${data.id}/adjust`}
              className={cn(buttonVariants({ variant: "outline", size: "sm" }))}
            >
              Adjust
            </Link>
          ) : null}
          {withdrawable ? (
            <Link
              to={`/accounts/${data.id}/withdraw`}
              className={cn(buttonVariants({ variant: "outline", size: "sm" }))}
            >
              Withdraw
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
            <DetailItem label="Custody">{data.custodyMode}</DetailItem>
            <DetailItem label="Currency">{data.currency}</DetailItem>
            <DetailItem label="Status">
              <StatusBadge status={data.status} />
            </DetailItem>
            <DetailItem label={isCustodial(data) ? "Cash" : "Cash balance"}>{data.cashBalance}</DetailItem>
            {isCustodial(data) && (data.reservedCash ?? 0) !== 0 ? (
              // D69: withdrawals and orders.
              <DetailItem label="Reserved">{formatMoney(data.reservedCash ?? 0, data.currency, places)}</DetailItem>
            ) : null}
            {isCustodial(data) ? (
              <DetailItem label="Available">{formatMoney(data.availableCash ?? data.cashBalance, data.currency, places)}</DetailItem>
            ) : null}
            {isCustodial(data) && (data.unsettledCash ?? 0) !== 0 ? (
              <DetailItem label="Not settled">{formatMoney(data.unsettledCash ?? 0, data.currency, places)}</DetailItem>
            ) : null}
            {isCustodial(data) ? (
              <DetailItem label="Available to withdraw">
                {formatMoney(data.withdrawableCash ?? data.availableCash ?? data.cashBalance, data.currency, places)}
              </DetailItem>
            ) : null}
            {isCustodial(data) && data.depositReference ? (
              <DetailItem label="Deposit reference">{data.depositReference}</DetailItem>
            ) : null}
            {isCustodial(data) && (data.accruedInterest ?? 0) > 0 ? (
              // interest-run D62: interest accrued and not yet paid, to 4 decimals.
              <DetailItem label="Interest accrued, not yet paid">
                {`${(data.accruedInterest ?? 0).toFixed(4)} ${data.currency}${data.interestAccruedThrough ? ` up to ${data.interestAccruedThrough}` : ""}`}
              </DetailItem>
            ) : null}
          </DetailGrid>
        </CardContent>
      </Card>
      {isCustodial(data) ? (
        <DividendReinvestmentSection
          account={data}
          canManage={canAccessAccounts(role)}
          followsPortfolio={assignment.data?.status === "Active"}
        />
      ) : null}
      {eligible ? (
        <Section title="Deposit instructions">
          {depositInstructions.data ? (
            <Card>
              <CardContent>
                <DetailGrid>
                  <DetailItem label="Bank">{depositInstructions.data.bankName}</DetailItem>
                  <DetailItem label="Account name">{depositInstructions.data.accountName}</DetailItem>
                  <DetailItem label="Account number">{depositInstructions.data.accountNumber}</DetailItem>
                  <DetailItem label="Reference">{depositInstructions.data.reference}</DetailItem>
                  <DetailItem label="Currency">{depositInstructions.data.currency}</DetailItem>
                </DetailGrid>
                <p className="text-muted-foreground mt-3 text-sm">
                  Deposits are credited from the bank statement when the customer pays in with this reference.
                </p>
              </CardContent>
            </Card>
          ) : isNotFound(depositInstructions.error) ? (
            <Muted>{`No client money account in ${data.currency}. Ask the platform operator to add one.`}</Muted>
          ) : depositInstructions.isError ? (
            <Muted>Deposit instructions could not be loaded.</Muted>
          ) : (
            <Muted>Loading…</Muted>
          )}
        </Section>
      ) : null}
      {eligible && !closing && (portfolioChoices.data || portfolioChoices.isError) ? (
        <Section title="Portfolio">
          {locked ? (
            <div className="flex flex-wrap items-end gap-3">
              <Button type="button" variant="outline" onClick={() => void onUnassign()}>
                Unassign
              </Button>
              <Button type="button" variant="outline" onClick={() => void onRebalance()}>
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
          {locked ? <AssignmentRebalanceLine rebalance={assignment.data?.rebalance ?? null} /> : null}
          {assignStatus ? <p>{assignStatus}</p> : null}
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
                    reserved: item.reservedQuantity ?? 0,
                    unsettled: item.unsettledQuantity ?? 0,
                    cost: String(item.cost),
                    costCurrency: item.costCurrency,
                    target: "",
                  }))).map((item) => (
                    <TableRow key={item.key}>
                      <TableCell>{item.symbol}</TableCell>
                      <TableCell>{item.name}</TableCell>
                      <TableCell>
                        {item.quantity}
                        {/* D69: the part live sell orders reserve. */}
                        {item.reserved > 0 ? <p className="text-muted-foreground text-xs">{`${item.reserved} reserved`}</p> : null}
                        {/* in-kind-transfers D86: bought and not settled yet. */}
                        {item.unsettled > 0 ? <p className="text-muted-foreground text-xs">{`${item.unsettled} not settled`}</p> : null}
                      </TableCell>
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
      {isCustodial(data) ? <AccountOrdersSection accountId={data.id} /> : null}
      {isCustodial(data) ? <AccountTransfersSection accountId={data.id} /> : null}
      {isCustodial(data) ? <AccountWithdrawalsSection accountId={data.id} /> : null}
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
                    <TableHead>
                      <span className="sr-only">Actions</span>
                    </TableHead>
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
                      <TableCell>{item.corporateAction ? transactionTypeLabel(item) : item.type}</TableCell>
                      <TableCell>{formatInstant(item.bookedAt)}</TableCell>
                      <TableCell>
                        <ActivityAmount
                          legs={item.cash.filter((leg) => leg.accountId === data.id)}
                          decimalPlaces={currencies.data?.find((currency) => currency.code === data.currency)?.decimalPlaces}
                        />
                        {item.fx ? <p className="text-muted-foreground text-xs">{rateLine(item.fx)}</p> : null}
                        {/* D74: a fill that has not settled. */}
                        {item.execution && !item.execution.settledAt ? (
                          <p className="text-muted-foreground text-xs">{`Settles ${shortDay(item.execution.settlementDate)}`}</p>
                        ) : null}
                      </TableCell>
                      <TableCell>{canReverse(item, role) && !closing ? <ReverseTransactionButton id={item.id} /> : null}</TableCell>
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
