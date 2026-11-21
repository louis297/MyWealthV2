import { type FormEvent, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { Messages, Muted, Page, PageHeading, recordLinkClass, SelectField, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { SelectItem } from "@/components/ui/select";
import { useGetAccountQuery } from "@/features/accounts/accountsApi";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { useGetNominatedBankAccountsQuery } from "@/features/nominatedBankAccounts/nominatedBankAccountsApi";
import { problemMessages } from "@/features/statementLines/statementLineLabels";
import { useRequestWithdrawalMutation } from "@/features/withdrawals/withdrawalsApi";
import { formatMoney } from "@/shared/format/money";

/** The last four letters or digits as ****1234, as the withdrawal item shows a payee (withdrawals R25). */
export function maskAccountNumber(number: string) {
  const symbols = [...number].filter((ch) => /[0-9A-Za-z]/.test(ch));
  return symbols.length < 5 ? "****" : `****${symbols.slice(-4).join("")}`;
}

function FieldError({ messages }: { messages?: string[] }) {
  return messages?.length ? <p className="text-sm text-destructive">{messages.join(" ")}</p> : null;
}

/** D45: request a withdrawal from a Custodial account to one of the Customer's verified bank accounts. */
export function WithdrawPage() {
  const { id: accountId = "" } = useParams();
  const navigate = useNavigate();
  const [idempotencyKey] = useState(() => crypto.randomUUID());
  const account = useGetAccountQuery(accountId, { skip: !accountId });
  const currencies = useGetCurrenciesQuery();
  const payees = useGetNominatedBankAccountsQuery(
    { customerId: account.data?.customerId, status: "Verified", currency: account.data?.currency },
    { skip: !account.data },
  );
  const [request, { isLoading }] = useRequestWithdrawalMutation();
  const [payeeId, setPayeeId] = useState("");
  const [amount, setAmount] = useState("");
  const [reference, setReference] = useState("");
  const [memo, setMemo] = useState("");
  const [reviewing, setReviewing] = useState(false);
  const [fields, setFields] = useState<Record<string, string[]>>({});
  const [messages, setMessages] = useState<string[]>([]);

  if (!account.data) {
    return <Muted>Loading…</Muted>;
  }

  const from = account.data;
  const places = currencies.data?.find((item) => item.code === from.currency)?.decimalPlaces;
  const choices = payees.data?.items ?? [];
  const payee = choices.find((item) => item.id === payeeId);

  function onReview(event: FormEvent) {
    event.preventDefault();
    setReviewing(true);
  }

  async function onConfirm() {
    try {
      const created = await request({
        body: {
          accountId: from.id,
          nominatedBankAccountId: payeeId,
          amount: Number(amount),
          reference: reference.trim() === "" ? null : reference.trim(),
          memo: memo.trim() === "" ? null : memo.trim(),
        },
        idempotencyKey,
      }).unwrap();
      void navigate(`/withdrawals/${created.id}`);
    } catch (error: unknown) {
      const result = problemMessages(error);
      setFields(result.fields);
      setMessages(result.messages);
      setReviewing(false);
    }
  }

  return (
    <Page className="max-w-lg">
      <PageHeading title="Withdraw" />
      <Messages messages={messages} />
      {payees.data && choices.length === 0 ? (
        <p className="text-sm">
          {`No verified bank account in ${from.currency}. Add one on the `}
          <Link to={`/customers/${from.customerId}`} className={recordLinkClass}>
            customer page
          </Link>
          .
        </p>
      ) : (
        <Card>
          <CardContent>
            <form className="flex flex-col gap-4" onSubmit={onReview}>
              <SelectField id="withdraw-to" label="To" value={payeeId} required
                onChange={(event) => { setPayeeId(event.target.value); setReviewing(false); }}>
                <SelectItem value="">Select a bank account</SelectItem>
                {choices.map((item) => (
                  <SelectItem key={item.id} value={item.id}>
                    {`${item.bankName} ${item.accountName} ${maskAccountNumber(item.accountNumber)}`}
                  </SelectItem>
                ))}
              </SelectField>
              <FieldError messages={fields.nominatedbankaccountid} />
              <TextField id="withdraw-amount" label="Amount" inputMode="decimal" value={amount} required
                onChange={(event) => { setAmount(event.target.value); setReviewing(false); }} />
              <p className="text-muted-foreground text-sm">
                {`Available ${formatMoney(from.availableCash ?? from.cashBalance, from.currency, places ?? 2)}`}
              </p>
              <FieldError messages={fields.amount} />
              <TextField id="withdraw-reference" label="Reference" value={reference} maxLength={18}
                placeholder={from.depositReference ?? ""}
                onChange={(event) => { setReference(event.target.value); setReviewing(false); }} />
              <FieldError messages={fields.reference} />
              <TextField id="withdraw-memo" label="Memo" value={memo} maxLength={200}
                onChange={(event) => { setMemo(event.target.value); setReviewing(false); }} />
              <FieldError messages={fields.memo} />
              <Button type="submit" variant="outline">
                Review
              </Button>
            </form>
          </CardContent>
        </Card>
      )}
      {reviewing && payee ? (
        <Card>
          <CardContent className="flex flex-col gap-3">
            <p>
              {`Withdraw ${formatMoney(Number(amount), from.currency, places ?? 2)} from ${from.name} to ${payee.bankName} ${payee.accountName} ${maskAccountNumber(payee.accountNumber)}. A firm administrator must approve it.`}
            </p>
            <Button type="button" disabled={isLoading} onClick={() => void onConfirm()}>
              Confirm withdrawal
            </Button>
          </CardContent>
        </Card>
      ) : null}
    </Page>
  );
}
