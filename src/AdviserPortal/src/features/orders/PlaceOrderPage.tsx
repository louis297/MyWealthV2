import { type FormEvent, useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { Messages, Muted, Page, PageHeading, SelectField, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { SelectItem } from "@/components/ui/select";
import { useGetAccountHoldingsQuery, useGetAccountQuery } from "@/features/accounts/accountsApi";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { useGetInstrumentsQuery } from "@/features/instruments/instrumentsApi";
import { shortDay } from "@/features/orders/orderLabels";
import { type OrderPreview, useLazyPreviewOrderQuery, usePlaceOrderMutation } from "@/features/orders/ordersApi";
import { problemMessages } from "@/features/statementLines/statementLineLabels";
import { useDebouncedValue } from "@/shared/hooks/useDebouncedValue";
import { formatAmount, formatMoney } from "@/shared/format/money";

function FieldError({ messages }: { messages?: string[] }) {
  return messages?.length ? <p className="text-sm text-destructive">{messages.join(" ")}</p> : null;
}

/** D70: what the order is expected to cost or bring, from the preview. */
export function previewSentence(side: string, preview: OrderPreview, places: number | undefined) {
  const money = (value: number) => formatMoney(value, preview.currency, places ?? 2);
  return side === "Buy"
    ? `About ${money(preview.estimatedGross)} at the last price ${formatAmount(preview.referencePrice, 2)} (${shortDay(preview.referencePriceDate)}), plus about ${money(preview.estimatedFee)} in fees. At most ${money(preview.maximumCost ?? preview.maximumGross + preview.maximumFee)} is reserved until the order is filled. Available ${money(preview.availableCash)}.`
    : `About ${money(preview.estimatedGross)} before fees. ${preview.availableQuantity ?? 0} available to sell.`;
}

/** D70: the price the broker will not go beyond. */
export function limitSentence(side: string, preview: OrderPreview) {
  return `The broker fills at the market price, not ${side === "Buy" ? "above" : "below"} ${formatAmount(preview.priceLimit, 2)}.`;
}

/** D70: place a market order on a Custodial account. No price and no booked-at input. */
export function PlaceOrderPage() {
  const { id: accountId = "" } = useParams();
  const navigate = useNavigate();
  const [idempotencyKey] = useState(() => crypto.randomUUID());
  const account = useGetAccountQuery(accountId, { skip: !accountId });
  const holdings = useGetAccountHoldingsQuery(accountId, { skip: !accountId });
  const instruments = useGetInstrumentsQuery();
  const currencies = useGetCurrenciesQuery();
  const [previewOrder] = useLazyPreviewOrderQuery();
  const [place, { isLoading }] = usePlaceOrderMutation();
  const [side, setSide] = useState("Buy");
  const [search, setSearch] = useState("");
  const [instrumentId, setInstrumentId] = useState("");
  const [quantity, setQuantity] = useState("");
  const [memo, setMemo] = useState("");
  const [preview, setPreview] = useState<OrderPreview | null>(null);
  const [reviewing, setReviewing] = useState(false);
  const [fields, setFields] = useState<Record<string, string[]>>({});
  const [messages, setMessages] = useState<string[]>([]);
  const wanted = useDebouncedValue(`${side}|${instrumentId}|${quantity}`, 300);

  useEffect(() => {
    const [wantedSide, wantedInstrument, wantedQuantity] = wanted.split("|");
    const amount = Number(wantedQuantity);
    if (!accountId || !wantedInstrument || !(amount > 0)) {
      return;
    }
    let stale = false;
    previewOrder({ accountId, instrumentId: wantedInstrument, side: wantedSide, quantity: amount })
      .unwrap()
      .then((result) => {
        if (!stale) {
          setPreview(result);
          setFields({});
          setMessages([]);
        }
      })
      .catch((error: unknown) => {
        if (!stale) {
          const result = problemMessages(error);
          setPreview(null);
          setFields(result.fields);
          setMessages(result.messages);
        }
      });
    return () => {
      stale = true;
    };
  }, [accountId, previewOrder, wanted]);

  if (!account.data) {
    return <Muted>Loading…</Muted>;
  }

  const from = account.data;
  const places = (code: string) => currencies.data?.find((item) => item.code === code)?.decimalPlaces;
  const needle = search.trim().toLowerCase();
  const catalog = (instruments.data?.items ?? []).filter(
    (item) => needle === "" || item.id === instrumentId || `${item.symbol} ${item.name}`.toLowerCase().includes(needle),
  );
  const held = holdings.data?.items ?? [];

  function change(apply: () => void) {
    apply();
    setPreview(null);
    setReviewing(false);
  }

  async function onConfirm() {
    try {
      const created = await place({
        body: { accountId: from.id, instrumentId, side, quantity: Number(quantity), memo: memo.trim() === "" ? null : memo.trim() },
        idempotencyKey,
      }).unwrap();
      void navigate(`/orders/${created.id}`);
    } catch (error: unknown) {
      const result = problemMessages(error);
      setFields(result.fields);
      setMessages(result.messages);
      setReviewing(false);
    }
  }

  return (
    <Page className="max-w-lg">
      <PageHeading title="Place order" />
      <Messages messages={messages} />
      <FieldError messages={fields.accountid} />
      <Card>
        <CardContent>
          <form className="flex flex-col gap-4" onSubmit={(event: FormEvent) => { event.preventDefault(); setReviewing(true); }}>
            <SelectField id="order-side" label="Side" value={side}
              onChange={(event) => change(() => { setSide(event.target.value || "Buy"); setInstrumentId(""); })}>
              <SelectItem value="Buy">Buy</SelectItem>
              <SelectItem value="Sell">Sell</SelectItem>
            </SelectField>
            {side === "Buy" ? (
              <TextField id="order-instrument-search" label="Search instruments" value={search}
                onChange={(event) => setSearch(event.target.value)} />
            ) : null}
            <SelectField id="order-instrument" label="Instrument" value={instrumentId} required
              onChange={(event) => change(() => setInstrumentId(event.target.value))}>
              <SelectItem value="">Select an instrument</SelectItem>
              {side === "Buy"
                ? catalog.map((item) => (
                  // orders R9: an order buys in the account's currency only.
                  <SelectItem key={item.id} value={item.id} disabled={item.quoteCurrency !== from.currency}>
                    {item.quoteCurrency === from.currency
                      ? `${item.symbol} ${item.name}`
                      : `${item.symbol} ${item.name} (Quoted in ${item.quoteCurrency})`}
                  </SelectItem>
                ))
                : held.map((item) => (
                  <SelectItem key={item.instrumentId} value={item.instrumentId}>
                    {`${item.symbol} ${item.name} (${item.availableQuantity ?? item.quantity} available)`}
                  </SelectItem>
                ))}
            </SelectField>
            <FieldError messages={fields.instrumentid} />
            <TextField id="order-quantity" label="Quantity" inputMode="decimal" value={quantity} required
              onChange={(event) => change(() => setQuantity(event.target.value))} />
            <FieldError messages={fields.quantity} />
            {preview ? <p className="text-muted-foreground text-sm">{previewSentence(side, preview, places(preview.currency))}</p> : null}
            <TextField id="order-memo" label="Memo" value={memo} maxLength={200}
              onChange={(event) => { setMemo(event.target.value); setReviewing(false); }} />
            <FieldError messages={fields.memo} />
            <Button type="submit" variant="outline" disabled={!preview}>
              Review
            </Button>
          </form>
        </CardContent>
      </Card>
      {reviewing && preview ? (
        <Card>
          <CardContent className="flex flex-col gap-3">
            <p>{previewSentence(side, preview, places(preview.currency))}</p>
            <p>{limitSentence(side, preview)}</p>
            <Button type="button" disabled={isLoading} onClick={() => void onConfirm()}>
              Confirm order
            </Button>
          </CardContent>
        </Card>
      ) : null}
    </Page>
  );
}
