import { type FormEvent, useState } from "react";
import { useNavigate } from "react-router";
import { Messages, Page, PageHeading, SelectField, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { SelectItem } from "@/components/ui/select";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { useGetInstrumentsQuery } from "@/features/instruments/instrumentsApi";
import { useCreatePortfolioMutation, type CreatePortfolioBody } from "@/features/portfolios/portfoliosApi";

type DraftLine = {
  key: string;
  instrumentId: string;
  weight: string;
};

function problemMessages(error: unknown): string[] {
  if (typeof error !== "object" || error === null || !("data" in error)) {
    return [];
  }
  const data = (error as { data?: { errors?: Record<string, string[]>; detail?: string } }).data;
  if (data?.errors) {
    return Object.values(data.errors).flat();
  }
  if (data?.detail) {
    return [data.detail];
  }
  return [];
}

function optionalNumber(value: string): number | undefined {
  if (value.trim() === "") {
    return undefined;
  }
  return Number(value);
}

function lineMessages(lines: DraftLine[], activeIds: ReadonlySet<string>): string[] {
  const messages: string[] = [];
  const seen = new Set<string>();
  let cents = 0;
  let weightsValid = true;
  for (const line of lines) {
    const weight = Number(line.weight);
    const rounded = Math.round(weight * 100);
    if (line.weight.trim() === "" || !Number.isFinite(weight) || rounded <= 0 || rounded > 10000) {
      weightsValid = false;
    } else {
      cents += rounded;
    }
    if (!line.instrumentId || !activeIds.has(line.instrumentId) || seen.has(line.instrumentId)) {
      messages.push("Choose an active instrument.");
    }
    if (line.instrumentId) {
      seen.add(line.instrumentId);
    }
  }
  if (!weightsValid) {
    messages.push("Each weight must be greater than 0 and no more than 100.");
  } else if (cents !== 10000) {
    messages.push("Weights must total 100.00.");
  }
  return [...new Set(messages)];
}

export function CreatePortfolioPage() {
  const navigate = useNavigate();
  const [name, setName] = useState("");
  const [baseCurrency, setBaseCurrency] = useState("");
  const [lines, setLines] = useState<DraftLine[]>([{ key: "line-1", instrumentId: "", weight: "" }]);
  const [driftAbsolute, setDriftAbsolute] = useState("");
  const [driftRelative, setDriftRelative] = useState("");
  const [minimumTrade, setMinimumTrade] = useState("");
  const [cashResidualPercent, setCashResidualPercent] = useState("");
  const [messages, setMessages] = useState<string[]>([]);
  const { data: currencies } = useGetCurrenciesQuery();
  const { data: instruments } = useGetInstrumentsQuery();
  const [createPortfolio] = useCreatePortfolioMutation();
  const activeIds = new Set((instruments?.items ?? []).filter((item) => item.isActive).map((item) => item.id));

  function updateLine(key: string, patch: Partial<DraftLine>) {
    setLines((current) => current.map((line) => (line.key === key ? { ...line, ...patch } : line)));
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    const problems = lineMessages(lines, activeIds);
    if (problems.length > 0) {
      setMessages(problems);
      return;
    }

    const body: CreatePortfolioBody = {
      name: name.trim(),
      baseCurrency,
      lines: lines.map((line) => ({
        instrumentId: line.instrumentId,
        targetWeight: Number(line.weight),
      })),
    };
    const drift = optionalNumber(driftAbsolute);
    const relative = optionalNumber(driftRelative);
    const minimum = optionalNumber(minimumTrade);
    const residual = optionalNumber(cashResidualPercent);
    if (drift !== undefined) {
      body.driftAbsolute = drift;
    }
    if (relative !== undefined) {
      body.driftRelative = relative;
    }
    if (minimum !== undefined) {
      body.minimumTrade = minimum;
    }
    if (residual !== undefined) {
      body.cashResidualPercent = residual;
    }

    try {
      const created = await createPortfolio(body).unwrap();
      void navigate(`/portfolios/${created.id}`);
    } catch (error: unknown) {
      setMessages(problemMessages(error));
    }
  }

  return (
    <Page className="max-w-lg">
      <PageHeading title="New portfolio" />
      <Messages messages={messages} />
      <Card>
        <CardContent>
          <form className="flex flex-col gap-4" onSubmit={(event) => void onSubmit(event)}>
            <TextField id="portfolio-name" label="Name" value={name} onChange={(event) => setName(event.target.value)} required />
            <SelectField
              id="portfolio-base-currency"
              label="Base currency"
              value={baseCurrency}
              onChange={(event) => setBaseCurrency(event.target.value)}
              required
            >
              <SelectItem value="">Select a currency</SelectItem>
              {(currencies ?? []).map((item) => (
                <SelectItem key={item.code} value={item.code}>
                  {item.code}
                </SelectItem>
              ))}
            </SelectField>
            {lines.map((line, index) => (
              <div key={line.key} className="grid gap-4 sm:grid-cols-2">
                <SelectField
                  id={`portfolio-instrument-${line.key}`}
                  label="Instrument"
                  value={line.instrumentId}
                  onChange={(event) => updateLine(line.key, { instrumentId: event.target.value })}
                >
                  <SelectItem value="">Select an instrument</SelectItem>
                  {(instruments?.items ?? []).map((item) => (
                    <SelectItem key={item.id} value={item.id}>
                      {item.symbol}
                    </SelectItem>
                  ))}
                </SelectField>
                <TextField
                  id={`portfolio-weight-${line.key}`}
                  label="Weight"
                  value={line.weight}
                  onChange={(event) => updateLine(line.key, { weight: event.target.value })}
                  inputMode="decimal"
                  required
                />
                {index > 0 ? (
                  <Button
                    type="button"
                    variant="outline"
                    onClick={() => setLines((current) => current.filter((item) => item.key !== line.key))}
                  >
                    Remove line
                  </Button>
                ) : null}
              </div>
            ))}
            <Button
              type="button"
              variant="outline"
              onClick={() =>
                setLines((current) => [...current, { key: `line-${current.length + 1}`, instrumentId: "", weight: "" }])
              }
            >
              Add line
            </Button>
            <TextField
              id="portfolio-drift-absolute"
              label="Drift absolute"
              value={driftAbsolute}
              onChange={(event) => setDriftAbsolute(event.target.value)}
              inputMode="decimal"
            />
            <TextField
              id="portfolio-drift-relative"
              label="Drift relative"
              value={driftRelative}
              onChange={(event) => setDriftRelative(event.target.value)}
              inputMode="decimal"
            />
            <TextField
              id="portfolio-minimum-trade"
              label="Minimum trade"
              value={minimumTrade}
              onChange={(event) => setMinimumTrade(event.target.value)}
              inputMode="decimal"
            />
            <TextField
              id="portfolio-cash-residual"
              label="Cash residual"
              value={cashResidualPercent}
              onChange={(event) => setCashResidualPercent(event.target.value)}
              inputMode="decimal"
            />
            <Button type="submit">Create portfolio</Button>
          </form>
        </CardContent>
      </Card>
    </Page>
  );
}
