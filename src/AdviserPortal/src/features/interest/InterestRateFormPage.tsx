import { type FormEvent, useEffect, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { Messages, Page, PageHeading, SelectField, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { SelectItem } from "@/components/ui/select";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { utcDaysHelp } from "@/features/fees/TradeScheduleForm";
import {
  useCreateInterestRateScheduleMutation,
  useGetInterestRateScheduleQuery,
  useUpdateInterestRateScheduleMutation,
} from "@/features/interest/interestApi";
import { inEffectRateMessage, ratesHelp } from "@/features/interest/interestText";
import { problemMessages } from "@/features/statementLines/statementLineLabels";

type DraftBand = { key: string; from: string; ratePercent: string };

function problemCode(error: unknown) {
  return typeof error === "object" && error !== null && "data" in error
    ? (error as { data?: { code?: string } }).data?.code
    : undefined;
}

/** D59: the create and edit form of an interest rate: the band editor of the fees page without minimum and cap. */
export function InterestRateFormPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const existing = useGetInterestRateScheduleQuery(id ?? "", { skip: !id });
  const currencies = useGetCurrenciesQuery();
  const [create, creating] = useCreateInterestRateScheduleMutation();
  const [update, updating] = useUpdateInterestRateScheduleMutation();
  const [currency, setCurrency] = useState("");
  const [effectiveFrom, setEffectiveFrom] = useState("");
  const [bands, setBands] = useState<DraftBand[]>([{ key: "band-0", from: "0", ratePercent: "" }]);
  const [messages, setMessages] = useState<string[]>([]);
  const seq = useRef(1);
  const loaded = useRef(false);

  useEffect(() => {
    if (existing.data && !loaded.current) {
      loaded.current = true;
      setCurrency(existing.data.currency);
      setEffectiveFrom(existing.data.effectiveFrom);
      setBands(existing.data.bands.map((band, index) => ({ key: `band-${index}`, from: String(band.from), ratePercent: String(band.ratePercent) })));
      seq.current = existing.data.bands.length;
    }
  }, [existing.data]);

  function setBand(key: string, patch: Partial<DraftBand>) {
    setBands((current) => current.map((band) => (band.key === key ? { ...band, ...patch } : band)));
  }

  function addBand() {
    const key = `band-${seq.current}`;
    seq.current += 1;
    setBands((current) => [...current, { key, from: "", ratePercent: "" }]);
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    const body = { effectiveFrom, bands: bands.map((band) => ({ from: Number(band.from), ratePercent: Number(band.ratePercent) })) };
    try {
      if (id && existing.data) {
        await update({ id, body: { ...body, rowVersion: existing.data.rowVersion } }).unwrap();
      } else {
        await create({ currency, ...body }).unwrap();
      }
      void navigate("/interest/rates");
    } catch (error: unknown) {
      if (problemCode(error) === "interest_schedule_in_effect") {
        setMessages([inEffectRateMessage]);
        void existing.refetch();
        return;
      }
      const result = problemMessages(error);
      setMessages([...result.messages, ...Object.values(result.fields).flat()]);
    }
  }

  return (
    <Page className="max-w-2xl">
      <PageHeading title={id ? "Edit interest rate" : "New interest rate"} description={ratesHelp} />
      <Card>
        <CardContent>
          <form className="flex flex-col gap-4" onSubmit={onSubmit}>
            <Messages messages={messages} />
            <SelectField id="interest-currency" label="Currency" value={currency} disabled={Boolean(id)}
              onChange={(event) => setCurrency(event.target.value)}>
              {(currencies.data ?? []).filter((item) => item.isActive).map((item) => (
                <SelectItem key={item.code} value={item.code}>{item.code}</SelectItem>
              ))}
            </SelectField>
            <TextField id="interest-effective-from" label="Effective from" type="date" value={effectiveFrom}
              onChange={(event) => setEffectiveFrom(event.target.value)} />
            <p className="text-muted-foreground text-sm">{utcDaysHelp}</p>
            {bands.map((band, index) => (
              <div key={band.key} className="flex items-end gap-2">
                <TextField id={`band-${index}-from`} label={`Band ${index + 1} from`} value={band.from} disabled={index === 0}
                  inputMode="decimal" onChange={(event) => setBand(band.key, { from: event.target.value })} />
                <TextField id={`band-${index}-rate`} label={`Band ${index + 1} rate %`} value={band.ratePercent}
                  inputMode="decimal" onChange={(event) => setBand(band.key, { ratePercent: event.target.value })} />
                {index > 0 ? (
                  <Button type="button" variant="outline" size="sm"
                    onClick={() => setBands((current) => current.filter((item) => item.key !== band.key))}>
                    Remove
                  </Button>
                ) : null}
              </div>
            ))}
            <div className="flex gap-2">
              <Button type="button" variant="outline" onClick={addBand} disabled={bands.length >= 10}>Add band</Button>
              <Button type="submit" disabled={creating.isLoading || updating.isLoading}>Save</Button>
            </div>
          </form>
        </CardContent>
      </Card>
    </Page>
  );
}
