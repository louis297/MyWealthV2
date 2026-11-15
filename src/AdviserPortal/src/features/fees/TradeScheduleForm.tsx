import { type FormEvent, type ReactNode, useRef, useState } from "react";
import { Messages, SelectField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Field, FieldDescription, FieldError, FieldLabel, FieldLegend, FieldSet } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { SelectItem } from "@/components/ui/select";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { FeePreviewPanel, type PreviewSchedule, useFeePreview } from "@/features/fees/FeePreview";
import {
  draftErrors,
  draftValues,
  type DraftBand,
  type FieldErrors,
  hasErrors,
  maxBands,
  type ScheduleDraft,
} from "@/features/fees/scheduleDraft";
import { nextDay, todayUtc } from "@/features/fees/scheduleState";

export const utcDaysHelp =
  "Dates are UTC days. A schedule from 1 Dec applies to trades booked from 1 Dec 00:00 UTC (1 Dec 13:00 in New Zealand during daylight time).";

export function emptyDraft(): ScheduleDraft {
  return { quoteCurrency: "", effectiveFrom: "", minimum: "", cap: "", bands: [{ key: "band-0", from: "0", ratePercent: "" }] };
}

/** The form values the preview sends, or null while the schedule fields fail the form's checks (F19). */
function previewSchedule(draft: ScheduleDraft, decimalPlaces: number | undefined, earliest: string): PreviewSchedule | null {
  const errors = draftErrors(draft, { decimalPlaces, earliest, required: true, effectiveFrom: false });
  if (hasErrors(errors)) {
    return null;
  }
  const { quoteCurrency, minimum, cap, bands } = draftValues(draft);
  return { quoteCurrency, minimum, cap, bands };
}

function FormField({
  id,
  label,
  errors,
  description,
  children,
}: {
  id: string;
  label: string;
  errors: string[] | undefined;
  description?: ReactNode;
  children: ReactNode;
}) {
  return (
    <Field data-invalid={errors && errors.length > 0 ? true : undefined}>
      <FieldLabel htmlFor={id}>{label}</FieldLabel>
      {children}
      {description ? <FieldDescription>{description}</FieldDescription> : null}
      <FieldError errors={errors?.map((message) => ({ message }))} />
    </Field>
  );
}

/**
 * The create / edit form for one trade fee schedule (adviser-portal-fees "Create / edit form"). Checks the
 * draft before calling `onSubmit` and shows the form's own errors, then `serverErrors`, under each field.
 */
