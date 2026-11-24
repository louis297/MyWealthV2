import { type FormEvent, useEffect, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { Messages, Page, PageHeading, SelectField, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { SelectItem } from "@/components/ui/select";
import {
  useCreateCorporateActionMutation,
  useGetCatalogInstrumentsQuery,
  useGetCorporateActionQuery,
  useUpdateCorporateActionMutation,
} from "@/features/corporateActions/corporateActionsApi";
import {
  conflictMessage,
  dividendExDateHelp,
  exDateHelp,
  fromMarketData,
  kindLabel,
  pastExDateWarning,
  ratioSentence,
  utcToday,
} from "@/features/corporateActions/corporateActionText";
import { problemMessages, problemStatus } from "@/features/statementLines/statementLineLabels";

const kinds = ["Split", "Consolidation", "Bonus", "Dividend"];

function FieldError({ messages }: { messages?: string[] }) {
  return messages?.length ? <p className="text-destructive text-sm">{messages.join(" ")}</p> : null;
}

/** D66: enter a corporate action of an instrument no connection serves; D67: change one that is still Announced. */
export function CorporateActionFormPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const existing = useGetCorporateActionQuery(id ?? "", { skip: !id });
  const catalog = useGetCatalogInstrumentsQuery(undefined, { skip: Boolean(id) });
  const [create, creating] = useCreateCorporateActionMutation();
  const [update, updating] = useUpdateCorporateActionMutation();
  const [search, setSearch] = useState("");
  const [instrumentId, setInstrumentId] = useState("");
  const [kind, setKind] = useState("Split");
  const [ratioNew, setRatioNew] = useState("");
  const [ratioHeld, setRatioHeld] = useState("");
  const [exDate, setExDate] = useState("");
  const [recordDate, setRecordDate] = useState("");
  const [paymentDate, setPaymentDate] = useState("");
  const [memo, setMemo] = useState("");
  // dividends D96: the terms of a dividend, in place of the ratio.
  const [amountPerShare, setAmountPerShare] = useState("");
  const [taxSource, setTaxSource] = useState("NewZealand");
  const [credit, setCredit] = useState("");
  const [foreignRate, setForeignRate] = useState("");
  const [messages, setMessages] = useState<string[]>([]);
  const [fields, setFields] = useState<Record<string, string[]>>({});
  const loaded = useRef(false);

  useEffect(() => {
    if (existing.data && !loaded.current) {
      loaded.current = true;
      setInstrumentId(existing.data.instrument.id);
      setKind(existing.data.kind);
      setRatioNew(existing.data.ratioNew == null ? "" : String(existing.data.ratioNew));
      setRatioHeld(existing.data.ratioHeld == null ? "" : String(existing.data.ratioHeld));
      if (existing.data.dividend) {
        setAmountPerShare(String(existing.data.dividend.amountPerShare));
        setTaxSource(existing.data.dividend.taxSource);
        setCredit(String(existing.data.dividend.imputationCreditPerShare));
        setForeignRate(String(existing.data.dividend.foreignTaxRatePercent));
      }
      setExDate(existing.data.exDate);
      setRecordDate(existing.data.recordDate ?? "");
      setPaymentDate(existing.data.paymentDate ?? "");
      setMemo(existing.data.memo ?? "");
    }
  }, [existing.data]);

  const term = search.trim().toLowerCase();
  const matches = (catalog.data?.items ?? [])
    .filter((item) => term.length > 0 && (item.symbol.toLowerCase().includes(term) || item.name.toLowerCase().includes(term)))
    .slice(0, 10);
  const chosen = catalog.data?.items.find((item) => item.id === instrumentId);
  const isDividend = kind === "Dividend";
  const preview = !isDividend && Number(ratioNew) > 0 && Number(ratioHeld) > 0
    ? ratioSentence(kind, Number(ratioNew), Number(ratioHeld))
    : null;

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    const terms = isDividend
      ? {
        amountPerShare: Number(amountPerShare),
        taxSource,
        imputationCreditPerShare: taxSource === "NewZealand" && credit ? Number(credit) : 0,
        foreignTaxRatePercent: taxSource === "Foreign" && foreignRate ? Number(foreignRate) : 0,
      }
      : { ratioNew: Number(ratioNew), ratioHeld: Number(ratioHeld) };
    const body = {
      kind,
      ...terms,
      exDate,
      recordDate: recordDate || null,
      paymentDate: paymentDate || null,
      memo: memo.trim() ? memo : null,
    };
    try {
      if (id && existing.data) {
        await update({ id, body: { ...body, rowVersion: existing.data.rowVersion } }).unwrap();
        void navigate(`/corporate-actions/${id}`);
      } else {
        const created = await create({ instrumentId, ...body }).unwrap();
        void navigate(`/corporate-actions/${created.id}`);
      }
    } catch (error: unknown) {
      if (problemStatus(error) === 409) {
        setMessages([conflictMessage]);
        setFields({});
        loaded.current = false;
        void existing.refetch();
        return;
      }
      const result = problemMessages(error);
      setFields(result.fields);
      setMessages(result.messages);
    }
  }

  return (
    <Page className="max-w-2xl">
      <PageHeading title={id ? "Edit corporate action" : "New corporate action"} />
      <Card>
        <CardContent>
          <form className="flex flex-col gap-4" onSubmit={onSubmit}>
            <Messages messages={messages} />
            {id ? (
              <p className="text-sm">{existing.data ? `Instrument: ${existing.data.instrument.symbol} · ${existing.data.instrument.name}` : ""}</p>
            ) : (
              <div className="flex flex-col gap-2">
                <TextField id="corporate-action-instrument" label="Instrument" value={search}
                  placeholder="Search by symbol or name" onChange={(event) => setSearch(event.target.value)} />
                {chosen ? <p className="text-sm">{`Chosen: ${chosen.symbol} · ${chosen.name}`}</p> : null}
                <ul aria-label="Instruments" className="flex flex-col gap-1">
                  {matches.map((item) => (
                    <li key={item.id}>
                      <Button type="button" variant="outline" size="sm" disabled={Boolean(item.marketDataConnection)}
                        onClick={() => { setInstrumentId(item.id); setSearch(""); }}>
                        {`${item.symbol} · ${item.name}`}
                        {item.marketDataConnection ? ` (${fromMarketData})` : ""}
                      </Button>
                    </li>
                  ))}
                </ul>
                <FieldError messages={fields.instrumentid} />
              </div>
            )}
            <SelectField id="corporate-action-kind" label="Kind" value={kind} onChange={(event) => setKind(event.target.value)}>
              {kinds.map((item) => <SelectItem key={item} value={item}>{kindLabel(item)}</SelectItem>)}
            </SelectField>
            <FieldError messages={fields.kind} />
            {isDividend ? (
              <>
                <TextField id="corporate-action-amount" label="Amount per share" value={amountPerShare}
                  inputMode="decimal" onChange={(event) => setAmountPerShare(event.target.value)} />
                <FieldError messages={fields.amountpershare} />
                <SelectField id="corporate-action-company" label="Company" value={taxSource}
                  onChange={(event) => setTaxSource(event.target.value)}>
                  <SelectItem value="NewZealand">New Zealand</SelectItem>
                  <SelectItem value="Foreign">Foreign</SelectItem>
                </SelectField>
                <FieldError messages={fields.taxsource} />
                {taxSource === "NewZealand" ? (
                  <TextField id="corporate-action-credit" label="Imputation credit per share" value={credit}
                    inputMode="decimal" onChange={(event) => setCredit(event.target.value)} />
                ) : (
                  <TextField id="corporate-action-foreign-rate" label="Foreign tax rate" value={foreignRate}
                    inputMode="decimal" onChange={(event) => setForeignRate(event.target.value)} />
                )}
                <FieldError messages={fields.imputationcreditpershare ?? fields.foreigntaxratepercent} />
              </>
            ) : (
              <>
                <div className="flex items-end gap-2">
                  <TextField id="corporate-action-ratio-new" label="New shares" value={ratioNew}
                    inputMode="numeric" onChange={(event) => setRatioNew(event.target.value)} />
                  <TextField id="corporate-action-ratio-held" label="For every held" value={ratioHeld}
                    inputMode="numeric" onChange={(event) => setRatioHeld(event.target.value)} />
                </div>
                {preview ? <p className="text-sm">{preview}</p> : null}
                <FieldError messages={fields.rationew} />
              </>
            )}
            <TextField id="corporate-action-ex-date" label="Ex-date" type="date" value={exDate}
              onChange={(event) => setExDate(event.target.value)} />
            <p className="text-muted-foreground text-sm">{isDividend ? dividendExDateHelp : exDateHelp}</p>
            {exDate && exDate < utcToday() ? <p className="text-sm text-amber-600">{pastExDateWarning}</p> : null}
            <FieldError messages={fields.exdate} />
            <TextField id="corporate-action-record-date" label="Record date" type="date" value={recordDate}
              onChange={(event) => setRecordDate(event.target.value)} />
            <TextField id="corporate-action-payment-date" label="Payment date" type="date" value={paymentDate}
              onChange={(event) => setPaymentDate(event.target.value)} />
            <TextField id="corporate-action-memo" label="Memo" value={memo} maxLength={200}
              onChange={(event) => setMemo(event.target.value)} />
            <FieldError messages={fields.memo} />
            <FieldError messages={fields.status ?? fields.origin} />
            <div className="flex gap-2">
              <Button type="submit" disabled={creating.isLoading || updating.isLoading}>Save</Button>
            </div>
          </form>
        </CardContent>
      </Card>
    </Page>
  );
}
