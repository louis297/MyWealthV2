import { type ChangeEvent, type FormEvent, useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { Messages, Muted, Page, PageHeading, Section, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useAppDispatch } from "@/app/hooks";
import { accountsApi } from "@/features/accounts/accountsApi";
import { instrumentsApi, useGetInstrumentsQuery } from "@/features/instruments/instrumentsApi";
import { readCsv } from "@/features/migrationBatches/migrationBatchLabels";
import {
  type MigrationBatchBody,
  useCreateMigrationBatchMutation,
  useGetMigrationBatchQuery,
  useReplaceMigrationBatchMutation,
} from "@/features/migrationBatches/migrationBatchesApi";
import { problemMessages, problemStatus } from "@/features/statementLines/statementLineLabels";
import { formatAmount } from "@/shared/format/money";

type MoneyRow = { currency: string; amount: string };
type QuantityRow = { symbol: string; quantity: string };
type Entry = {
  accountId: string;
  accountName: string;
  depositReference: string;
  externalReference: string | null;
  cash: number | null;
  holdings: { instrumentId: string; symbol: string; quantity: number; cost: number }[];
};

function parseNumber(value: string) {
  const trimmed = value.trim();
  return trimmed.length === 0 || Number.isNaN(Number(trimmed)) ? null : Number(trimmed);
}

/**
 * D81: a new migration batch, or a Draft to change. The control totals are typed by hand and never filled in from
 * the entries; the entries come from a CSV the browser reads. Save sends the whole body.
 */
export function MigrationBatchEditPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const dispatch = useAppDispatch();
  const existing = useGetMigrationBatchQuery(id ?? "", { skip: !id });
  const instruments = useGetInstrumentsQuery();
  const [create, { isLoading: creating }] = useCreateMigrationBatchMutation();
  const [replace, { isLoading: replacing }] = useReplaceMigrationBatchMutation();
  const [loaded, setLoaded] = useState(false);
  const [name, setName] = useState("");
  const [previousProvider, setPreviousProvider] = useState("");
  const [asOfDate, setAsOfDate] = useState("");
  const [accountCount, setAccountCount] = useState("");
  const [cash, setCash] = useState<MoneyRow[]>([]);
  const [cost, setCost] = useState<MoneyRow[]>([]);
  const [quantity, setQuantity] = useState<QuantityRow[]>([]);
  const [entries, setEntries] = useState<Entry[]>([]);
  const [csvErrors, setCsvErrors] = useState<string[]>([]);
  const [messages, setMessages] = useState<string[]>([]);
  const [entryErrors, setEntryErrors] = useState<Record<number, string[]>>({});

  const batch = existing.data;
  useEffect(() => {
    if (!batch || loaded) {
      return;
    }
    const symbols = new Map(batch.entries.flatMap((entry) => entry.holdings.map((holding) => [holding.instrument.id, holding.instrument.symbol])));
    setName(batch.name);
    setPreviousProvider(batch.previousProvider);
    setAsOfDate(batch.asOfDate);
    setAccountCount(String(batch.controlTotals.accountCount));
    setCash(batch.controlTotals.cash.map((row) => ({ currency: row.currency, amount: String(row.amount) })));
    setCost(batch.controlTotals.cost.map((row) => ({ currency: row.currency, amount: String(row.amount) })));
    setQuantity(batch.controlTotals.quantity.map((row) => ({
      symbol: symbols.get(row.instrumentId) ?? instruments.data?.items.find((item) => item.id === row.instrumentId)?.symbol ?? "",
      quantity: String(row.quantity),
    })));
    setEntries(batch.entries.map((entry) => ({
      accountId: entry.account.id,
      accountName: entry.account.name,
      depositReference: entry.account.depositReference ?? "",
      externalReference: entry.externalReference,
      cash: entry.cash,
      holdings: entry.holdings.map((holding) => ({
        instrumentId: holding.instrument.id,
        symbol: holding.instrument.symbol,
        quantity: holding.quantity,
        cost: holding.cost,
      })),
    })));
    setLoaded(true);
  }, [batch, loaded, instruments.data]);

  if (id && !batch) {
    return <Muted>{existing.isError ? "Not found" : "Loading…"}</Muted>;
  }

  if (batch && batch.status !== "Draft") {
    return <Muted>Only a draft migration batch can be changed.</Muted>;
  }

  async function onFile(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    if (!file) {
      return;
    }
    const { rows, errors } = readCsv(await file.text());
    const listed = instruments.data
      ?? (await dispatch(instrumentsApi.endpoints.getInstruments.initiate(undefined, { subscribe: false }))).data;
    const bySymbol = new Map((listed?.items ?? []).map((item) => [item.symbol.toUpperCase(), item]));
    const references = [...new Set(rows.map((row) => row.account.toUpperCase()).filter((value) => value.length > 0))];
    const accounts = new Map<string, { id: string; name: string; depositReference: string }>();
    for (const reference of references) {
      const result = await dispatch(accountsApi.endpoints.getAccounts.initiate(
        { depositReference: reference, custodyMode: "Custodial", pageSize: 20 },
        { subscribe: false },
      ));
      const found = result.data?.items.find((item) => item.depositReference?.toUpperCase() === reference);
      if (found) {
        accounts.set(reference, { id: found.id, name: found.name, depositReference: found.depositReference ?? reference });
      }
    }

    const merged = new Map<string, Entry>();
    const problems = [...errors];
    for (const row of rows) {
      const account = accounts.get(row.account.toUpperCase());
      if (!account) {
        problems.push(`Line ${row.line}: no Custodial account has the deposit reference ${row.account}.`);
        continue;
      }
      const rowCash = parseNumber(row.cash);
      const hasHolding = row.symbol.length > 0;
      if ((!hasHolding && rowCash === null) || (row.cash.length > 0 && rowCash === null)) {
        problems.push(`Line ${row.line}: a row has cash, or a symbol with quantity and cost.`);
        continue;
      }
      const instrument = hasHolding ? bySymbol.get(row.symbol.toUpperCase()) : undefined;
      if (hasHolding && !instrument) {
        problems.push(`Line ${row.line}: no instrument has the symbol ${row.symbol}.`);
        continue;
      }
      const rowQuantity = parseNumber(row.quantity);
      const rowCost = parseNumber(row.cost);
      if (hasHolding && (rowQuantity === null || rowCost === null)) {
        problems.push(`Line ${row.line}: a holding needs a quantity and a cost.`);
        continue;
      }
      const entry = merged.get(account.id) ?? {
        accountId: account.id,
        accountName: account.name,
        depositReference: account.depositReference,
        externalReference: null,
        cash: null,
        holdings: [],
      };
      if (!entry.externalReference && row.externalReference.length > 0) {
        entry.externalReference = row.externalReference;
      }
      if (rowCash !== null) {
        entry.cash = (entry.cash ?? 0) + rowCash;
      }
      if (instrument) {
        if (entry.holdings.some((holding) => holding.instrumentId === instrument.id)) {
          problems.push(`Line ${row.line}: ${instrument.symbol} is already in this account.`);
          continue;
        }
        entry.holdings.push({ instrumentId: instrument.id, symbol: instrument.symbol, quantity: rowQuantity!, cost: rowCost! });
      }
      merged.set(account.id, entry);
    }
    setEntries([...merged.values()]);
    setCsvErrors(problems);
    setEntryErrors({});
  }

  function moneyRows(rows: MoneyRow[]) {
    return rows.map((row) => ({ currency: row.currency.trim().toUpperCase(), amount: parseNumber(row.amount) ?? Number.NaN }));
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    const bySymbol = new Map((instruments.data?.items ?? []).map((item) => [item.symbol.toUpperCase(), item.id]));
    const unknown = quantity.filter((row) => !bySymbol.has(row.symbol.trim().toUpperCase()));
    if (unknown.length > 0) {
      setMessages(unknown.map((row) => `No instrument has the symbol ${row.symbol}.`));
      return;
    }
    const body: MigrationBatchBody = {
      name,
      previousProvider,
      asOfDate,
      controlTotals: {
        accountCount: accountCount.trim().length === 0 ? (null as unknown as number) : Number(accountCount),
        cash: moneyRows(cash),
        quantity: quantity.map((row) => ({
          instrumentId: bySymbol.get(row.symbol.trim().toUpperCase())!,
          quantity: parseNumber(row.quantity) ?? Number.NaN,
        })),
        cost: moneyRows(cost),
      },
      entries: entries.map((entry) => ({
        accountId: entry.accountId,
        externalReference: entry.externalReference,
        cash: entry.cash,
        holdings: entry.holdings.map((holding) => ({ instrumentId: holding.instrumentId, quantity: holding.quantity, cost: holding.cost })),
      })),
    };
    try {
      if (batch) {
        await replace({ id: batch.id, rowVersion: batch.rowVersion, body }).unwrap();
        void navigate(`/migration-batches/${batch.id}`);
      } else {
        const created = await create(body).unwrap();
        void navigate(`/migration-batches/${created.id}`);
      }
    } catch (problem: unknown) {
      if (problemStatus(problem) === 409) {
        setMessages(["This batch has changed. Open it again to see it as it is now."]);
        return;
      }
      const result = problemMessages(problem);
      const perEntry: Record<number, string[]> = {};
      const other: string[] = [...result.messages];
      for (const [key, values] of Object.entries(result.fields)) {
        const match = /^entries\[(\d+)\]/.exec(key);
        if (match) {
          perEntry[Number(match[1])] = [...(perEntry[Number(match[1])] ?? []), ...values];
        } else {
          other.push(...values);
        }
      }
      setEntryErrors(perEntry);
      setMessages(other);
    }
  }

  function totalRows(
    label: string,
    rows: MoneyRow[],
    setRows: (rows: MoneyRow[]) => void,
  ) {
    return (
      <div className="flex flex-col gap-2">
        {rows.map((row, index) => (
          <div key={index} className="flex items-end gap-2">
            <TextField id={`${label}-currency-${index}`} label={`${label} currency ${index + 1}`} className="w-24" value={row.currency}
              onChange={(event) => setRows(rows.map((item, at) => (at === index ? { ...item, currency: event.target.value } : item)))} />
            <TextField id={`${label}-amount-${index}`} label={`${label} amount ${index + 1}`} inputMode="decimal" value={row.amount}
              onChange={(event) => setRows(rows.map((item, at) => (at === index ? { ...item, amount: event.target.value } : item)))} />
            <Button type="button" size="sm" variant="ghost" onClick={() => setRows(rows.filter((_, at) => at !== index))}>Remove</Button>
          </div>
        ))}
        <Button type="button" size="sm" variant="outline" className="self-start"
          onClick={() => setRows([...rows, { currency: "", amount: "" }])}>
          {`Add ${label.toLowerCase()} total`}
        </Button>
      </div>
    );
  }

  return (
    <Page className="max-w-4xl">
      <PageHeading title={batch ? `Change ${batch.name}` : "New migration batch"} />
      <Messages messages={messages} />
      <form className="flex flex-col gap-6" onSubmit={(event) => void onSubmit(event)}>
        <Card>
          <CardContent className="grid gap-4 sm:grid-cols-3">
            <TextField id="migration-name" label="Name" maxLength={100} value={name} onChange={(event) => setName(event.target.value)} />
            <TextField id="migration-provider" label="Previous provider" maxLength={100} value={previousProvider}
              onChange={(event) => setPreviousProvider(event.target.value)} />
            <TextField id="migration-as-of" label="As at" type="date" value={asOfDate} onChange={(event) => setAsOfDate(event.target.value)} />
          </CardContent>
        </Card>
        <Section title="Control totals">
          <Card>
            <CardContent className="flex flex-col gap-4">
              <p className="text-sm text-muted-foreground">Type these from the previous provider's closing statement.</p>
              <TextField id="migration-account-count" label="Number of accounts" type="number" min={0} className="w-32"
                value={accountCount} onChange={(event) => setAccountCount(event.target.value)} />
              {totalRows("Cash", cash, setCash)}
              {totalRows("Cost", cost, setCost)}
              <div className="flex flex-col gap-2">
                {quantity.map((row, index) => (
                  <div key={index} className="flex items-end gap-2">
                    <TextField id={`quantity-symbol-${index}`} label={`Quantity symbol ${index + 1}`} className="w-32" value={row.symbol}
                      onChange={(event) => setQuantity(quantity.map((item, at) => (at === index ? { ...item, symbol: event.target.value } : item)))} />
                    <TextField id={`quantity-${index}`} label={`Quantity ${index + 1}`} inputMode="decimal" value={row.quantity}
                      onChange={(event) => setQuantity(quantity.map((item, at) => (at === index ? { ...item, quantity: event.target.value } : item)))} />
                    <Button type="button" size="sm" variant="ghost" onClick={() => setQuantity(quantity.filter((_, at) => at !== index))}>Remove</Button>
                  </div>
                ))}
                <Button type="button" size="sm" variant="outline" className="self-start"
                  onClick={() => setQuantity([...quantity, { symbol: "", quantity: "" }])}>
                  Add quantity total
                </Button>
              </div>
            </CardContent>
          </Card>
        </Section>
        <Section title="Entries">
          <div className="flex flex-col gap-2">
            <label htmlFor="migration-csv" className="text-sm font-medium">Import CSV</label>
            <input id="migration-csv" type="file" accept=".csv,text/csv" onChange={(event) => void onFile(event)} />
            <p className="text-xs text-muted-foreground">Columns: account,externalReference,cash,symbol,quantity,cost. The account is its deposit reference.</p>
          </div>
          {csvErrors.length > 0 ? (
            <ul className="text-sm text-destructive">
              {csvErrors.map((error) => <li key={error}>{error}</li>)}
            </ul>
          ) : null}
          {entries.length === 0 ? <Muted>No entries yet.</Muted> : (
            <Card className="gap-0 py-0">
              <CardContent className="px-0">
                <Table aria-label="Entries">
                  <TableHeader>
                    <TableRow>
                      <TableHead>Account</TableHead>
                      <TableHead>Deposit reference</TableHead>
                      <TableHead>Previous reference</TableHead>
                      <TableHead>Cash</TableHead>
                      <TableHead>Holdings</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {entries.map((entry, index) => (
                      <TableRow key={entry.accountId}>
                        <TableCell>
                          {entry.accountName}
                          {entryErrors[index] ? <p className="text-xs text-destructive">{entryErrors[index].join(" ")}</p> : null}
                        </TableCell>
                        <TableCell>{entry.depositReference}</TableCell>
                        <TableCell>{entry.externalReference ?? "—"}</TableCell>
                        <TableCell>{entry.cash === null ? "—" : formatAmount(entry.cash, 2)}</TableCell>
                        <TableCell>
                          {entry.holdings.length === 0 ? "—" : entry.holdings.map((holding) => `${formatAmount(holding.quantity)} ${holding.symbol}`).join(", ")}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </CardContent>
            </Card>
          )}
        </Section>
        <Button type="submit" className="self-start" disabled={creating || replacing}>Save</Button>
      </form>
    </Page>
  );
}