export function TradeScheduleForm({
  initial,
  currencyLocked,
  submitLabel,
  serverErrors,
  generalErrors,
  onSubmit,
}: {
  initial: ScheduleDraft;
  currencyLocked: boolean;
  submitLabel: string;
  serverErrors: FieldErrors;
  generalErrors: string[];
  onSubmit: (draft: ScheduleDraft) => void;
}) {
  const [draft, setDraft] = useState(initial);
  const [submitted, setSubmitted] = useState(false);
  const bandSeq = useRef(initial.bands.length);
  const currencies = useGetCurrenciesQuery();
  const decimalPlaces = currencies.data?.find((item) => item.code === draft.quoteCurrency)?.decimalPlaces;
  const earliest = nextDay(todayUtc());
  const ownErrors = draftErrors(draft, { decimalPlaces, earliest, required: submitted, effectiveFrom: true });
  const preview = useFeePreview(previewSchedule(draft, decimalPlaces, earliest));

  function errorsFor(key: string) {
    return ownErrors[key] ?? serverErrors[key] ?? preview.fieldErrors[key];
  }

  function update(patch: Partial<ScheduleDraft>) {
    setDraft((current) => ({ ...current, ...patch }));
  }

  function updateBand(key: string, patch: Partial<DraftBand>) {
    setDraft((current) => ({
      ...current,
      bands: current.bands.map((band) => (band.key === key ? { ...band, ...patch } : band)),
    }));
  }

  function addBand() {
    const key = `band-${bandSeq.current}`;
    bandSeq.current += 1;
    setDraft((current) => ({ ...current, bands: [...current.bands, { key, from: "", ratePercent: "" }] }));
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    setSubmitted(true);
    const errors = draftErrors(draft, { decimalPlaces, earliest, required: true, effectiveFrom: true });
    if (hasErrors(errors)) {
      return;
    }
    onSubmit(draft);
  }

  return (
    <>
      <Messages messages={generalErrors} />
      <Card>
        <CardContent>
          <form className="flex flex-col gap-4" onSubmit={submit} noValidate>
            {currencyLocked ? (
              <FormField id="fee-quote-currency" label="Quote currency" errors={errorsFor("quotecurrency")}>
                <Input id="fee-quote-currency" value={draft.quoteCurrency} readOnly />
              </FormField>
            ) : (
              <div className="flex flex-col gap-1">
                <SelectField
                  id="fee-quote-currency"
                  label="Quote currency"
                  value={draft.quoteCurrency}
                  onChange={(event) => update({ quoteCurrency: event.target.value })}
                  required
                >
                  <SelectItem value="">Select a currency</SelectItem>
                  {(currencies.data ?? []).map((item) => (
                    <SelectItem key={item.code} value={item.code}>
                      {item.code}
                    </SelectItem>
                  ))}
                </SelectField>
                <FieldError errors={errorsFor("quotecurrency")?.map((message) => ({ message }))} />
              </div>
            )}
            <FormField
              id="fee-effective-from"
              label="Effective from"
              errors={errorsFor("effectivefrom")}
              description={utcDaysHelp}
            >
              <Input
                id="fee-effective-from"
                type="date"
                min={earliest}
                value={draft.effectiveFrom}
                onChange={(event) => update({ effectiveFrom: event.target.value })}
                required
              />
            </FormField>
            <div className="grid gap-4 sm:grid-cols-2">
              <FormField id="fee-minimum" label="Minimum" errors={errorsFor("minimum")}>
                <Input
                  id="fee-minimum"
                  inputMode="decimal"
                  value={draft.minimum}
                  onChange={(event) => update({ minimum: event.target.value })}
                  required
                />
              </FormField>
              <FormField id="fee-cap" label="Cap" errors={errorsFor("cap")} description="Optional. Leave empty for no cap.">
                <Input
                  id="fee-cap"
                  inputMode="decimal"
                  value={draft.cap}
                  onChange={(event) => update({ cap: event.target.value })}
                />
              </FormField>
            </div>
            <FieldSet>
              <FieldLegend>Bands</FieldLegend>
              <FieldDescription>
                Each band's rate applies to the part of the trade amount from its start up to the next band.
              </FieldDescription>
              {draft.bands.map((band, index) => {
                const number = index + 1;
                return (
                  <div key={band.key} className="grid items-start gap-4 sm:grid-cols-[1fr_1fr_auto]">
                    <FormField id={`fee-band-${band.key}-from`} label={`Band ${number} from`} errors={errorsFor(`bands[${index}].from`)}>
                      <Input
                        id={`fee-band-${band.key}-from`}
                        inputMode="decimal"
                        value={band.from}
                        readOnly={index === 0}
                        onChange={(event) => updateBand(band.key, { from: event.target.value })}
                      />
                    </FormField>
                    <FormField
                      id={`fee-band-${band.key}-rate`}
                      label={`Band ${number} rate (%)`}
                      errors={errorsFor(`bands[${index}].ratepercent`)}
                    >
                      <Input
                        id={`fee-band-${band.key}-rate`}
                        inputMode="decimal"
                        value={band.ratePercent}
                        onChange={(event) => updateBand(band.key, { ratePercent: event.target.value })}
                      />
                    </FormField>
                    {index > 0 ? (
                      <Button
                        type="button"
                        variant="outline"
                        className="sm:mt-6"
                        aria-label={`Remove band ${number}`}
                        onClick={() =>
                          setDraft((current) => ({
                            ...current,
                            bands: current.bands.filter((item) => item.key !== band.key),
                          }))
                        }
                      >
                        Remove
                      </Button>
                    ) : null}
                  </div>
                );
              })}
              <FieldError errors={errorsFor("bands")?.map((message) => ({ message }))} />
              {draft.bands.length < maxBands ? (
                <Button type="button" variant="outline" className="self-start" onClick={addBand}>
                  Add band
                </Button>
              ) : (
                <p className="text-sm text-muted-foreground">A schedule can have at most 10 bands.</p>
              )}
            </FieldSet>
            <FeePreviewPanel preview={preview} decimalPlaces={decimalPlaces} />
            <Button type="submit" className="self-start">
              {submitLabel}
            </Button>
          </form>
        </CardContent>
      </Card>
    </>
  );
}
