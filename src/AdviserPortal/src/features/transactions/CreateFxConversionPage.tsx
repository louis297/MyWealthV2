import { skipToken } from "@reduxjs/toolkit/query";
import { useState } from "react";
import { useNavigate, useParams } from "react-router";
import { Messages, Muted, Page, PageHeading, Section, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { type Account, useGetAccountQuery, useGetAccountsQuery } from "@/features/accounts/accountsApi";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { useGetCustomerQuery } from "@/features/customers/customersApi";
import {
  type FxQuote,
  type FxQuoteBody,
  useCreateFxConversionMutation,
  useGetFxQuoteQuery,
} from "@/features/transactions/transactionsApi";
import { rateLine } from "@/features/transactions/fxFormat";
import { useDebouncedValue } from "@/shared/hooks/useDebouncedValue";
import { toUtcIso } from "@/shared/time/instant";
import { cn } from "cn";

type Side = "sell" | "buy";

/** Client-side eligibility of a destination Account (fx-conversions §9). */
function ineligibleReason(candidate: Account, source: Account): string | null {
  if (candidate.id === source.id) {
    return "Source account";
  }
  if (candidate.status !== "Open") {
    return "Closed";
  }
  if (candidate.currency === source.currency) {
    return "Same currency";
  }
  return null;
}

function errorStatus(error: unknown): number | undefined {
  if (typeof error === "object" && error !== null && "status" in error && typeof error.status === "number") {
    return error.status;
  }
  return undefined;
}

function problemMessages(error: unknown): string[] {
  if (errorStatus(error) === 404) {
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

/** True for the 409 that says the stored rate is not the quoted one (fx-conversions R12). */
function isRateChanged(error: unknown): boolean {
  if (errorStatus(error) !== 409 || typeof error !== "object" || error === null || !("data" in error)) {
    return false;
  }
  const errors = (error as { data?: { errors?: Record<string, string[]> } }).data?.errors ?? {};
  return Object.keys(errors).some((key) => key.toLowerCase() === "expectedrate");
}

export function CreateFxConversionPage() {
  const { id: sourceId } = useParams();
  const navigate = useNavigate();
  const [idempotencyKey] = useState(() => crypto.randomUUID());
  const source = useGetAccountQuery(sourceId ?? "", { skip: !sourceId });
  const sourceCustomer = useGetCustomerQuery(source.data?.customerId ?? "", { skip: !source.data });
  const accounts = useGetAccountsQuery(
    { customerId: source.data?.customerId, pageSize: 100 },
    { skip: !source.data },
  );
  const currencies = useGetCurrenciesQuery();
  const [toAccountId, setToAccountId] = useState("");
  const [side, setSide] = useState<Side>("sell");
  const [amount, setAmount] = useState("");
  const [bookedAt, setBookedAt] = useState("");
  const [memo, setMemo] = useState("");
  const [reference, setReference] = useState("");
  const [reviewing, setReviewing] = useState(false);
  const [messages, setMessages] = useState<string[]>([]);
  const [createFxConversion, { isLoading }] = useCreateFxConversionMutation();

  const from = source.data;
  const candidates = accounts.data?.items ?? [];
  const destination = from
    ? candidates.find((item) => item.id === toAccountId && ineligibleReason(item, from) === null)
    : undefined;
  const amountValue = Number(amount);
  const amountValid = amount.trim() !== "" && Number.isFinite(amountValue) && amountValue > 0;

  const quoteBody: FxQuoteBody | null =
    from && destination && amountValid
      ? {
          fromAccountId: from.id,
          toAccountId: destination.id,
          ...(side === "sell" ? { sellAmount: amountValue } : { buyAmount: amountValue }),
          ...(bookedAt ? { bookedAt: toUtcIso(bookedAt) } : {}),
        }
      : null;
  const quoteKey = quoteBody ? JSON.stringify(quoteBody) : "";
  const debouncedKey = useDebouncedValue(quoteKey, 300);
  const quote = useGetFxQuoteQuery(debouncedKey ? (JSON.parse(debouncedKey) as FxQuoteBody) : skipToken);
  const quoteIsCurrent = debouncedKey !== "" && debouncedKey === quoteKey;
  const currentQuote: FxQuote | undefined =
    quoteIsCurrent && !quote.isError && !quote.isFetching ? quote.data : undefined;
  const quoteMessages = quoteIsCurrent && quote.isError ? problemMessages(quote.error) : [];

  if (source.isError) {
    return (
      <Page>
        <PageHeading title="Convert" />
        <Messages messages={problemMessages(source.error)} />
      </Page>
    );
  }
  if (!from) {
    return (
      <Page>
        <PageHeading title="Convert" />
        <Muted>Loading…</Muted>
      </Page>
    );
  }

  const noEligible = accounts.data !== undefined && !candidates.some((item) => ineligibleReason(item, from) === null);
  const typedCurrency = side === "sell" ? from.currency : (destination?.currency ?? "…");

  function format(value: number, currency: string) {
    const places = currencies.data?.find((item) => item.code === currency)?.decimalPlaces ?? 2;
    return `${value.toFixed(places)} ${currency}`;
  }

  function resetReview() {
    setReviewing(false);
  }

  function onReview() {
    const problems: string[] = [];
    if (!destination) {
      problems.push("Choose a destination account.");
    }
    if (!amountValid) {
      problems.push("Amount must be greater than 0.");
    }
    if (!bookedAt) {
      problems.push("Booked at is required.");
    }
    if (!currentQuote) {
      problems.push("Wait for the quote.");
    }
    setMessages(problems);
    setReviewing(problems.length === 0);
  }

  async function onConfirm() {
    if (!destination || !currentQuote || !from) {
      return;
    }
    try {
      await createFxConversion({
        idempotencyKey,
        body: {
          fromAccountId: from.id,
          toAccountId: destination.id,
          ...(side === "sell" ? { sellAmount: amountValue } : { buyAmount: amountValue }),
          expectedRate: currentQuote.rate,
          bookedAt: toUtcIso(bookedAt),
          memo: memo.trim() || null,
          reference: reference.trim() || null,
        },
      }).unwrap();
      navigate(`/accounts/${from.id}`);
    } catch (error: unknown) {
      setReviewing(false);
      const problems = problemMessages(error);
      setMessages(problems.length > 0 ? problems : ["Conversion failed. Check the details and try again."]);
      if (isRateChanged(error)) {
        void quote.refetch();
      }
    }
  }

  return (
    <Page>
      <PageHeading title="Convert" description={`From ${from.name} (${sourceCustomer.data?.name ?? "…"})`} />
      <Messages messages={messages} />
      <Section title="Destination">
        <Card>
          <CardContent className="flex flex-col gap-4">
            {noEligible ? (
              <Muted>No open account in another currency. Open one for this customer first.</Muted>
            ) : null}
            <div role="radiogroup" aria-label="Destination account" className="flex flex-col gap-2">
              {candidates.map((item) => {
                const reason = ineligibleReason(item, from);
                return (
                  <label
                    key={item.id}
                    className={cn("flex items-center gap-2 text-sm", reason ? "text-muted-foreground opacity-60" : null)}
                  >
                    <input
                      type="radio"
                      name="convert-destination"
                      value={item.id}
                      disabled={reason !== null}
                      checked={toAccountId === item.id}
                      onChange={() => {
                        setToAccountId(item.id);
                        resetReview();
                      }}
                    />
                    <span>{item.name}</span>
                    <span>{item.currency}</span>
                    {reason ? <span>({reason})</span> : null}
                  </label>
                );
              })}
            </div>
          </CardContent>
        </Card>
      </Section>
      <Section title="Details">
        <Card>
          <CardContent className="flex flex-col gap-4">
            <div role="radiogroup" aria-label="Conversion side" className="flex gap-4 text-sm">
              {(["sell", "buy"] as const).map((value) => (
                <label key={value} className="flex items-center gap-2">
                  <input
                    type="radio"
                    name="convert-side"
                    value={value}
                    checked={side === value}
                    onChange={() => {
                      setSide(value);
                      resetReview();
                    }}
                  />
                  <span>{value === "sell" ? "I sell" : "I buy"}</span>
                </label>
              ))}
            </div>
            <TextField
              id="convert-amount"
              label={`Amount (${typedCurrency})`}
              inputMode="decimal"
              value={amount}
              onChange={(event) => {
                setAmount(event.target.value);
                resetReview();
              }}
            />
            {currentQuote ? (
              <div className="flex flex-col gap-1 text-sm" aria-live="polite">
                <p>{rateLine(currentQuote)}</p>
                <p>
                  {side === "sell"
                    ? `You get ${format(currentQuote.buy.amount, currentQuote.buy.currency)}`
                    : `You pay ${format(currentQuote.sell.amount, currentQuote.sell.currency)}`}
                </p>
              </div>
            ) : null}
            <Messages messages={quoteMessages} />
            <TextField
              id="convert-booked-at"
              label="Booked at"
              type="datetime-local"
              value={bookedAt}
              onChange={(event) => {
                setBookedAt(event.target.value);
                resetReview();
              }}
            />
            <TextField
              id="convert-memo"
              label="Memo"
              value={memo}
              onChange={(event) => setMemo(event.target.value)}
              maxLength={200}
            />
            <TextField
              id="convert-reference"
              label="Reference"
              value={reference}
              onChange={(event) => setReference(event.target.value)}
              maxLength={100}
            />
            {reviewing && destination && currentQuote ? (
              <div className="flex flex-col gap-3">
                <p className="text-sm font-medium">
                  {`Convert ${format(currentQuote.sell.amount, currentQuote.sell.currency)} from ${from.name} to ${format(currentQuote.buy.amount, currentQuote.buy.currency)} in ${destination.name} at ${rateLine(currentQuote)}`}
                </p>
                <div className="flex gap-2">
                  <Button type="button" disabled={isLoading} onClick={() => void onConfirm()}>
                    Confirm conversion
                  </Button>
                  <Button type="button" variant="outline" onClick={resetReview}>
                    Edit
                  </Button>
                </div>
              </div>
            ) : (
              <Button type="button" disabled={quoteMessages.length > 0} onClick={onReview}>
                Review conversion
              </Button>
            )}
          </CardContent>
        </Card>
      </Section>
    </Page>
  );
}
