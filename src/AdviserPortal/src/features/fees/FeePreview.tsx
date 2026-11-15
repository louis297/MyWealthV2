import { skipToken } from "@reduxjs/toolkit/query";
import { Fragment, useRef, useState } from "react";
import { Muted } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Field, FieldError, FieldLabel } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import {
  type FeeBand,
  type TradeFeePreview,
  type TradeFeePreviewBody,
  usePreviewTradeFeesQuery,
} from "@/features/fees/feesApi";
import { type FieldErrors, problemErrors } from "@/features/fees/scheduleDraft";
import { useDebouncedValue } from "@/shared/hooks/useDebouncedValue";
import { formatMoney } from "@/shared/format/money";

/** The schedule part of a preview body. Null while the form fails its own checks (F19). */
export type PreviewSchedule = {
  quoteCurrency: string;
  minimum: number;
  cap: number | null;
  bands: FeeBand[];
};

type DraftAmount = { key: string; value: string };

export const maxPreviewAmounts = 5;
const previewDelayMs = 300;
const amountPattern = /^\d+(?:\.\d{1,4})?$/;

function amountError(value: string) {
  const text = value.trim();
  return amountPattern.test(text) && Number(text) > 0 ? undefined : "Each amount must be above 0 with at most 4 decimals.";
}

export type FeePreviewState = {
  amounts: DraftAmount[];
  setAmount: (key: string, value: string) => void;
  addAmount: () => void;
  removeAmount: (key: string) => void;
  amountErrors: (index: number) => string[] | undefined;
  incomplete: boolean;
  result: TradeFeePreview | undefined;
  /** A preview 400's `errors` for the schedule fields, keyed like `draftErrors`. */
  fieldErrors: FieldErrors;
};

/**
 * adviser-portal-fees "Fee preview": one request 300 ms after the last change to the schedule or the amounts.
 * The query is keyed by the body, so a late response to an older body never shows (F16).
 */
export function useFeePreview(schedule: PreviewSchedule | null): FeePreviewState {
  const [amounts, setAmounts] = useState<DraftAmount[]>(() =>
    ["1000", "10000", "100000"].map((value, index) => ({ key: `amount-${index}`, value })),
  );
  const amountSeq = useRef(amounts.length);
  const ownAmountErrors = amounts.map((amount) => amountError(amount.value));
  const ready = schedule !== null && ownAmountErrors.every((error) => error === undefined);
  const key = ready ? JSON.stringify({ ...schedule, amounts: amounts.map((amount) => Number(amount.value)) }) : "";
  // A debounced key has already been sent, so an incomplete form never starts a request (F19).
  const debouncedKey = useDebouncedValue(key, previewDelayMs);
  const preview = usePreviewTradeFeesQuery(debouncedKey ? (JSON.parse(debouncedKey) as TradeFeePreviewBody) : skipToken);
  const serverErrors = preview.isError ? problemErrors(preview.error).fields : {};

  return {
    amounts,
    setAmount: (amountKey, value) =>
      setAmounts((current) => current.map((amount) => (amount.key === amountKey ? { ...amount, value } : amount))),
    addAmount: () => {
      const next = `amount-${amountSeq.current}`;
      amountSeq.current += 1;
      setAmounts((current) => (current.length >= maxPreviewAmounts ? current : [...current, { key: next, value: "" }]));
    },
    removeAmount: (amountKey) => setAmounts((current) => current.filter((amount) => amount.key !== amountKey)),
    amountErrors: (index) => {
      const own = ownAmountErrors[index];
      return own ? [own] : (serverErrors[`amounts[${index}]`] ?? (index === 0 ? serverErrors.amounts : undefined));
    },
    incomplete: schedule === null,
    result: key === "" ? undefined : preview.currentData,
    fieldErrors: serverErrors,
  };
}

function limitText(limit: "minimum" | "cap" | null) {
  if (limit === "minimum") {
    return "minimum applied";
  }
  if (limit === "cap") {
    return "cap applied";
  }
  return "";
}

