import { SelectField, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { SelectItem } from "@/components/ui/select";
import { taxKindLabel } from "@/features/interest/interestText";

/** external-income R5: at most three taxes on one payment. */
export const maxTaxRows = 3;

/** One row of the "Tax withheld" list, as typed (external-income D111, D112). */
export type TaxRow = { key: number; kind: string; ratePercent: string; amount: string };

/** The kinds a row offers: by the company of a dividend (D111), or all three on an interest (D112). */
export function taxKindsFor(company: string | null): string[] {
  switch (company) {
    case "NewZealand":
      return ["Rwt", "Nrwt"];
    case "Foreign":
      return ["ForeignWithholding", "Rwt"];
    default:
      return ["Rwt", "Nrwt", "ForeignWithholding"];
  }
}

/** The rows as the POST sends them, or no `taxes` when the list is empty. */
export function taxesBody(rows: TaxRow[]) {
  return rows.length === 0
    ? {}
    : { taxes: rows.map((row) => ({ kind: row.kind, ratePercent: Number(row.ratePercent), amount: Number(row.amount) })) };
}

function places(value: string) {
  const point = value.trim().indexOf(".");
  return point < 0 ? 0 : value.trim().length - point - 1;
}

/**
 * D111, D112: "Net <amount> <currency>", the gross less the typed taxes, as display text only. It keeps the most
 * decimal places of what was typed; null until the gross is a number.
 */
export function netText(gross: string, rows: TaxRow[], currency: string | undefined) {
  const value = Number(gross);
  if (gross.trim().length === 0 || Number.isNaN(value) || !currency) {
    return null;
  }
  const amounts = rows.map((row) => row.amount).filter((amount) => amount.trim().length > 0 && !Number.isNaN(Number(amount)));
  const decimals = Math.max(places(gross), ...amounts.map(places));
  const scale = 10 ** decimals;
  const net = amounts.reduce((total, amount) => total - Math.round(Number(amount) * scale), Math.round(value * scale)) / scale;
  return `Net ${net.toFixed(decimals)} ${currency}`;
}

/** A row's 400 messages, by the error keys `Taxes[i].Kind`, `.RatePercent` and `.Amount`. */
export type TaxRowErrors = Record<number, string[]>;

/** Splits the server's 400 into the list's own messages (`Taxes`) and each row's (`Taxes[i].*`). */
export function taxErrors(errors: Record<string, string[]>) {
  const list: string[] = [];
  const rows: TaxRowErrors = {};
  const rest: Record<string, string[]> = {};
  for (const [key, values] of Object.entries(errors)) {
    const row = /^taxes\[(\d+)\]/i.exec(key);
    if (row) {
      const index = Number(row[1]);
      rows[index] = [...(rows[index] ?? []), ...values];
    } else if (key.toLowerCase() === "taxes") {
      list.push(...values);
    } else {
      rest[key] = values;
    }
  }
  return { list, rows, rest };
}

/** D111, D112: the "Tax withheld" list. "Add tax" adds a row of Kind, Rate % and Amount, up to three rows. */
export function TaxRowsInput({
  idPrefix,
  rows,
  onChange,
  kinds,
  currency,
  listErrors,
  rowErrors,
}: {
  idPrefix: string;
  rows: TaxRow[];
  onChange: (rows: TaxRow[]) => void;
  kinds: string[];
  currency: string | undefined;
  listErrors: string[];
  rowErrors: TaxRowErrors;
}) {
  function update(key: number, change: Partial<TaxRow>) {
    onChange(rows.map((row) => (row.key === key ? { ...row, ...change } : row)));
  }

  return (
    <div className="flex flex-col gap-3">
      <p className="text-sm font-medium">Tax withheld</p>
      {rows.map((row, index) => (
        <fieldset key={row.key} className="flex flex-col gap-2 rounded-md border p-3">
          <legend className="px-1 text-xs text-muted-foreground">{`Tax ${index + 1}`}</legend>
          <SelectField
            id={`${idPrefix}-tax-${row.key}-kind`}
            label="Kind"
            value={kinds.includes(row.kind) ? row.kind : ""}
            onChange={(event) => update(row.key, { kind: event.target.value })}
            required
          >
            <SelectItem value="">Select a kind</SelectItem>
            {kinds.map((kind) => (
              <SelectItem key={kind} value={kind}>
                {taxKindLabel(kind)}
              </SelectItem>
            ))}
          </SelectField>
          <TextField
            id={`${idPrefix}-tax-${row.key}-rate`}
            label="Rate %"
            value={row.ratePercent}
            onChange={(event) => update(row.key, { ratePercent: event.target.value })}
            required
          />
          <TextField
            id={`${idPrefix}-tax-${row.key}-amount`}
            label={`Amount (${currency ?? ""})`}
            value={row.amount}
            onChange={(event) => update(row.key, { amount: event.target.value })}
            required
          />
          {(rowErrors[index] ?? []).length > 0 ? (
            <p className="text-sm text-destructive">{rowErrors[index].join(" ")}</p>
          ) : null}
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => onChange(rows.filter((item) => item.key !== row.key))}
          >
            Remove
          </Button>
        </fieldset>
      ))}
      {rows.length < maxTaxRows ? (
        <Button
          type="button"
          variant="outline"
          onClick={() =>
            onChange([...rows, { key: Math.max(0, ...rows.map((row) => row.key)) + 1, kind: "", ratePercent: "", amount: "" }])
          }
        >
          Add tax
        </Button>
      ) : null}
      {listErrors.length > 0 ? (
        <p id={`${idPrefix}-taxes-error`} className="text-sm text-destructive">
          {listErrors.join(" ")}
        </p>
      ) : null}
    </div>
  );
}
