import { type FormEvent, useState } from "react";
import { useNavigate, useParams, useSearchParams } from "react-router";
import { Messages, Muted, Page, PageHeading, SelectField, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { useGetAccountHoldingsQuery, useGetAccountQuery } from "@/features/accounts/accountsApi";
import { useGetInstrumentsQuery } from "@/features/instruments/instrumentsApi";
import { useCreateTransactionMutation } from "@/features/transactions/transactionsApi";

const securityTypes = ["Buy", "Sell", "StockIn", "StockOut", "Split", "Bonus", "Scrip"] as const;
const cashTypes = ["TransferIn", "TransferOut"] as const;
const types = [...securityTypes, ...cashTypes] as const;

function initialType(value: string | null) {
  if (value !== null && types.includes(value as (typeof types)[number])) {
    return value;
  }
  return "Buy";
}

function isCashType(value: string): value is (typeof cashTypes)[number] {
  return (cashTypes as readonly string[]).includes(value);
}

function cashSignError(type: (typeof cashTypes)[number], amount: number) {
  const invalid = type === "TransferIn" ? !(amount > 0) : !(amount < 0);
  return invalid ? "Amount sign is not valid for this transaction type." : null;
}

function isNotFound(error: unknown) {
  return typeof error === "object" && error !== null && "status" in error && error.status === 404;
}

function problemMessages(error: unknown): string[] {
  if (isNotFound(error)) {
    return ["Not found"];
  }
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

export function CreateTransactionPage() {
  const { id: accountId } = useParams();
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const [idempotencyKey] = useState(() => crypto.randomUUID());
  const [type, setType] = useState(() => initialType(params.get("type")));
  const [instrumentId, setInstrumentId] = useState("");
  const [quantity, setQuantity] = useState("");
  const [cost, setCost] = useState("");
  const [amount, setAmount] = useState("");
  const [memo, setMemo] = useState("");
  const [bookedAt, setBookedAt] = useState("");
  const [messages, setMessages] = useState<string[]>([]);
  const account = useGetAccountQuery(accountId ?? "", { skip: !accountId });
  const holdings = useGetAccountHoldingsQuery(accountId ?? "", { skip: !accountId });
  const instruments = useGetInstrumentsQuery();
  const [createTransaction] = useCreateTransactionMutation();

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!accountId) {
      return;
    }

    const trimmedMemo = memo.trim();
    if (isCashType(type)) {
      const signError = cashSignError(type, Number(amount));
      if (signError) {
        setMessages([signError]);
        return;
      }
    }
    try {
      if (isCashType(type)) {
        await createTransaction({
          idempotencyKey,
          body: {
            accountId,
            type,
            amount: Number(amount),
            bookedAt,
            memo: trimmedMemo.length === 0 ? null : trimmedMemo,
          },
        }).unwrap();
      } else {
        await createTransaction({
          idempotencyKey,
          body: {
            accountId,
            type,
            bookedAt,
            memo: null,
            reference: null,
            security: [
              {
                instrumentId,
                quantity: Number(quantity),
                ...(type === "StockIn" ? { cost: Number(cost) } : {}),
              },
            ],
          },
        }).unwrap();
      }
      void navigate(`/accounts/${accountId}`);
    } catch (error: unknown) {
      setMessages(problemMessages(error));
    }
  }

  const offerSecurity = !account.data
    || (account.data.status === "Open"
      && (account.data.type === "Brokerage" || account.data.type === "Other"));
  const offerCash = !account.data || account.data.status === "Open";
  const offeredTypes = [
    ...(offerSecurity ? securityTypes : []),
    ...(offerCash ? cashTypes : []),
  ];
  const cashType = isCashType(type);

  if (isNotFound(account.error)) {
    return <Muted>Not found</Muted>;
  }

  return (
    <Page className="max-w-lg">
      <PageHeading title="New transaction" />
      {account.data ? (
        <p className="text-sm">
          Cash balance <span>{account.data.cashBalance}</span>
        </p>
      ) : null}
      {(holdings.data?.items ?? []).map((item) => (
        <p key={item.id} className="text-sm">
          {item.symbol} quantity <span>{item.quantity}</span>
        </p>
      ))}
      <Messages messages={messages} />
      <Card>
        <CardContent>
          <form className="flex flex-col gap-4" onSubmit={(event) => void onSubmit(event)}>
            <SelectField id="transaction-type" label="Type" value={type} onChange={(event) => setType(event.target.value)}>
              {offeredTypes.map((item) => (
                <option key={item} value={item}>
                  {item}
                </option>
              ))}
            </SelectField>
            {cashType ? (
              <>
                <TextField
                  id="transaction-amount"
                  label="Amount"
                  value={amount}
                  onChange={(event) => setAmount(event.target.value)}
                  required
                />
                <TextField
                  id="transaction-memo"
                  label="Memo"
                  value={memo}
                  onChange={(event) => setMemo(event.target.value)}
                  maxLength={200}
                />
              </>
            ) : (
              <>
                <SelectField
                  id="transaction-instrument"
                  label="Instrument"
                  value={instrumentId}
                  onChange={(event) => setInstrumentId(event.target.value)}
                  required
                >
                  <option value="">Select an instrument</option>
                  {(instruments.data?.items ?? []).map((item) => (
                    <option key={item.id} value={item.id}>
                      {item.symbol} {item.name}
                    </option>
                  ))}
                </SelectField>
                <TextField
                  id="transaction-quantity"
                  label={type === "Split" || type === "Bonus" || type === "Scrip" ? "New total quantity" : "Quantity"}
                  value={quantity}
                  onChange={(event) => setQuantity(event.target.value)}
                  required
                />
                {type === "StockIn" ? (
                  <TextField
                    id="transaction-cost"
                    label="Cost"
                    value={cost}
                    onChange={(event) => setCost(event.target.value)}
                    required
                  />
                ) : null}
              </>
            )}
            <TextField
              id="transaction-booked-at"
              label="Booked at"
              type="datetime-local"
              value={bookedAt}
              onChange={(event) => setBookedAt(event.target.value)}
              required
            />
            <Button type="submit">Post transaction</Button>
          </form>
        </CardContent>
      </Card>
    </Page>
  );
}
