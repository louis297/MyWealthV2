import { type FormEvent, useEffect, useState } from "react";
import { useNavigate, useParams, useSearchParams } from "react-router";
import { Messages, Muted, Page, PageHeading, SelectField, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { SelectItem } from "@/components/ui/select";
import { useGetAccountHoldingsQuery, useGetAccountQuery } from "@/features/accounts/accountsApi";
import { allowsManualEntry, bookedWhenPosted, isCustodial, offersCloseOut } from "@/features/accounts/custodyRules";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { useGetAccountPortfolioQuery } from "@/features/portfolios/portfoliosApi";
import { useGetInstrumentsQuery } from "@/features/instruments/instrumentsApi";
import { useCreateTransactionMutation } from "@/features/transactions/transactionsApi";
import { toUtcIso } from "@/shared/time/instant";

const securityTypes = ["Buy", "Sell", "StockIn", "StockOut", "Split", "Bonus", "Scrip"] as const;
// D44 (withdrawals R46): CloseOut is offered only on a Custodial account with a remainder below the minor unit.
const cashTypes = ["TransferIn", "TransferOut", "CloseOut"] as const;
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
  // TransferOut and CloseOut are negative.
  const invalid = type === "TransferIn" ? !(amount > 0) : !(amount < 0);
  return invalid ? "Amount sign is not valid for this transaction type." : null;
}

function isNotFound(error: unknown) {
  return typeof error === "object" && error !== null && "status" in error && error.status === 404;
}

/** The body fields whose 400 is shown at their input (D22), by the error key the server sends. */
const fieldKeys = { price: "security[0].price", fee: "fee", fxRate: "fxrate" } as const;
type FieldErrors = Partial<Record<keyof typeof fieldKeys, string[]>>;

function problem(error: unknown): { messages: string[]; fields: FieldErrors } {
  if (isNotFound(error)) {
    return { messages: ["Not found"], fields: {} };
  }
  if (typeof error !== "object" || error === null || !("data" in error)) {
    return { messages: [], fields: {} };
  }
  const data = (error as { data?: { errors?: Record<string, string[]>; detail?: string } }).data;
  if (data?.errors) {
    const fields: FieldErrors = {};
    const messages: string[] = [];
    for (const [key, values] of Object.entries(data.errors)) {
      const field = (Object.keys(fieldKeys) as (keyof typeof fieldKeys)[])
        .find((name) => fieldKeys[name] === key.toLowerCase());
      if (field) {
        fields[field] = values;
      } else {
        messages.push(...values);
      }
    }
    return { messages, fields };
  }
  if (data?.detail) {
    return { messages: [data.detail], fields: {} };
  }
  return { messages: [], fields: {} };
}