/** Shows what the preview route returns. The page does no fee arithmetic (F17). */
export function FeePreviewPanel({
  preview,
  decimalPlaces,
}: {
  preview: FeePreviewState;
  decimalPlaces: number | undefined;
}) {
  const [expanded, setExpanded] = useState<ReadonlySet<number>>(new Set());
  const result = preview.result;
  const money = (value: number) => formatMoney(value, result?.currency ?? "", decimalPlaces);

  function toggle(index: number) {
    setExpanded((current) => {
      const next = new Set(current);
      if (next.has(index)) {
        next.delete(index);
      } else {
        next.add(index);
      }
      return next;
    });
  }

  return (
    <section className="flex flex-col gap-3 rounded-lg border p-4" aria-labelledby="fee-preview-heading">
      <h2 id="fee-preview-heading" className="text-base font-medium">
        Fee preview
      </h2>
      <div className="flex flex-col gap-3">
        <div className="grid gap-3 sm:grid-cols-2">
          {preview.amounts.map((amount, index) => {
            const errors = preview.amountErrors(index);
            const id = `fee-preview-${amount.key}`;
            return (
              <Field key={amount.key} data-invalid={errors ? true : undefined}>
                <FieldLabel htmlFor={id}>{`Trade amount ${index + 1}`}</FieldLabel>
                <div className="flex gap-2">
                  <Input
                    id={id}
                    inputMode="decimal"
                    value={amount.value}
                    onChange={(event) => preview.setAmount(amount.key, event.target.value)}
                  />
                  {preview.amounts.length > 1 ? (
                    <Button
                      type="button"
                      variant="outline"
                      aria-label={`Remove amount ${index + 1}`}
                      onClick={() => preview.removeAmount(amount.key)}
                    >
                      Remove
                    </Button>
                  ) : null}
                </div>
                <FieldError errors={errors?.map((message) => ({ message }))} />
              </Field>
            );
          })}
        </div>
        {preview.amounts.length < maxPreviewAmounts ? (
          <Button type="button" variant="outline" className="self-start" onClick={preview.addAmount}>
            Add amount
          </Button>
        ) : (
          <p className="text-sm text-muted-foreground">You can preview up to 5 amounts.</p>
        )}
      </div>
      {preview.incomplete ? <Muted>Complete the bands, minimum and cap to see a preview.</Muted> : null}
      {!preview.incomplete && result ? (
        <Table aria-label="Fee preview">
          <TableHeader>
            <TableRow>
              <TableHead>Trade amount</TableHead>
              <TableHead>Fee</TableHead>
              <TableHead>Limit</TableHead>
              <TableHead>
                <span className="sr-only">Bands</span>
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {result.items.map((item, index) => {
              const label = money(item.amount);
              const open = expanded.has(index);
              return (
                <Fragment key={`${index}-${item.amount}`}>
                  <TableRow>
                    <TableCell>{label}</TableCell>
                    <TableCell>{money(item.fee)}</TableCell>
                    <TableCell>{limitText(item.limitApplied)}</TableCell>
                    <TableCell>
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        aria-expanded={open}
                        aria-label={`${open ? "Hide" : "Show"} bands for ${label}`}
                        onClick={() => toggle(index)}
                      >
                        {open ? "Hide bands" : "Bands"}
                      </Button>
                    </TableCell>
                  </TableRow>
                  {open ? (
                    <TableRow>
                      <TableCell colSpan={4} className="bg-muted/40">
                        <Table aria-label={`Bands for ${label}`}>
                          <TableHeader>
                            <TableRow>
                              <TableHead>From</TableHead>
                              <TableHead>Rate</TableHead>
                              <TableHead>Slice</TableHead>
                              <TableHead>Band fee</TableHead>
                            </TableRow>
                          </TableHeader>
                          <TableBody>
                            {item.bands.map((band) => (
                              <TableRow key={band.from}>
                                <TableCell>{money(band.from)}</TableCell>
                                <TableCell>{`${band.ratePercent}%`}</TableCell>
                                <TableCell>{money(band.slice)}</TableCell>
                                <TableCell>{money(band.fee)}</TableCell>
                              </TableRow>
                            ))}
                          </TableBody>
                        </Table>
                        <p className="px-2 pt-2 text-sm">{`Banded ${money(item.banded)}`}</p>
                      </TableCell>
                    </TableRow>
                  ) : null}
                </Fragment>
              );
            })}
          </TableBody>
        </Table>
      ) : null}
    </section>
  );
}
