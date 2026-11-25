import { useState } from "react";
import { useNavigate, useParams } from "react-router";
import { Messages, Muted, Page, PageHeading, SelectField, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { SelectItem } from "@/components/ui/select";
import { useGetAccountHoldingsQuery, useGetAccountQuery } from "@/features/accounts/accountsApi";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { useGetInstrumentsQuery } from "@/features/instruments/instrumentsApi";
import { approvalSentence } from "@/features/adjustmentRequests/adjustmentRequestLabels";
import { type RequestAdjustmentBody, useRequestAdjustmentMutation } from "@/features/adjustmentRequests/adjustmentRequestsApi";
import { adjustmentReasonLabel } from "@/features/transactions/ReverseTransactionButton";

const reasons = ["EntryError", "CorporateAction", "ReconciliationBreak", "Other"] as const;

/** The body fields whose 400 is shown at their input (D29), by the lower-cased error key the server sends. */
const fieldKeys = {
  reason: "reason",
  memo: "memo",
  amount: "amount",
  instrument: "security",
  instrumentId: "security.instrumentid",
  quantity: "security.quantitychange",
  cost: "security.costchange",
} as const;
type FieldName = keyof typeof fieldKeys;
type FieldErrors = Partial<Record<FieldName, string[]>>;

type Choice = { id: string; symbol: string; name: string; currency: string };

function isNotFound(error: unknown) {
  return typeof error === "object" && error !== null && "status" in error && error.status === 404;
}

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
      const field = (Object.keys(fieldKeys) as FieldName[]).find((name) => fieldKeys[name] === key.toLowerCase());
      if (field) {
        fields[field] = [...(fields[field] ?? []), ...values];
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

function parse(text: string): number | null {
  const value = Number(text);
  return text.trim() !== "" && Number.isFinite(value) ? value : null;
}

/** A signed number as entered, with a true minus sign; no arithmetic on balances (D29). */
function signed(value: number, places?: number) {
  const text = places === undefined ? String(Math.abs(value)) : Math.abs(value).toFixed(places);
  if (value > 0) {
    return `+${text}`;
  }
  if (value < 0) {
    return `−${text}`;
  }
  return text;
}

/** D29: `/accounts/:id/adjust`, a TenantAdmin's correction of the cash, one holding, or both. adjustment-review D102:
 * Confirm requests it, and a second firm administrator approves it on `/adjustments/:id`. */
export function CreateAdjustmentPage() {
  const { id: accountId } = useParams();
  const navigate = useNavigate();
  const [idempotencyKey] = useState(() => crypto.randomUUID());
  const account = useGetAccountQuery(accountId ?? "", { skip: !accountId });
  const holdings = useGetAccountHoldingsQuery(accountId ?? "", { skip: !accountId });
  const instruments = useGetInstrumentsQuery();
  const currencies = useGetCurrenciesQuery();
  const [requestAdjustment, { isLoading }] = useRequestAdjustmentMutation();
  const [reason, setReason] = useState("");
  const [memo, setMemo] = useState("");
  const [cash, setCash] = useState("");
  const [instrumentId, setInstrumentId] = useState("");
  const [quantity, setQuantity] = useState("");
  const [cost, setCost] = useState("");
  const [reviewing, setReviewing] = useState(false);
  const [messages, setMessages] = useState<string[]>([]);
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});

  // The Account's holdings first, then the enabled instruments it does not hold.
  const choices: Choice[] = [];
  for (const item of holdings.data?.items ?? []) {
    choices.push({ id: item.instrumentId, symbol: item.symbol, name: item.name, currency: item.costCurrency });
  }
  for (const item of instruments.data?.items ?? []) {
    if (!choices.some((choice) => choice.id === item.id)) {
      choices.push({ id: item.id, symbol: item.symbol, name: item.name, currency: item.quoteCurrency });
    }
  }
  const picked = choices.find((choice) => choice.id === instrumentId);
  const places = (code: string | undefined) =>
    currencies.data?.find((item) => item.code === code)?.decimalPlaces ?? 2;

  function edit<T>(setter: (value: T) => void) {
    return (value: T) => {
      setter(value);
      setReviewing(false);
    };
  }

  function draft(): { body: RequestAdjustmentBody; review: string } | { problems: string[] } {
    const problems: string[] = [];
    const cashValue = parse(cash);
    const quantityValue = quantity.trim() === "" ? 0 : parse(quantity);
    const costValue = cost.trim() === "" ? 0 : parse(cost);
    if (reason === "") {
      problems.push("Choose a reason.");
    }
    if (memo.trim() === "") {
      problems.push("Enter a memo.");
    }
    if (cash.trim() !== "" && cashValue === null) {
      problems.push("Cash change must be a number.");
    }
    if (picked && (quantityValue === null || costValue === null)) {
      problems.push("Quantity change and cost change must be numbers.");
    }
    if (cash.trim() === "" && !picked) {
      problems.push("Enter a cash change, a holding change, or both.");
    }
    if (problems.length > 0 || !account.data || !accountId) {
      return { problems };
    }

    const parts: string[] = [];
    const body: RequestAdjustmentBody = { accountId, reason, memo: memo.trim(), reference: null };
    if (cashValue !== null) {
      body.amount = cashValue;
      parts.push(`Cash ${signed(cashValue, places(account.data.currency))} ${account.data.currency}`);
    }
    if (picked) {
      body.security = { instrumentId: picked.id, quantityChange: quantityValue!, costChange: costValue! };
      parts.push(
        `${picked.symbol} quantity ${signed(quantityValue!)}, cost ${signed(costValue!, places(picked.currency))} ${picked.currency}`,
      );
    }
    return { body, review: parts.join("; ") };
  }

  function onReview() {
    const result = draft();
    if ("problems" in result) {
      setMessages(result.problems);
      setReviewing(false);
      return;
    }
    setMessages([]);
    setReviewing(true);
  }

  async function onConfirm() {
    const result = draft();
    if ("problems" in result) {
      setMessages(result.problems);
      setReviewing(false);
      return;
    }
    setFieldErrors({});
    try {
      const created = await requestAdjustment({ body: result.body, idempotencyKey }).unwrap();
      void navigate(`/adjustments/${created.id}`);
    } catch (error: unknown) {
      const parsed = problem(error);
      setMessages(parsed.messages);
      setFieldErrors(parsed.fields);
      setReviewing(false);
    }
  }

  const current = reviewing ? draft() : null;
  const review = current && "review" in current ? current.review : null;
  const securityErrors = [...(fieldErrors.instrument ?? []), ...(fieldErrors.instrumentId ?? [])];
  const describedBy = (name: string, errors?: string[]) =>
    errors && errors.length > 0 ? `adjust-${name}-error` : undefined;

  return (
    <Page className="max-w-lg">
      <PageHeading title="Adjust" description={account.data?.name} />
      {isNotFound(account.error) ? <Muted>Not found</Muted> : null}
      <Messages messages={messages} />
      {account.data ? (
        <Card>
          <CardContent>
            <form className="flex flex-col gap-4" onSubmit={(event) => event.preventDefault()}>
              <SelectField id="adjust-reason" label="Reason" value={reason} onChange={(event) => edit(setReason)(event.target.value)} required>
                <SelectItem value="">Select a reason</SelectItem>
                {reasons.map((item) => (
                  <SelectItem key={item} value={item}>
                    {adjustmentReasonLabel(item)}
                  </SelectItem>
                ))}
              </SelectField>
              <FieldError id="adjust-reason-error" messages={fieldErrors.reason} />
              <TextField
                id="adjust-memo"
                label="Memo"
                value={memo}
                onChange={(event) => edit(setMemo)(event.target.value)}
                maxLength={200}
                aria-describedby={describedBy("memo", fieldErrors.memo)}
                required
              />
              <p className="text-muted-foreground text-xs">The customer sees this memo</p>
              <FieldError id="adjust-memo-error" messages={fieldErrors.memo} />
              <TextField
                id="adjust-cash"
                label={`Cash change (${account.data.currency})`}
                value={cash}
                onChange={(event) => edit(setCash)(event.target.value)}
                inputMode="decimal"
                aria-describedby={describedBy("cash", fieldErrors.amount)}
              />
              <FieldError id="adjust-cash-error" messages={fieldErrors.amount} />
              <SelectField
                id="adjust-instrument"
                label="Instrument"
                value={instrumentId}
                onChange={(event) => edit(setInstrumentId)(event.target.value)}
              >
                <SelectItem value="">No holding change</SelectItem>
                {choices.map((choice) => (
                  <SelectItem key={choice.id} value={choice.id}>
                    {choice.symbol} {choice.name}
                  </SelectItem>
                ))}
              </SelectField>
              <FieldError id="adjust-instrument-error" messages={securityErrors} />
              {picked ? (
                <>
                  <TextField
                    id="adjust-quantity"
                    label="Quantity change"
                    value={quantity}
                    onChange={(event) => edit(setQuantity)(event.target.value)}
                    inputMode="decimal"
                    aria-describedby={describedBy("quantity", fieldErrors.quantity)}
                  />
                  <FieldError id="adjust-quantity-error" messages={fieldErrors.quantity} />
                  <TextField
                    id="adjust-cost"
                    label={`Cost change (${picked.currency})`}
                    value={cost}
                    onChange={(event) => edit(setCost)(event.target.value)}
                    inputMode="decimal"
                    aria-describedby={describedBy("cost", fieldErrors.cost)}
                  />
                  <FieldError id="adjust-cost-error" messages={fieldErrors.cost} />
                </>
              ) : null}
              {review ? (
                <div className="flex flex-col gap-2">
                  <p className="text-sm">{review}</p>
                  <p className="text-muted-foreground text-sm">{approvalSentence}</p>
                  <div className="flex gap-2">
                    <Button type="button" disabled={isLoading} onClick={() => void onConfirm()}>
                      Confirm adjustment
                    </Button>
                    <Button type="button" variant="outline" onClick={() => setReviewing(false)}>
                      Back
                    </Button>
                  </div>
                </div>
              ) : (
                <Button type="button" onClick={onReview}>
                  Review adjustment
                </Button>
              )}
            </form>
          </CardContent>
        </Card>
      ) : null}
    </Page>
  );
}
