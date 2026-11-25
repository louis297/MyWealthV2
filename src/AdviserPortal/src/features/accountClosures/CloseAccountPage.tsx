import { type FormEvent, type ReactNode, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import {
  DetailGrid,
  DetailItem,
  Messages,
  Muted,
  Page,
  PageHeading,
  recordLinkClass,
  Section,
  SelectField,
  TextField,
} from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { SelectItem } from "@/components/ui/select";
import { useAppSelector } from "@/app/hooks";
import { useRequestAccountClosureMutation } from "@/features/accountClosures/accountClosuresApi";
import { useGetAccountHoldingsQuery, useGetAccountQuery } from "@/features/accounts/accountsApi";
import { useGetAdjustmentRequestsQuery } from "@/features/adjustmentRequests/adjustmentRequestsApi";
import { closureAmounts, isCustodial } from "@/features/accounts/custodyRules";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { useGetNominatedBankAccountsQuery } from "@/features/nominatedBankAccounts/nominatedBankAccountsApi";
import { orderFilters } from "@/features/orders/orderLabels";
import { useGetOrdersQuery } from "@/features/orders/ordersApi";
import { useGetAccountPortfolioQuery } from "@/features/portfolios/portfoliosApi";
import { canManageAdjustments } from "@/features/session/roles";
import { problemMessages } from "@/features/statementLines/statementLineLabels";
import { maskAccountNumber } from "@/features/withdrawals/WithdrawPage";
import { useGetWithdrawalsQuery } from "@/features/withdrawals/withdrawalsApi";
import { formatMoney } from "@/shared/format/money";

/** Account closure R5, R6: the sentences of the preconditions, as the server words them. */
export const holdingSentence = "Sell every holding before closing the account.";
export const openOrderSentence = "Open orders must be filled, cancelled or expired before the account can be closed.";
export const unsettledSentence = "Trades must settle before the account can be closed.";
export const portfolioSentence = "Unassign the portfolio before closing the account.";
export const openWithdrawalSentence = "Open withdrawals must be paid, rejected or cancelled before the account can be closed.";
/** Adjustment review R17. */
export const openAdjustmentSentence =
  "Open adjustment requests must be approved, rejected or cancelled before the account can be closed.";

function FieldError({ messages }: { messages?: string[] }) {
  return messages?.length ? <p className="text-sm text-destructive">{messages.join(" ")}</p> : null;
}

function Check({ label, passed, children }: { label: string; passed: boolean; children?: ReactNode }) {
  return (
    <li className="flex flex-col gap-1">
      <span className="flex items-center gap-2">
        <span aria-hidden="true">{passed ? "✓" : "✗"}</span>
        <span>{label}</span>
      </span>
      {passed ? null : <span className="text-sm text-destructive">{children}</span>}
    </li>
  );
}

/**
 * D54: close a Custodial account with an account closure. A checklist of the preconditions (account-closure R5, R6),
 * the cash balance, the payout and the remainder written off (R7), the payee when there is a payout (R8), a memo,
 * then a review. Confirm posts `POST /accounts/{id}/closures` with one Idempotency-Key per form open.
 */
export const accruedInterestSentence =
  "Interest accrued so far is added before the payout. The amount to pay out may be higher than shown.";

export function CloseAccountPage() {
  const { id: accountId = "" } = useParams();
  const navigate = useNavigate();
  const [idempotencyKey] = useState(() => crypto.randomUUID());
  const account = useGetAccountQuery(accountId, { skip: !accountId });
  const holdings = useGetAccountHoldingsQuery(accountId, { skip: !accountId });
  const assignment = useGetAccountPortfolioQuery(accountId, { skip: !accountId });
  const open = useGetWithdrawalsQuery({ accountId, status: "Requested,Approved,Sent" }, { skip: !accountId });
  const orders = useGetOrdersQuery({ accountId, status: orderFilters.Open.status }, { skip: !accountId });
  // adjustment-review D105: a TenantAdmin sees the open adjustment requests; an Adviser gets the server's refusal.
  const role = useAppSelector((state) => state.session.currentUser?.role);
  const adjustments = useGetAdjustmentRequestsQuery(
    { accountId, status: "Requested", pageSize: 1 },
    { skip: !accountId || !canManageAdjustments(role) },
  );
  const currencies = useGetCurrenciesQuery();
  const places = currencies.data?.find((item) => item.code === account.data?.currency)?.decimalPlaces;
  const amounts = account.data ? closureAmounts(account.data.cashBalance, places) : { payout: 0, remainder: 0 };
  const payees = useGetNominatedBankAccountsQuery(
    { customerId: account.data?.customerId, status: "Verified", currency: account.data?.currency },
    { skip: !account.data || amounts.payout <= 0 },
  );
  const [requestClosure, { isLoading }] = useRequestAccountClosureMutation();
  const [payeeId, setPayeeId] = useState("");
  const [memo, setMemo] = useState("");
  const [reviewing, setReviewing] = useState(false);
  const [fields, setFields] = useState<Record<string, string[]>>({});
  const [messages, setMessages] = useState<string[]>([]);

  if (!account.data || !holdings.data || !open.data || assignment.isLoading) {
    return <Muted>Loading…</Muted>;
  }

  const data = account.data;
  if (!isCustodial(data) || data.status !== "Open") {
    return <Muted>Only an open Custodial account is closed with an account closure.</Muted>;
  }

  const activeAssignment = assignment.data?.status === "Active" ? assignment.data : null;
  const openWithdrawal = open.data.items[0];
  // D75 (orders R47): a live order and a trade that has not settled block the closure.
  const openOrder = orders.data?.items[0];
  const unsettled = (data.unsettledCash ?? 0) > 0;
  const openAdjustment = adjustments.data?.items[0];
  const checksPass = holdings.data.items.length === 0 && !activeAssignment && !openWithdrawal && !openOrder && !unsettled
    && !openAdjustment;
  const withPayout = amounts.payout > 0;
  const choices = payees.data?.items ?? [];
  const payee = choices.find((item) => item.id === payeeId);
  const noPayee = withPayout && payees.data !== undefined && choices.length === 0;
  const ready = checksPass && memo.trim() !== "" && (!withPayout || payee !== undefined);
  const payout = formatMoney(amounts.payout, data.currency, places);
  const remainder = formatMoney(amounts.remainder, data.currency, places);

  function onReview(event: FormEvent) {
    event.preventDefault();
    if (ready) {
      setReviewing(true);
    }
  }

  async function onConfirm() {
    try {
      await requestClosure({
        accountId: data.id,
        body: { nominatedBankAccountId: withPayout ? payeeId : null, memo: memo.trim() },
        idempotencyKey,
      }).unwrap();
      void navigate(`/accounts/${data.id}`);
    } catch (error: unknown) {
      const result = problemMessages(error);
      setFields(result.fields);
      setMessages(result.messages);
      setReviewing(false);
    }
  }

  return (
    <Page className="max-w-lg">
      <PageHeading title={`Close ${data.name}`} />
      <Messages messages={[...messages, ...(fields.accountid ?? [])]} />
      <Section title="Before closing">
        <ul className="flex flex-col gap-2">
          <Check label="No holdings" passed={holdings.data.items.length === 0}>
            {holdingSentence}{" "}
            <Link to={`/accounts/${data.id}`} className={recordLinkClass}>View the holdings</Link>
          </Check>
          <Check label="No portfolio" passed={!activeAssignment}>
            {portfolioSentence}{" "}
            {activeAssignment ? (
              <Link to={`/portfolios/${activeAssignment.portfolioId}`} className={recordLinkClass}>View the portfolio</Link>
            ) : null}
          </Check>
          <Check label="No open orders" passed={!openOrder}>
            {openOrderSentence}{" "}
            {openOrder ? (
              <Link to={`/orders/${openOrder.id}`} className={recordLinkClass}>View the order</Link>
            ) : null}
          </Check>
          <Check label="Every trade settled" passed={!unsettled}>
            {unsettledSentence}
          </Check>
          <Check label="No open withdrawals" passed={!openWithdrawal}>
            {openWithdrawalSentence}{" "}
            {openWithdrawal ? (
              <Link to={`/withdrawals/${openWithdrawal.id}`} className={recordLinkClass}>View the withdrawal</Link>
            ) : null}
          </Check>
          {canManageAdjustments(role) ? (
            <Check label="No open adjustment requests" passed={!openAdjustment}>
              {openAdjustmentSentence}{" "}
              {openAdjustment ? (
                <Link to={`/adjustments/${openAdjustment.id}`} className={recordLinkClass}>View the adjustment</Link>
              ) : null}
            </Check>
          ) : null}
        </ul>
      </Section>
      <Card>
        <CardContent>
          <DetailGrid>
            <DetailItem label="Cash balance">{formatMoney(data.cashBalance, data.currency, places)}</DetailItem>
            <DetailItem label="To pay out">{payout}</DetailItem>
            {amounts.remainder > 0 ? <DetailItem label="Written off">{remainder}</DetailItem> : null}
          </DetailGrid>
          {(data.accruedInterest ?? 0) > 0 ? (
            // interest-run D62, R33: the closure posts the interest accrued so far before the payout.
            <p className="text-muted-foreground mt-3 text-sm">{accruedInterestSentence}</p>
          ) : null}
        </CardContent>
      </Card>
      <Card>
        <CardContent>
          <form className="flex flex-col gap-4" onSubmit={onReview}>
            {withPayout && noPayee ? (
              <p className="text-sm">
                {`No verified bank account in ${data.currency}. Add one on the `}
                <Link to={`/customers/${data.customerId}`} className={recordLinkClass}>
                  customer page
                </Link>
                .
              </p>
            ) : null}
            {withPayout && !noPayee ? (
              <SelectField id="close-to" label="To" value={payeeId}
                onChange={(event) => { setPayeeId(event.target.value); setReviewing(false); }}>
                <SelectItem value="">Select a bank account</SelectItem>
                {choices.map((item) => (
                  <SelectItem key={item.id} value={item.id}>
                    {`${item.bankName} ${item.accountName} ${maskAccountNumber(item.accountNumber)}`}
                  </SelectItem>
                ))}
              </SelectField>
            ) : null}
            <FieldError messages={fields.nominatedbankaccountid} />
            <TextField id="close-memo" label="Memo" value={memo} maxLength={200} required
              onChange={(event) => { setMemo(event.target.value); setReviewing(false); }} />
            <FieldError messages={fields.memo} />
            <Button type="submit" variant="outline" disabled={!ready}>
              Review
            </Button>
          </form>
        </CardContent>
      </Card>
      {reviewing && ready ? (
        <Card>
          <CardContent className="flex flex-col gap-3">
            {withPayout && payee ? (
              <p>
                {`Pay ${payout} to ${payee.bankName} ${payee.accountName} ${maskAccountNumber(payee.accountNumber)} and close the account when the bank has paid. A firm administrator must approve the payment. Until then the account accepts no new activity.`}
              </p>
            ) : (
              <>
                <p>Close the account now.</p>
                {amounts.remainder > 0 ? <p>{`${remainder} is written off.`}</p> : null}
              </>
            )}
            <Button type="button" disabled={isLoading || !checksPass} onClick={() => void onConfirm()}>
              Confirm
            </Button>
          </CardContent>
        </Card>
      ) : null}
    </Page>
  );
}
