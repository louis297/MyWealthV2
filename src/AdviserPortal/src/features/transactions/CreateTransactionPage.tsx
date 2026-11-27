import { type FormEvent, useEffect, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { Messages, Muted, Page, PageHeading, recordLinkClass, SelectField, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { SelectItem } from "@/components/ui/select";
import { useGetAccountHoldingsQuery, useGetAccountQuery } from "@/features/accounts/accountsApi";
import { allowsManualEntry, isCustodial, transferSentence } from "@/features/accounts/custodyRules";
import { useGetAccountPortfolioQuery } from "@/features/portfolios/portfoliosApi";
import { useGetInstrumentsQuery } from "@/features/instruments/instrumentsApi";
import { useCreateDividendMutation, useCreateTransactionMutation } from "@/features/transactions/transactionsApi";
import {
  netText,
  type TaxRow,
  TaxRowsInput,
  taxErrors,
  taxesBody,
  taxKindsFor,
  type TaxRowErrors,
} from "@/features/transactions/TaxRowsInput";
import { toUtcIso } from "@/shared/time/instant";

const securityTypes = ["Buy", "Sell", "StockIn", "StockOut", "Split", "Bonus", "Scrip"] as const;
// D53 (account-closure R23): CloseOut is not offered; on a Custodial account the account closure writes off a remainder.
// external-income D112: a hand Interest, with the taxes an outside payer withheld.
const cashTypes = ["TransferIn", "TransferOut", "Interest"] as const;
// external-income D111: a dividend an outside party paid, on any account type, on its own route.
const incomeTypes = ["Dividend"] as const;
const types = [...securityTypes, ...incomeTypes, ...cashTypes] as const;

function initialType(value: string | null) {
  if (value !== null && types.includes(value as (typeof types)[number])) {
    return value;
  }
  return "Buy";
}

function isCashType(value: string): value is (typeof cashTypes)[number] {
  return (cashTypes as readonly string[]).includes(value);
}

function cashSignError(type: (typeof cashTypes)[number], amount: number, taxed: boolean) {
  if (type === "Interest") {
    // posting R20: a hand Interest is either sign; with a tax it is the gross (external-income R22).
    return taxed && !(amount > 0) ? "Gross interest must be above 0." : null;
  }
  // TransferOut is negative.
  const invalid = type === "TransferIn" ? !(amount > 0) : !(amount < 0);
  return invalid ? "Amount sign is not valid for this transaction type." : null;
}

const companies = [
  { value: "NewZealand", label: "New Zealand" },
  { value: "Foreign", label: "Foreign" },
] as const;

/** external-income D111: the dividend fields whose 400 is shown at their input, by the error key. */
const dividendFieldKeys = {
  quantity: "quantity",
  grossAmount: "grossamount",
  amountPerShare: "amountpershare",
  taxSource: "taxsource",
  imputationCredit: "imputationcredit",
  fxRate: "fxrate",
} as const;
type DividendFieldErrors = Partial<Record<keyof typeof dividendFieldKeys, string[]>>;

function dividendProblem(error: unknown): {
  messages: string[];
  fields: DividendFieldErrors;
  taxList: string[];
  taxRows: TaxRowErrors;
} {
  const base = { messages: [] as string[], fields: {}, taxList: [] as string[], taxRows: {} };
  if (isNotFound(error)) {
    return { ...base, messages: ["Not found"] };
  }
  const data = typeof error === "object" && error !== null && "data" in error
    ? (error as { data?: { errors?: Record<string, string[]>; detail?: string } }).data
    : undefined;
  if (!data?.errors) {
    return data?.detail ? { ...base, messages: [data.detail] } : base;
  }
  const { list, rows, rest } = taxErrors(data.errors);
  const fields: DividendFieldErrors = {};
  const messages: string[] = [];
  for (const [key, values] of Object.entries(rest)) {
    const field = (Object.keys(dividendFieldKeys) as (keyof typeof dividendFieldKeys)[])
      .find((name) => dividendFieldKeys[name] === key.toLowerCase());
    if (field) {
      fields[field] = values;
    } else {
      messages.push(...values);
    }
  }
  return { messages, fields, taxList: list, taxRows: rows };
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
  const [reference, setReference] = useState("");
  const [taxRows, setTaxRows] = useState<TaxRow[]>([]);
  const [taxListErrors, setTaxListErrors] = useState<string[]>([]);
  const [taxRowErrors, setTaxRowErrors] = useState<TaxRowErrors>({});
  const [grossAmount, setGrossAmount] = useState("");
  const [amountPerShare, setAmountPerShare] = useState("");
  const [company, setCompany] = useState("");
  const [imputationCredit, setImputationCredit] = useState("");
  const [exDate, setExDate] = useState("");
  const [recordDate, setRecordDate] = useState("");
  const [paymentDate, setPaymentDate] = useState("");
  const [dividendErrors, setDividendErrors] = useState<DividendFieldErrors>({});
  const account = useGetAccountQuery(accountId ?? "", { skip: !accountId });
  const custodial = isCustodial(account.data);
  const assignment = useGetAccountPortfolioQuery(accountId ?? "", { skip: !accountId });
  const holdings = useGetAccountHoldingsQuery(accountId ?? "", { skip: !accountId });
  const instruments = useGetInstrumentsQuery();
  const [createTransaction] = useCreateTransactionMutation();
  const [createDividend] = useCreateDividendMutation();
  const pickedInstrument = (instruments.data?.items ?? []).find((item) => item.id === instrumentId);
  const quoteCurrency = pickedInstrument?.quoteCurrency;
  const accountCurrency = account.data?.currency;
  const pricedType = type === "Buy" || type === "Sell";
  // D22: an External trade carries the caller's numbers; the inputs appear once an instrument is picked.
  const callerNumbers = pricedType && !custodial && quoteCurrency !== undefined;
  const crossCurrency = callerNumbers && accountCurrency !== undefined && quoteCurrency !== accountCurrency;
  const dividend = type === "Dividend";
  // D111: the FX rate only when the instrument's quote currency differs from the Account currency.
  const dividendCrossCurrency = dividend && quoteCurrency !== undefined && accountCurrency !== undefined
    && quoteCurrency !== accountCurrency;
  // D112: only a hand Interest takes the tax list on POST /transactions.
  const taxedInterest = type === "Interest" && taxRows.length > 0;

  function optional(value: string) {
    const trimmed = value.trim();
    return trimmed.length === 0 ? null : trimmed;
  }

  async function submitDividend() {
    setDividendErrors({});
    setTaxListErrors([]);
    setTaxRowErrors({});
    try {
      await createDividend({
        idempotencyKey,
        body: {
          accountId: accountId!,
          instrumentId,
          quantity: Number(quantity),
          grossAmount: Number(grossAmount),
          ...(amountPerShare.trim().length > 0 ? { amountPerShare: Number(amountPerShare) } : {}),
          taxSource: company,
          ...(company === "NewZealand" && imputationCredit.trim().length > 0
            ? { imputationCredit: Number(imputationCredit) }
            : {}),
          ...taxesBody(taxRows),
          ...(dividendCrossCurrency ? { fxRate: Number(fxRate) } : {}),
          ...(exDate ? { exDate } : {}),
          ...(recordDate ? { recordDate } : {}),
          ...(paymentDate ? { paymentDate } : {}),
          bookedAt: toUtcIso(bookedAt),
          memo: optional(memo),
          reference: optional(reference),
        },
      }).unwrap();
      void navigate(`/accounts/${accountId}`);
    } catch (error: unknown) {
      const result = dividendProblem(error);
      setMessages(result.messages);
      setDividendErrors(result.fields);
      setTaxListErrors(result.taxList);
      setTaxRowErrors(result.taxRows);
    }
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!accountId) {
      return;
    }
    if (dividend) {
      await submitDividend();
      return;
    }
    if (assignment.isSuccess && assignment.data?.status === "Active" && !isCashType(type)) {
      return;
    }

    const trimmedMemo = memo.trim();
    const booking = { bookedAt: toUtcIso(bookedAt) };
    if (isCashType(type)) {
      const signError = cashSignError(type, Number(amount), taxedInterest);
      if (signError) {
        setMessages([signError]);
        return;
      }
    }
    setFieldErrors({});
    setTaxListErrors([]);
    setTaxRowErrors({});
    try {
      if (type === "Interest") {
        await createTransaction({
          idempotencyKey,
          body: {
            accountId,
            type,
            amount: Number(amount),
            ...booking,
            memo: trimmedMemo.length === 0 ? null : trimmedMemo,
            reference: optional(reference),
            ...taxesBody(taxRows),
          },
        }).unwrap();
      } else if (isCashType(type)) {
        await createTransaction({
          idempotencyKey,
          body: {
            accountId,
            type: type as Exclude<(typeof cashTypes)[number], "Interest">,
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
      const data = typeof error === "object" && error !== null && "data" in error
        ? (error as { data?: { errors?: Record<string, string[]> } }).data
        : undefined;
      const taxes = taxErrors(data?.errors ?? {});
      const result = problem(data?.errors ? { ...(error as object), data: { ...data, errors: taxes.rest } } : error);
      setMessages(result.messages);
      setFieldErrors(result.fields);
      setTaxListErrors(taxes.list);
      setTaxRowErrors(taxes.rows);
    }
  }

  const offerSecurity = !account.data
    || (account.data.status === "Open"
      && (account.data.type === "Brokerage" || account.data.type === "Other"));
  const offerCash = !account.data || account.data.status === "Open";
  const offeredTypes = [
    ...(offerSecurity ? securityTypes : []),
    // D111 (external-income R10): a dividend on any account type that can be opened.
    ...(offerCash ? incomeTypes : []),
    ...(offerCash ? cashTypes : []),
  ].filter((item) => !account.data || allowsManualEntry(account.data.custodyMode, item));
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
  const securityBlocked = assignment.isSuccess && assignment.data?.status === "Active" && !cashType && !dividend;

  if (isNotFound(account.error)) {
    return <Muted>Not found</Muted>;
  }

  // D69 (orders R48, R51): a person types no transaction on a Custodial account; a trade there is an order.
  if (account.data && custodial) {
    return (
      <Page className="max-w-lg">
        <PageHeading title="New transaction" />
        <p className="text-sm">Trades on a Custodial account go through the broker.</p>
        {account.data.status === "Open" ? (
          <Link to={`/accounts/${account.data.id}/order`} className={recordLinkClass}>
            Place an order
          </Link>
        ) : null}
        {/* in-kind-transfers D86 (R48): StockIn and StockOut are an in-kind transfer. */}
        <p className="text-sm">{transferSentence}</p>
        {account.data.status === "Open" ? (
          <Link to={`/accounts/${account.data.id}/transfer-securities`} className={recordLinkClass}>
            Request a transfer
          </Link>
        ) : null}
      </Page>
    );
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
                {dividend ? (
                  <DividendFields
                    instruments={instruments.data?.items ?? []}
                    instrumentId={instrumentId}
                    onInstrument={setInstrumentId}
                    quoteCurrency={quoteCurrency}
                    accountCurrency={accountCurrency}
                    crossCurrency={dividendCrossCurrency}
                    values={{ quantity, grossAmount, amountPerShare, company, imputationCredit, fxRate, exDate, recordDate, paymentDate, memo, reference }}
                    setters={{
                      quantity: setQuantity,
                      grossAmount: setGrossAmount,
                      amountPerShare: setAmountPerShare,
                      company: setCompany,
                      imputationCredit: setImputationCredit,
                      fxRate: setFxRate,
                      exDate: setExDate,
                      recordDate: setRecordDate,
                      paymentDate: setPaymentDate,
                      memo: setMemo,
                      reference: setReference,
                    }}
                    errors={dividendErrors}
                    taxRows={taxRows}
                    onTaxRows={setTaxRows}
                    taxListErrors={taxListErrors}
                    taxRowErrors={taxRowErrors}
                  />
                ) : cashType ? (
                  <>
                    <TextField
                      id="transaction-amount"
                      label={type === "Interest"
                        ? `${taxedInterest ? "Gross interest" : "Amount"} (${accountCurrency ?? ""})`
                        : "Amount"}
                      value={amount}
                      onChange={(event) => setAmount(event.target.value)}
                      required
                    />
                    {type === "Interest" ? (
                      <>
                        <TaxRowsInput
                          idPrefix="interest"
                          rows={taxRows}
                          onChange={setTaxRows}
                          kinds={taxKindsFor(null)}
                          currency={accountCurrency}
                          listErrors={taxListErrors}
                          rowErrors={taxRowErrors}
                        />
                        {taxedInterest ? <p className="text-sm">{netText(amount, taxRows, accountCurrency)}</p> : null}
                      </>
                    ) : null}
                    <TextField
                      id="transaction-memo"
                      label="Memo"
                      value={memo}
                      onChange={(event) => setMemo(event.target.value)}
                      maxLength={200}
                    />
                    {type === "Interest" ? (
                      <TextField
                        id="transaction-reference"
                        label="Reference"
                        value={reference}
                        onChange={(event) => setReference(event.target.value)}
                        maxLength={100}
                      />
                    ) : null}
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
              </>
            )}
          </form>
        </CardContent>
      </Card>
    </Page>
  );
}

type DividendValues = {
  quantity: string;
  grossAmount: string;
  amountPerShare: string;
  company: string;
  imputationCredit: string;
  fxRate: string;
  exDate: string;
  recordDate: string;
  paymentDate: string;
  memo: string;
  reference: string;
};

/**
 * external-income D111: the dividend form. Amounts are in the instrument's quote currency; the form shows the net as
 * display text and works out nothing else.
 */
function DividendFields({
  instruments,
  instrumentId,
  onInstrument,
  quoteCurrency,
  accountCurrency,
  crossCurrency,
  values,
  setters,
  errors,
  taxRows,
  onTaxRows,
  taxListErrors,
  taxRowErrors,
}: {
  instruments: { id: string; symbol: string; name: string }[];
  instrumentId: string;
  onInstrument: (id: string) => void;
  quoteCurrency: string | undefined;
  accountCurrency: string | undefined;
  crossCurrency: boolean;
  values: DividendValues;
  setters: Record<keyof DividendValues, (value: string) => void>;
  errors: DividendFieldErrors;
  taxRows: TaxRow[];
  onTaxRows: (rows: TaxRow[]) => void;
  taxListErrors: string[];
  taxRowErrors: TaxRowErrors;
}) {
  const quote = quoteCurrency ?? "";
  const field = (name: keyof DividendFieldErrors) => (errors[name] ? `dividend-${idOf[name]}-error` : undefined);
  const net = netText(values.grossAmount, taxRows, quoteCurrency);

  return (
    <>
      <SelectField
        id="transaction-instrument"
        label="Instrument"
        value={instrumentId}
        onChange={(event) => onInstrument(event.target.value)}
        required
      >
        <SelectItem value="">Select an instrument</SelectItem>
        {instruments.map((item) => (
          <SelectItem key={item.id} value={item.id}>
            {item.symbol} {item.name}
          </SelectItem>
        ))}
      </SelectField>
      <TextField
        id="dividend-quantity"
        label="Shares"
        value={values.quantity}
        onChange={(event) => setters.quantity(event.target.value)}
        aria-describedby={field("quantity")}
        required
      />
      <FieldError id="dividend-quantity-error" messages={errors.quantity} />
      <TextField
        id="dividend-gross"
        label={`Gross amount (${quote})`}
        value={values.grossAmount}
        onChange={(event) => setters.grossAmount(event.target.value)}
        aria-describedby={field("grossAmount")}
        required
      />
      <FieldError id="dividend-gross-error" messages={errors.grossAmount} />
      <TextField
        id="dividend-per-share"
        label={`Amount per share (${quote})`}
        value={values.amountPerShare}
        onChange={(event) => setters.amountPerShare(event.target.value)}
        aria-describedby={field("amountPerShare")}
      />
      <FieldError id="dividend-per-share-error" messages={errors.amountPerShare} />
      <SelectField
        id="dividend-company"
        label="Company"
        value={values.company}
        onChange={(event) => setters.company(event.target.value)}
        required
      >
        <SelectItem value="">Select the company</SelectItem>
        {companies.map((item) => (
          <SelectItem key={item.value} value={item.value}>
            {item.label}
          </SelectItem>
        ))}
      </SelectField>
      <FieldError id="dividend-company-error" messages={errors.taxSource} />
      {values.company === "NewZealand" ? (
        <>
          <TextField
            id="dividend-credit"
            label={`Imputation credit (${quote})`}
            value={values.imputationCredit}
            onChange={(event) => setters.imputationCredit(event.target.value)}
            aria-describedby={field("imputationCredit")}
          />
          <FieldError id="dividend-credit-error" messages={errors.imputationCredit} />
        </>
      ) : null}
      <TaxRowsInput
        idPrefix="dividend"
        rows={taxRows}
        onChange={onTaxRows}
        kinds={taxKindsFor(values.company || null)}
        currency={quoteCurrency}
        listErrors={taxListErrors}
        rowErrors={taxRowErrors}
      />
      {net ? <p className="text-sm">{net}</p> : null}
      {crossCurrency ? (
        <>
          <TextField
            id="dividend-fx-rate"
            label={`FX rate (1 ${quote} = ? ${accountCurrency})`}
            value={values.fxRate}
            onChange={(event) => setters.fxRate(event.target.value)}
            aria-describedby={field("fxRate")}
            required
          />
          <FieldError id="dividend-fx-rate-error" messages={errors.fxRate} />
        </>
      ) : null}
      <TextField id="dividend-ex-date" label="Ex-date" type="date" value={values.exDate}
        onChange={(event) => setters.exDate(event.target.value)} />
      <TextField id="dividend-record-date" label="Record date" type="date" value={values.recordDate}
        onChange={(event) => setters.recordDate(event.target.value)} />
      <TextField id="dividend-payment-date" label="Payment date" type="date" value={values.paymentDate}
        onChange={(event) => setters.paymentDate(event.target.value)} />
      <TextField id="dividend-memo" label="Memo" value={values.memo}
        onChange={(event) => setters.memo(event.target.value)} maxLength={200} />
      <TextField id="dividend-reference" label="Reference" value={values.reference}
        onChange={(event) => setters.reference(event.target.value)} maxLength={100} />
    </>
  );
}

const idOf: Record<keyof DividendFieldErrors, string> = {
  quantity: "quantity",
  grossAmount: "gross",
  amountPerShare: "per-share",
  taxSource: "company",
  imputationCredit: "credit",
  fxRate: "fx-rate",
};