function FieldError({ id, messages }: { id: string; messages?: string[] }) {
  if (!messages || messages.length === 0) {
    return null;
  }
  return (
    <p id={id} className="text-sm text-destructive">
      {messages.join(" ")}
    </p>
  );
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
  const [price, setPrice] = useState("");
  const [fee, setFee] = useState("");
  const [fxRate, setFxRate] = useState("");
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
  const [amount, setAmount] = useState("");
  const [memo, setMemo] = useState("");
  const [bookedAt, setBookedAt] = useState("");
  const [messages, setMessages] = useState<string[]>([]);
  const account = useGetAccountQuery(accountId ?? "", { skip: !accountId });
  const custodial = isCustodial(account.data);
  const currencies = useGetCurrenciesQuery();
  const places = currencies.data?.find((item) => item.code === account.data?.currency)?.decimalPlaces;
  const closeOut = account.data ? offersCloseOut(account.data, places) : false;
  const assignment = useGetAccountPortfolioQuery(accountId ?? "", { skip: !accountId });
  const holdings = useGetAccountHoldingsQuery(accountId ?? "", { skip: !accountId });
  const instruments = useGetInstrumentsQuery();
  const [createTransaction] = useCreateTransactionMutation();
  const pickedInstrument = (instruments.data?.items ?? []).find((item) => item.id === instrumentId);
  const quoteCurrency = pickedInstrument?.quoteCurrency;
  const accountCurrency = account.data?.currency;
  const pricedType = type === "Buy" || type === "Sell";
  // D22: an External trade carries the caller's numbers; the inputs appear once an instrument is picked.
  const callerNumbers = pricedType && !custodial && quoteCurrency !== undefined;
  const crossCurrency = callerNumbers && accountCurrency !== undefined && quoteCurrency !== accountCurrency;

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!accountId) {
      return;
    }
    if (assignment.isSuccess && assignment.data?.status === "Active" && !isCashType(type)) {
      return;
    }

    const trimmedMemo = memo.trim();
    const booking = custodial ? {} : { bookedAt: toUtcIso(bookedAt) };
    if (isCashType(type)) {
      const signError = cashSignError(type, Number(amount));
      if (signError) {
        setMessages([signError]);
        return;
      }
    }
    setFieldErrors({});
    try {
      if (isCashType(type)) {
        await createTransaction({
          idempotencyKey,
          body: {
            accountId,
            type,
            amount: Number(amount),
            ...booking,
            memo: trimmedMemo.length === 0 ? null : trimmedMemo,
          },
        }).unwrap();
      } else {
        await createTransaction({
          idempotencyKey,
          body: {
            accountId,
            type,
            ...booking,
            memo: null,
            reference: null,
            security: [
              {
                instrumentId,
                quantity: Number(quantity),
                ...(type === "StockIn" ? { cost: Number(cost) } : {}),
                ...(callerNumbers ? { price: Number(price) } : {}),
              },
            ],
            ...(callerNumbers && fee.trim().length > 0 ? { fee: Number(fee) } : {}),
            ...(crossCurrency ? { fxRate: Number(fxRate) } : {}),
          },
        }).unwrap();
      }
      void navigate(`/accounts/${accountId}`);
    } catch (error: unknown) {
      const result = problem(error);
      setMessages(result.messages);
      setFieldErrors(result.fields);
    }
  }

  const offerSecurity = !account.data
    || (account.data.status === "Open"
      && (account.data.type === "Brokerage" || account.data.type === "Other"));
  const offerCash = !account.data || account.data.status === "Open";
  const offeredTypes = [
    ...(offerSecurity ? securityTypes : []),
    ...(offerCash ? cashTypes : []),
  ].filter((item) => item === "CloseOut"
    ? closeOut
    : !account.data || allowsManualEntry(account.data.custodyMode, item));
  // D39: a type the account does not take (for example TransferIn on a Custodial account, from the address) is not
  // kept; the form starts on the first type the account offers.
  const offeredKey = offeredTypes.join(",");
  useEffect(() => {
    if (account.data && offeredTypes.length > 0 && !(offeredTypes as readonly string[]).includes(type)) {
      setType(offeredTypes[0]);
    }
    // offeredKey stands for offeredTypes, which is rebuilt on every render.
  }, [account.data, offeredKey, type]);
  const cashType = isCashType(type);
  // D44: a CloseOut writes off the whole remainder, so its amount is the balance, negated.
  useEffect(() => {
    if (type === "CloseOut" && account.data) {
      setAmount(String(-account.data.cashBalance));
    }
  }, [type, account.data]);
  const securityBlocked = assignment.isSuccess && assignment.data?.status === "Active" && !cashType;

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
                <SelectItem key={item} value={item}>
                  {item}
                </SelectItem>
              ))}
            </SelectField>
            {securityBlocked ? (
              <p>Manual security trades are blocked while a portfolio is assigned.</p>
            ) : (
              <>
                {cashType ? (
                  <>
                    <TextField
                      id="transaction-amount"
                      label="Amount"
                      value={amount}
                      readOnly={type === "CloseOut"}
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
                      <SelectItem value="">Select an instrument</SelectItem>
                      {(instruments.data?.items ?? []).map((item) => (
                        <SelectItem key={item.id} value={item.id}>
                          {item.symbol} {item.name}
                        </SelectItem>
                      ))}
                    </SelectField>
                    <TextField
                      id="transaction-quantity"
                      label={type === "Split" || type === "Bonus" || type === "Scrip" ? "New total quantity" : "Quantity"}
                      value={quantity}
                      onChange={(event) => setQuantity(event.target.value)}
                      required
                    />
                    {callerNumbers ? (
                      <>
                        <TextField
                          id="transaction-price"
                          label={`Price per unit (${quoteCurrency})`}
                          value={price}
                          onChange={(event) => setPrice(event.target.value)}
                          aria-describedby={fieldErrors.price ? "transaction-price-error" : undefined}
                          required
                        />
                        <FieldError id="transaction-price-error" messages={fieldErrors.price} />
                        <TextField
                          id="transaction-fee"
                          label={`Fee (${quoteCurrency})`}
                          value={fee}
                          onChange={(event) => setFee(event.target.value)}
                          aria-describedby={fieldErrors.fee ? "transaction-fee-error" : undefined}
                        />
                        <FieldError id="transaction-fee-error" messages={fieldErrors.fee} />
                        {crossCurrency ? (
                          <>
                            <TextField
                              id="transaction-fx-rate"
                              label={`FX rate (1 ${quoteCurrency} = ? ${accountCurrency})`}
                              value={fxRate}
                              onChange={(event) => setFxRate(event.target.value)}
                              aria-describedby={fieldErrors.fxRate ? "transaction-fx-rate-error" : undefined}
                              required
                            />
                            <FieldError id="transaction-fx-rate-error" messages={fieldErrors.fxRate} />
                          </>
                        ) : null}
                      </>
                    ) : null}
                    {pricedType && custodial ? (
                      <p className="text-sm text-muted-foreground">
                        Brokerage is added by the server from the trade fee schedule.
                      </p>
                    ) : null}
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
                {custodial ? (
                  <Muted>{bookedWhenPosted}</Muted>
                ) : (
                  <TextField
                    id="transaction-booked-at"
                    label="Booked at"
                    type="datetime-local"
                    value={bookedAt}
                    onChange={(event) => setBookedAt(event.target.value)}
                    required
                  />
                )}
                <Button type="submit">Post transaction</Button>
              </>
            )}
          </form>
        </CardContent>
      </Card>
    </Page>
  );
}
