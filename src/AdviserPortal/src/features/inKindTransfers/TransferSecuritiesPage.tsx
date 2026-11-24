import { type FormEvent, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { Messages, Muted, Page, PageHeading, SelectField, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { SelectItem } from "@/components/ui/select";
import { useGetAccountHoldingsQuery, useGetAccountQuery } from "@/features/accounts/accountsApi";
import { approvalSentence, ownerChangesText, transferSummary } from "@/features/inKindTransfers/inKindTransferLabels";
import { type RequestInKindTransferBody, useRequestInKindTransferMutation } from "@/features/inKindTransfers/inKindTransfersApi";
import { useGetInstrumentsQuery } from "@/features/instruments/instrumentsApi";
import { problemMessages } from "@/features/statementLines/statementLineLabels";

function FieldError({ messages }: { messages?: string[] }) {
  return messages?.length ? <p className="text-sm text-destructive">{messages.join(" ")}</p> : null;
}

/** The fields of the request body the form shows errors at (lower case, as problemMessages keys them). */
const formFields = [
  "direction",
  "instrumentid",
  "quantity",
  "cost",
  "acquiredon",
  "changesbeneficialowner",
  "counterparty.name",
  "counterparty.account",
  "counterparty.holdername",
  "memo",
];

/** D87: request an in-kind transfer in or out of a Custodial account. */
export function TransferSecuritiesPage() {
  const { id: accountId = "" } = useParams();
  const navigate = useNavigate();
  const [idempotencyKey] = useState(() => crypto.randomUUID());
  const account = useGetAccountQuery(accountId, { skip: !accountId });
  const holdings = useGetAccountHoldingsQuery(accountId, { skip: !accountId || !account.data });
  const instruments = useGetInstrumentsQuery();
  const [request, { isLoading }] = useRequestInKindTransferMutation();
  const [direction, setDirection] = useState<"" | "In" | "Out">("");
  const [instrumentId, setInstrumentId] = useState("");
  const [quantity, setQuantity] = useState("");
  const [cost, setCost] = useState("");
  const [acquiredOn, setAcquiredOn] = useState("");
  const [ownerChanges, setOwnerChanges] = useState<boolean | null>(null);
  const [counterpartyName, setCounterpartyName] = useState("");
  const [counterpartyAccount, setCounterpartyAccount] = useState("");
  const [holderName, setHolderName] = useState("");
  const [memo, setMemo] = useState("");
  const [reviewing, setReviewing] = useState(false);
  const [fields, setFields] = useState<Record<string, string[]>>({});
  const [messages, setMessages] = useState<string[]>([]);

  if (!account.data) {
    return <Muted>Loading…</Muted>;
  }

  const from = account.data;
  const inChoices = (instruments.data?.items ?? []).map((item) => ({
    id: item.id,
    symbol: item.symbol,
    label: item.quoteCurrency === from.currency
      ? `${item.symbol} ${item.name}`
      : `${item.symbol} ${item.name} (Quoted in ${item.quoteCurrency})`,
    disabled: item.quoteCurrency !== from.currency,
  }));
  const outChoices = (holdings.data?.items ?? []).map((item) => ({
    id: item.instrumentId,
    symbol: item.symbol,
    label: `${item.symbol} ${item.name} (${item.transferableQuantity ?? item.availableQuantity ?? item.quantity} available to transfer)`,
    disabled: false,
  }));
  const choices = direction === "In" ? inChoices : direction === "Out" ? outChoices : [];
  const chosen = choices.find((item) => item.id === instrumentId);

  function edit<T>(setter: (value: T) => void) {
    return (value: T) => {
      setter(value);
      setReviewing(false);
    };
  }

  function onReview(event: FormEvent) {
    event.preventDefault();
    if (ownerChanges === null) {
      setFields({ changesbeneficialowner: ["Choose whether the owner changes."] });
      return;
    }
    setFields({});
    setReviewing(true);
  }

  async function onConfirm() {
    const body: RequestInKindTransferBody = {
      accountId: from.id,
      direction: direction as "In" | "Out",
      instrumentId,
      quantity: Number(quantity),
      ...(direction === "In" ? { cost: Number(cost), acquiredOn: acquiredOn === "" ? null : acquiredOn } : {}),
      changesBeneficialOwner: ownerChanges === true,
      counterparty: { name: counterpartyName.trim(), account: counterpartyAccount.trim(), holderName: holderName.trim() },
      memo: memo.trim() === "" ? null : memo.trim(),
    };
    try {
      const created = await request({ body, idempotencyKey }).unwrap();
      void navigate(`/transfers/${created.id}`);
    } catch (error: unknown) {
      const result = problemMessages(error);
      const other = Object.entries(result.fields).filter(([key]) => !formFields.includes(key)).flatMap(([, values]) => values);
      setFields(result.fields);
      setMessages([...result.messages, ...other]);
      setReviewing(false);
    }
  }

  return (
    <Page className="max-w-lg">
      <PageHeading title="Transfer securities" description={from.name} />
      <Messages messages={messages} />
      <Card>
        <CardContent>
          <form className="flex flex-col gap-4" onSubmit={onReview}>
            <SelectField id="transfer-direction" label="Direction" value={direction} required
              onChange={(event) => { edit(setDirection)(event.target.value as "" | "In" | "Out"); setInstrumentId(""); }}>
              <SelectItem value="">Select a direction</SelectItem>
              <SelectItem value="In">In</SelectItem>
              <SelectItem value="Out">Out</SelectItem>
            </SelectField>
            <FieldError messages={fields.direction} />
            <SelectField id="transfer-instrument" label="Instrument" value={instrumentId} required disabled={direction === ""}
              onChange={(event) => edit(setInstrumentId)(event.target.value)}>
              <SelectItem value="">Select an instrument</SelectItem>
              {choices.map((item) => (
                <SelectItem key={item.id} value={item.id} disabled={item.disabled}>
                  {item.label}
                </SelectItem>
              ))}
            </SelectField>
            <FieldError messages={fields.instrumentid} />
            <TextField id="transfer-quantity" label="Quantity" inputMode="decimal" value={quantity} required
              onChange={(event) => edit(setQuantity)(event.target.value)} />
            <FieldError messages={fields.quantity} />
            {direction === "In" ? (
              <>
                <TextField id="transfer-cost" label="Cost" inputMode="decimal" value={cost} required
                  onChange={(event) => edit(setCost)(event.target.value)} />
                <p className="text-muted-foreground text-xs">{`What the customer paid for these shares in total, in ${from.currency}`}</p>
                <FieldError messages={fields.cost} />
                <TextField id="transfer-acquired-on" label="Acquired on (optional)" type="date" value={acquiredOn}
                  onChange={(event) => edit(setAcquiredOn)(event.target.value)} />
                <FieldError messages={fields.acquiredon} />
              </>
            ) : null}
            <fieldset className="flex flex-col gap-1">
              <legend className="text-sm font-medium">Does the owner change?</legend>
              <label className="flex items-center gap-2 text-sm">
                <input type="radio" name="transfer-owner" checked={ownerChanges === false}
                  onChange={() => edit(setOwnerChanges)(false)} />
                No
              </label>
              <p className="text-muted-foreground pl-6 text-xs">The customer owns these shares before and after</p>
              <label className="flex items-center gap-2 text-sm">
                <input type="radio" name="transfer-owner" checked={ownerChanges === true}
                  onChange={() => edit(setOwnerChanges)(true)} />
                Yes
              </label>
            </fieldset>
            <FieldError messages={fields.changesbeneficialowner} />
            <TextField id="transfer-counterparty-name" label="Broker or registry" value={counterpartyName} required maxLength={140}
              onChange={(event) => edit(setCounterpartyName)(event.target.value)} />
            <FieldError messages={fields["counterparty.name"]} />
            <TextField id="transfer-counterparty-account" label="Account or holder number" value={counterpartyAccount} required maxLength={34}
              onChange={(event) => edit(setCounterpartyAccount)(event.target.value)} />
            <FieldError messages={fields["counterparty.account"]} />
            <TextField id="transfer-holder-name" label="Name on the account" value={holderName} required maxLength={140}
              onChange={(event) => edit(setHolderName)(event.target.value)} />
            <FieldError messages={fields["counterparty.holdername"]} />
            <TextField id="transfer-memo" label="Memo" value={memo} maxLength={200}
              onChange={(event) => edit(setMemo)(event.target.value)} />
            <FieldError messages={fields.memo} />
            <Button type="submit" variant="outline">
              Review
            </Button>
          </form>
        </CardContent>
      </Card>
      {reviewing && chosen && ownerChanges !== null ? (
        <Card>
          <CardContent className="flex flex-col gap-3">
            <p>
              {`${transferSummary({ direction, quantity: Number(quantity), instrument: chosen, counterparty: { name: counterpartyName.trim() } })}, ${
                direction === "In" ? "into" : "out of"
              } ${from.name}. ${ownerChangesText(ownerChanges)}.`}
            </p>
            <p className="text-sm">{approvalSentence}</p>
            <Button type="button" disabled={isLoading} onClick={() => void onConfirm()}>
              Confirm transfer
            </Button>
          </CardContent>
        </Card>
      ) : null}
    </Page>
  );
}
